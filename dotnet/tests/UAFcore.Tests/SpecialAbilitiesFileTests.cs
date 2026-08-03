using UAF.Data;

namespace UAFcore.Tests;

/// <summary>
/// A design's <c>specialAbilities.txt</c> — where its GPDL scripts live.
/// </summary>
/// <remarks>
/// Verified against three real files carrying 1,131 abilities between them, which is what makes
/// this stronger than the event readers no shipped design exercises.
/// </remarks>
public class SpecialAbilitiesFileTests
{
    private static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    private static List<SpecialAbility> Real(string relative)
    {
        var root = RepoRoot();
        string? path = root is null
            ? null
            : Path.Combine(root.FullName, Path.Combine(relative.Split('/')));

        return path is not null && File.Exists(path) ? SpecialAbilitiesFile.Load(path) : [];
    }

    private static List<SpecialAbility> Parse(params string[] lines) =>
        SpecialAbilitiesFile.Parse(lines);

    // ---- the shape -------------------------------------------------------------------------------

    [Fact]
    public void An_object_runs_from_begin_to_end_and_takes_its_identity_from_name()
    {
        var abilities = Parse("\\(BEGIN)", "name = Bless", "cost = 5", "\\(END)");

        var bless = Assert.Single(abilities);
        Assert.Equal("Bless", bless.Name);

        // `name` is pulled out rather than kept as an entry.
        Assert.Equal(["cost"], bless.Entries.Select(e => e.Name));
    }

    [Fact]
    public void Everything_before_the_first_begin_is_discarded()
    {
        // The loader enumerates objects from 1, and object 0 is whatever preceded the first
        // delimiter -- which is how the file's own `//` header block survives a parser whose
        // comment marker is `\\`.
        var abilities = Parse("// Special Abilities database file", "// Sample:",
                              "\\(BEGIN)", "name = Bless", "\\(END)");

        Assert.Equal("Bless", Assert.Single(abilities).Name);
    }

    [Fact]
    public void An_object_with_no_name_is_dropped_and_the_file_keeps_reading()
    {
        var abilities = Parse("\\(BEGIN)", "cost = 5", "\\(END)",
                              "\\(BEGIN)", "name = Bless", "\\(END)");

        Assert.Equal(["Bless"], abilities.Select(a => a.Name));
    }

    [Fact]
    public void The_comment_marker_is_two_backslashes_and_not_two_slashes()
    {
        // IsComment tests for `\\` (ItemDB.cpp:3116). A `//` line INSIDE an object is data, not a
        // comment -- it simply has no `=` and is skipped for that reason instead.
        Assert.True(SpecialAbilitiesFile.IsComment("\\\\ a real comment"));
        Assert.False(SpecialAbilitiesFile.IsComment("// not a comment here"));

        var abilities = Parse("\\(BEGIN)", "name = Bless", "\\\\ dropped", "cost = 5", "\\(END)");

        Assert.Equal(["cost"], Assert.Single(abilities).Entries.Select(e => e.Name));
    }

    // ---- entry kinds -----------------------------------------------------------------------------

    [Fact]
    public void The_brackets_around_a_name_decide_what_the_entry_is()
    {
        var ability = Assert.Single(Parse(
            "\\(BEGIN)", "name = Test",
            "[Activation Script] = $RETURN 1;",
            "(parameterA) = 5",
            "<table> = 1,2,3",
            "plain = value",
            "\\(END)"));

        Assert.Equal(
            [SpecialAbilityEntryKind.Script, SpecialAbilityEntryKind.Variable,
             SpecialAbilityEntryKind.IntegerTable, SpecialAbilityEntryKind.Constant],
            ability.Entries.Select(e => e.Kind));

        // The brackets are stripped from the stored name.
        Assert.Equal(["Activation Script", "parameterA", "table", "plain"],
                     ability.Entries.Select(e => e.Name));
    }

    [Fact]
    public void A_two_character_bracket_pair_is_a_constant_rather_than_an_empty_script()
    {
        // The reference requires three characters before it will strip, so "[]" names a constant.
        var ability = Assert.Single(Parse("\\(BEGIN)", "name = Test", "[] = x", "\\(END)"));

        var entry = Assert.Single(ability.Entries);
        Assert.Equal(SpecialAbilityEntryKind.Constant, entry.Kind);
        Assert.Equal("[]", entry.Name);
    }

    [Fact]
    public void Splitting_is_on_the_first_equals_with_no_escape_handling()
    {
        // This decoder is a plain Find('='), unlike the general config splitter which honours
        // backslash escapes. A value containing an `=` keeps it; a NAME containing one does not.
        var ability = Assert.Single(Parse(
            "\\(BEGIN)", "name = Test", "[s] = a = b = c", "\\(END)"));

        Assert.Equal("a = b = c", Assert.Single(ability.Entries).Value);
    }

    // ---- continuations ---------------------------------------------------------------------------

    [Fact]
    public void A_leading_hyphen_continues_the_previous_line_and_is_dropped()
    {
        var ability = Assert.Single(Parse(
            "\\(BEGIN)", "name = Test",
            "[Script] = $VAR x;",
            "-x = 1;",
            "-$RETURN x;",
            "\\(END)"));

        Assert.Equal("$VAR x;\r\nx = 1;\r\n$RETURN x;", ability.Script("Script"));
    }

    [Fact]
    public void Continuations_are_joined_with_real_newlines()
    {
        // What comes out is GPDL source the compiler sees with line breaks in it. Joining with
        // spaces would merge a trailing `//` comment into the statement after it.
        string? script = Assert.Single(Parse(
            "\\(BEGIN)", "name = Test",
            "[Script] = $VAR me; // who is trying",
            "-$RETURN me;",
            "\\(END)")).Script("Script");

        Assert.Contains("\r\n", script);
        Assert.EndsWith("$RETURN me;", script);
    }

    // ---- lookup ----------------------------------------------------------------------------------

    [Fact]
    public void Scripts_are_found_by_name_case_sensitively()
    {
        var ability = Assert.Single(Parse(
            "\\(BEGIN)", "name = Test", "[Ability] = $RETURN 1;", "\\(END)"));

        Assert.Equal("$RETURN 1;", ability.Script("Ability"));
        Assert.Null(ability.Script("ability"));
        Assert.Null(ability.Script("Missing"));
    }

    [Fact]
    public void A_non_script_entry_is_not_returned_as_a_script()
    {
        var ability = Assert.Single(Parse(
            "\\(BEGIN)", "name = Test", "(Ability) = 5", "\\(END)"));

        Assert.Null(ability.Script("Ability"));
        Assert.Equal("5", ability.Find("Ability")!.Value);
    }

    [Fact]
    public void An_object_missing_its_end_is_closed_by_the_next_begin()
    {
        // Every \(BEGIN) starts a new object number and each object's lines are decoded on their
        // own, so a missing closer costs nothing. DefaultDesign relies on this: 182 openers
        // against 181 closers, and requiring the closer silently loses one of its abilities.
        var abilities = Parse("\\(BEGIN)", "name = First", "cost = 1",
                              "\\(BEGIN)", "name = Second", "\\(END)");

        Assert.Equal(["First", "Second"], abilities.Select(a => a.Name));
        Assert.Equal(["cost"], abilities[0].Entries.Select(e => e.Name));
    }

    [Fact]
    public void An_object_left_open_at_end_of_file_is_kept_too()
    {
        var abilities = Parse("\\(BEGIN)", "name = Last", "cost = 1");

        Assert.Equal(["Last"], abilities.Select(a => a.Name));
    }

    // ---- the real files --------------------------------------------------------------------------

    [Theory]
    [InlineData("reference/SomethingWild.dsn/Data/specialAbilities.txt", 441)]
    [InlineData("reference/dc-default/databases/specialAbilities.txt", 508)]
    [InlineData("src/UAFWinEd/DefaultDesign.dsn/Data/specialAbilities.txt", 182)]
    public void Every_ability_in_a_real_file_is_read(string relative, int expected)
    {
        var abilities = Real(relative);
        if (abilities.Count == 0)
        {
            return;                                      // gitignored fixture absent
        }

        // The count is the file's own `\(BEGIN)` delimiters, so a parser that lost objects or
        // invented them shows up here immediately. Note dc-default's 508 exceeds its 507 lines
        // matching `^name = ` exactly: one object spaces the key differently, and the reference
        // trims before comparing.
        Assert.Equal(expected, abilities.Count);
        Assert.All(abilities, a => Assert.NotEmpty(a.Name));
    }

    [Fact]
    public void A_real_design_defines_the_who_tries_hook_this_port_could_not_run()
    {
        // SomethingWild carries $EVENT_WhoTries_Attempt outright -- the hook EventWhoTries names
        // as unrunnable for want of a script bridge. This is the source it needs.
        var abilities = Real("reference/SomethingWild.dsn/Data/specialAbilities.txt");
        if (abilities.Count == 0)
        {
            return;
        }

        var hook = abilities.FirstOrDefault(a => a.Name == "$EVENT_WhoTries_Attempt");

        Assert.NotNull(hook);
        Assert.NotNull(hook!.Script("Ability"));
        Assert.Contains("$GET_HOOK_PARAM", hook.Script("Ability")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Real_scripts_come_back_as_multi_line_source()
    {
        var abilities = Real("reference/SomethingWild.dsn/Data/specialAbilities.txt");
        if (abilities.Count == 0)
        {
            return;
        }

        var scripts = abilities
            .SelectMany(a => a.Entries)
            .Where(e => e.Kind == SpecialAbilityEntryKind.Script)
            .ToList();

        Assert.NotEmpty(scripts);

        // If continuations were not being joined, every script would be one line.
        Assert.Contains(scripts, s => s.Value.Contains("\r\n", StringComparison.Ordinal));
    }
}
