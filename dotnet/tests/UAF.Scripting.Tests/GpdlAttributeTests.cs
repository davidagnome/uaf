using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// Covers the attribute sub-opcodes — the first family of game-state calls the VM can serve.
/// </summary>
/// <remarks>
/// Driven through real GPDL source rather than by poking the interpreter, so the argument order the
/// compiler emits is under test alongside the sub-opcodes themselves.
/// </remarks>
public class GpdlAttributeTests
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

    private static GpdlUnhostedEnvironment Host() => new();

    // ---- reading and writing -------------------------------------------------------------------

    [Fact]
    public void Setting_a_global_attribute_yields_the_value_that_was_set()
    {
        var host = Host();

        Assert.Equal("Win",
            Run("""$RETURN $SET_GLOBAL_ASL("Combat Result", "Win");""", host));
        Assert.Equal("Win", host.Attributes[GpdlAslScope.Global]["Combat Result"]);
    }

    [Fact]
    public void The_key_is_the_first_argument_and_the_value_the_second()
    {
        // GPDL pushes arguments left to right, so the value ends up on top and is popped first.
        // Reading the pops in source order stores the key under the value.
        var host = Host();
        Run("""$SET_GLOBAL_ASL("key", "value");""", host);

        Assert.Equal("value", host.Attributes[GpdlAslScope.Global]["key"]);
        Assert.False(host.Attributes[GpdlAslScope.Global].ContainsKey("value"));
    }

    [Fact]
    public void Reading_an_attribute_that_was_set_gives_it_back()
    {
        var host = Host();

        Assert.Equal("two", Run("""
            $SET_GLOBAL_ASL("chapter", "two");
            $RETURN $GET_GLOBAL_ASL("chapter");
            """, host));
    }

    [Fact]
    public void Reading_an_absent_attribute_gives_the_empty_string()
    {
        // Lookup returns a shared empty string rather than signalling, so a script cannot tell an
        // unset attribute from one set to nothing by reading it.
        Assert.Equal("", Run("""$RETURN $GET_GLOBAL_ASL("never set");""", Host()));
    }

    [Fact]
    public void The_party_store_is_separate_from_the_global_one()
    {
        var host = Host();

        Assert.Equal("party", Run("""
            $SET_GLOBAL_ASL("k", "global");
            $SET_PARTY_ASL("k", "party");
            $RETURN $GET_PARTY_ASL("k");
            """, host));

        Assert.Equal("global", host.Attributes[GpdlAslScope.Global]["k"]);
    }

    // ---- testing and removing ------------------------------------------------------------------

    [Fact]
    public void Existence_is_reported_as_the_vms_own_true_and_false()
    {
        var host = Host();

        Assert.Equal("", Run("""$RETURN $IF_PARTY_ASL("absent");""", host));
        Assert.Equal("1", Run("""
            $SET_PARTY_ASL("present", "x");
            $RETURN $IF_PARTY_ASL("present");
            """, host));
    }

    [Fact]
    public void An_attribute_set_to_nothing_still_exists()
    {
        // The other half of the empty-string trap: reading cannot tell them apart, but asking
        // whether the key exists can.
        var host = Host();

        Assert.Equal("1", Run("""
            $SET_PARTY_ASL("blank", "");
            $RETURN $IF_PARTY_ASL("blank");
            """, host));

        Assert.Equal("", Run("""$RETURN $GET_PARTY_ASL("blank");""", host));
    }

    [Fact]
    public void Deleting_removes_the_attribute()
    {
        var host = Host();

        Run("""
            $SET_PARTY_ASL("gone", "x");
            $DELETE_PARTY_ASL("gone");
            """, host);

        Assert.False(host.Attributes[GpdlAslScope.Party].ContainsKey("gone"));
    }

    [Fact]
    public void Deleting_always_reports_false_even_when_it_removed_something()
    {
        // The push exists to balance the stack -- the reference's own comment is "Must supply a
        // result" -- so a script testing the result of a delete learns nothing from it.
        var host = Host();

        Assert.Equal("", Run("""
            $SET_PARTY_ASL("gone", "x");
            $RETURN $DELETE_PARTY_ASL("gone");
            """, host));

        Assert.Equal("", Run("""$RETURN $DELETE_PARTY_ASL("never there");""", host));
    }

    // ---- per-character stores ------------------------------------------------------------------

    [Fact]
    public void A_characters_attribute_is_kept_under_that_character()
    {
        var host = Host();

        Assert.Equal("wounded", Run("""
            $SET_CHAR_ASL("hero", "mood", "wounded");
            $RETURN $GET_CHAR_ASL("hero", "mood");
            """, host));

        Assert.Equal("wounded", host.CharacterAttributes["hero"]["mood"]);
    }

    [Fact]
    public void Two_characters_do_not_share_a_store()
    {
        var host = Host();

        Assert.Equal("b", Run("""
            $SET_CHAR_ASL("alice", "mood", "a");
            $SET_CHAR_ASL("bob", "mood", "b");
            $RETURN $GET_CHAR_ASL("bob", "mood");
            """, host));

        Assert.Equal("a", host.CharacterAttributes["alice"]["mood"]);
    }

    [Fact]
    public void Reading_a_character_nobody_answers_to_gives_the_empty_string()
    {
        Assert.Equal("", Run("""$RETURN $GET_CHAR_ASL("nobody", "mood");""", Host()));
    }

    [Fact]
    public void If_char_asl_pushes_the_value_rather_than_a_boolean()
    {
        // Despite the name, it is the same call as $GET_CHAR_ASL -- there is no existence check
        // anywhere in it. A script using it as a boolean is testing the value for emptiness.
        var host = Host();

        Assert.Equal("wounded", Run("""
            $SET_CHAR_ASL("hero", "mood", "wounded");
            $RETURN $IF_CHAR_ASL("hero", "mood");
            """, host));
    }

    [Fact]
    public void If_char_asl_on_an_attribute_set_to_nothing_reads_as_false()
    {
        // The consequence of the previous test: an attribute that exists but is empty is
        // indistinguishable from one that was never set, because the value is what comes back.
        var host = Host();

        Assert.Equal("", Run("""
            $SET_CHAR_ASL("hero", "mood", "");
            $RETURN $IF_CHAR_ASL("hero", "mood");
            """, host));
    }

    [Fact]
    public void Setting_a_character_attribute_yields_the_value()
    {
        Assert.Equal("v", Run("""$RETURN $SET_CHAR_ASL("hero", "k", "v");""", Host()));
    }

    // ---- character stats -----------------------------------------------------------------------

    private static GpdlUnhostedEnvironment WithStats(string actor,
                                                     params (GpdlCharStat Stat, string Value)[] stats)
    {
        var host = Host();
        host.CharacterStats[actor] = stats.ToDictionary(s => s.Stat, s => s.Value);
        return host;
    }

    [Fact]
    public void A_characters_name_comes_back_as_a_string()
    {
        var host = WithStats("hero", (GpdlCharStat.Name, "Aldric"));

        Assert.Equal("Aldric", Run("""$RETURN $GET_CHAR_NAME("hero");""", host));
    }

    [Theory]
    [InlineData("$GET_CHAR_HITPOINTS", GpdlCharStat.HitPoints, "7")]
    [InlineData("$GET_CHAR_MAXHITPOINTS", GpdlCharStat.MaxHitPoints, "12")]
    [InlineData("$GET_CHAR_AC", GpdlCharStat.ArmorClass, "5")]
    [InlineData("$GET_CHAR_RDYTOTRAIN", GpdlCharStat.ReadyToTrain, "1")]
    [InlineData("$GET_CHAR_GENDER", GpdlCharStat.Gender, "0")]
    [InlineData("$GET_CHAR_THAC0", GpdlCharStat.Thac0, "18")]
    [InlineData("$GET_CHAR_ADJTHAC0", GpdlCharStat.AdjustedThac0, "15")]
    [InlineData("$GET_CHAR_ADJAC", GpdlCharStat.AdjustedArmorClass, "3")]
    public void Each_stat_call_reaches_its_own_stat(string call, GpdlCharStat stat, string value)
    {
        // Every one of these is the same shape in the reference -- a macro over one accessor -- so
        // what is worth testing is that the sub-opcodes are not crossed.
        var host = WithStats("hero", (stat, value));

        Assert.Equal(value, Run($"""$RETURN {call}("hero");""", host));
    }

    [Fact]
    public void An_integer_stat_arrives_as_text_because_the_stack_holds_nothing_else()
    {
        // A script comparing a stat against a literal is comparing text.
        var host = WithStats("hero", (GpdlCharStat.HitPoints, "7"));

        Assert.Equal("1", Run("""$RETURN $GET_CHAR_HITPOINTS("hero") == "7";""", host));
    }

    [Fact]
    public void A_stat_read_off_nobody_gives_the_empty_string()
    {
        Assert.Equal("", Run("""$RETURN $GET_CHAR_NAME("nobody");""", Host()));
    }

    // ---- the ability scores, in three layers each ------------------------------------------------

    [Theory]
    [InlineData("$GET_CHAR_PERM_STR", GpdlCharStat.PermanentStrength)]
    [InlineData("$GET_CHAR_ADJ_STR", GpdlCharStat.AdjustedStrength)]
    [InlineData("$GET_CHAR_LIMITED_STR", GpdlCharStat.LimitedStrength)]
    [InlineData("$GET_CHAR_PERM_STRMOD", GpdlCharStat.PermanentStrengthMod)]
    [InlineData("$GET_CHAR_ADJ_STRMOD", GpdlCharStat.AdjustedStrengthMod)]
    [InlineData("$GET_CHAR_LIMITED_STRMOD", GpdlCharStat.LimitedStrengthMod)]
    [InlineData("$GET_CHAR_PERM_INT", GpdlCharStat.PermanentIntelligence)]
    [InlineData("$GET_CHAR_ADJ_INT", GpdlCharStat.AdjustedIntelligence)]
    [InlineData("$GET_CHAR_LIMITED_INT", GpdlCharStat.LimitedIntelligence)]
    [InlineData("$GET_CHAR_PERM_WIS", GpdlCharStat.PermanentWisdom)]
    [InlineData("$GET_CHAR_ADJ_WIS", GpdlCharStat.AdjustedWisdom)]
    [InlineData("$GET_CHAR_LIMITED_WIS", GpdlCharStat.LimitedWisdom)]
    [InlineData("$GET_CHAR_PERM_DEX", GpdlCharStat.PermanentDexterity)]
    [InlineData("$GET_CHAR_ADJ_DEX", GpdlCharStat.AdjustedDexterity)]
    [InlineData("$GET_CHAR_LIMITED_DEX", GpdlCharStat.LimitedDexterity)]
    [InlineData("$GET_CHAR_PERM_CON", GpdlCharStat.PermanentConstitution)]
    [InlineData("$GET_CHAR_ADJ_CON", GpdlCharStat.AdjustedConstitution)]
    [InlineData("$GET_CHAR_LIMITED_CON", GpdlCharStat.LimitedConstitution)]
    [InlineData("$GET_CHAR_PERM_CHA", GpdlCharStat.PermanentCharisma)]
    [InlineData("$GET_CHAR_ADJ_CHA", GpdlCharStat.AdjustedCharisma)]
    [InlineData("$GET_CHAR_LIMITED_CHA", GpdlCharStat.LimitedCharisma)]
    public void Every_ability_layer_reaches_its_own_stat(string call, GpdlCharStat stat)
    {
        // Twenty-one sub-opcodes of identical shape, which is exactly the population where a
        // crossed pair would go unnoticed. Each is given a value nothing else has.
        var host = WithStats("hero", (stat, "13"));

        Assert.Equal("13", Run($"""$RETURN {call}("hero");""", host));
    }

    [Fact]
    public void The_three_layers_of_one_score_are_three_separate_reads()
    {
        // Permanent, adjusted and limited are different questions about the same score, and a
        // script asking the wrong one gets a real but different answer.
        var host = WithStats("hero",
                             (GpdlCharStat.PermanentStrength, "18"),
                             (GpdlCharStat.AdjustedStrength, "31"),
                             (GpdlCharStat.LimitedStrength, "25"));

        Assert.Equal("18", Run("""$RETURN $GET_CHAR_PERM_STR("hero");""", host));
        Assert.Equal("31", Run("""$RETURN $GET_CHAR_ADJ_STR("hero");""", host));
        Assert.Equal("25", Run("""$RETURN $GET_CHAR_LIMITED_STR("hero");""", host));
    }

    [Theory]
    [InlineData("$GET_CHAR_AGE", GpdlCharStat.Age)]
    [InlineData("$GET_CHAR_MAXAGE", GpdlCharStat.MaxAge)]
    [InlineData("$GET_CHAR_ENC", GpdlCharStat.Encumbrance)]
    [InlineData("$GET_CHAR_MAXENC", GpdlCharStat.MaxEncumbrance)]
    [InlineData("$GET_CHAR_MAXMOVE", GpdlCharStat.MaxMovement)]
    [InlineData("$GET_CHAR_MORALE", GpdlCharStat.Morale)]
    [InlineData("$GET_CHAR_MAGICRESIST", GpdlCharStat.MagicResistance)]
    [InlineData("$GET_CHAR_DAMAGEBONUS", GpdlCharStat.DamageBonus)]
    [InlineData("$GET_CHAR_HITBONUS", GpdlCharStat.HitBonus)]
    [InlineData("$GET_CHAR_ICON_INDEX", GpdlCharStat.IconIndex)]
    [InlineData("$GET_CHAR_CLASS", GpdlCharStat.Class)]
    [InlineData("$GET_CHAR_UNDEAD", GpdlCharStat.UndeadType)]
    [InlineData("$GET_CHAR_ALIGNMENT", GpdlCharStat.Alignment)]
    [InlineData("$GET_CHAR_STATUS", GpdlCharStat.Status)]
    [InlineData("$GET_CHAR_SIZE", GpdlCharStat.Size)]
    [InlineData("$GET_CHAR_NBRHITDICE", GpdlCharStat.HitDice)]
    [InlineData("$GET_CHAR_NBRATTACKS", GpdlCharStat.NumberOfAttacks)]
    public void The_rest_of_the_character_block_reaches_its_own_stat(string call,
                                                                    GpdlCharStat stat)
    {
        var host = WithStats("hero", (stat, "7"));

        Assert.Equal("7", Run($"""$RETURN {call}("hero");""", host));
    }

    // ---- writing back ----------------------------------------------------------------------------

    [Theory]
    [InlineData("$SET_CHAR_HITPOINTS", GpdlCharStat.HitPoints)]
    [InlineData("$SET_CHAR_MORALE", GpdlCharStat.Morale)]
    [InlineData("$SET_CHAR_STATUS", GpdlCharStat.Status)]
    [InlineData("$SET_CHAR_AGE", GpdlCharStat.Age)]
    [InlineData("$SET_CHAR_PERM_STR", GpdlCharStat.PermanentStrength)]
    [InlineData("$SET_CHAR_PERM_CHA", GpdlCharStat.PermanentCharisma)]
    [InlineData("$SET_CHAR_MAXENC", GpdlCharStat.MaxEncumbrance)]
    [InlineData("$SET_CHAR_ALIGNMENT", GpdlCharStat.Alignment)]
    public void Each_setter_writes_its_own_stat(string call, GpdlCharStat stat)
    {
        var host = Host();

        Run($"""{call}("hero", "9");""", host);

        Assert.Equal("9", host.GetCharStat("hero", stat));
    }

    [Fact]
    public void A_setter_yields_the_empty_string()
    {
        // m_SetCharInt ends in m_pushEmptyString, so the call is an expression with no value --
        // and the compiler relies on it leaving exactly one thing on the stack.
        var host = Host();

        Assert.Equal("", Run("""$RETURN $SET_CHAR_MORALE("hero", "40");""", host));
    }

    [Fact]
    public void The_value_is_popped_before_the_actor()
    {
        // Actor pushed first, value second. Getting this backwards writes the stat onto a
        // character named "9" and leaves the real one alone.
        var host = Host();

        Run("""$SET_CHAR_MORALE("hero", "9");""", host);

        Assert.Equal("9", host.GetCharStat("hero", GpdlCharStat.Morale));
        Assert.Equal("", host.GetCharStat("9", GpdlCharStat.Morale));
    }

    [Fact]
    public void Sex_and_gender_are_two_names_for_one_setter()
    {
        var host = Host();

        Run("""$SET_CHAR_SEX("hero", "1");""", host);
        Assert.Equal("1", host.GetCharStat("hero", GpdlCharStat.Gender));

        Run("""$SET_CHAR_GENDER("hero", "0");""", host);
        Assert.Equal("0", host.GetCharStat("hero", GpdlCharStat.Gender));
    }

    [Fact]
    public void The_percentile_is_its_own_score_not_a_part_of_strength()
    {
        var host = WithStats("hero",
                             (GpdlCharStat.PermanentStrength, "18"),
                             (GpdlCharStat.PermanentStrengthMod, "75"));

        Assert.Equal("75", Run("""$RETURN $GET_CHAR_PERM_STRMOD("hero");""", host));
    }
}
