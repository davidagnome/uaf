using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// A legacy design's items, upgraded on load so they can be saved.
/// </summary>
/// <remarks>
/// <para>
/// <b>The payoff, tested where it is felt.</b> <see cref="ItemUsabilityUpgrade"/> is unit-tested
/// on its own; what this asserts is that <see cref="LoadedDesign"/> actually runs it on a real
/// legacy design — and, just as importantly, what is <i>still</i> in the way afterwards.
/// </para>
/// <para>
/// <c>DefaultDesign</c> is the case that matters and is committed to the repository, so this runs
/// on a bare checkout — the editor's own template is the design File &gt; New would start from.
/// </para>
/// </remarks>
public class LegacyItemUpgradeTests
{
    private static string? TemplateDesign()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null
            ? null
            : Path.Combine(dir.FullName, "src", "UAFWinEd", "DefaultDesign.dsn");

        return root is not null && Directory.Exists(root) ? root : null;
    }

    /// <summary>
    /// The premise: the template is a legacy design whose items really carry the old shape.
    /// </summary>
    /// <remarks>
    /// Read as <see cref="ArchiveRole.Engine"/> the conversion branch is not taken at all — the
    /// engine refuses such designs — so this also pins which role sees the mask.
    /// </remarks>
    [Fact]
    public void The_template_is_a_legacy_design()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);

        Assert.True(design.Globals.Version.Value < 0.998101);
        Assert.NotNull(design.Items);
        Assert.NotEmpty(design.Items!.Items);

        // And its classes.dat is a shape nobody ported, so the conversion runs with no class
        // table at all -- which is the path that has to work, not just the tidy one.
        Assert.Null(design.Classes);
    }

    /// <summary>
    /// Every item comes back with its usability mask converted.
    /// </summary>
    /// <remarks>
    /// <b>This is what the upgrade is for.</b> Before it, <c>ItemRecordWriter.CanWrite</c> refused
    /// the first item in this database — "Arrow carries the pre-0.998101 Usable_by_Class bitmask"
    /// — and refused the design with it.
    /// </remarks>
    [Fact]
    public void Every_item_loses_its_usability_mask()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);

        Assert.All(design.Items!.Items,
                   item => Assert.False(ItemUsabilityUpgrade.NeedsUpgrade(item)));
    }

    /// <summary>
    /// The whole database now writes, which it could not before.
    /// </summary>
    /// <remarks>
    /// <b>This replaces a test that asserted the opposite.</b> Two legacy shapes stood in the way
    /// — the usability bitmask and a pre-0.921 special-ability block — and its remarks said that
    /// when the second landed, whoever did it should come back and assert the database writes.
    /// </remarks>
    [Fact]
    public void The_database_writes()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);
        var items = design.Items!;

        Assert.All(items.Items,
                   i => Assert.True(ItemRecordWriter.CanWrite(i, out string why), why));

        byte[] written = DesignFileWriter.ToBytes(
            ItemRecordWriter.WrittenVersion,
            ar => ItemRecordWriter.WriteDatabase(ar, items.Items, items.AmmoTypes));

        // And it reads back, with the same records in it.
        using var stream = new MemoryStream(written, writable: false);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database);
        stream.Seek(16, SeekOrigin.Begin);

        var back = ItemRecordReader.ReadDatabase(
            CarArchiveReader.Open(stream), header.Version, ArchiveRole.Editor);

        Assert.Equal(items.Items.Count, back.Items.Count);
        Assert.Equal(items.Items[0].Names.IdName, back.Items[0].Names.IdName);
        Assert.Equal(items.Items[0].Tail.UsableByBaseclass, back.Items[0].Tail.UsableByBaseclass);
    }

    /// <summary>
    /// The template's items carry legacy ability slots, and every one of them is empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, because it is why this design writes at all.</b> Its 285 items carry 9,120
    /// legacy slots between them — thirty-two each — and not one passes the reference's
    /// <c>empty != 0</c> test, so the conversion correctly drops all of them and the records come
    /// out with no abilities rather than with invented ones.
    /// </para>
    /// <para>
    /// <b>So no corpus design exercises the invention path</b>: this one has nothing to invent
    /// from, and the other two are above 0.921 and never had legacy slots. That path is covered by
    /// <c>SpecabUpgradeTests</c> instead, which is the honest place for it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_templates_legacy_slots_are_all_empty()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);
        var items = design.Items!.Items;

        // Converted, so nothing legacy survives on the records themselves.
        Assert.All(items, i => Assert.False(SpecabUpgrade.NeedsUpgrade(i.Tail.SpecialAbilities)));

        // And nothing was invented, because there was nothing in them.
        Assert.All(items, i => Assert.Empty(i.Tail.SpecialAbilities.Pairs));
    }

    /// <summary>
    /// The conversion says something rather than emptying the field.
    /// </summary>
    /// <remarks>
    /// A mask of zero is a legitimate value — an item nobody can use — so "every list is empty"
    /// would satisfy the test above while having converted nothing. At least one item in a real
    /// database names at least one baseclass.
    /// </remarks>
    [Fact]
    public void The_conversion_names_baseclasses()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);

        var named = design.Items!.Items
            .SelectMany(i => i.Tail.UsableByBaseclass)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(named);

        // With no class table the built-in names are used, so everything named must be one of
        // the seven -- a name from anywhere else would mean the fallback went somewhere odd.
        Assert.All(named, n => Assert.Contains(
            n, new[] { "fighter", "magicUser", "cleric", "thief", "paladin", "ranger", "druid" }));
    }
}
