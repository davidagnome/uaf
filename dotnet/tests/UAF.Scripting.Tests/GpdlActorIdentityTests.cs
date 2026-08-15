using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$Myself</c>, <c>$MyIndex</c> and <c>$IndexOf</c> — how a script names itself.
/// </summary>
public class GpdlActorIdentityTests
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

    /// <summary>A host that reports an index for one actor.</summary>
    private sealed class IndexHost(string actor, string index) : GpdlUnhostedEnvironment
    {
        public override string IndexOf(string who) =>
            who == actor ? index : base.IndexOf(who);
    }

    /// <summary>A host that hands back whichever actor it was asked about.</summary>
    /// <remarks>
    /// <b><c>$Myself</c> returns an <c>ACTOR</c>, not a string</b> — its table row declares the
    /// return type, same as <c>$CharacterContext</c> — so <c>$RETURN $Myself();</c> does not
    /// compile and it can only be used where an actor is wanted. <c>$IndexOf</c> takes one and
    /// returns a string, so echoing the actor through it is how these tests see what
    /// <c>$Myself</c> produced.
    /// </remarks>
    private sealed class EchoHost : GpdlUnhostedEnvironment
    {
        public override string IndexOf(string who) => who;
    }

    /// <summary>What <c>$Myself()</c> evaluated to, read through a call that takes an actor.</summary>
    private static string Myself(GpdlUnhostedEnvironment host) =>
        Run("$RETURN $IndexOf($Myself());", host);

    /// <summary>
    /// <c>$Myself</c> is the character the engine is operating on.
    /// </summary>
    [Fact]
    public void Myself_is_the_character_being_operated_on()
    {
        var host = new EchoHost();

        using (host.Context.PushActor("hero"))
        {
            Assert.Equal("hero", Myself(host));
        }
    }

    /// <summary>
    /// Its stack is not the script context's character.
    /// </summary>
    /// <remarks>
    /// <b>Two stacks in the reference, and this is the assertion that keeps them apart.</b>
    /// <c>charContextStack</c> is pushed by whoever is operating on a character — rolling stats,
    /// updating them, enabling abilities — while the script context's character is set when a
    /// script runs <i>for</i> someone. They usually hold the same actor, which is precisely why
    /// collapsing them into one would be hard to notice.
    /// </remarks>
    [Fact]
    public void Myself_and_the_character_context_are_separate_stacks()
    {
        var host = new EchoHost();

        host.Context.Push();
        host.Context.Set(GpdlContext.Character, "subject");

        using (host.Context.PushActor("operator"))
        {
            Assert.Equal("operator", Myself(host));
        }

        // The script context is untouched by the actor stack, and vice versa.
        Assert.Equal("subject", host.Context.Get(GpdlContext.Character));
    }

    /// <summary>
    /// It nests, and unwinding restores what was underneath.
    /// </summary>
    [Fact]
    public void The_actor_stack_nests()
    {
        var host = new EchoHost();

        using (host.Context.PushActor("outer"))
        {
            using (host.Context.PushActor("inner"))
            {
                Assert.Equal("inner", Myself(host));
            }

            Assert.Equal("outer", Myself(host));
        }
    }

    /// <summary>
    /// With nothing pushed it answers empty, and says so.
    /// </summary>
    /// <remarks>
    /// The reference shows the player an error box and carries on with the null actor. There is no
    /// dialog here, so the complaint is collected instead — a script reaching for a character
    /// nobody established is broken in a way worth surfacing.
    /// </remarks>
    [Fact]
    public void With_no_actor_pushed_it_answers_empty_and_complains()
    {
        var host = new EchoHost();

        Assert.Equal(string.Empty, Myself(host));
        Assert.Contains(host.Context.Missing,
                        m => m.Contains("Character Context", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>$MyIndex</c> is <c>$IndexOf($Myself())</c>, and inherits both halves.
    /// </summary>
    [Fact]
    public void MyIndex_is_the_index_of_myself()
    {
        var host = new IndexHost("hero", "3");

        using (host.Context.PushActor("hero"))
        {
            // The two really are the same call: $MyIndex is m_IndexOf(m_Myself()).
            Assert.Equal("3", Run("$RETURN $MyIndex();", host));
            Assert.Equal("3", Run("$RETURN $IndexOf($Myself());", host));
        }
    }

    /// <summary>
    /// An actor with no valid instance answers a sentence, not a number.
    /// </summary>
    /// <remarks>
    /// <b>"Invalid Context" is a value a design can test for</b>, and it is also what
    /// <c>atoi</c> reads as zero — so a script doing arithmetic on the result gets index 0 rather
    /// than an error. Keeping the literal is what lets a design tell the two apart.
    /// </remarks>
    [Fact]
    public void An_actor_with_no_instance_answers_invalid_context()
    {
        var host = new IndexHost("hero", "3");

        using (host.Context.PushActor("stranger"))
        {
            Assert.Equal(GpdlActorIndex.InvalidContext, Run("$RETURN $MyIndex();", host));
        }

        Assert.Equal("Invalid Context", GpdlActorIndex.InvalidContext);
    }

    /// <summary>
    /// A combatant that joined mid-fight is offset so it cannot be mistaken for the others.
    /// </summary>
    /// <remarks>
    /// Party position, combat order and new-combatant index all share one number, and 10000 is what
    /// keeps the third from colliding with the first two.
    /// </remarks>
    [Fact]
    public void A_new_combatants_index_is_offset()
    {
        Assert.Equal(10000, GpdlActorIndex.NewCombatantOffset);

        var host = new IndexHost(
            "latecomer", (GpdlActorIndex.NewCombatantOffset + 2).ToString());

        using (host.Context.PushActor("latecomer"))
        {
            Assert.Equal("10002", Run("$RETURN $MyIndex();", host));
        }
    }

    /// <summary>A character the party built during play answers -2 whatever its instance.</summary>
    [Fact]
    public void A_created_character_answers_minus_two()
    {
        var host = new IndexHost("newcomer", GpdlActorIndex.CreatedCharacter);

        using (host.Context.PushActor("newcomer"))
        {
            Assert.Equal("-2", Run("$RETURN $MyIndex();", host));
        }
    }

    /// <summary>
    /// <c>$IndexOf</c> takes an ACTOR, so a quoted name will not compile.
    /// </summary>
    /// <remarks>
    /// The table types the parameter <c>ACTOR</c> (<c>GPDLcomp.cpp:1472</c>), and an actor-typed
    /// parameter has to be a system-function call. This is why every test here reaches it through
    /// <c>$Myself()</c> rather than a literal.
    /// </remarks>
    [Fact]
    public void IndexOf_refuses_a_quoted_name()
    {
        var compiler = new GpdlCompiler();

        Assert.NotEqual(0, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN $IndexOf("hero"); } f;"""));
    }
}
