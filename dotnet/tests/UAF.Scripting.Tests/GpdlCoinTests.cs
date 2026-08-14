using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The coin family — <c>$COINNAME</c>, <c>$COINRATE</c> and <c>$COINCOUNT</c>.
/// </summary>
/// <remarks>
/// All three take a <b>one-based</b> ordinal that the reference decrements before indexing
/// (<c>GPDLexec.cpp:3293</c>–<c>3334</c>). <c>$CURR_CHANGE_BY_VAL</c> belongs to the same family
/// and is <i>not</i> here: it reads <c>GetIntermediateResult()</c>, a VM concept this port does
/// not have.
/// </remarks>
public class GpdlCoinTests
{
    private sealed class CoinHost : GpdlUnhostedEnvironment
    {
        public bool Fighting { get; set; }

        public override bool InCombat => Fighting;

        public override string CoinName(int ordinal) => ordinal switch
        {
            1 => "Platinum",
            2 => "Gold",
            _ => string.Empty,
        };

        public override double CoinRate(int ordinal) => ordinal switch
        {
            1 => 5.0,
            2 => 1.0,
            _ => 0.0,
        };

        public override int CoinCount(int ordinal) => ordinal == 1 ? 37 : 0;
    }

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

    /// <summary>A coin is named by its one-based ordinal.</summary>
    [Theory]
    [InlineData(1, "Platinum")]
    [InlineData(2, "Gold")]
    [InlineData(3, "")]
    public void A_coin_is_named_by_its_ordinal(int ordinal, string expected) =>
        Assert.Equal(expected, Run($"$RETURN $COINNAME({ordinal});", new CoinHost()));

    /// <summary>And priced by it.</summary>
    [Fact]
    public void A_coin_has_a_rate()
    {
        Assert.Equal("5", Run("$RETURN $COINRATE(1);", new CoinHost()));
        Assert.Equal("1", Run("$RETURN $COINRATE(2);", new CoinHost()));

        // A slot the design never configured has rate zero rather than no answer.
        Assert.Equal("0", Run("$RETURN $COINRATE(9);", new CoinHost()));
    }

    /// <summary>
    /// The count comes from the host outside combat, and takes the coin ordinal FIRST.
    /// </summary>
    /// <remarks>
    /// <b>The reference pops once for a two-parameter function.</b> Its table declares
    /// <c>$COINCOUNT(coin_ordinal, party_index)</c> but the runtime pops a single value and calls
    /// it the coin ordinal — so it actually reads the party index and leaves the coin ordinal on
    /// the stack, desynchronising everything after the call. This port pops both. Asking for coin
    /// 1 and character 2 has to give coin 1's count, not character 2's index treated as a coin.
    /// </remarks>
    [Fact]
    public void A_count_is_answered_outside_combat()
    {
        Assert.Equal("37", Run("$RETURN $COINCOUNT(1, 1);", new CoinHost()));

        // The second parameter is the party index and is discarded -- the reference's own
        // index-using block is commented out in favour of the current character context. Coin 1
        // answers 37 whatever is asked for alongside it.
        Assert.Equal("37", Run("$RETURN $COINCOUNT(1, 4);", new CoinHost()));

        // And the FIRST parameter is the coin: asking for coin 2 must not answer coin 1.
        Assert.Equal("0", Run("$RETURN $COINCOUNT(2, 1);", new CoinHost()));
    }

    /// <summary>
    /// And is refused during it.
    /// </summary>
    /// <remarks>
    /// <b>Zero, not the real count.</b> The reference logs an interpreter error and pushes zero,
    /// because the call reads the party's active character and there is no such thing mid-fight.
    /// A host that answered anyway would give a script a number the reference never produces.
    /// </remarks>
    [Fact]
    public void A_count_is_refused_during_combat()
    {
        var peace = new CoinHost();
        var fight = new CoinHost { Fighting = true };

        Assert.Equal("37", Run("$RETURN $COINCOUNT(1, 1);", peace));
        Assert.Equal("0", Run("$RETURN $COINCOUNT(1, 1);", fight));
    }

    /// <summary>An ordinal outside the table names nothing rather than wrapping.</summary>
    /// <remarks>
    /// <b>Including zero.</b> The reference clamps an ordinal above the maximum back to 1 but never
    /// checks the lower bound, so <c>$COINCOUNT(0)</c> reads <c>Coins[-1]</c>. There is no value
    /// there to reproduce, so the port refuses instead.
    /// </remarks>
    [Fact]
    public void An_ordinal_outside_the_table_names_nothing()
    {
        Assert.Equal(string.Empty, Run("$RETURN $COINNAME(0);", new CoinHost()));
        Assert.Equal("0", Run("$RETURN $COINRATE(0);", new CoinHost()));
        Assert.Equal("0", Run("$RETURN $COINCOUNT(0, 1);", new CoinHost()));
    }
}
