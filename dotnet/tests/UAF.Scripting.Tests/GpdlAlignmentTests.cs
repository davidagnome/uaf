using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The six alignment calls, and the three accessors beside them.
/// </summary>
/// <remarks>
/// All nine read one stat off a character — the same accessor <c>$GET_CHAR_*</c> uses — and differ
/// only in what they make of it. <c>$Alignment</c>, <c>$Status</c> and <c>$Gender</c> name it;
/// the five predicates test it; <c>$HitPoints</c> reports it.
/// </remarks>
public class GpdlAlignmentTests
{
    /// <summary>A host that answers one stat for one actor.</summary>
    private sealed class StatHost(string actor, GpdlCharStat stat, string value)
        : GpdlUnhostedEnvironment
    {
        public override string GetCharStat(string who, GpdlCharStat which) =>
            who == actor && which == stat ? value : base.GetCharStat(who, which);
    }

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

    private static string Ask(string call, GpdlCharStat stat, int ordinal) =>
        Run($"""$RETURN {call}($AttackerContext());""",
            Attacker(new StatHost("hero", stat, ordinal.ToString())));

    /// <summary>A host with "hero" in the attacker slot, so an ACTOR parameter can be filled.</summary>
    private static GpdlUnhostedEnvironment Attacker(GpdlUnhostedEnvironment host)
    {
        host.Context.Push();
        host.Context.Set(GpdlContext.Attacker, "hero");
        return host;
    }

    /// <summary>Each alignment answers its own name.</summary>
    [Theory]
    [InlineData(0, "LAWFUL GOOD")]
    [InlineData(1, "NEUTRAL GOOD")]
    [InlineData(2, "CHAOTIC GOOD")]
    [InlineData(3, "LAWFUL NEUTRAL")]
    [InlineData(4, "TRUE NEUTRAL")]
    [InlineData(5, "CHAOTIC NEUTRAL")]
    [InlineData(6, "LAWFUL EVIL")]
    [InlineData(7, "NEUTRAL EVIL")]
    [InlineData(8, "CHAOTIC EVIL")]
    public void An_alignment_answers_its_name(int ordinal, string expected) =>
        Assert.Equal(expected, Ask("$Alignment", GpdlCharStat.Alignment, ordinal));

    /// <summary>
    /// The ordering is moral axis first, then ethical — not the grid a player pictures.
    /// </summary>
    /// <remarks>
    /// Worth pinning on its own: an implementation that assumed lawful/neutral/chaotic varied
    /// slowest would name six of the nine wrongly and still look plausible.
    /// </remarks>
    [Fact]
    public void The_ordinals_run_good_then_neutral_then_evil()
    {
        Assert.Equal(0, (int)GpdlAlignmentValue.LawfulGood);
        Assert.Equal(2, (int)GpdlAlignmentValue.ChaoticGood);
        Assert.Equal(3, (int)GpdlAlignmentValue.LawfulNeutral);
        Assert.Equal(8, (int)GpdlAlignmentValue.ChaoticEvil);
    }

    /// <summary>
    /// The five predicates, for every alignment.
    /// </summary>
    /// <remarks>
    /// <b>The whole table, because the interesting part is where two are true at once.</b>
    /// <c>$AlignmentNeutral</c> is the odd one: it covers five alignments, not three, taking in
    /// Neutral Good and Neutral Evil as well as the three ethical neutrals. So a Neutral Good
    /// character is both good and neutral, and True Neutral is the only alignment answering yes to
    /// exactly one.
    /// </remarks>
    [Theory]
    // ordinal, good, evil, lawful, neutral, chaotic
    [InlineData(0, true, false, true, false, false)]   // Lawful Good
    [InlineData(1, true, false, false, true, false)]   // Neutral Good
    [InlineData(2, true, false, false, false, true)]   // Chaotic Good
    [InlineData(3, false, false, true, true, false)]   // Lawful Neutral
    [InlineData(4, false, false, false, true, false)]  // True Neutral
    [InlineData(5, false, false, false, true, true)]   // Chaotic Neutral
    [InlineData(6, false, true, true, false, false)]   // Lawful Evil
    [InlineData(7, false, true, false, true, false)]   // Neutral Evil
    [InlineData(8, false, true, false, false, true)]   // Chaotic Evil
    public void The_five_predicates_answer_for_every_alignment(
        int ordinal, bool good, bool evil, bool lawful, bool neutral, bool chaotic)
    {
        Assert.Equal(good, GpdlAlignment.Is(ordinal, GpdlAlignmentTest.Good));
        Assert.Equal(evil, GpdlAlignment.Is(ordinal, GpdlAlignmentTest.Evil));
        Assert.Equal(lawful, GpdlAlignment.Is(ordinal, GpdlAlignmentTest.Lawful));
        Assert.Equal(neutral, GpdlAlignment.Is(ordinal, GpdlAlignmentTest.Neutral));
        Assert.Equal(chaotic, GpdlAlignment.Is(ordinal, GpdlAlignmentTest.Chaotic));
    }

    /// <summary>
    /// Neutral is not the complement of lawful and chaotic.
    /// </summary>
    /// <remarks>
    /// The symmetric reading — "neutral means neither lawful nor chaotic" — gets three alignments
    /// wrong: it would call Neutral Good and Neutral Evil not-neutral, and it agrees with the real
    /// answer everywhere else. This is the assertion that catches the tidy-up.
    /// </remarks>
    [Fact]
    public void Neutral_covers_five_alignments_not_three()
    {
        int[] neutral = [.. Enumerable.Range(0, 9)
                                      .Where(a => GpdlAlignment.Is(a, GpdlAlignmentTest.Neutral))];

        Assert.Equal([1, 3, 4, 5, 7], neutral);

        // And two of those are also good or evil, so the five are not exclusive.
        Assert.True(GpdlAlignment.Is(1, GpdlAlignmentTest.Good));
        Assert.True(GpdlAlignment.Is(7, GpdlAlignmentTest.Evil));

        // True Neutral is the only one answering yes to exactly one of the five.
        var tests = Enum.GetValues<GpdlAlignmentTest>();
        Assert.Equal([4], Enumerable.Range(0, 9)
                                    .Where(a => tests.Count(t => GpdlAlignment.Is(a, t)) == 1));
    }

    /// <summary>The predicates run through the VM as booleans, not names.</summary>
    [Theory]
    [InlineData("$AlignmentGood", 0, true)]
    [InlineData("$AlignmentGood", 8, false)]
    [InlineData("$AlignmentEvil", 8, true)]
    [InlineData("$AlignmentLawful", 3, true)]
    [InlineData("$AlignmentNeutral", 1, true)]
    [InlineData("$AlignmentChaotic", 5, true)]
    [InlineData("$AlignmentChaotic", 3, false)]
    public void A_predicate_answers_true_or_false(string call, int ordinal, bool expected)
    {
        string result = Ask(call, GpdlCharStat.Alignment, ordinal);

        // GPDL false is the empty string, and true is anything else.
        Assert.Equal(expected, result.Length > 0);
    }

    /// <summary>
    /// An actor nobody recognises is false everywhere, not an error.
    /// </summary>
    /// <remarks>
    /// Every one of the reference's helpers begins by resolving the actor and returns
    /// <c>m_false</c> when it cannot — so asking about nobody is indistinguishable from asking
    /// about someone who is not good.
    /// </remarks>
    [Fact]
    public void An_unresolved_actor_is_false_everywhere()
    {
        var host = Attacker(new GpdlUnhostedEnvironment());

        Assert.Equal(string.Empty, Run("""$RETURN $Alignment($AttackerContext());""", host));
        Assert.Equal(string.Empty, Run("""$RETURN $AlignmentGood($AttackerContext());""", host));
        Assert.Equal(string.Empty, Run("""$RETURN $Status($AttackerContext());""", host));
        Assert.Equal(string.Empty, Run("""$RETURN $Gender($AttackerContext());""", host));
    }

    /// <summary>
    /// <c>$HitPoints</c> answers "0" for an unresolved actor where the others answer empty.
    /// </summary>
    /// <remarks>
    /// <b>The one exception in the group</b> (<c>GPDLexec.cpp:7760</c>). A script adding hit points
    /// up gets a number either way — and so cannot tell a missing actor from a dead one.
    /// </remarks>
    [Fact]
    public void HitPoints_answers_zero_rather_than_empty()
    {
        Assert.Equal("0",
                     Run("""$RETURN $HitPoints($AttackerContext());""",
                         Attacker(new GpdlUnhostedEnvironment())));

        Assert.Equal("17", Ask("$HitPoints", GpdlCharStat.HitPoints, 17));
    }

    /// <summary>Status names come from the character sheet's own table.</summary>
    [Theory]
    [InlineData(0, "OKAY")]
    [InlineData(2, "DEAD")]
    [InlineData(7, "TEMP GONE")]
    [InlineData(9, "DYING")]
    public void A_status_answers_its_name(int ordinal, string expected) =>
        Assert.Equal(expected, Ask("$Status", GpdlCharStat.Status, ordinal));

    /// <summary>
    /// Gender has three values but only two names, and the third answers empty.
    /// </summary>
    /// <remarks>
    /// <b>A deliberate divergence.</b> <c>genderType</c> is <c>Male=0, Female=1, Bishop=2</c> but
    /// <c>CharGenderTypeText</c> has two rows, so the reference reads off the end of the array for
    /// a Bishop. There is nothing to reproduce faithfully about that; the engine's own
    /// <c>GetGenderName</c> agrees the value has no name.
    /// </remarks>
    [Theory]
    [InlineData(0, "MALE")]
    [InlineData(1, "FEMALE")]
    [InlineData(2, "")]
    public void A_gender_answers_its_name_or_nothing(int ordinal, string expected) =>
        Assert.Equal(expected, Ask("$Gender", GpdlCharStat.Gender, ordinal));

    /// <summary>An ordinal past the end of a table answers empty rather than reading past it.</summary>
    [Fact]
    public void An_ordinal_past_the_table_answers_empty()
    {
        Assert.Equal(string.Empty, Ask("$Alignment", GpdlCharStat.Alignment, 9));
        Assert.Equal(string.Empty, Ask("$Alignment", GpdlCharStat.Alignment, -1));
        Assert.Equal(string.Empty, Ask("$Status", GpdlCharStat.Status, 10));

        Assert.False(GpdlAlignment.Is(9, GpdlAlignmentTest.Good));
        Assert.False(GpdlAlignment.Is(null, GpdlAlignmentTest.Neutral));
    }
}
