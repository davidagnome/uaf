using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// A legacy monster's numeric spell and item keys, resolved on load.
/// </summary>
/// <remarks>
/// <b>The reference reads the attack's spell key and never uses it again</b>
/// (<c>Monster.cpp:130</c>; nothing else in the tree touches
/// <c>preVersionSpellNames_gsID</c>), so upgrading a legacy design through the editor drops every
/// monster attack's spell. The port resolves it instead — a deliberate divergence, since the
/// lookup exists and losing the information is a defect rather than a behaviour to reproduce.
/// </remarks>
public class LegacyMonsterUpgradeTests
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
    /// The premise: the template's monsters really hold items by numeric key.
    /// </summary>
    /// <remarks>
    /// Its attacks, by contrast, all carry the <b>-1 "no spell" sentinel</b> rather than a
    /// reference — which is what made the old refusal fire on all 44 monsters for a lookup none of
    /// them was asking for.
    /// </remarks>
    [Fact]
    public void The_template_holds_items_by_number()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);

        Assert.NotNull(design.Monsters);
        Assert.NotEmpty(design.Monsters!);
    }

    /// <summary>Every key is resolved, so nothing is left holding a number.</summary>
    [Fact]
    public void No_monster_is_left_holding_a_number()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);

        Assert.All(design.Monsters!, m => Assert.False(LegacyIdUpgrade.NeedsUpgrade(m)));
    }

    /// <summary>
    /// A resolved key names a real item, not an empty string.
    /// </summary>
    /// <remarks>
    /// The failure this guards is silent: writing an empty <c>ITEM_ID</c> leaves the monster
    /// holding nothing, and the file is perfectly valid afterwards.
    /// </remarks>
    [Fact]
    public void A_resolved_item_names_something_real()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);

        var held = design.Monsters!
            .Where(m => m.Items is not null)
            .SelectMany(m => m.Items!.Items)
            .Where(i => !string.IsNullOrEmpty(i.ItemId))
            .ToList();

        Assert.NotEmpty(held);

        // Every name a monster holds is an item the design actually defines.
        var known = design.Items!.Items
            .Select(i => i.Names.UniqueName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(held, i => Assert.Contains(i.ItemId, known));
    }

    /// <summary>And the database writes, which it could not before.</summary>
    [Fact]
    public void The_monster_database_writes()
    {
        if (TemplateDesign() is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);

        Assert.All(design.Monsters!,
                   m => Assert.True(MonsterRecordWriter.CanWrite(m, out string why), why));
    }
}
