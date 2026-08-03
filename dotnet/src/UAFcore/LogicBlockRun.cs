using UAF.Serialization;

namespace UAFcore;

/// <summary>What running one logic block produced.</summary>
/// <param name="Values">Every terminal, for a design that asked to record them.</param>
/// <param name="Result">1 when the final gate produced anything, 0 when it produced empty.</param>
/// <param name="ChainTo">
/// The event to run instead, or null to stop. Null does <b>not</b> mean "follow the ordinary
/// chain" — see <see cref="ChainsNormally"/>.
/// </param>
/// <param name="ChainsNormally">
/// True when the block defers to the event's own <c>chainEventHappen</c> rather than its two
/// targets.
/// </param>
public sealed record LogicBlockOutcome(
    LogicBlockValues Values, int Result, uint? ChainTo, bool ChainsNormally);

/// <summary>
/// Runs a whole <c>LOGIC_BLOCK_DATA</c> — its five inputs, its gate network, its two actions and
/// its chaining (<c>ProcessLogicBlock</c>, <c>RunEvent.cpp:14360</c>).
/// </summary>
/// <remarks>
/// <para>
/// The three halves were ported separately and unwired — <see cref="LogicBlock"/> for the gates,
/// <see cref="LogicBlockInputs"/> for the terminals, <see cref="LogicBlockActions"/> for the
/// writes — because a gate network fed all-false inputs takes a branch rather than failing
/// visibly. This is what joins them.
/// </para>
/// <para>
/// <b>The actions run after the network and before the chaining</b>, and they see the working
/// slots the network produced — which is how a design writes a computed value into an attribute.
/// </para>
/// </remarks>
public static class LogicBlockRun
{
    /// <summary>Runs one block.</summary>
    /// <param name="isValidEvent">Whether an id names an event this level holds.</param>
    /// <param name="grep">
    /// The regex gate's matcher, and the thing that fills the captures
    /// <see cref="LogicInput.Wiggle"/> later reads.
    /// </param>
    /// <param name="runInputScript">Runs a GPDL input program; null refuses those terminals.</param>
    /// <param name="runActionScript">Runs a GPDL action program; null refuses those actions.</param>
    public static LogicBlockOutcome Run(
        LogicBlockEvent block, ILogicBlockActionHost host, Func<uint, bool> isValidEvent,
        Func<string, string, string?>? grep = null,
        Func<string, IReadOnlyList<string>, string>? runInputScript = null,
        Action<string>? runActionScript = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(isValidEvent);

        // The slots the network has filled so far, which a later terminal's parameter can name
        // through &A..&L. Passed by reference into the input reader for exactly that reason.
        var slots = new string[LogicBlock.SlotCount];
        Array.Fill(slots, string.Empty);

        var values = LogicBlock.Evaluate(
            block,
            terminal =>
            {
                var (type, parameter) = InputAt(block, terminal);
                string read = LogicBlockInputs.Read(type, parameter, slots, host, runInputScript);

                // Keep the slot in step as the network fills it, so a terminal read later can
                // name one read earlier.
                int slot = terminal - 'A';
                if (slot >= 0 && slot < slots.Length)
                {
                    slots[slot] = read;
                }
                return read;
            },
            grep);

        // Everything the network computed, for the actions' &A..&L.
        for (int i = 0; i < slots.Length && i < values.Values.Count; i++)
        {
            slots[i] = values.Values[i];
        }

        bool result = values.Result != 0;
        for (int i = 0; i < ActionCount; i++)
        {
            var (when, type, parameter) = ActionAt(block, i);
            LogicBlockActions.Run(when, result, type, parameter, slots, host, runActionScript);
        }

        return new LogicBlockOutcome(
            values, values.Result,
            LogicBlock.NextEvent(block, values.Result, isValidEvent),
            LogicBlock.ChainsNormally(block));
    }

    /// <summary>How many actions a block carries.</summary>
    public const int ActionCount = 2;

    /// <summary>The type and parameter behind one input terminal.</summary>
    private static (LogicInput Type, string Parameter) InputAt(LogicBlockEvent block, char terminal)
    {
        int i = Array.IndexOf(LogicBlockEventReader.InputTerminals, terminal);
        if (i < 0)
        {
            return (LogicInput.NotImplemented, string.Empty);
        }

        var type = i < block.InputTypes.Count ? (LogicInput)block.InputTypes[i]
                                              : LogicInput.NotImplemented;
        string parameter = i < block.Inputs.Count ? block.Inputs[i] : string.Empty;
        return (type, parameter);
    }

    /// <summary>When, what and with what, for one of the two actions.</summary>
    private static (LogicActionWhen When, LogicAction Type, string Parameter) ActionAt(
        LogicBlockEvent block, int index)
    {
        var when = index < block.IfTrue.Count ? (LogicActionWhen)block.IfTrue[index]
                                               : LogicActionWhen.Always;
        var type = index < block.ActionTypes.Count ? (LogicAction)block.ActionTypes[index]
                                                   : LogicAction.Nothing;
        string parameter = index < block.ActionParams.Count ? block.ActionParams[index]
                                                            : string.Empty;
        return (when, type, parameter);
    }
}
