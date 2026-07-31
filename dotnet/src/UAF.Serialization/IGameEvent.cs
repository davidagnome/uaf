namespace UAF.Serialization;

/// <summary>
/// A parsed event body, whatever its type.
/// </summary>
/// <remarks>
/// <para>
/// Every event record already carries a <see cref="GameEventBase"/> as its first member — the
/// shared header the C++ writes ahead of each subclass's own fields — so this interface costs the
/// records nothing but gives the readers a return type. Until it existed, <c>LevelFileReader</c>
/// took a callback returning <c>bool</c> and <b>discarded every event it parsed</b>: the port
/// could prove it understood all 6,234 events across the reference designs and could not hand one
/// to a caller.
/// </para>
/// <para>
/// That is the gate on the whole content layer. A design's traps, shops, conversations, teleports
/// and combats are all events, so nothing downstream can begin while they are a count.
/// </para>
/// </remarks>
public interface IGameEvent
{
    /// <summary>The shared header every event type begins with.</summary>
    GameEventBase Base { get; }
}
