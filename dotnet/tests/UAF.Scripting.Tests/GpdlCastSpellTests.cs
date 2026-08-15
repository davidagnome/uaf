using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The two spell-casting calls.
/// </summary>
/// <remarks>
/// They differ in more than who casts: <c>$CastSpellOnTarget</c> takes no caster and the reference
/// <i>invents</i> a maximally capable one, so the same spell lands differently through the two.
/// </remarks>
public class GpdlCastSpellTests
{
    private sealed class CastingHost : GpdlUnhostedEnvironment
    {
        public (string Target, string Spell, string? Caster)? Cast { get; private set; }

        public bool Answer { get; set; } = true;

        public override bool CastSpellOnTarget(string target, string spell, string? caster)
        {
            Cast = (target, spell, caster);
            return Answer;
        }
    }

    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        Assert.True(compiler.Compile("$PUBLIC $FUNC f() { " + body + " } f;") == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        host.Context.Push();
        host.Context.Set(GpdlContext.Attacker, "victim");
        host.Context.Set(GpdlContext.Target, "wizard");

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    /// <summary>
    /// The plain form names no caster, and null is how the host is told to invent one.
    /// </summary>
    /// <remarks>
    /// <b>Null is "nobody in particular", not "no caster".</b> The reference builds a throwaway
    /// Chaotic Neutral human male Fighter with 18 in every ability and casts through it — so the
    /// spell lands as though cast by someone maximally capable.
    /// </remarks>
    [Fact]
    public void The_plain_form_names_no_caster()
    {
        var host = new CastingHost();

        Assert.NotEqual(string.Empty,
                        Run("""$RETURN $CastSpellOnTarget($AttackerContext(), "Bless");""", host));

        Assert.Equal(("victim", "Bless", null), host.Cast);
    }

    /// <summary>The "As" form names its caster last, so that argument pops first.</summary>
    [Fact]
    public void The_as_form_names_its_caster()
    {
        var host = new CastingHost();

        Assert.NotEqual(string.Empty, Run(
            """$RETURN $CastSpellOnTargetAs($AttackerContext(), "Bless", $TargetContext());""",
            host));

        Assert.Equal(("victim", "Bless", "wizard"), host.Cast);
    }

    /// <summary>
    /// A successful cast answers true, where the reference answers nothing at all.
    /// </summary>
    /// <remarks>
    /// <b>A divergence, and the same defect as <c>$SET_CHAR_Exp</c>.</b> Both cast calls push
    /// <c>false</c> on every failure path, but the engine build's <i>success</i> path pushes
    /// nothing — only the editor build pushes true. Since the compiler emits a <c>POP</c> after
    /// every statement-level call, a script that successfully casts a spell has a value belonging
    /// to the caller eaten.
    /// </remarks>
    [Fact]
    public void A_successful_cast_answers_true_and_leaves_the_stack_alone()
    {
        var host = new CastingHost { Answer = true };

        Assert.Equal("kept", Run("""
            $CastSpellOnTarget($AttackerContext(), "Bless");
            $RETURN "kept";
            """, host));

        // And a refused one is false, which is what the reference's failure paths push.
        host.Answer = false;
        Assert.Equal(string.Empty,
                     Run("""$RETURN $CastSpellOnTarget($AttackerContext(), "Bless");""", host));
    }

    /// <summary>Both take an ACTOR for the target, so a quoted name will not compile.</summary>
    [Fact]
    public void Both_take_actors_not_names()
    {
        var one = new GpdlCompiler();
        Assert.NotEqual(0, one.Compile(
            """$PUBLIC $FUNC f() { $RETURN $CastSpellOnTarget("bob", "Bless"); } f;"""));

        // And the "As" form's caster is an ACTOR too.
        var two = new GpdlCompiler();
        Assert.NotEqual(0, two.Compile(
            """$PUBLIC $FUNC f() { $RETURN $CastSpellOnTargetAs($AttackerContext(), "Bless", "bob"); } f;"""));
    }
}
