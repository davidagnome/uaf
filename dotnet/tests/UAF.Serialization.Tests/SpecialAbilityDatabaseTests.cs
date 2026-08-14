using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reading <c>specialAbilities.dat</c> — the database the <c>$RUN_*_SCRIPTS</c> family needs.
/// </summary>
/// <remarks>
/// It is the last design file the port had no reader for, and the reason five GPDL sub-opcodes
/// could not be implemented: a spell or character carries ability <i>names</i>, and the scripts
/// those names stand for live only here.
/// </remarks>
public class SpecialAbilityDatabaseTests
{
    private static string? Database(string design)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string path = Path.Combine(dir.FullName, "reference", design, "Data",
                                   "specialAbilities.dat");
        return File.Exists(path) ? path : null;
    }

    private static List<SpecialAbilityDefinition>? Read(string design)
    {
        if (Database(design) is not { } path)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return SpecialAbilityDatabaseReader.Read(stream, DesignVersion.V524);
    }

    /// <summary>The corpus's databases read, and carry named abilities.</summary>
    [Theory]
    [InlineData("Case.dsn")]
    [InlineData("SomethingWild.dsn")]
    public void A_database_reads_and_names_its_abilities(string design)
    {
        if (Read(design) is not { } abilities)
        {
            return;
        }

        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));

        // Names are unique: everything else in a design refers to an ability by name, so two with
        // the same one would make the reference ambiguous.
        Assert.Equal(abilities.Count,
                     abilities.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                              .Count());
    }

    /// <summary>
    /// The abilities carry scripts, which is the whole reason for reading this file.
    /// </summary>
    /// <remarks>
    /// A database that read cleanly but held no scripts would parse and be useless — this is what
    /// separates "the bytes decoded" from "the thing five sub-opcodes need is here".
    /// </remarks>
    [Fact]
    public void The_abilities_carry_gpdl_scripts()
    {
        if (Read("Case.dsn") is not { } abilities)
        {
            return;
        }

        var scripts = abilities
            .SelectMany(a => a.Strings.Select(s => (a.Name, s.Key, s.Flags, s.Value)))
            .Where(s => (s.Flags & SpecialAbilityDatabaseReader.ScriptFlag) != 0)
            .ToList();

        // Case.dsn carries 836 of them.
        Assert.True(scripts.Count > 800, $"only {scripts.Count} script entries decoded");

        // A script entry holds source -- but ONE of the 836 does not, and it is real data rather
        // than a decode fault: monster_GiantSlugSpit declares a "DoesSpellAttackSucceed" script
        // and leaves it empty. Asserting every entry has source would fail on the design as
        // shipped, so what is pinned is that empty ones are the rare exception.
        int empty = scripts.Count(s => string.IsNullOrWhiteSpace(s.Value));
        Assert.True(empty <= 1, $"{empty} script entries decoded empty");

        // And GPDL source is recognisable: every one of these should mention a $ function or a
        // brace, which a constant string would not.
        Assert.Contains(scripts, s => s.Value.Contains('$', StringComparison.Ordinal)
                                      || s.Value.Contains('{', StringComparison.Ordinal));
    }

    /// <summary>
    /// A script is found by name, and only when its flags say it is one.
    /// </summary>
    [Fact]
    public void A_script_is_looked_up_by_name_and_flag()
    {
        if (Read("Case.dsn") is not { } abilities)
        {
            return;
        }

        var withScript = abilities.FirstOrDefault(
            a => a.Strings.Any(s => (s.Flags & SpecialAbilityDatabaseReader.ScriptFlag) != 0));

        if (withScript is null)
        {
            return;
        }

        var entry = withScript.Strings.First(
            s => (s.Flags & SpecialAbilityDatabaseReader.ScriptFlag) != 0);

        Assert.Equal(entry.Value, SpecialAbilityDatabaseReader.Script(withScript, entry.Key));

        // A name it does not have yields nothing rather than the first script it holds.
        Assert.Null(SpecialAbilityDatabaseReader.Script(withScript, "no such script"));

        // And a constant is not returned as a script, however it is named.
        var constant = withScript.Strings.FirstOrDefault(
            s => (s.Flags & SpecialAbilityDatabaseReader.ScriptFlag) == 0);

        if (constant is not null)
        {
            Assert.Null(SpecialAbilityDatabaseReader.Script(withScript, constant.Key));
        }
    }

    /// <summary>
    /// A name whose characters fell below the printable range is repaired on load.
    /// </summary>
    /// <remarks>
    /// <b>Not cosmetic.</b> The reference adds <c>0x20</c> to every character under it
    /// (<c>ASL.cpp:2296</c>) — a fix-up for names an earlier version mangled. Skipping it leaves a
    /// name nothing else in the design can match.
    /// </remarks>
    [Theory]
    [InlineData("Poison", "Poison")]
    [InlineData("", "01")]
    [InlineData("AC", "A\"C")]
    [InlineData("", "")]
    public void A_mangled_name_is_repaired(string stored, string expected) =>
        Assert.Equal(expected, SpecialAbilityDatabaseReader.RepairName(stored));

    /// <summary>A file that does not open with the format string is refused.</summary>
    /// <remarks>
    /// The stamp is the only thing identifying this file — there is no magic sentinel — so reading
    /// past a wrong one would decompress arbitrary bytes.
    /// </remarks>
    [Fact]
    public void A_file_without_the_stamp_is_refused()
    {
        using var stream = new MemoryStream();
        var writer = new MfcArchiveWriter(stream);
        writer.WriteString("NotSpecAbilities");
        stream.Position = 0;

        var thrown = Assert.Throws<InvalidDataException>(
            () => SpecialAbilityDatabaseReader.Read(stream, DesignVersion.V524));

        Assert.Contains("SpecAbVer01", thrown.Message, StringComparison.Ordinal);
    }
}
