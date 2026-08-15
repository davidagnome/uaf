using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$ToHitComputation_Roll</c> and <c>$ComputeAttackDamage</c>.
/// </summary>
public class GpdlAttackTests
{
    private sealed class AttackHost : GpdlUnhostedEnvironment
    {
        public int? Roll { get; set; }

        public (int Attacker, int Target)? Asked { get; private set; }

        public int Damage { get; set; }

        public override int? ToHitRoll => Roll;

        public override int ComputeAttackDamage(int attacker, int target)
        {
            Asked = (attacker, target);
            return Damage;
        }
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

    /// <summary>Inside an attack it reads what was rolled.</summary>
    [Fact]
    public void Inside_an_attack_it_reads_the_roll()
    {
        var host = new AttackHost { Roll = 17 };

        Assert.Equal("17", Run("$RETURN $ToHitComputation_Roll();", host));
    }

    /// <summary>
    /// Outside one it answers ten — a plausible roll, not a marker.
    /// </summary>
    /// <remarks>
    /// <b>So a script reading this in the wrong place cannot tell.</b> The reference writes a debug
    /// line and carries on with 10, which is a perfectly ordinary d20 result: an ability that asks
    /// outside an attack behaves as though every swing rolled ten, and nothing in the script says
    /// otherwise.
    /// </remarks>
    [Fact]
    public void Outside_an_attack_it_answers_a_plausible_ten()
    {
        var host = new AttackHost { Roll = null };

        Assert.Equal("10", Run("$RETURN $ToHitComputation_Roll();", host));
        Assert.Equal(10, GpdlCombat.NoToHitRoll);

        // And a real roll of ten is the same answer, so the two are indistinguishable.
        host.Roll = 10;
        Assert.Equal("10", Run("$RETURN $ToHitComputation_Roll();", host));
    }

    /// <summary>It takes nothing at all.</summary>
    [Fact]
    public void It_takes_no_arguments()
    {
        var compiler = new GpdlCompiler();

        Assert.NotEqual(0, compiler.Compile(
            """$PUBLIC $FUNC f() { $RETURN $ToHitComputation_Roll("1"); } f;"""));
    }

    /// <summary>The attacker and the target arrive in that order.</summary>
    [Fact]
    public void The_attacker_comes_before_the_target()
    {
        var host = new AttackHost { Damage = 6 };

        Assert.Equal("6", Run("""$RETURN $ComputeAttackDamage("2", "5");""", host));
        Assert.Equal((2, 5), host.Asked);
    }

    /// <summary>A miss and a combatant that is not there are both zero.</summary>
    /// <remarks>
    /// So a script cannot tell a miss from a hit that did nothing, nor either from a bad index.
    /// </remarks>
    [Fact]
    public void A_miss_and_a_missing_combatant_are_both_zero()
    {
        var host = new AttackHost { Damage = 0 };

        Assert.Equal("0", Run("""$RETURN $ComputeAttackDamage("2", "5");""", host));
        Assert.Equal("0", Run("""$RETURN $ComputeAttackDamage("99", "5");""",
                              new GpdlUnhostedEnvironment()));
    }
}
