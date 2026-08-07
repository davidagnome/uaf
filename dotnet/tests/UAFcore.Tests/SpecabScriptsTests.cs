using UAF.Data;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the walk every named script hook goes through.</summary>
public class SpecabScriptsTests
{
    /// <summary>An ability whose <paramref name="script"/> hook returns <paramref name="answer"/>.</summary>
    private static SpecialAbility Ability(string name, string script, string answer) =>
        new(name, [new SpecialAbilityEntry(script, $"$RETURN \"{answer}\";",
                                           SpecialAbilityEntryKind.Script)]);

    private static GlobalScripts Scripts(params SpecialAbility[] abilities) => new(abilities);

    private static IReadOnlyList<SpecabPair> Carrying(params string[] names) =>
        [.. names.Select(n => new SpecabPair(n, string.Empty))];

    private static GpdlUnhostedEnvironment Host(string seed = "")
    {
        var host = new GpdlUnhostedEnvironment();
        host.SetHookParam(GpdlHookParameters.ResultSlot, seed);
        return host;
    }

    private static string Run(IReadOnlyList<SpecabPair> carrying, GlobalScripts scripts,
                              GpdlUnhostedEnvironment host, ScriptCallback? callback = null,
                              string hook = "HOOK") =>
        SpecabScripts.Run(carrying, hook, scripts, host, callback ?? ScriptCallbacks.RunAll);

    // ---- no scripts at all ---------------------------------------------------------------------

    [Fact]
    public void A_record_with_no_abilities_leaves_the_seed_alone()
    {
        // The whole basis of the engine's script-backed defaults: a design that overrides nothing
        // gets back exactly what the caller put in.
        var host = Host("1");

        Assert.Equal("1", Run(Carrying(), Scripts(), host));
        Assert.Equal("1", host.GetHookParam(GpdlHookParameters.ResultSlot));
    }

    [Fact]
    public void An_ability_the_design_does_not_define_is_skipped()
    {
        var host = Host("1");

        Assert.Equal("1", Run(Carrying("Nonexistent"), Scripts(), host));
    }

    [Fact]
    public void An_ability_with_no_script_under_this_name_is_skipped()
    {
        var host = Host("1");
        var scripts = Scripts(Ability("Ward", "OTHER_HOOK", "N"));

        Assert.Equal("1", Run(Carrying("Ward"), scripts, host));
    }

    // ---- run-all -------------------------------------------------------------------------------

    [Fact]
    public void One_script_overwrites_the_seed()
    {
        var host = Host("1");
        var scripts = Scripts(Ability("Ward", "HOOK", "N"));

        Assert.Equal("N", Run(Carrying("Ward"), scripts, host));
    }

    [Fact]
    public void With_several_scripts_the_last_one_wins()
    {
        // RunAll never stops the walk and never rewrites the answer, so each script simply
        // overwrites hook parameter 0 in turn.
        var host = Host("1");
        var scripts = Scripts(Ability("First", "HOOK", "A"), Ability("Second", "HOOK", "B"));

        Assert.Equal("B", Run(Carrying("First", "Second"), scripts, host));
    }

    [Fact]
    public void The_records_own_order_decides_which_runs_last()
    {
        var host = Host();
        var scripts = Scripts(Ability("First", "HOOK", "A"), Ability("Second", "HOOK", "B"));

        Assert.Equal("A", Run(Carrying("Second", "First"), scripts, host));
    }

    [Fact]
    public void Run_all_never_blanks_an_exhausted_search()
    {
        // Its ENDOFSCRIPTS arm -- which would blank the result -- sits below an unconditional
        // return and is dead code.
        string result = "kept";

        Assert.Equal(ScriptCallbackResult.Continue,
                     ScriptCallbacks.RunAll(ScriptCallbackKind.EndOfScripts, ref result));
        Assert.Equal("kept", result);
    }

    // ---- look-for-char --------------------------------------------------------------------------

    [Fact]
    public void Look_for_char_stops_at_the_first_matching_answer()
    {
        var host = Host();
        var scripts = Scripts(Ability("First", "HOOK", "N"), Ability("Second", "HOOK", "Y"));

        Assert.Equal("N", Run(Carrying("First", "Second"), scripts, host,
                              ScriptCallbacks.LookForChar("YN")));
    }

    [Fact]
    public void Look_for_char_trims_the_answer_to_the_one_character()
    {
        // A script answering in a sentence still comes back as a single letter, so the caller's
        // result[0] test works on it.
        var host = Host();
        var scripts = Scripts(Ability("Ward", "HOOK", "NO, THE WARD HOLDS"));

        Assert.Equal("N", Run(Carrying("Ward"), scripts, host,
                              ScriptCallbacks.LookForChar("YN")));
    }

    [Fact]
    public void Look_for_char_finds_the_character_anywhere_in_the_answer()
    {
        // FindOneOf, not a prefix test -- and the first one in the ANSWER wins, not the first in
        // the wanted set.
        var host = Host();
        var scripts = Scripts(Ability("Ward", "HOOK", "IT IS NOT YOURS"));

        Assert.Equal("N", Run(Carrying("Ward"), scripts, host,
                              ScriptCallbacks.LookForChar("YN")));
    }

    [Fact]
    public void Look_for_char_skips_an_answer_that_matches_nothing()
    {
        var host = Host();
        var scripts = Scripts(Ability("First", "HOOK", "0"), Ability("Second", "HOOK", "Y"));

        Assert.Equal("Y", Run(Carrying("First", "Second"), scripts, host,
                              ScriptCallbacks.LookForChar("YN")));
    }

    [Fact]
    public void An_ordinary_word_containing_a_wanted_letter_is_read_as_an_answer()
    {
        // FindOneOf scans the whole string, so a script answering "MAYBE" is taken as "Y" -- the
        // trap in writing a hook that returns prose. Found by writing a test fixture that meant
        // to match nothing and matched.
        var host = Host();
        var scripts = Scripts(Ability("Ward", "HOOK", "MAYBE"));

        Assert.Equal("Y", Run(Carrying("Ward"), scripts, host,
                              ScriptCallbacks.LookForChar("YN")));
    }

    [Fact]
    public void Look_for_char_blanks_an_exhausted_search()
    {
        // Which is the whole difference from RunAll, and what lets a caller chain to the next
        // source on an empty answer.
        var host = Host("1");
        var scripts = Scripts(Ability("Ward", "HOOK", "0"));

        Assert.Equal("", Run(Carrying("Ward"), scripts, host, ScriptCallbacks.LookForChar("YN")));
    }

    [Fact]
    public void Look_for_char_leaves_the_seed_when_there_were_no_scripts_at_all()
    {
        // CBF_DEFAULT falls to the switch's default arm, which continues without touching the
        // result -- so "no scripts" and "scripts that all declined" end differently.
        var host = Host("1");

        Assert.Equal("1", Run(Carrying(), Scripts(), host, ScriptCallbacks.LookForChar("YN")));
    }

    // ---- the collection limit -------------------------------------------------------------------

    [Fact]
    public void Abilities_past_the_limit_are_skipped_rather_than_stopping_the_scan()
    {
        // The reference's `continue` keeps scanning, so which are dropped depends on the order the
        // record lists them -- here the first twenty run and the twenty-first does not.
        var abilities = Enumerable.Range(0, 21)
                                  .Select(i => Ability($"A{i}", "HOOK", $"{i}"))
                                  .ToArray();

        var carrying = Carrying([.. abilities.Select(a => a.Name)]);
        var host = Host();

        Assert.Equal(20, SpecabScripts.MaxScripts);
        Assert.Equal("19", Run(carrying, Scripts(abilities), host));
    }

    // ---- the block overload ---------------------------------------------------------------------

    [Fact]
    public void A_record_with_no_block_at_all_runs_nothing()
    {
        var host = Host("1");

        Assert.Equal("1", SpecabScripts.Run((SpecabBlock?)null, "HOOK", Scripts(), host,
                                            ScriptCallbacks.RunAll));
    }
}
