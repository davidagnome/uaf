using UAF.Serialization;

namespace UAFcore;

/// <summary>How a <c>FLOW_CONTROL_EVENT_DATA</c> changes its global (<c>GameEvent.h:2043</c>).</summary>
public enum ValueModification
{
    Illegal = 0,
    NoChange = 1,
    Set = 2,
    Increment = 3,
    Decrement = 4,
}

/// <summary>What a flow-control event does afterwards (<c>GameEvent.h:2053</c>).</summary>
/// <remarks>
/// <b>Only <see cref="None"/> is distinguished at runtime.</b> <c>GOTO</c>, <c>CALL</c>,
/// <c>RETURN</c> and <c>POP</c> all take the same branch in
/// <c>FLOW_CONTROL_EVENT_DATA::OnInitialEvent</c> — go to <c>destID</c> — so the call stack the
/// last three imply was never built. They are kept named because the value is serialized and an
/// editor will need them.
/// </remarks>
public enum FlowAction
{
    Illegal = 0,
    None = 1,
    Goto = 2,
    Call = 3,
    Return = 4,
    Pop = 5,
}

/// <summary>When the action fires (<c>GameEvent.h:2064</c>).</summary>
public enum FlowCondition
{
    Illegal = 0,
    Always = 1,
    Equals = 2,
    NotEquals = 3,
}

/// <summary>What a flow-control event decided to do next.</summary>
/// <param name="GoTo">
/// The event to run instead of this one, or null to follow the ordinary chain.
/// </param>
/// <param name="Stop">
/// True when the run ends here — the action fired and named an event the level does not contain.
/// </param>
public readonly record struct FlowOutcome(uint? GoTo, bool Stop)
{
    /// <summary>Follow the event's own chain, as <c>ChainHappened()</c> does.</summary>
    public static readonly FlowOutcome Chain = new(null, false);
}

/// <summary>
/// Runs a <c>FLOW_CONTROL_EVENT_DATA</c> (<c>RunEvent.cpp:11540</c>) — the design's `if` statement.
/// </summary>
/// <remarks>
/// <para>
/// The most common unexecuted event type in the corpus by a wide margin: 314 of them across the
/// four designs. It does two things in order — modify a global attribute, then branch on the
/// result — and both halves have edges worth stating.
/// </para>
/// <para>
/// <b>The three markers are not used here.</b> <c>entryMarker</c>, <c>exitMarker</c> and
/// <c>destinationMarker</c> are editor navigation aids; the runtime branches on <c>destID</c>
/// alone. A port that resolved markers at runtime would be inventing a mechanism.
/// </para>
/// </remarks>
public static class FlowControl
{
    /// <summary>
    /// Applies the event and says what should happen next.
    /// </summary>
    /// <param name="globals">The global attribute list — <see cref="Game.Globals"/>.</param>
    /// <param name="isValidEvent">Whether an id names an event this level holds.</param>
    /// <remarks>
    /// <para>
    /// <b>The variable is modified even when the action is <see cref="FlowAction.None"/>.</b> The
    /// reference does the modification first and only then tests the action, so a
    /// design using flow control purely as a counter — no branch at all — still counts.
    /// </para>
    /// <para>
    /// <b>"Equals" starts out true when the event carries no value.</b> It is initialised from
    /// <c>value.IsEmpty()</c> before anything is read, so an event with no value and an
    /// <see cref="FlowCondition.Equals"/> condition fires unless a lookup overwrites the answer.
    /// </para>
    /// <para>
    /// <b>Increment and decrement do nothing at all to a variable that does not exist yet</b> —
    /// the reference breaks out before the insert, so there is no implicit "starts at zero". Only
    /// <see cref="ValueModification.Set"/> creates one. A design that increments an unset counter
    /// gets no counter, which is worth knowing before assuming a design's logic is broken.
    /// </para>
    /// <para>
    /// The comparison is textual throughout: increment reads the value with C's <c>atoi</c>
    /// semantics — leading digits, zero for anything else — writes it back with <c>%d</c>, and then
    /// compares the design's value string against that numeral. So a value of <c>"007"</c> never
    /// equals an incremented <c>"7"</c>.
    /// </para>
    /// </remarks>
    public static FlowOutcome Run(FlowControlEvent flow, AttributeList globals,
                                  Func<uint, bool> isValidEvent)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(isValidEvent);

        bool equals = flow.Value.Length == 0;

        if (flow.GlobalVariableName.Length > 0)
        {
            equals = Modify(flow, globals, equals);
        }

        if ((FlowAction)flow.Action == FlowAction.None)
        {
            return FlowOutcome.Chain;
        }

        bool doAction = (FlowCondition)flow.ActionCondition switch
        {
            FlowCondition.Always => true,
            FlowCondition.Equals => equals,
            FlowCondition.NotEquals => !equals,
            _ => false,                                  // CONDITION_ILLEGAL falls through as false
        };

        if (!doAction || flow.DestinationId == 0)
        {
            return FlowOutcome.Chain;
        }

        // A destination the level does not contain ends the run, exactly as CHAIN_EVENT's does.
        return isValidEvent(flow.DestinationId)
            ? new FlowOutcome(flow.DestinationId, Stop: false)
            : new FlowOutcome(null, Stop: true);
    }

    /// <summary>Applies the variable change and reports whether the value now matches.</summary>
    private static bool Modify(FlowControlEvent flow, AttributeList globals, bool equals)
    {
        switch ((ValueModification)flow.ValueModification)
        {
            case ValueModification.NoChange:
                // A read, not a write. A variable that is not there leaves `equals` alone rather
                // than comparing against empty.
                return globals.Find(flow.GlobalVariableName) is { } current
                    ? flow.Value == current
                    : equals;

            case ValueModification.Set:
                // The only case that creates a variable -- and note the flags are 0, not Modified,
                // so a value set this way is not marked as changed from the design's own.
                globals.Insert(flow.GlobalVariableName, flow.Value);
                return true;

            case ValueModification.Increment:
            case ValueModification.Decrement:
                if (globals.Find(flow.GlobalVariableName) is not { } existing)
                {
                    return equals;
                }

                int step = (ValueModification)flow.ValueModification == ValueModification.Increment
                    ? 1
                    : -1;
                string updated = (Atoi(existing) + step).ToString();

                globals.Insert(flow.GlobalVariableName, updated, AttributeFlags.Modified);
                return flow.Value == updated;

            default:
                return equals;
        }
    }

    /// <summary>
    /// C's <c>atoi</c>: leading whitespace, an optional sign, then digits — and <b>zero</b> for
    /// anything it cannot read, with no error.
    /// </summary>
    /// <remarks>
    /// <c>int.TryParse</c> is not a substitute. It rejects <c>"12 apples"</c> where <c>atoi</c>
    /// returns 12, and rejects trailing text a design may well have put there.
    /// </remarks>
    public static int Atoi(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int i = 0;
        while (i < value.Length && char.IsWhiteSpace(value[i]))
        {
            i++;
        }

        int sign = 1;
        if (i < value.Length && (value[i] == '+' || value[i] == '-'))
        {
            sign = value[i] == '-' ? -1 : 1;
            i++;
        }

        long result = 0;
        while (i < value.Length && char.IsAsciiDigit(value[i]))
        {
            result = (result * 10) + (value[i] - '0');
            i++;

            // Overflow in C is undefined; clamping keeps a runaway counter from wrapping to a
            // negative, which would read as a plausible value.
            if (result > int.MaxValue)
            {
                return sign > 0 ? int.MaxValue : int.MinValue;
            }
        }

        return (int)(result * sign);
    }
}
