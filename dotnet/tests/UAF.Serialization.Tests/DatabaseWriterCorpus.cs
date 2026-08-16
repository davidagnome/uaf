using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Locating the designs the five design-database writers round-trip over, and comparing the
/// structures they share.
/// </summary>
/// <remarks>
/// <para>
/// The five files — <c>ability.dat</c>, <c>baseclass.dat</c>, <c>classes.dat</c>, <c>races.dat</c>
/// and <c>specialAbilities.dat</c> — had readers and no writers, which is what stopped an editor
/// saving a design it had opened. Their corpus tests all need the same three things: the repository
/// root, a design's <c>Data</c> folder, and the design version from <c>game.dat</c> beside it — a
/// tagged database carries a container tag and a record count and <b>no version</b>, so the version
/// has to come from next door.
/// </para>
/// <para>
/// <b>Only <c>DefaultDesign</c> is committed.</b> Everything under <c>reference/</c> is gitignored,
/// so every case over one of those designs returns early when it is absent, and each test file
/// carries a premise case that says what it proved on a checkout that has none.
/// </para>
/// <para>
/// <b>The comparison helpers exist because these records hold lists and arrays.</b> A C# record's
/// generated equality compares those by reference, so <c>Assert.Equal(before, after)</c> on a
/// <see cref="BaseclassRecord"/> fails on two identical records and passes on nothing.
/// </para>
/// </remarks>
internal static class DatabaseWriterCorpus
{
    /// <summary>
    /// The repository root, found by walking up for the folder holding the C++ reference.
    /// </summary>
    /// <remarks>
    /// <c>src/Shared</c> rather than <c>.git</c>: a worktree or submodule checkout has no
    /// <c>.git</c> directory, and every fixture path is relative to the C++ sources.
    /// </remarks>
    public static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    /// <summary>The committed design, which is what stops a fresh checkout proving nothing.</summary>
    public const string DefaultDesign = "src/UAFWinEd/DefaultDesign.dsn";

    /// <summary>A design's <c>Data</c> folder, or null when the design is not on this machine.</summary>
    public static string? DataDirectory(string relativeDesign)
    {
        if (RepoRoot() is not { } root)
        {
            return null;
        }

        string path = Path.Combine(root.FullName,
                                   Path.Combine(relativeDesign.Split('/')), "Data");
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>One of a design's files, or null when either it or the design is absent.</summary>
    public static string? File(string relativeDesign, string fileName)
    {
        if (DataDirectory(relativeDesign) is not { } data)
        {
            return null;
        }

        string path = Path.Combine(data, fileName);
        return System.IO.File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The design version, off <c>game.dat</c>.
    /// </summary>
    /// <remarks>
    /// Load-bearing for three of the four tagged databases: it selects the special-abilities
    /// branch, whether <c>races.dat</c> carries its five skill lists, and whether an editor-role
    /// reader takes a numeric id where a modern one takes a name.
    /// </remarks>
    public static DesignVersion? Version(string relativeDesign)
    {
        if (File(relativeDesign, "game.dat") is not { } path)
        {
            return null;
        }

        using var stream = System.IO.File.OpenRead(path);
        return GameDataReader.Open(stream).Version;
    }

    /// <summary>Compares two <c>DICEPLUS</c> expressions field by field.</summary>
    /// <remarks>
    /// The record holds an adjustment list, so its generated equality compares by reference. Only
    /// <c>DP2</c> can be written and it carries no adjustments at all, so the count is asserted
    /// rather than the contents — a non-empty one here would mean the writer emitted a form it
    /// claims it cannot.
    /// </remarks>
    public static void AssertSameDice(DicePlus expected, DicePlus actual)
    {
        Assert.Equal(expected.Tag, actual.Tag);
        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal(expected.Binary, actual.Binary);
        Assert.Equal(expected.Adjustments.Count, actual.Adjustments.Count);
    }

    /// <summary>Compares two special-abilities blocks.</summary>
    /// <remarks>
    /// Pairs only: the legacy slots and ordinals cannot be written at all, and every writer refuses
    /// a block that still holds them rather than dropping them, so both sides are necessarily
    /// empty by the time this is reached.
    /// </remarks>
    public static void AssertSameSpecabs(SpecabBlock expected, SpecabBlock actual)
    {
        Assert.Equal(expected.Pairs, actual.Pairs);
        Assert.Empty(expected.LegacySlots);
        Assert.Empty(actual.LegacySlots);
        Assert.Empty(expected.LegacyOrdinals);
        Assert.Empty(actual.LegacyOrdinals);
    }

    /// <summary>Compares two <c>ABILITY_REQ</c> lists.</summary>
    public static void AssertSameRequirements(IReadOnlyList<AbilityRequirement> expected,
                                              IReadOnlyList<AbilityRequirement> actual)
    {
        // These hold no lists, so record equality is real value equality here.
        Assert.Equal(expected, actual);
    }

    /// <summary>Compares two <c>SKILL</c> lists.</summary>
    public static void AssertSameSkills(IReadOnlyList<Skill> expected, IReadOnlyList<Skill> actual) =>
        Assert.Equal(expected, actual);

    /// <summary>
    /// Compares two skill-adjustment lists, including the blitted table.
    /// </summary>
    /// <remarks>
    /// <b>The table is the point.</b> It is a <c>byte[]</c>, so record equality would compare the
    /// reference and pass on two adjustments with completely different contents — and the four
    /// families write 50, 80, 2 and 0 bytes there, which is exactly the thing most likely to be
    /// wrong.
    /// </remarks>
    public static void AssertSameAdjustments(IReadOnlyList<BaseclassSkillAdjustment> expected,
                                             IReadOnlyList<BaseclassSkillAdjustment> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].SkillId, actual[i].SkillId);
            Assert.Equal(expected[i].SourceId, actual[i].SourceId);
            Assert.Equal(expected[i].AdjustmentType, actual[i].AdjustmentType);
            Assert.Equal(expected[i].AdjustmentTable, actual[i].AdjustmentTable);
            Assert.Equal(expected[i].SpecialAbilityName, actual[i].SpecialAbilityName);
            Assert.Equal(expected[i].ScriptName, actual[i].ScriptName);
        }
    }
}
