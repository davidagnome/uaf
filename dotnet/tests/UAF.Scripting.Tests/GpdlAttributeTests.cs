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
}
