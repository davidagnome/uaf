using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The sixteen creature traits — <c>$GET_ISMAMMAL</c> and its fifteen siblings.
/// </summary>
/// <remarks>
/// They are one family, not sixteen singles: each tests a single bit of one of the four unrelated
/// bitfields a <c>MONSTER_DATA</c> carries (<c>Monster.h:60</c>–<c>126</c>), reached through
/// <c>CHARACTER</c> by the reference's <c>GET_ACTOR_BOOL</c> macro.
/// </remarks>
public class GpdlCreatureTraitTests
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

    /// <summary>Every one of the sixteen compiles and runs rather than throwing.</summary>
    /// <remarks>
    /// Before this they all reached <c>ExecuteSubOp</c>'s default and threw with a source citation,
    /// which is what "239 of 387 implemented" counted.
    /// </remarks>
    [Theory]
    [InlineData("$GET_ISMAMMAL")]
    [InlineData("$GET_ISANIMAL")]
    [InlineData("$GET_ISSNAKE")]
    [InlineData("$GET_ISGIANT")]
    [InlineData("$GET_ISALWAYSLARGE")]
    [InlineData("$GET_HASDEATHIMMUNITY")]
    [InlineData("$GET_HASPOISONIMMUNITY")]
    [InlineData("$GET_HASCONFUSIONIMMUNITY")]
    [InlineData("$GET_HASVORPALIMMUNITY")]
    [InlineData("$GET_HASDWARFACPENALTY")]
    [InlineData("$GET_HASDWARFTHAC0PENALTY")]
    [InlineData("$GET_HASGNOMEACPENALTY")]
    [InlineData("$GET_HASGNOMETHAC0PENALTY")]
    [InlineData("$GET_HASRANGERDMGPENALTY")]
    [InlineData("$GET_CANBEHELDORCHARMED")]
    [InlineData("$GET_AFFECTEDBYDISPELEVIL")]
    public void Every_trait_runs(string function)
    {
        // An ACTOR parameter cannot be a quoted string: compileTypedSystemFunctionCall
        // (GPDLcomp.cpp:2787) demands one system-function call of the matching type, so the actor
        // has to be named by a query rather than spelled out.
        string result = Run($"$RETURN {function}($MOST_DAMAGED_ENEMY());",
                            new GpdlUnhostedEnvironment());

        // A BOOL through m_pushInteger1: "1" or "0", never "true" and never empty.
        Assert.True(result is "0" or "1", $"{function} gave '{result}'");
    }

    /// <summary>
    /// A non-monster is a mammal and can be held or charmed; the other fourteen are false.
    /// </summary>
    /// <remarks>
    /// <b>This is the assertion the whole family exists for.</b> Fourteen accessors return
    /// <c>FALSE</c> for a non-monster and two return <c>TRUE</c> — answering false for all sixteen
    /// would make hold-person and charm fail against the entire party, which looks like a spell bug
    /// rather than a missing default.
    /// </remarks>
    [Theory]
    [InlineData("$GET_ISMAMMAL", "1")]
    [InlineData("$GET_CANBEHELDORCHARMED", "1")]
    [InlineData("$GET_ISANIMAL", "0")]
    [InlineData("$GET_ISSNAKE", "0")]
    [InlineData("$GET_ISGIANT", "0")]
    [InlineData("$GET_ISALWAYSLARGE", "0")]
    [InlineData("$GET_HASDEATHIMMUNITY", "0")]
    [InlineData("$GET_HASPOISONIMMUNITY", "0")]
    [InlineData("$GET_HASCONFUSIONIMMUNITY", "0")]
    [InlineData("$GET_HASVORPALIMMUNITY", "0")]
    [InlineData("$GET_HASDWARFACPENALTY", "0")]
    [InlineData("$GET_HASDWARFTHAC0PENALTY", "0")]
    [InlineData("$GET_HASGNOMEACPENALTY", "0")]
    [InlineData("$GET_HASGNOMETHAC0PENALTY", "0")]
    [InlineData("$GET_HASRANGERDMGPENALTY", "0")]
    [InlineData("$GET_AFFECTEDBYDISPELEVIL", "0")]
    public void A_non_monster_answers_the_references_literal(string function, string expected)
    {
        string result = Run($"$RETURN {function}($MOST_DAMAGED_ENEMY());",
                            new GpdlUnhostedEnvironment());

        Assert.Equal(expected, result);
    }

    /// <summary>Exactly two of the sixteen default to true.</summary>
    /// <remarks>
    /// Stated as a count as well as case by case: a table edit that flipped a third would pass
    /// every individual assertion above that it did not touch.
    /// </remarks>
    [Fact]
    public void Exactly_two_traits_default_to_true()
    {
        var traits = Enum.GetValues<GpdlCharStat>().Where(GpdlCharStats.IsTrait).ToList();

        Assert.Equal(16, traits.Count);
        Assert.Equal(2, traits.Count(t => GpdlCharStats.NonMonsterTrait(t) == "1"));
        Assert.All(traits, t => Assert.Contains(GpdlCharStats.NonMonsterTrait(t), new[] { "0", "1" }));
    }

    /// <summary>A host that sets a trait is believed over the default.</summary>
    [Fact]
    public void A_set_trait_overrides_the_default()
    {
        var host = new GpdlUnhostedEnvironment();

        // A snake that is not a mammal -- the two the default gets wrong for a real monster.
        host.SetCharStat("ATTACKER", GpdlCharStat.IsMammal, "0");
        host.SetCharStat("ATTACKER", GpdlCharStat.IsSnake, "1");

        Assert.Equal("0", host.GetCharStat("ATTACKER", GpdlCharStat.IsMammal));
        Assert.Equal("1", host.GetCharStat("ATTACKER", GpdlCharStat.IsSnake));

        // Untouched traits still answer their own default rather than the last one written.
        Assert.Equal("1", host.GetCharStat("ATTACKER", GpdlCharStat.CanBeHeldOrCharmed));
        Assert.Equal("0", host.GetCharStat("ATTACKER", GpdlCharStat.HasDeathImmunity));
    }

    /// <summary>
    /// The one writable trait writes; the other fifteen have no setter in the reference either.
    /// </summary>
    [Fact]
    public void Only_dispel_evil_can_be_set()
    {
        var host = new GpdlUnhostedEnvironment();
        Run("$SET_AFFECTEDBYDISPELEVIL($MOST_DAMAGED_ENEMY(), \"1\");", host);

        Assert.Equal("1", host.GetCharStat(host.NullActor, GpdlCharStat.AffectedByDispelEvil));
    }

    /// <summary>A trait is not confused with an ordinary stat.</summary>
    /// <remarks>
    /// <c>IsTrait</c> decides whether an unset value falls back to a literal or to the empty
    /// string, so a stat wrongly counted as a trait would start answering "0" where a script
    /// expects nothing.
    /// </remarks>
    [Fact]
    public void An_ordinary_stat_is_not_a_trait()
    {
        Assert.False(GpdlCharStats.IsTrait(GpdlCharStat.HitPoints));
        Assert.False(GpdlCharStats.IsTrait(GpdlCharStat.Name));
        Assert.False(GpdlCharStats.IsTrait(GpdlCharStat.ArmorClass));

        var host = new GpdlUnhostedEnvironment();
        Assert.Equal(string.Empty, host.GetCharStat("ATTACKER", GpdlCharStat.HitPoints));
    }
}
