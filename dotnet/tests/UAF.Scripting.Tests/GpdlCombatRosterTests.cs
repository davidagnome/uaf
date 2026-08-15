using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The combat-roster calls: friendliness, adjacency, walking the list, and naming an actor.
/// </summary>
public class GpdlCombatRosterTests
{
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

    /// <summary>A host with a small fixed roster.</summary>
    private sealed class RosterHost : GpdlUnhostedEnvironment
    {
        public Dictionary<(int Combatant, string Which), int> Values { get; } = [];

        public List<(int Combatant, int Adjustment)> Sets { get; } = [];

        public Dictionary<int, string> Adjacent { get; } = [];

        public List<(int? After, int Filter)> Walks { get; } = [];

        public int? NextAnswer { get; set; }

        public override int? Friendly(int combatant, string which) =>
            Values.TryGetValue((combatant, which), out int value) ? value : null;

        public override int SetFriendly(int combatant, int adjustment)
        {
            Sets.Add((combatant, adjustment));
            return 2;
        }

        public override string AdjacentCombatants(int combatant) =>
            Adjacent.TryGetValue(combatant, out string? list) ? list : string.Empty;

        public override int? NextCreature(int? after, int filter)
        {
            Walks.Add((after, filter));
            return NextAnswer;
        }
    }

    /// <summary>Each of the three codes reads its own value.</summary>
    [Theory]
    [InlineData("B", 1)]
    [InlineData("A", 3)]
    [InlineData("F", 0)]
    public void Each_friendliness_code_reads_its_own(string code, int expected)
    {
        var host = new RosterHost();
        host.Values[(2, "B")] = 1;
        host.Values[(2, "A")] = 3;
        host.Values[(2, "F")] = 0;

        Assert.Equal(expected.ToString(),
                     Run($"""$RETURN $GetFriendly("2", "{code}");""", host));
    }

    /// <summary>
    /// A missing combatant and an unrecognised code both answer "Huh?".
    /// </summary>
    /// <remarks>
    /// <b>A word, not a code</b> — and the same word for both, so a script cannot tell a bad
    /// combatant from a bad question. It is also not a number, so arithmetic on the result reads
    /// it as zero.
    /// </remarks>
    [Theory]
    [InlineData("""$GetFriendly("99", "F")""")]
    [InlineData("""$GetFriendly("2", "X")""")]
    [InlineData("""$GetFriendly("2", "")""")]
    [InlineData("""$GetFriendly("2", "b")""")]
    public void A_bad_combatant_or_code_answers_huh(string call)
    {
        var host = new RosterHost();
        host.Values[(2, "F")] = 1;

        Assert.Equal(GpdlCombat.NoSuchAnswer, Run($"$RETURN {call};", host));
    }

    /// <summary>
    /// The codes are case-sensitive, which is easy to miss beside the rest of the family.
    /// </summary>
    [Fact]
    public void The_codes_are_case_sensitive()
    {
        var host = new RosterHost();
        host.Values[(0, "F")] = 1;

        Assert.Equal("1", Run("""$RETURN $GetFriendly("0", "F");""", host));
        Assert.Equal(GpdlCombat.NoSuchAnswer, Run("""$RETURN $GetFriendly("0", "f");""", host));
    }

    /// <summary>Setting answers the override as it was before the change.</summary>
    [Fact]
    public void Setting_answers_the_previous_override()
    {
        var host = new RosterHost();

        Assert.Equal("2", Run("""$RETURN $SetFriendly("1", "3");""", host));
        Assert.Equal([(1, 3)], host.Sets);
    }

    /// <summary>The adjacency list is pipe-prefixed, so an empty one is the empty string.</summary>
    [Fact]
    public void The_adjacency_list_is_pipe_prefixed()
    {
        var host = new RosterHost();
        host.Adjacent[0] = "|3|7";

        Assert.Equal("|3|7", Run("""$RETURN $ListAdjacentCombatants("0");""", host));

        // Nobody adjacent is empty rather than a bare delimiter, so the field count is right.
        Assert.Equal(string.Empty, Run("""$RETURN $ListAdjacentCombatants("1");""", host));
    }

    /// <summary>
    /// An empty "after" starts the walk; a number continues after it.
    /// </summary>
    /// <remarks>
    /// <b>The reference tests the stack slot itself, not the popped value</b>, so <c>""</c> and
    /// <c>"0"</c> mean different things: the first is "from the beginning" and the second is
    /// "after combatant 0". A port that read both through <c>atoi</c> would skip combatant 0 on
    /// every walk.
    /// </remarks>
    [Fact]
    public void An_empty_start_is_not_the_same_as_zero()
    {
        var host = new RosterHost { NextAnswer = 4 };

        Run("""$RETURN $NextCreatureIndex("", "1");""", host);
        Run("""$RETURN $NextCreatureIndex("0", "1");""", host);

        Assert.Equal([(null, 1), (0, 1)], host.Walks);
    }

    /// <summary>The end of the walk is false, which is how a script's loop stops.</summary>
    [Fact]
    public void The_end_of_the_walk_is_false()
    {
        var host = new RosterHost { NextAnswer = null };

        Assert.Equal(string.Empty, Run("""$RETURN $NextCreatureIndex("", "0");""", host));

        host.NextAnswer = 0;

        // And combatant 0 is NOT the end -- "0" is a real answer, distinct from empty.
        Assert.Equal("0", Run("""$RETURN $NextCreatureIndex("", "0");""", host));
    }

    /// <summary>
    /// The filter flags are skip-rules, and two of them contradict.
    /// </summary>
    /// <remarks>
    /// Setting both <see cref="GpdlCreatureFilter.Hostile"/> and
    /// <see cref="GpdlCreatureFilter.Friendly"/> skips everybody — the first drops every friendly
    /// combatant and the second every hostile one. Nothing warns about it.
    /// </remarks>
    [Fact]
    public void The_filter_flags_reach_the_host_as_written()
    {
        var host = new RosterHost { NextAnswer = 1 };

        Run("""$RETURN $NextCreatureIndex("", "9");""", host);

        // 9 is Alive | OnMap.
        Assert.Equal((int)(GpdlCreatureFilter.Alive | GpdlCreatureFilter.OnMap),
                     Assert.Single(host.Walks).Filter);

        Assert.Equal(6, (int)(GpdlCreatureFilter.Hostile | GpdlCreatureFilter.Friendly));
    }

    /// <summary><c>$Name</c> answers an actor, so it cannot be returned as a string.</summary>
    /// <remarks>
    /// Its table row declares the return type <c>ACTOR</c> — the fourth call in the port with that
    /// shape, after <c>$Myself</c>, <c>$CharacterContext</c> and <c>$IndexToActor</c>.
    /// </remarks>
    [Fact]
    public void Name_and_IndexToActor_return_actors()
    {
        var compiler = new GpdlCompiler();
        Assert.NotEqual(0, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN $Name("Bob"); } f;"""));

        var other = new GpdlCompiler();
        Assert.NotEqual(0, other.Compile("""$PUBLIC $FUNC f() { $RETURN $IndexToActor("1"); } f;"""));
    }

    /// <summary>Both reach the host and produce an actor a call can take.</summary>
    [Fact]
    public void Both_produce_an_actor_the_next_call_can_take()
    {
        var host = new NamingHost();

        // Read back through $IndexOf, which takes an ACTOR and returns a string.
        Assert.Equal("Bob", Run("""$RETURN $IndexOf($Name("Bob"));""", host));
        Assert.Equal("at-3", Run("""$RETURN $IndexOf($IndexToActor("3"));""", host));
    }

    private sealed class NamingHost : GpdlUnhostedEnvironment
    {
        public override string ActorNamed(string name) => name;

        public override string IndexToActor(int index) => $"at-{index}";

        public override string IndexOf(string who) => who;
    }
}
