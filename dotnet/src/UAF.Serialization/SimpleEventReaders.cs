using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>CHAIN_EVENT</c> — jumps to another event by key.</summary>
public sealed record ChainEvent(GameEventBase Base, uint Chain);

/// <summary>
/// Event subclasses small enough not to warrant a file each.
/// </summary>
/// <remarks>
/// Each is the shared <see cref="GameEventReader"/> base plus a handful of fields. They are grouped
/// rather than split so the base-then-fields shape stays visible at a glance.
/// </remarks>
public static class SimpleEventReaders
{
    /// <summary>
    /// Reads a <c>CHAIN_EVENT</c> (<c>GameEvent.cpp:10261</c>) — the base plus one <c>DWORD</c>.
    /// </summary>
    /// <remarks>
    /// The smallest subclass with any payload of its own. Note the chain target is an event
    /// <i>key</i>, not an index, so it is meaningful only against the level's event list.
    /// </remarks>
    public static ChainEvent ReadChain(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        return new ChainEvent(baseEvent, ar.ReadUInt32());
    }
}
