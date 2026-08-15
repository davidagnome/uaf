using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// How the VM dispatches <c>$SkillAdj</c> and <c>$SpellAdj</c>.
/// </summary>
/// <remarks>
/// What the two <i>do</i> to a character is covered against a real one in
/// <c>GameScriptHostAdjustmentTests</c>; this pins the argument order and the refusal.
/// </remarks>
public class GpdlAdjustmentTests
{
    private sealed class RecordingHost : GpdlUnhostedEnvironment
    {
        public (string Actor, string School, string Adjustment,
                int First, int Last, int Percent, int Bonus)? Spell
        { get; private set; }

        public (string Actor, string Skill, string Adjustment, string Type, int Value)? Skill
        { get; private set; }

        public string? SkillAnswer { get; set; } = string.Empty;

        public override void SpellAdjustment(string actor, string school, string adjustment,
                                             int firstLevel, int lastLevel, int percent,
                                             int bonus) =>
            Spell = (actor, school, adjustment, firstLevel, lastLevel, percent, bonus);

        public override string? SkillAdjustment(string actor, string skill, string adjustment,
                                                string adjustmentType, int value)
        {
            Skill = (actor, skill, adjustment, adjustmentType, value);
            return SkillAnswer;
        }
    }

    /// <summary>
    /// Compiles a body and hands back the machine, with "hero" available as an actor.
    /// </summary>
    /// <remarks>
    /// <b>Both calls take an <c>ACTOR</c> first, so a quoted name will not compile</b> — the fifth
    /// call in the port with a type slot that decides how it may be written. Check the table row
    /// before writing the test.
    /// </remarks>
    private static GpdlVirtualMachine Machine(string body, GpdlUnhostedEnvironment host)
    {
        host.Context.Push();
        host.Context.Set(GpdlContext.Attacker, "hero");

        var compiler = new GpdlCompiler();
        Assert.True(compiler.Compile("$PUBLIC $FUNC f() { " + body + " } f;") == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        return new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
    }

    /// <summary>All seven of <c>$SpellAdj</c>'s arguments arrive in order.</summary>
    /// <remarks>
    /// The most arguments any call in the table takes, and the order is easy to get wrong: the
    /// rightmost pops first, so the actor comes off last.
    /// </remarks>
    [Fact]
    public void Every_spell_argument_arrives_in_order()
    {
        var host = new RecordingHost();

        Assert.Equal(string.Empty, Machine(
            """$RETURN $SpellAdj($AttackerContext(), "Cleric", "Blessing", "1", "9", "50", "2");""", host)
            .Execute("f"));

        Assert.Equal(("hero", "Cleric", "Blessing", 1, 9, 50, 2), host.Spell);
    }

    /// <summary>And all five of <c>$SkillAdj</c>'s.</summary>
    [Fact]
    public void Every_skill_argument_arrives_in_order()
    {
        var host = new RecordingHost { SkillAnswer = "42" };

        Assert.Equal("42", Machine(
            """$RETURN $SkillAdj($AttackerContext(), "Climb", "Boots", "+", "7");""", host).Execute("f"));

        Assert.Equal(("hero", "Climb", "Boots", "+", 7), host.Skill);
    }

    /// <summary>
    /// A host that cannot answer makes the call fail loudly rather than return a number.
    /// </summary>
    /// <remarks>
    /// <b>The four computed reads need a skill-value computation this port does not have.</b>
    /// Answering them with a plausible number would be worse than refusing: a design would branch
    /// on it and nothing would say the branch was wrong. Null from the host is turned into the same
    /// refusal an unported sub-opcode gets, with a citation.
    /// </remarks>
    [Fact]
    public void A_computed_read_is_refused_with_a_citation()
    {
        var host = new RecordingHost { SkillAnswer = null };

        var thrown = Assert.Throws<NotSupportedException>(
            () => Machine("""$RETURN $SkillAdj($AttackerContext(), "Climb", "Boots", "B", "0");""", host)
                  .Execute("f"));

        Assert.Contains("$SkillAdj", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("class.cpp", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Which type characters mean what.</summary>
    /// <remarks>
    /// Only the first character is looked at, and five of the eight both select the write and are
    /// the arithmetic it stores.
    /// </remarks>
    [Theory]
    [InlineData("+", GpdlSkillAdjustment.Kind.Set)]
    [InlineData("%bonus", GpdlSkillAdjustment.Kind.Set)]
    [InlineData("=", GpdlSkillAdjustment.Kind.Set)]
    [InlineData("-", GpdlSkillAdjustment.Kind.Set)]
    [InlineData("*", GpdlSkillAdjustment.Kind.Set)]
    [InlineData("D", GpdlSkillAdjustment.Kind.Delete)]
    [InlineData("A", GpdlSkillAdjustment.Kind.Stored)]
    [InlineData("F", GpdlSkillAdjustment.Kind.Computed)]
    [InlineData("f", GpdlSkillAdjustment.Kind.Computed)]
    [InlineData("b", GpdlSkillAdjustment.Kind.Computed)]
    [InlineData("B", GpdlSkillAdjustment.Kind.Computed)]
    [InlineData("d", GpdlSkillAdjustment.Kind.Unknown)]
    [InlineData("a", GpdlSkillAdjustment.Kind.Unknown)]
    [InlineData("Z", GpdlSkillAdjustment.Kind.Unknown)]
    [InlineData("", GpdlSkillAdjustment.Kind.Unknown)]
    public void The_type_character_selects_the_operation(
        string type, GpdlSkillAdjustment.Kind expected) =>
        Assert.Equal(expected, GpdlSkillAdjustment.KindOf(type));
}
