using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The gate network of a <c>LOGIC_BLOCK_DATA</c> — a design's circuit diagram.
/// </summary>
/// <remarks>
/// 52 across the corpus. What is exercised here is the network and the chaining; reading the five
/// inputs and running the two actions are supplied as delegates and are not ported.
/// </remarks>
public class LogicBlockTests
{
    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>A block whose gates are all named, with everything else inert.</summary>
    private static LogicBlockEvent Block(
        LogicGate c = LogicGate.False, LogicGate e = LogicGate.False,
        LogicGate h = LogicGate.False, LogicGate i = LogicGate.False,
        LogicGate j = LogicGate.False, LogicGate k = LogicGate.False,
        LogicGate l = LogicGate.False,
        byte notC = 0, byte notE = 0, byte notH = 0,
        byte notI = 0, byte notJ = 0, byte notK = 0,
        LogicBlockChaining chaining = LogicBlockChaining.Never,
        byte chainIfTrue = 1, byte chainIfFalse = 1,
        uint trueChain = 100, uint falseChain = 200) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.LogicBlock, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            falseChain, trueChain,
            ["", "", "", "", ""], ["", ""],
            [(byte)c, (byte)e, (byte)h, (byte)i, (byte)j, (byte)k, (byte)l],
            [0, 0, 0, 0, 0], [0, 0],
            chainIfFalse, chainIfTrue, (byte)chaining,
            [notC, notE, notH, notI, notJ, notK], [0, 0],
            0, string.Empty);

    /// <summary>Feeds named values to the five input terminals.</summary>
    private static Func<char, string> Inputs(string a = "", string b = "", string d = "",
                                             string f = "", string g = "") =>
        terminal => terminal switch
        {
            'A' => a, 'B' => b, 'D' => d, 'F' => f, 'G' => g,
            _ => throw new ArgumentOutOfRangeException(nameof(terminal), terminal, "not an input"),
        };

    // ---- truth is emptiness ----------------------------------------------------------------------

    [Fact]
    public void A_computed_zero_is_true_because_the_string_is_not_empty()
    {
        // The single most surprising thing about this event. There is no boolean type in the
        // network -- Result is `w[11] == "" ? 0 : 1` -- so "0" from an arithmetic gate is TRUE.
        Assert.True(LogicBlock.IsTrue("0"));
        Assert.False(LogicBlock.IsTrue(""));

        Assert.Equal("0", LogicBlock.Gate(LogicGate.Plus, "2", "-2"));
        Assert.True(LogicBlock.IsTrue(LogicBlock.Gate(LogicGate.Plus, "2", "-2")));
    }

    // ---- the gates -------------------------------------------------------------------------------

    [Theory]
    [InlineData(LogicGate.Top, "top", "side", "top")]
    [InlineData(LogicGate.Side, "top", "side", "side")]
    [InlineData(LogicGate.True, "", "", "1")]
    [InlineData(LogicGate.False, "x", "y", "")]
    [InlineData(LogicGate.StringEqual, "x", "x", "1")]
    [InlineData(LogicGate.StringEqual, "x", "y", "")]
    [InlineData(LogicGate.And, "1", "1", "1")]
    [InlineData(LogicGate.And, "1", "", "")]
    [InlineData(LogicGate.Or, "", "1", "1")]
    [InlineData(LogicGate.Or, "", "", "")]
    public void The_logical_gates_do_what_they_say(LogicGate gate, string top, string side,
                                                   string expected)
    {
        Assert.Equal(expected, LogicBlock.Gate(gate, top, side));
    }

    [Theory]
    [InlineData(LogicGate.Plus, "12", "5", "17")]
    [InlineData(LogicGate.Minus, "12", "5", "7")]
    [InlineData(LogicGate.Multiply, "12", "5", "60")]
    [InlineData(LogicGate.Divide, "13", "5", "2")]
    [InlineData(LogicGate.Modulo, "13", "5", "3")]
    public void The_arithmetic_gates_run_on_gpdls_own_string_arithmetic(
        LogicGate gate, string top, string side, string expected)
    {
        // These call LongAdd/LongSubtract/LongMultiply/LongDivide directly, which the GPDL port
        // already has -- so they are arbitrary precision, not int.
        Assert.Equal(expected, LogicBlock.Gate(gate, top, side));
    }

    [Fact]
    public void The_arithmetic_really_is_arbitrary_precision()
    {
        string big = new('9', 40);

        Assert.Equal(41, LogicBlock.Gate(LogicGate.Plus, big, "1").Length);
    }

    [Theory]
    [InlineData("5", "3", "1")]                          // top > side
    [InlineData("3", "5", "")]
    [InlineData("5", "5", "")]                           // strictly greater
    public void Greater_compares_top_against_side(string top, string side, string expected)
    {
        // LBagreater is LongSubtract(side, top) tested for a leading '-', so the operands read
        // the other way round from what it computes.
        Assert.Equal(expected, LogicBlock.Gate(LogicGate.Greater, top, side));
    }

    [Fact]
    public void An_unrecognised_gate_type_is_false_rather_than_a_throw()
    {
        // The reference's default arm logs and leaves `result` at its constructed empty value.
        Assert.Equal("", LogicBlock.Gate((LogicGate)0x7E, "top", "side"));
        Assert.Equal("", LogicBlock.Gate(LogicGate.NotImplemented, "top", "side"));
    }

    [Fact]
    public void Grep_yields_the_matched_text_and_is_false_without_an_engine()
    {
        // The port has no regex engine behind IGpdlHost.Grep, and that seam answers a bool where
        // this gate needs the matched substring. A null delegate is false, not a pretend match.
        Assert.Equal("", LogicBlock.Gate(LogicGate.Grep, "hello world", "wor"));
        Assert.Equal("wor", LogicBlock.Gate(LogicGate.Grep, "hello world", "wor",
                                            (top, pattern) => top.Contains(pattern) ? pattern : null));
    }

    // ---- the inverters ---------------------------------------------------------------------------

    [Fact]
    public void A_cleared_inverter_passes_the_value_through_with_its_digits_intact()
    {
        // Not a copy of the truth value: an arithmetic result keeps its digits and can feed the
        // next gate's arithmetic. Inverting collapses it and that information is gone.
        Assert.Equal("42", LogicBlock.Not(0, "42"));
        Assert.Equal("", LogicBlock.Not(1, "42"));
        Assert.Equal("1", LogicBlock.Not(1, ""));
    }

    // ---- the topology ----------------------------------------------------------------------------

    [Fact]
    public void Gate_c_takes_b_as_its_top_and_a_as_its_side()
    {
        var values = LogicBlock.Evaluate(Block(c: LogicGate.Top), Inputs(a: "AAA", b: "BBB"));

        Assert.Equal("BBB", values.Values[2]);

        values = LogicBlock.Evaluate(Block(c: LogicGate.Side), Inputs(a: "AAA", b: "BBB"));
        Assert.Equal("AAA", values.Values[2]);
    }

    [Fact]
    public void Gate_h_takes_f_as_its_top_and_a_as_its_side()
    {
        // The asymmetry with C: both draw their side from A, but H's top is F rather than B. The
        // ASCII diagram above the calls does not agree with the calls, and the calls are right.
        var values = LogicBlock.Evaluate(Block(h: LogicGate.Top), Inputs(a: "AAA", f: "FFF"));

        Assert.Equal("FFF", values.Values[7]);
    }

    [Fact]
    public void Gate_l_combines_the_two_inverted_outputs_rather_than_the_gates()
    {
        // L reads w15 and w17 -- the outputs of I's and K's inverters -- so an inverter left clear
        // still routes through those slots.
        var block = Block(i: LogicGate.True, k: LogicGate.False,
                          notI: 1,                       // w15 = not(true) = ""
                          l: LogicGate.Top);

        var values = LogicBlock.Evaluate(block, Inputs());

        Assert.Equal("1", values.Values[8]);             // gate I itself
        Assert.Equal("", values.Values[15]);             // inverted
        Assert.Equal("", values.Values[11]);             // L took the inverted one
        Assert.Equal(0, values.Result);
    }

    [Fact]
    public void The_left_branch_runs_a_to_c_to_e_to_i()
    {
        // A true at A survives C(top=B) only through the side, so this pins the chain rather than
        // any one gate: A -> C -> notC -> E -> notE -> I -> notI -> L.
        var block = Block(c: LogicGate.Side, e: LogicGate.Top, i: LogicGate.Top,
                          l: LogicGate.Top);

        Assert.Equal(1, LogicBlock.Evaluate(block, Inputs(a: "yes")).Result);
        Assert.Equal(0, LogicBlock.Evaluate(block, Inputs(a: "")).Result);
    }

    [Fact]
    public void Result_is_one_when_the_last_gate_produced_anything_at_all()
    {
        Assert.Equal(1, LogicBlock.Evaluate(Block(l: LogicGate.True), Inputs()).Result);
        Assert.Equal(0, LogicBlock.Evaluate(Block(l: LogicGate.False), Inputs()).Result);
    }

    [Fact]
    public void All_eighteen_slots_are_reported()
    {
        // LBF_RECORD_VALUES writes exactly this array out for the design author, so the whole
        // array is the useful unit rather than the result alone.
        var values = LogicBlock.Evaluate(Block(), Inputs());

        Assert.Equal(LogicBlock.SlotCount, values.Values.Count);
        Assert.All(values.Values, v => Assert.NotNull(v));
    }

    // ---- chaining --------------------------------------------------------------------------------

    [Fact]
    public void Only_chain_always_follows_the_events_ordinary_chain()
    {
        Assert.True(LogicBlock.ChainsNormally(Block(chaining: LogicBlockChaining.Always)));
        Assert.False(LogicBlock.ChainsNormally(Block(chaining: LogicBlockChaining.Never)));
        Assert.False(LogicBlock.ChainsNormally(Block(chaining: LogicBlockChaining.OnResult)));
    }

    [Theory]
    [InlineData(1, 100u)]
    [InlineData(0, 200u)]
    public void A_conditional_block_replaces_itself_with_its_own_target(int result, uint expected)
    {
        var block = Block(chaining: LogicBlockChaining.OnResult);

        Assert.Equal(expected, LogicBlock.NextEvent(block, result, _ => true));
    }

    [Fact]
    public void A_conditional_branch_whose_flag_is_clear_stops_the_run()
    {
        var block = Block(chaining: LogicBlockChaining.OnResult, chainIfTrue: 0);

        Assert.Null(LogicBlock.NextEvent(block, 1, _ => true));
        Assert.Equal(200u, LogicBlock.NextEvent(block, 0, _ => true));
    }

    [Fact]
    public void An_unreachable_conditional_target_stops_rather_than_falling_back()
    {
        // The reference pushes a do-nothing event, which amounts to stopping -- and unlike
        // WHO_TRIES, it does not fall back on the ordinary chain.
        var block = Block(chaining: LogicBlockChaining.OnResult);

        Assert.Null(LogicBlock.NextEvent(block, 1, _ => false));
    }

    [Theory]
    [InlineData(LogicBlockChaining.Never)]
    [InlineData((LogicBlockChaining)7)]
    public void Anything_but_a_conditional_block_names_no_target(LogicBlockChaining chaining)
    {
        // Chain-always goes down the ordinary chain, which is not this method's business; never
        // and any out-of-range value end the run.
        Assert.Null(LogicBlock.NextEvent(Block(chaining: chaining), 1, _ => true));
    }
}
