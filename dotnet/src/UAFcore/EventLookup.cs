using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Finds the events attached to a map cell.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>GameEventList::GetFirstEvent</c> (<c>GameEvent.cpp:3314</c>), which linear-scans
/// the level's flat event list for the first entry whose coordinates match, commenting "find first
/// event in chain at x,y".
/// </para>
/// <para>
/// <b>Cells do not reference events; events reference cells.</b> An <c>AreaMapCell</c> carries only
/// an <c>EventExists</c> flag — no index — and the coordinates live on <see cref="GameEventBase"/>.
/// So the lookup is by search, not by lookup, and several events can share a cell: they form a
/// chain, and the first match is its head.
/// </para>
/// <para>
/// The scan is linear because the original's is. A level's event list is small enough that it has
/// never mattered, and an index built here would have to be invalidated on the same terms the
/// original never bothers with.
/// </para>
/// </remarks>
public sealed class EventLookup(IReadOnlyList<IGameEvent> events)
{
    private readonly IReadOnlyList<IGameEvent> events =
        events ?? throw new ArgumentNullException(nameof(events));

    public int Count => events.Count;

    /// <summary>The first event at a cell, or null when there is none.</summary>
    public IGameEvent? FirstAt(int x, int y)
    {
        foreach (var candidate in events)
        {
            if (candidate.Base.X == x && candidate.Base.Y == y)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Every event at a cell, in list order — the chain.</summary>
    public IEnumerable<IGameEvent> AllAt(int x, int y) =>
        events.Where(e => e.Base.X == x && e.Base.Y == y);

    /// <summary>Whether any event sits at a cell.</summary>
    public bool Any(int x, int y) => FirstAt(x, y) is not null;
}
