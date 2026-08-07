using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>Covers the ambient actors a script reads its contexts from.</summary>
public class GpdlScriptContextTests
{
    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        string source = "$PUBLIC $FUNC f() { " + body + " } f;";
        Assert.True(compiler.Compile(source) == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    // ---- the stack -------------------------------------------------------------------------------

    [Fact]
    public void A_frame_holds_what_was_set_on_it()
    {
        var context = new GpdlScriptContext();
        using var frame = context.Push();

        context.Set(GpdlContext.Attacker, "hero");

        Assert.Equal("hero", context.Get(GpdlContext.Attacker));
    }

    [Fact]
    public void Closing_a_frame_takes_its_actors_with_it()
    {
        var context = new GpdlScriptContext();

        using (context.Push())
        {
            context.Set(GpdlContext.Attacker, "hero");
        }

        Assert.Equal(0, context.Depth);
        Assert.Equal("", context.Get(GpdlContext.Attacker));
    }

    [Fact]
    public void A_new_frame_inherits_nothing_from_the_one_below()
    {
        // The reference's constructor nulls every field rather than copying, which is why the
        // hooks set the same two or three contexts over and over.
        var context = new GpdlScriptContext();

        using var outer = context.Push();
        context.Set(GpdlContext.Attacker, "hero");

        using (context.Push())
        {
            Assert.Equal("", context.Get(GpdlContext.Attacker));
        }

        // And the outer frame is untouched by what the inner one did or did not have.
        Assert.Equal("hero", context.Get(GpdlContext.Attacker));
    }

    [Fact]
    public void An_inner_frame_shadows_rather_than_replaces()
    {
        var context = new GpdlScriptContext();

        using var outer = context.Push();
        context.Set(GpdlContext.Target, "outer");

        using (context.Push())
        {
            context.Set(GpdlContext.Target, "inner");
            Assert.Equal("inner", context.Get(GpdlContext.Target));
        }

        Assert.Equal("outer", context.Get(GpdlContext.Target));
    }

    [Fact]
    public void The_four_contexts_are_independent()
    {
        var context = new GpdlScriptContext();
        using var frame = context.Push();

        context.Set(GpdlContext.Attacker, "a");
        context.Set(GpdlContext.Target, "t");
        context.Set(GpdlContext.Combatant, "c");
        context.Set(GpdlContext.MonsterType, "m");

        Assert.Equal("a", context.Get(GpdlContext.Attacker));
        Assert.Equal("t", context.Get(GpdlContext.Target));
        Assert.Equal("c", context.Get(GpdlContext.Combatant));
        Assert.Equal("m", context.Get(GpdlContext.MonsterType));
    }

    [Fact]
    public void Setting_with_no_frame_open_is_ignored_rather_than_throwing()
    {
        var context = new GpdlScriptContext();

        context.Set(GpdlContext.Attacker, "hero");

        Assert.Equal("", context.Get(GpdlContext.Attacker));
    }

    [Fact]
    public void Popping_with_nothing_open_is_not_an_error()
    {
        var context = new GpdlScriptContext();

        context.Pop();

        Assert.Equal(0, context.Depth);
    }

    // ---- the missing-context complaints -----------------------------------------------------------

    [Fact]
    public void A_context_nobody_set_is_recorded_rather_than_silently_empty()
    {
        // The reference puts an error box in front of the player and carries on with "". There is
        // no dialog here, so the complaint is collected -- a script reaching for a context nobody
        // set is broken in a way worth surfacing.
        var context = new GpdlScriptContext();

        Assert.Equal("", context.Get(GpdlContext.Target));

        Assert.Equal(["$TargetContext() called when no target context exists"], context.Missing);
    }

    [Fact]
    public void Each_context_has_its_own_complaint()
    {
        Assert.Equal("$AttackerContext() called when no attacker context exists",
                     GpdlScriptContext.MessageFor(GpdlContext.Attacker));
        Assert.Equal("$CombatantContext() called when no combatant context exists",
                     GpdlScriptContext.MessageFor(GpdlContext.Combatant));
        Assert.Equal("$MonsterTypeContext() called when no monster type context exists",
                     GpdlScriptContext.MessageFor(GpdlContext.MonsterType));
    }

    // ---- through the VM ---------------------------------------------------------------------------

    /// <summary>Echoes the actor it is handed, so a context's result is visible.</summary>
    private sealed class Echoing : GpdlUnhostedEnvironment
    {
        public override string CombatantState(string actor) => actor;
    }

    [Theory]
    [InlineData("$AttackerContext", GpdlContext.Attacker)]
    [InlineData("$TargetContext", GpdlContext.Target)]
    [InlineData("$CombatantContext", GpdlContext.Combatant)]
    public void Each_actor_context_reads_its_own_slot(string call, GpdlContext which)
    {
        // These three are actor-typed, so they cannot be returned directly -- they have to feed a
        // call whose parameter wants an actor.
        var host = new Echoing();
        using var frame = host.Context.Push();
        host.Context.Set(which, "wanted");

        Assert.Equal("wanted", Run($"""$RETURN $GetCombatantState({call}());""", host));
    }

    [Fact]
    public void The_monster_type_context_is_a_plain_string_not_an_actor()
    {
        // It pushes pMonstertypeContext->monsterID -- a database id, not a combatant -- so its
        // type flag is 0 and it can be returned like any other string. The other three cannot.
        var host = new Echoing();
        using var frame = host.Context.Push();
        host.Context.Set(GpdlContext.MonsterType, "orc");

        Assert.Equal("orc", Run("""$RETURN $MonsterTypeContext();""", host));
    }

    [Fact]
    public void A_script_asking_for_a_context_nobody_set_gets_nothing()
    {
        var host = new Echoing();

        Assert.Equal("", Run("""$RETURN $GetCombatantState($AttackerContext());""", host));
        Assert.Single(host.Context.Missing);
    }

    // ---- the ability that is running --------------------------------------------------------------

    [Fact]
    public void A_script_can_read_the_ability_that_is_running_it()
    {
        var host = new Echoing();
        using var frame = host.Context.Push();
        host.Context.SetAbility("Regeneration", "3");

        Assert.Equal("Regeneration", Run("""$RETURN $SA_NAME();""", host));
        Assert.Equal("3", Run("""$RETURN $SA_PARAM_GET();""", host));
    }

    [Fact]
    public void With_no_ability_running_both_answer_the_sentinel()
    {
        // A five-character sentinel, not an empty string -- which is how a script tells "no such
        // ability" from "the parameter is blank".
        var host = new Echoing();
        using var frame = host.Context.Push();

        Assert.Equal("-?-?-", Run("""$RETURN $SA_NAME();""", host));
        Assert.Equal("-?-?-", Run("""$RETURN $SA_PARAM_GET();""", host));
        Assert.Equal("-?-?-", GpdlScriptContext.NoSuchAbility);
    }

    [Fact]
    public void Setting_the_parameter_yields_what_it_was_given()
    {
        // The reference pushes the value back, where the character and party setters push the
        // empty string -- so this one setter is usable as an expression.
        var host = new Echoing();
        using var frame = host.Context.Push();
        host.Context.SetAbility("Regeneration", "3");

        Assert.Equal("7", Run("""$RETURN $SA_PARAM_SET("7");""", host));
        Assert.Equal("7", host.Context.AbilityParameter);
    }

    [Fact]
    public void A_blank_parameter_is_not_the_sentinel()
    {
        var host = new Echoing();
        using var frame = host.Context.Push();
        host.Context.SetAbility("Regeneration", "");

        Assert.Equal("", Run("""$RETURN $SA_PARAM_GET();""", host));
    }

    [Fact]
    public void Removing_the_running_ability_yields_its_value()
    {
        var host = new Echoing();
        using var frame = host.Context.Push();
        host.Context.SetAbility("Regeneration", "3");

        Assert.Equal("3", Run("""$RETURN $SA_REMOVE();""", host));
        Assert.Equal(["Regeneration"], host.Context.Removed);
        Assert.Equal("-?-?-", host.Context.AbilityName);
    }

    [Fact]
    public void Removing_when_nothing_is_running_answers_the_sentinel()
    {
        var host = new Echoing();
        using var frame = host.Context.Push();

        Assert.Equal("-?-?-", Run("""$RETURN $SA_REMOVE();""", host));
        Assert.Empty(host.Context.Removed);
    }

    // ---- where the script came from ---------------------------------------------------------------

    [Theory]
    [InlineData(GpdlScriptSource.Class, "CLASS")]
    [InlineData(GpdlScriptSource.Spell, "SPELL")]
    [InlineData(GpdlScriptSource.Combatant, "COMBATANT")]
    [InlineData(GpdlScriptSource.EventTrigger, "EVENT TRIGGER")]
    [InlineData(GpdlScriptSource.Unknown, "Unknown")]
    public void The_source_type_is_a_word_a_design_compares_against(GpdlScriptSource source,
                                                                    string word)
    {
        // "EVENT TRIGGER" keeps its space and "Unknown" its capitalisation: these literals are the
        // wire format as far as a script is concerned.
        var host = new Echoing();
        host.Context.Source = source;

        Assert.Equal(word, Run("""$RETURN $SA_SOURCE_TYPE();""", host));
        Assert.Equal(word, GpdlScriptContext.NameOf(source));
    }

    [Fact]
    public void The_source_name_comes_off_the_context()
    {
        var host = new Echoing();
        host.Context.SourceName = "Bless";

        Assert.Equal("Bless", Run("""$RETURN $SA_SOURCE_NAME();""", host));
    }
}
