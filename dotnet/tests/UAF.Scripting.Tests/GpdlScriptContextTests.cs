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

        /// <summary>
        /// An actor producer, since a literal cannot satisfy an actor-typed parameter.
        /// </summary>
        /// <remarks>
        /// <see cref="InCombat"/> has to be true with it: the selector takes an early exit out of
        /// combat that answers the null actor without ever asking the host.
        /// </remarks>
        public override string MostDamaged(GpdlDamageQuery query) => query.ToString();

        /// <inheritdoc cref="MostDamaged"/>
        public override bool InCombat => true;
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

    // ---- other records' abilities -----------------------------------------------------------------

    [Theory]
    [InlineData("$SA_ITEM_GET", GpdlSaRecord.Item)]
    [InlineData("$SA_CHARACTER_GET", GpdlSaRecord.Character)]
    [InlineData("$SA_COMBATANT_GET", GpdlSaRecord.Combatant)]
    [InlineData("$SA_CLASS_GET", GpdlSaRecord.Class)]
    [InlineData("$SA_BASECLASS_GET", GpdlSaRecord.Baseclass)]
    [InlineData("$SA_SPELL_GET", GpdlSaRecord.Spell)]
    [InlineData("$SA_MONSTERTYPE_GET", GpdlSaRecord.MonsterType)]
    [InlineData("$SA_RACE_GET", GpdlSaRecord.Race)]
    [InlineData("$SA_ABILITY_GET", GpdlSaRecord.Ability)]
    public void Each_lookup_reads_its_own_records_list(string call, GpdlSaRecord record)
    {
        // Nine calls of identical shape, which is exactly where a crossed pair hides.
        var host = new Echoing();
        host.Context.SetAbilities(record, new Dictionary<string, string> { ["Ward"] = "7" });

        Assert.Equal("7", Run($"""$RETURN {call}("Ward");""", host));
    }

    [Fact]
    public void A_lookup_on_another_records_list_does_not_see_this_one()
    {
        var host = new Echoing();
        host.Context.SetAbilities(GpdlSaRecord.Item,
                                  new Dictionary<string, string> { ["Ward"] = "7" });

        Assert.Equal("-?-?-", Run("""$RETURN $SA_CLASS_GET("Ward");""", host));
    }

    [Fact]
    public void An_absent_ability_and_an_absent_list_answer_the_same_thing()
    {
        // The reference distinguishes them only in what it logs.
        var host = new Echoing();
        host.Context.SetAbilities(GpdlSaRecord.Item, new Dictionary<string, string>());

        Assert.Equal("-?-?-", Run("""$RETURN $SA_ITEM_GET("Ward");""", host));
        Assert.Equal("-?-?-", Run("""$RETURN $SA_RACE_GET("Ward");""", host));
    }

    [Fact]
    public void A_blank_value_is_not_the_sentinel()
    {
        var host = new Echoing();
        host.Context.SetAbilities(GpdlSaRecord.Item,
                                  new Dictionary<string, string> { ["Ward"] = "" });

        Assert.Equal("", Run("""$RETURN $SA_ITEM_GET("Ward");""", host));
    }

    [Fact]
    public void The_missing_list_complaint_is_made_once_per_process_not_once_per_call()
    {
        // The guard is a `static bool error`, so a design with a broken lookup in a loop gets one
        // line and then silence.
        var context = new GpdlScriptContext();

        context.Ability(GpdlSaRecord.Item, "Ward");
        context.Ability(GpdlSaRecord.Class, "Ward");
        context.Ability(GpdlSaRecord.Race, "Ward");

        Assert.Single(context.MissingLists);
    }

    // ---- naming the record instead of the context -------------------------------------------------

    private static Echoing WithList(GpdlSaRecord record, string who, SpecabList list)
    {
        var host = new Echoing();
        host.AbilityLists[(record, who)] = list;
        return host;
    }

    /// <summary>
    /// An actor produced by a call, since an actor-typed parameter refuses a literal.
    /// </summary>
    /// <remarks>
    /// The character and combatant variants take an actor; the database ones take a plain id
    /// string. Two shapes in what looks like one family, and the compiler is what says so.
    /// </remarks>
    private const string AnActor = "$MOST_DAMAGED_ENEMY()";

    /// <summary>What <see cref="Echoing.MostDamaged"/> answers for that call.</summary>
    private const string ActorName = "MostDamagedEnemy";

    [Theory]
    [InlineData("$GET_CHARACTER_SA", GpdlSaRecord.Character)]
    [InlineData("$GET_COMBATANT_SA", GpdlSaRecord.Combatant)]
    public void Each_actor_typed_getter_reaches_its_own_record(string call, GpdlSaRecord record)
    {
        var host = WithList(record, ActorName,
                            new SpecabList(false, [new KeyValuePair<string, string>("Ward", "7")]));

        Assert.Equal("7", Run($"""$RETURN {call}({AnActor}, "Ward");""", host));
    }

    [Theory]
    [InlineData("$GET_ITEM_SA", GpdlSaRecord.Item)]
    [InlineData("$GET_SPELL_SA", GpdlSaRecord.Spell)]
    [InlineData("$GET_MONSTERTYPE_SA", GpdlSaRecord.MonsterType)]
    [InlineData("$GET_RACE_SA", GpdlSaRecord.Race)]
    [InlineData("$GET_ABILITY_SA", GpdlSaRecord.Ability)]
    [InlineData("$GET_CLASS_SA", GpdlSaRecord.Class)]
    [InlineData("$GET_BASECLASS_SA", GpdlSaRecord.Baseclass)]
    public void Each_database_getter_reaches_its_own_record(string call, GpdlSaRecord record)
    {
        var host = WithList(record, "hero",
                            new SpecabList(false, [new KeyValuePair<string, string>("Ward", "7")]));

        Assert.Equal("7", Run($"""$RETURN {call}("hero", "Ward");""", host));
    }

    [Fact]
    public void A_record_that_names_nothing_answers_the_sentinel()
    {
        // The reference pushes m_string3 without initialising it here, so it answers whatever the
        // last opcode left behind. This VM keeps its temporaries as locals, so it cannot reproduce
        // the garbage -- it answers the sentinel and the divergence is deliberate.
        Assert.Equal("-?-?-",
                     Run($"""$RETURN $GET_CHARACTER_SA({AnActor}, "Ward");""", new Echoing()));
    }

    [Fact]
    public void Setting_a_named_records_ability_yields_the_value()
    {
        var list = new SpecabList();
        var host = WithList(GpdlSaRecord.Character, ActorName, list);

        Assert.Equal("9", Run($"""$RETURN $SET_CHARACTER_SA({AnActor}, "Ward", "9");""", host));
        Assert.Equal("9", list.Get("Ward"));
    }

    [Fact]
    public void The_value_is_popped_before_the_name_and_the_name_before_the_record()
    {
        // Getting this order wrong writes an ability called "9" onto a character called "Ward".
        var list = new SpecabList();
        var host = WithList(GpdlSaRecord.Character, ActorName, list);

        Run($"""$SET_CHARACTER_SA({AnActor}, "Ward", "9");""", host);

        Assert.Equal(["Ward"], list.Abilities.Keys);
    }

    [Fact]
    public void Deleting_a_named_records_ability_yields_what_was_there()
    {
        var list = new SpecabList(false, [new KeyValuePair<string, string>("Ward", "7")]);
        var host = WithList(GpdlSaRecord.Character, ActorName, list);

        Assert.Equal("7", Run($"""$RETURN $DELETE_CHARACTER_SA({AnActor}, "Ward");""", host));
        Assert.Empty(list.Abilities);
    }

    [Fact]
    public void A_script_cannot_write_a_database_records_abilities()
    {
        // The definition is shared by every copy of that item in the design, so the list is
        // read-only -- and the refusal is silent.
        var list = new SpecabList(readOnly: true);
        var host = WithList(GpdlSaRecord.Character, ActorName, list);

        Assert.Equal("9", Run($"""$RETURN $SET_CHARACTER_SA({AnActor}, "Ward", "9");""", host));
        Assert.Empty(list.Abilities);
        Assert.Equal(1, list.Refused);
    }

    [Fact]
    public void Clearing_a_list_puts_it_back_to_absent()
    {
        var context = new GpdlScriptContext();
        context.SetAbilities(GpdlSaRecord.Item,
                             new Dictionary<string, string> { ["Ward"] = "7" });

        Assert.Equal("7", context.Ability(GpdlSaRecord.Item, "Ward"));

        context.SetAbilities(GpdlSaRecord.Item, null);

        Assert.Equal("-?-?-", context.Ability(GpdlSaRecord.Item, "Ward"));
    }
}
