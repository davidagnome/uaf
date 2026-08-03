using UAF.Scripting;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// What one gate of a <c>LOGIC_BLOCK_DATA</c> does (<c>LOGIC_BLOCK_GATE_TYPE</c>,
/// <c>GameEvent.h:3098</c>).
/// </summary>
/// <remarks>
/// <b>The ordinals are not in the order the editor lists them</b>, and three of the arithmetic
/// gates were appended after the string ones — so the enum reads as though it grew rather than
/// having been designed. It is serialized as a <c>BYTE</c>, so renumbering it would repoint every
/// gate in every design.
/// </remarks>
public enum LogicGate : byte
{
    /// <summary>Pass the top input through unchanged.</summary>
    Top = 0,

    /// <summary>Pass the side input through unchanged.</summary>
    Side = 1,

    And = 2,
    Or = 3,
    Plus = 4,
    Minus = 5,

    /// <summary>Match the top against the side as a pattern, yielding the matched text.</summary>
    Grep = 6,

    True = 7,
    False = 8,
    StringEqual = 9,
    Multiply = 10,
    Divide = 11,
    Greater = 12,
    Modulo = 13,

    NotImplemented = 0xFF,
}

/// <summary>How a logic block chains (<c>m_NoChain</c>).</summary>
public enum LogicBlockChaining : byte
{
    /// <summary>Never — the event ends whatever the result.</summary>
    Never = 0,

    /// <summary>Always, down the event's ordinary chain.</summary>
    Always = 1,

    /// <summary>On the result, to <c>trueChain</c> or <c>falseChain</c>.</summary>
    OnResult = 2,
}

/// <summary>
/// The value at every terminal of one evaluated block.
/// </summary>
/// <param name="Values">
/// The eighteen working slots, in the reference's own numbering — 0‥11 are the inputs and gates
/// in wiring order and 12‥17 the six negations. Exposed whole because the design's
/// <c>LBF_RECORD_VALUES</c> flag writes exactly this array out for the author to inspect.
/// </param>
/// <param name="Result">1 when the final gate produced anything, 0 when it produced empty.</param>
public sealed record LogicBlockValues(IReadOnlyList<string> Values, int Result);

/// <summary>
/// The gate network of a <c>LOGIC_BLOCK_DATA</c> (<c>ProcessLogicBlock</c>,
/// <c>UAFWin/RunEvent.cpp:14360</c>).
/// </summary>
/// <remarks>
/// <para>
/// A logic block is a design's circuit diagram: five inputs (A, B, D, F, G) feed seven gates
/// (C, E, H, I, J, K, L) through six optional inverters, and the last gate's output decides which
/// way the event chains and which of two actions run. 52 of them across the corpus.
/// </para>
/// <para>
/// <b>Every value is a string, and truth is "not empty".</b> There is no boolean type anywhere in
/// the network — a gate yields <c>"1"</c> for true and <c>""</c> for false, arithmetic gates yield
/// their digits, and <c>Result</c> is <c>w[11] == "" ? 0 : 1</c>. So an arithmetic gate producing
/// <c>"0"</c> is <b>true</b>, because the string is not empty. That is the single most surprising
/// thing about this event and it is load-bearing.
/// </para>
/// <para>
/// <b>The topology is fixed and asymmetric.</b> Input A feeds both halves; the left runs
/// A→C→E→I and the right A→H→J→K, and gate L combines the two <i>inverted</i> outputs
/// <c>w15</c> and <c>w17</c> rather than the gates themselves. The operand order differs per gate
/// too — C takes (B, A) where H takes (F, A) — so transcribing the diagram in the comment rather
/// than the calls beneath it gets several of them backwards.
/// </para>
/// <para>
/// <b>Gate L has no inverter.</b> Six gates carry a negation flag and L does not, which is why the
/// serialized negation run is six bytes rather than seven.
/// </para>
/// <para>
/// <b>Scope: this is the network, not the whole event.</b> Reading the five inputs
/// (<c>ProcessLBInput</c>, 220 lines) and running the two actions (<c>ProcessLBAction</c>, 174
/// lines, and the only part that reaches GPDL) are supplied as delegates. They touch a great deal
/// of game state and the actions can move the party, set attributes and run scripts; the network
/// itself is pure and is what decides everything.
/// </para>
/// </remarks>
public static class LogicBlock
{
    /// <summary>The true value every logical gate yields. Anything non-empty would do.</summary>
    public const string True = "1";

    /// <summary>The false value: the empty string, which is what <c>Result</c> tests for.</summary>
    public const string False = "";

    /// <summary>Working slots, matching the reference's <c>CString w[18]</c>.</summary>
    public const int SlotCount = 18;

    /// <summary>Whether a value counts as true — that is, whether it is non-empty.</summary>
    /// <remarks>
    /// <b>Not "is it non-zero".</b> An arithmetic gate that computes <c>"0"</c> is true here,
    /// because emptiness is the only falsehood the network has.
    /// </remarks>
    public static bool IsTrue(string value) => !string.IsNullOrEmpty(value);

    /// <summary>
    /// Applies one gate (<c>ProcessLBGate</c>, <c>RunEvent.cpp:14022</c>).
    /// </summary>
    /// <param name="grep">
    /// Matches <paramref name="side"/> as an EGrep pattern against <paramref name="top"/> and
    /// returns the matched text, or null for no match. The port has no regex engine behind
    /// <c>IGpdlHost.Grep</c> yet, and that seam answers a <c>bool</c> where this needs the matched
    /// substring — so a null delegate makes the gate yield false and says so, rather than
    /// pretending.
    /// </param>
    /// <remarks>
    /// <b>An unrecognised gate type yields the empty string it started with</b>, after logging.
    /// The reference's <c>default</c> arm writes a diagnostic and leaves <c>result</c> at its
    /// constructed value, so a corrupt gate byte reads as false rather than throwing.
    /// </remarks>
    public static string Gate(LogicGate type, string top, string side,
                              Func<string, string, string?>? grep = null) => type switch
    {
        LogicGate.Top => top,
        LogicGate.Side => side,
        LogicGate.And => IsTrue(top) && IsTrue(side) ? True : False,
        LogicGate.Or => IsTrue(top) || IsTrue(side) ? True : False,
        LogicGate.StringEqual => top == side ? True : False,

        // The GPDL arbitrary-precision string arithmetic, which these gates call directly.
        LogicGate.Plus => GpdlLongArithmetic.Add(top, side),
        LogicGate.Minus => GpdlLongArithmetic.Subtract(top, side),
        LogicGate.Multiply => GpdlLongArithmetic.Multiply(top, side),
        LogicGate.Divide => GpdlLongArithmetic.Divide(top, side).Quotient,
        LogicGate.Modulo => GpdlLongArithmetic.Divide(top, side).Remainder,

        // LBagreater is `LongSubtract(side, top)` and then a test for a leading '-', so it is
        // "top > side" -- with the operands the other way round from how it reads.
        LogicGate.Greater => GpdlLongArithmetic.Subtract(side, top).StartsWith('-') ? True : False,

        LogicGate.True => True,
        LogicGate.False => False,

        LogicGate.Grep => grep?.Invoke(top, side) ?? False,

        _ => False,
    };

    /// <summary>
    /// Applies an inverter (<c>ProcessLBNot</c>, <c>RunEvent.cpp:14114</c>).
    /// </summary>
    /// <remarks>
    /// <b>Zero passes the value through unchanged; anything else inverts.</b> The pass-through is
    /// not a copy of the truth value — an arithmetic result keeps its digits, so an uninverted
    /// terminal can carry a number onward into the next gate's arithmetic. Inverting collapses it
    /// to <c>"1"</c> or <c>""</c> and that information is gone.
    /// </remarks>
    public static string Not(byte negate, string input) =>
        negate == 0 ? input : (IsTrue(input) ? False : True);

    /// <summary>
    /// Runs the whole network and reports every terminal.
    /// </summary>
    /// <param name="input">
    /// Reads one input terminal — 'A', 'B', 'D', 'F' or 'G'. <c>ProcessLBInput</c> is not ported;
    /// see the class remarks.
    /// </param>
    /// <remarks>
    /// The wiring is transcribed from the <i>calls</i> at <c>RunEvent.cpp:14427</c>, not from the
    /// ASCII diagram above them — the two do not agree on operand order.
    /// </remarks>
    public static LogicBlockValues Evaluate(LogicBlockEvent block, Func<char, string> input,
                                            Func<string, string, string?>? grep = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(input);

        var w = new string[SlotCount];
        Array.Fill(w, False);

        LogicGate GateAt(char terminal)
        {
            int i = Array.IndexOf(LogicBlockEventReader.GateTerminals, terminal);
            return i >= 0 && i < block.GateTypes.Count ? (LogicGate)block.GateTypes[i]
                                                       : LogicGate.NotImplemented;
        }

        byte NotAt(char terminal)
        {
            int i = Array.IndexOf(LogicBlockEventReader.NegatedTerminals, terminal);
            return i >= 0 && i < block.Negations.Count ? block.Negations[i] : (byte)0;
        }

        string Read(char terminal) => input(terminal) ?? False;

        w[0] = Read('A');
        w[1] = Read('B');
        w[2] = Gate(GateAt('C'), w[1], w[0], grep);
        w[12] = Not(NotAt('C'), w[2]);

        w[3] = Read('D');
        w[4] = Gate(GateAt('E'), w[12], w[3], grep);
        w[13] = Not(NotAt('E'), w[4]);

        w[5] = Read('F');
        w[6] = Read('G');
        w[7] = Gate(GateAt('H'), w[5], w[0], grep);
        w[14] = Not(NotAt('H'), w[7]);

        w[8] = Gate(GateAt('I'), w[13], w[6], grep);
        w[15] = Not(NotAt('I'), w[8]);

        w[9] = Gate(GateAt('J'), w[14], w[3], grep);
        w[16] = Not(NotAt('J'), w[9]);

        w[10] = Gate(GateAt('K'), w[16], w[6], grep);
        w[17] = Not(NotAt('K'), w[10]);

        // L has no inverter, and it combines the two INVERTED outputs rather than the gates.
        w[11] = Gate(GateAt('L'), w[15], w[17], grep);

        return new LogicBlockValues(w, IsTrue(w[11]) ? 1 : 0);
    }

    /// <summary>
    /// Where the event goes next (<c>LOGIC_BLOCK_DATA::OnIdle</c>, <c>RunEvent.cpp:14496</c>).
    /// </summary>
    /// <param name="isValidEvent">Whether an id names an event this level holds.</param>
    /// <returns>The event to run instead, or null to stop. A logic block never chains onward.</returns>
    /// <remarks>
    /// <para>
    /// <b>Only <see cref="LogicBlockChaining.Always"/> follows the ordinary chain.</b> Under
    /// <see cref="LogicBlockChaining.OnResult"/> the block replaces itself with its own true or
    /// false target and the event's <c>chainEventHappen</c> is never consulted — and under
    /// <see cref="LogicBlockChaining.Never"/>, or any value outside the three, the run simply ends.
    /// </para>
    /// <para>
    /// <b>An unreachable conditional target stops the run</b>, as does a conditional branch whose
    /// own flag is clear. The reference pushes a do-nothing event for the first and pops for the
    /// second, which amount to the same thing — and unlike <c>WHO_TRIES</c>, neither falls back on
    /// the ordinary chain.
    /// </para>
    /// </remarks>
    public static uint? NextEvent(LogicBlockEvent block, int result, Func<uint, bool> isValidEvent)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(isValidEvent);

        if ((LogicBlockChaining)block.NoChain != LogicBlockChaining.OnResult)
        {
            return null;
        }

        bool wanted = result == 0 ? block.ChainIfFalse != 0 : block.ChainIfTrue != 0;
        if (!wanted)
        {
            return null;
        }

        uint target = result == 0 ? block.FalseChain : block.TrueChain;
        return isValidEvent(target) ? target : null;
    }

    /// <summary>Whether the block follows the event's ordinary chain rather than its own.</summary>
    public static bool ChainsNormally(LogicBlockEvent block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return (LogicBlockChaining)block.NoChain == LogicBlockChaining.Always;
    }
}
