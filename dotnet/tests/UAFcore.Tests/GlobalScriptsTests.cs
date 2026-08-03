using UAF.Data;
using UAF.Scripting;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Running a design's GPDL scripts by name (<c>RunGlobalScript</c>).
/// </summary>
/// <remarks>
/// The bridge every hook in the port has been waiting on: turning undead's <c>TURN_ATTEMPT</c>,
/// <c>WHO_TRIES</c>'s <c>Attempt</c> veto, scripted teleporter destinations, combat placement and
/// two of the logic block's input types all call it.
/// </remarks>
public class GlobalScriptsTests
{
    private static GlobalScripts Scripts(params string[] lines) =>
        new(SpecialAbilitiesFile.Parse(lines));

    private static GlobalScripts WithScript(string ability, string script, string source) =>
        Scripts("\\(BEGIN)", $"name = {ability}", $"[{script}] = {source}", "\\(END)");

    // ---- lookup ----------------------------------------------------------------------------------

    [Fact]
    public void A_designs_script_is_found_by_ability_then_by_script_name()
    {
        var scripts = WithScript("MyAbility", "Ability", "$RETURN \"yes\";");

        Assert.True(scripts.Has("MyAbility", "Ability"));
        Assert.False(scripts.Has("MyAbility", "Other"));
        Assert.False(scripts.Has("Other", "Ability"));
    }

    [Fact]
    public void There_is_exactly_one_built_in_default()
    {
        // Not a table that grew -- a single entry. So a hook with no design script and no default
        // simply has nothing, which is why TeleporterDestinations only works where authored.
        var single = Assert.Single(GlobalScripts.Defaults);

        Assert.Equal(("CombatPlacement", "PlaceMonsterFar"), (single.Ability, single.Script));
        Assert.True(Scripts().Has("CombatPlacement", "PlaceMonsterFar"));
        Assert.False(Scripts().Has("TeleporterDestinations", "Anything"));
    }

    [Fact]
    public void An_ability_the_design_defines_shuts_the_defaults_out_entirely()
    {
        // The reference reaches the defaults only in its `pSpecAb == NULL` branch, so the fallback
        // is per ABILITY, not per script. A design defining CombatPlacement without
        // PlaceMonsterFar loses the built-in rather than inheriting it.
        var scripts = WithScript("CombatPlacement", "SomethingElse", "$RETURN \"1\";");

        Assert.False(scripts.Has("CombatPlacement", "PlaceMonsterFar"));
    }

    // ---- compiling ---------------------------------------------------------------------------------

    [Fact]
    public void A_script_is_wrapped_before_it_is_compiled()
    {
        // The source is a bare statement list; the wrapper makes it a function called SA, which is
        // also the entry point executed.
        Assert.Equal("$PUBLIC $FUNC SA(){", GlobalScripts.FrontEnd);
        Assert.Equal("SA", GlobalScripts.EntryPoint);
        Assert.StartsWith("\n", GlobalScripts.BackEnd);   // or a trailing // eats the closing brace

        Assert.NotNull(WithScript("A", "S", "$RETURN \"1\";").Compile("A", "S"));
    }

    [Fact]
    public void A_script_that_does_not_compile_is_cached_as_a_failure()
    {
        // The reference flips the entry to SPECAB_SCRIPTERROR and never retries, so a design with
        // a broken script pays the error once rather than once per invocation.
        var scripts = WithScript("A", "S", "$THIS IS NOT GPDL(((");

        Assert.Null(scripts.Compile("A", "S"));
        Assert.NotEmpty(scripts.LastErrors);
        Assert.Null(scripts.Compile("A", "S"));
    }

    [Fact]
    public void A_missing_script_compiles_to_nothing_without_complaint()
    {
        Assert.Null(Scripts().Compile("Nothing", "Here"));
    }

    // ---- running -----------------------------------------------------------------------------------

    [Fact]
    public void A_script_runs_and_its_result_lands_in_hook_parameter_zero()
    {
        var host = new GpdlUnhostedEnvironment();
        var scripts = WithScript("A", "S", "$RETURN \"42\";");

        Assert.Equal("42", scripts.Run("A", "S", host));
        Assert.Equal("42", host.GetHookParam(GpdlHookParameters.ResultSlot));
    }

    [Fact]
    public void A_script_can_read_the_parameters_the_caller_left()
    {
        // This is how a hook is given its arguments -- $EVENT_WhoTries_Attempt reads slots 5 and 6.
        var host = new GpdlUnhostedEnvironment();
        host.SetHookParam(5, "Strength");

        // A bare integer: `#` belongs to the numeric COMPARISON operators (`>=#`), not to a
        // literal, so `$GET_HOOK_PARAM(#5)` is a syntax error.
        Assert.Equal("Strength", WithScript("A", "S", "$RETURN $GET_HOOK_PARAM(5);")
                                     .Run("A", "S", host));
    }

    [Fact]
    public void A_missing_script_returns_whatever_slot_zero_already_held()
    {
        // Not an error: the reference's else arm returns slot 0 as it stands, so a design that
        // overrides nothing keeps the caller's own default.
        var host = new GpdlUnhostedEnvironment();
        host.SetHookParam(GpdlHookParameters.ResultSlot, "caller default");

        Assert.Equal("caller default", Scripts().Run("Nothing", "Here", host));
    }

    // ---- the hook-parameter block ------------------------------------------------------------------

    [Fact]
    public void Setting_a_hook_parameter_returns_its_previous_contents()
    {
        // A swap, not a setter (GPDLexec.cpp:3213) -- a script written as though this returned
        // nothing leaves a value on the stack.
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal("", host.SetHookParam(3, "first"));
        Assert.Equal("first", host.SetHookParam(3, "second"));
        Assert.Equal("second", host.GetHookParam(3));
    }

    [Fact]
    public void The_block_is_ten_slots()
    {
        Assert.Equal(10, GpdlHookParameters.Count);

        var host = new GpdlUnhostedEnvironment();
        host.SetHookParam(9, "last");

        Assert.Equal("last", host.GetHookParam(9));
        Assert.Equal("", host.GetHookParam(10));
    }

    [Fact]
    public void An_index_off_either_end_reads_empty_rather_than_off_the_array()
    {
        // The reference guards only the upper bound on the read (GPDLexec.cpp:3198) where its
        // write guards both, so a negative index reads off the front. C# cannot reproduce that.
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal("", host.GetHookParam(-1));
        Assert.Equal("", host.GetHookParam(99));
        Assert.Equal("", host.SetHookParam(-1, "ignored"));
    }

    [Fact]
    public void The_swap_is_reachable_from_a_script()
    {
        var host = new GpdlUnhostedEnvironment();
        host.SetHookParam(2, "old");

        Assert.Equal("old", WithScript("A", "S", "$RETURN $SET_HOOK_PARAM(2, \"new\");")
                                .Run("A", "S", host));
        Assert.Equal("new", host.GetHookParam(2));
    }
}
