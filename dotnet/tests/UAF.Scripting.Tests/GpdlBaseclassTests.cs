using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The per-baseclass accessors, the readied-item calls, and the two predicates beside them.
/// </summary>
/// <remarks>
/// A multi-classed character carries a separate experience total and level for each baseclass,
/// which is why <c>$GET_CHAR_Exp</c> and <c>$GET_CHAR_Lvl</c> take a class id where the rest of
/// the <c>$GET_CHAR_*</c> family does not.
/// </remarks>
public class GpdlBaseclassTests
{
    /// <summary>A host holding one character's baseclasses and possessions.</summary>
    private sealed class PartyHost : GpdlUnhostedEnvironment
    {
        public Dictionary<(string Actor, string Baseclass), (int Experience, int Level)> Classes
        { get; } = [];

        public Dictionary<(string Actor, string Location, int Ordinal), string> Readied
        { get; } = [];

        public List<(string Actor, string Item, string Location)> Readyings { get; } = [];

        public HashSet<(string Actor, int Key, int Ordinal)> Identified { get; } = [];

        public override int BaseclassProgress(string actor, string baseclass, bool level) =>
            Classes.TryGetValue((actor, baseclass), out var found)
                ? level ? found.Level : found.Experience
                : 0;

        public override void SetBaseclassProgress(
            string actor, string baseclass, bool level, int value)
        {
            Classes.TryGetValue((actor, baseclass), out var found);
            Classes[(actor, baseclass)] =
                level ? (found.Experience, value) : (value, found.Level);
        }

        public override string HighestLevelBaseclass(string actor) =>
            Classes.Where(c => c.Key.Actor == actor)
                   .OrderByDescending(c => c.Value.Level)
                   .Select(c => c.Key.Baseclass)
                   .FirstOrDefault() ?? string.Empty;

        public override string ReadiedItem(string actor, string location, int ordinal) =>
            Readied.TryGetValue((actor, location, ordinal), out string? item)
                ? item
                : string.Empty;

        public override void Ready(string actor, string item, string location) =>
            Readyings.Add((actor, item, location));

        public override bool IsIdentified(string actor, int key, int ordinal) =>
            Identified.Contains((actor, key, ordinal));
    }

    /// <summary>
    /// Runs a body with "hero" available as an actor.
    /// </summary>
    /// <remarks>
    /// <b>The actor parameter is STRING for some of these calls and ACTOR for others, with no
    /// pattern to it.</b> <c>$GET_CHAR_Lvl</c> and the rest of the baseclass family take a plain
    /// string, so a quoted name compiles; <c>$GET_CHAR_Ready</c>, <c>$SET_CHAR_Ready</c>,
    /// <c>$IsIdentified</c> and <c>$IsUndead</c> take an <c>ACTOR</c>, which has to be a
    /// system-function call. That is why the tests below mix the two forms.
    /// </remarks>
    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        Assert.True(compiler.Compile("$PUBLIC $FUNC f() { " + body + " } f;") == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    private static PartyHost WithHero()
    {
        var host = new PartyHost();
        host.Context.Push();
        host.Context.Set(GpdlContext.Attacker, "hero");
        return host;
    }

    /// <summary>
    /// Which calls take a STRING actor and which take an ACTOR, since the split has no pattern.
    /// </summary>
    /// <remarks>
    /// <b>Two calls onto the same character, typed differently.</b> <c>$GET_CHAR_Lvl("hero", …)</c>
    /// compiles and <c>$GET_CHAR_Ready("hero", …)</c> does not — the first declares its actor
    /// STRING, the second ACTOR, and an actor-typed parameter has to be a system-function call.
    /// Nothing about what the calls do predicts which they use, so this pins the table rather than
    /// leaving the next person to find out by failing compiles.
    /// </remarks>
    [Theory]
    [InlineData("""$GET_CHAR_Exp("hero", "Fighter")""", true)]
    [InlineData("""$GET_CHAR_Lvl("hero", "Fighter")""", true)]
    [InlineData("""$GetBaseclassLevel("hero", "Fighter")""", true)]
    [InlineData("""$GetHighestLevelBaseclass("hero")""", true)]
    [InlineData("""$GET_CHAR_EFFAC("hero")""", true)]
    [InlineData("""$IsUndead("hero")""", false)]
    [InlineData("""$GET_CHAR_Ready("hero", "WEAPON", "0")""", false)]
    [InlineData("""$SET_CHAR_Ready("hero", "WEAPON", "Sword")""", false)]
    [InlineData("""$IsIdentified("hero", "1", "0")""", false)]
    public void A_quoted_name_compiles_only_where_the_actor_is_a_string(
        string call, bool takesAString)
    {
        var compiler = new GpdlCompiler();
        int errors = compiler.Compile($"$PUBLIC $FUNC f() {{ $RETURN {call}; }} f;");

        Assert.Equal(takesAString, errors == 0);
    }

    /// <summary>Experience and level are read per baseclass.</summary>
    [Fact]
    public void Each_baseclass_carries_its_own_experience_and_level()
    {
        var host = WithHero();
        host.Classes[("hero", "Fighter")] = (12000, 5);
        host.Classes[("hero", "Wizard")] = (3000, 2);

        Assert.Equal("12000",
                     Run("""$RETURN $GET_CHAR_Exp("hero", "Fighter");""", host));
        Assert.Equal("5",
                     Run("""$RETURN $GET_CHAR_Lvl("hero", "Fighter");""", host));

        // The other class is a separate pair, not the same numbers.
        Assert.Equal("3000",
                     Run("""$RETURN $GET_CHAR_Exp("hero", "Wizard");""", host));
        Assert.Equal("2",
                     Run("""$RETURN $GET_CHAR_Lvl("hero", "Wizard");""", host));
    }

    /// <summary>A class the character does not have is zero, not an error.</summary>
    [Fact]
    public void A_class_the_character_lacks_is_zero()
    {
        var host = WithHero();
        host.Classes[("hero", "Fighter")] = (12000, 5);

        Assert.Equal("0", Run("""$RETURN $GET_CHAR_Lvl("hero", "Cleric");""", host));
        Assert.Equal("0", Run("""$RETURN $GET_CHAR_Exp("hero", "Cleric");""", host));
    }

    /// <summary>Both setters write, and both answer the value they were given.</summary>
    /// <remarks>
    /// <b>A divergence, and a real one.</b> <c>$SET_CHAR_Lvl</c> pushes its value back in the
    /// reference; <c>$SET_CHAR_Exp</c> does <b>not</b> — a stray <c>break;</c> inside its block
    /// skips the push, and the reference's own comment calls it out ("Unreachable
    /// <c>m_pushInteger1()</c> // Have to provide an answer!", <c>GPDLexec.cpp:5361</c>). Since the
    /// compiler emits a <c>POP</c> after every statement-level call, the reference's version eats a
    /// value belonging to the caller. Both push here.
    /// </remarks>
    [Fact]
    public void Both_setters_write_and_answer_the_value()
    {
        var host = WithHero();
        host.Classes[("hero", "Fighter")] = (100, 1);

        Assert.Equal("9000",
                     Run("""$RETURN $SET_CHAR_Exp("hero", "Fighter", "9000");""",
                         host));
        Assert.Equal("7",
                     Run("""$RETURN $SET_CHAR_Lvl("hero", "Fighter", "7");""", host));

        Assert.Equal((9000, 7), host.Classes[("hero", "Fighter")]);
    }

    /// <summary>
    /// A statement-level <c>$SET_CHAR_Exp</c> leaves the stack as it found it.
    /// </summary>
    /// <remarks>
    /// The consequence of the missing push above: a value sitting under the call has to survive it.
    /// In the reference it does not.
    /// </remarks>
    [Fact]
    public void Setting_experience_does_not_disturb_the_stack()
    {
        var host = WithHero();
        host.Classes[("hero", "Fighter")] = (100, 1);

        Assert.Equal("kept",
                     Run("""
                         $SET_CHAR_Exp("hero", "Fighter", "500");
                         $RETURN "kept";
                         """, host));
    }

    /// <summary><c>$GetBaseclassLevel</c> is the level accessor under another name.</summary>
    [Fact]
    public void GetBaseclassLevel_reads_the_same_level()
    {
        var host = WithHero();
        host.Classes[("hero", "Ranger")] = (5000, 4);

        Assert.Equal("4",
                     Run("""$RETURN $GetBaseclassLevel("hero", "Ranger");""", host));
        Assert.Equal(
            Run("""$RETURN $GET_CHAR_Lvl("hero", "Ranger");""", host),
            Run("""$RETURN $GetBaseclassLevel("hero", "Ranger");""", host));
    }

    /// <summary>The highest-level class is named, not its level.</summary>
    [Fact]
    public void The_highest_level_baseclass_is_named()
    {
        var host = WithHero();
        host.Classes[("hero", "Fighter")] = (12000, 5);
        host.Classes[("hero", "Wizard")] = (90000, 9);

        Assert.Equal("Wizard",
                     Run("""$RETURN $GetHighestLevelBaseclass("hero");""", host));
    }

    /// <summary>A character with no classes answers nothing rather than failing.</summary>
    [Fact]
    public void No_classes_names_nothing() =>
        Assert.Equal(string.Empty,
                     Run("""$RETURN $GetHighestLevelBaseclass("hero");""",
                         WithHero()));

    /// <summary>
    /// <c>$IsUndead</c> is the undead <i>type</i> being non-empty, not a flag.
    /// </summary>
    /// <remarks>
    /// <b>Worth pinning because it looks like a boolean and is not.</b> The reference tests
    /// <c>!GetUndeadType().IsEmpty()</c> — so a creature is undead exactly when it names a kind of
    /// undead, and the empty string is the only way to not be one.
    /// </remarks>
    [Theory]
    [InlineData("Skeleton", true)]
    [InlineData("Lich", true)]
    [InlineData("0", true)]
    [InlineData("", false)]
    public void Undead_is_the_type_being_named(string undeadType, bool expected)
    {
        var host = new UndeadHost(undeadType);
        host.Context.Push();
        host.Context.Set(GpdlContext.Attacker, "hero");

        string result = Run("""$RETURN $IsUndead($AttackerContext());""", host);

        Assert.Equal(expected, result.Length > 0);
    }

    private sealed class UndeadHost(string undeadType) : GpdlUnhostedEnvironment
    {
        public override string GetCharStat(string actor, GpdlCharStat stat) =>
            stat == GpdlCharStat.UndeadType ? undeadType : base.GetCharStat(actor, stat);
    }

    /// <summary>A readied item is found by its body location and its place in it.</summary>
    [Fact]
    public void A_readied_item_is_found_by_location()
    {
        var host = WithHero();
        host.Readied[("hero", "WEAPON", 0)] = "Long Sword";
        host.Readied[("hero", "RING  ", 0)] = "Ring of Protection";
        host.Readied[("hero", "RING  ", 1)] = "Ring of Fire";

        Assert.Equal("Long Sword",
                     Run("""$RETURN $GET_CHAR_Ready($AttackerContext(), "WEAPON", "0");""", host));

        // The ordinal is what tells two rings apart.
        Assert.Equal("Ring of Fire",
                     Run("""$RETURN $GET_CHAR_Ready($AttackerContext(), "RING  ", "1");""", host));

        // Nothing there is empty rather than an error.
        Assert.Equal(string.Empty,
                     Run("""$RETURN $GET_CHAR_Ready($AttackerContext(), "SHIELD", "0");""", host));
    }

    /// <summary>Readying names the actor, the item and the location, in that order.</summary>
    [Fact]
    public void Readying_reaches_the_host_with_its_arguments_in_order()
    {
        var host = WithHero();

        Assert.Equal(string.Empty,
                     Run("""
                         $RETURN $SET_CHAR_Ready($AttackerContext(), "SHIELD", "Large Shield");
                         """, host));

        Assert.Equal([("hero", "Large Shield", "SHIELD")], host.Readyings);
    }

    /// <summary>Identification is per carried item, by its key on the character.</summary>
    [Fact]
    public void Identification_is_per_carried_item()
    {
        var host = WithHero();
        host.Identified.Add(("hero", 3, 0));

        Assert.NotEqual(string.Empty,
                        Run("""$RETURN $IsIdentified($AttackerContext(), "3", "0");""", host));

        // A different key, and a different one of the stack, are separate answers.
        Assert.Equal(string.Empty,
                     Run("""$RETURN $IsIdentified($AttackerContext(), "4", "0");""", host));
        Assert.Equal(string.Empty,
                     Run("""$RETURN $IsIdentified($AttackerContext(), "3", "1");""", host));
    }

    /// <summary>
    /// The three armour classes are distinct stats, not one under three names.
    /// </summary>
    /// <remarks>
    /// <c>$GET_CHAR_AC</c> is the base, <c>$GET_CHAR_ADJAC</c> folds in spell effects, and
    /// <c>$GET_CHAR_EFFAC</c> folds in readied equipment. A character in enchanted plate has three
    /// different answers.
    /// </remarks>
    [Fact]
    public void The_three_armour_classes_are_separate_stats()
    {
        var host = new ArmorHost();
        host.Context.Push();
        host.Context.Set(GpdlContext.Attacker, "hero");

        Assert.Equal("10", Run("""$RETURN $GET_CHAR_AC("hero");""", host));
        Assert.Equal("8", Run("""$RETURN $GET_CHAR_ADJAC("hero");""", host));
        Assert.Equal("4", Run("""$RETURN $GET_CHAR_EFFAC("hero");""", host));
    }

    private sealed class ArmorHost : GpdlUnhostedEnvironment
    {
        public override string GetCharStat(string actor, GpdlCharStat stat) => stat switch
        {
            GpdlCharStat.ArmorClass => "10",
            GpdlCharStat.AdjustedArmorClass => "8",
            GpdlCharStat.EffectiveArmorClass => "4",
            _ => base.GetCharStat(actor, stat),
        };
    }
}
