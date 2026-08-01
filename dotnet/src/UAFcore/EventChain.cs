using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// When an event hands control to another one (<c>chainTriggerType</c>,
/// <c>Shared/GameEvent.h:308</c>).
/// </summary>
/// <remarks>
/// Ordinal values — the field is serialized as an int on every event, so renumbering would repoint
/// every design's chains.
/// </remarks>
public enum ChainTrigger
{
    /// <summary>Chain whether the event ran or was suppressed.</summary>
    Always = 0,

    /// <summary>Chain only when the event actually ran.</summary>
    IfHappened = 1,

    /// <summary>Chain only when the event was suppressed.</summary>
    IfNotHappened = 2,
}

/// <summary>
/// Decides which event, if any, follows this one
/// (<c>ChainHappened</c>, <c>UAFWin/RunEvent.cpp:855</c>, and <c>OnChainNotHappened</c>,
/// <c>:893</c>).
/// </summary>
/// <remarks>
/// <para>
/// Chaining is how a design builds anything larger than a single cell: a question's options each
/// name an event, a text statement can lead into a teleport, a trap chains to a treasure. Without
/// it every event is an island.
/// </para>
/// <para>
/// <b>The two paths are not symmetric under <see cref="ChainTrigger.Always"/>.</b> The
/// not-happened path chains to <c>chainEventHappen</c> — the <i>happened</i> target — not to
/// <c>chainEventNotHappen</c> (<c>RunEvent.cpp:910</c>). So an <c>Always</c> event has one
/// destination regardless of whether it fired, and its not-happened id is dead unless the trigger
/// is <see cref="ChainTrigger.IfNotHappened"/>. That reads like a typo and is load-bearing: a
/// design relying on it would follow a different path if it were "fixed".
/// </para>
/// <para>
/// A target of 0 means "none". The guard is <c>&gt; 0</c> on both paths, so event id 0 can never be
/// chained to.
/// </para>
/// </remarks>
public static class EventChain
{
    /// <summary>
    /// The event id to run after <paramref name="source"/>, or null to stop.
    /// </summary>
    /// <param name="happened">
    /// Whether the event ran. False when <see cref="EventTrigger"/> suppressed it — which is the
    /// only way the original reaches <c>OnChainNotHappened</c>, since no event calls it directly.
    /// </param>
    public static uint? Next(GameEventBase source, bool happened)
    {
        ArgumentNullException.ThrowIfNull(source);

        var trigger = (ChainTrigger)source.Control.ChainTrigger;
        uint onHappened = (uint)source.ChainEventHappen;
        uint onNotHappened = (uint)source.ChainEventNotHappen;

        if (happened)
        {
            return trigger is ChainTrigger.IfHappened or ChainTrigger.Always && onHappened > 0
                ? onHappened
                : null;
        }

        if (trigger == ChainTrigger.IfNotHappened && onNotHappened > 0)
        {
            return onNotHappened;
        }

        // Always, on the not-happened path, takes the HAPPENED target -- see the remarks.
        return trigger == ChainTrigger.Always && onHappened > 0 ? onHappened : null;
    }
}
