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
    /// One legacy shape still stops this database being written, and it is the special abilities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted rather than described, so it cannot rot.</b> Converting the usability mask was
    /// one of the two things <c>ItemRecordWriter</c> refuses; the other is a special-ability block
    /// still in the pre-0.921 shape, which is a bare array of ability <i>ordinals</i>. Turning
    /// those into the modern named pairs needs the ability-name table, which is a second
    /// conversion of its own size — see <c>SpecabWriter</c>.
    /// </para>
    /// <para>
    /// <b>When that lands this test fails</b>, and the person who wrote it should replace it with
    /// one asserting the database writes.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_remaining_refusal_is_the_special_abilities()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);

        var reasons = design.Items!.Items
            .Where(i => !ItemRecordWriter.CanWrite(i, out _))
            .Select(i =>
            {
                ItemRecordWriter.CanWrite(i, out string reason);
                return reason;
            })
            .ToList();

        Assert.NotEmpty(reasons);
        Assert.All(reasons, r => Assert.Contains("pre-0.921", r, StringComparison.Ordinal));
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
