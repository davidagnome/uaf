using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;

namespace UAFedit.Events;

/// <summary>
/// One row of the event list.
/// </summary>
/// <remarks>
/// <para>
/// The original shows this as a tree, with an event's chain targets as its children
/// (<c>PopulateTreeChildren</c>, <c>EventViewer.cpp:2667</c>). A flat list plus an explicit chain
/// pane is a different trade: the tree makes structure obvious and makes everything else hard —
/// an event chained from three places appears three times, an unchained subroutine event appears
/// nowhere near its caller, and the walker has no visited-set, so genuinely cyclic data recurses
/// until the stack goes (the only guard is an <c>ASSERT</c> at <c>:2762</c>). A list is one row per
/// event, always, and the chains are shown from both directions beside it.
/// </para>
/// <para>
/// <see cref="Body"/> is settable because an edit replaces the record: the row holds the current
/// one and re-derives its columns rather than caching strings.
/// </para>
/// </remarks>
public sealed partial class EventListItemViewModel : ObservableObject
{
    public EventListItemViewModel(int index, IGameEvent body, EventType declaredType)
    {
        Index = index;
        this.body = body;
        DeclaredType = declaredType;
    }

    /// <summary>Position in the level's event list — the order on the wire.</summary>
    public int Index { get; }

    /// <summary>
    /// The ordinal the level's event list stored, which is not necessarily the one on the record.
    /// </summary>
    /// <remarks>
    /// The tag appears twice on the wire and both reads are real bytes (<c>EventDispatch</c>). They
    /// agree in every design that loads; keeping the list's copy means a design where they did not
    /// would show it rather than hide it.
    /// </remarks>
    public EventType DeclaredType { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(Position))]
    [NotifyPropertyChangedFor(nameof(Chains))]
    [NotifyPropertyChangedFor(nameof(TypeName))]
    private IGameEvent body;

    /// <summary>The chain currency — what other events point at.</summary>
    public uint Id => Body.Base.Id;

    public string TypeName => EventCatalog.Name((EventType)Body.Base.EventType);

    /// <summary>
    /// The cell the event sits on.
    /// </summary>
    /// <remarks>
    /// <b>Not every event has one.</b> A chained event is reached by id, so designs use off-map
    /// events as subroutines (<see cref="UAFcore.EventLookup.ById"/>) and those carry whatever
    /// coordinates they were created at.
    /// </remarks>
    public string Position => $"{Body.Base.X},{Body.Base.Y}";

    public string Summary => EventSummary.For(Body);

    /// <summary>Where this event can send control, as a glance.</summary>
    public string Chains
    {
        get
        {
            var links = EventChainLinks.Of(Body);

            return links.Count == 0
                ? string.Empty
                : string.Join(", ", links.Select(l => l.Taken ? l.Target.ToString()
                                                              : $"({l.Target})"));
        }
    }

    /// <summary>True once an edit has replaced the record this row started with.</summary>
    [ObservableProperty]
    private bool isModified;
}

/// <summary>
/// One edge of the chain graph, in either direction, with a jump.
/// </summary>
/// <remarks>
/// The outgoing list is <see cref="EventChainLinks"/> over the selected event. The incoming list is
/// the reverse relation, which the original's event editor <b>does not have at all</b> — the only
/// reverse lookup in the codebase is the <c>O(n·15)</c> rescan inside
/// <c>GameEventList::DumpEventText</c> (<c>Shared/GameEvent.cpp:4313</c>) and the separate
/// Cross Reference dialog. Following a chain backwards is the question a designer actually asks
/// ("what leads here, and can I change this safely?"), so it is a first-class pane here.
/// </remarks>
public sealed partial class EventChainReferenceViewModel(
    string label, uint targetId, string description, bool taken, bool resolves,
    Action<uint> goTo) : ObservableObject
{
    /// <summary>The slot's name, in the original's vocabulary — "Accept Chain", "Yes Chain".</summary>
    public string Label { get; } = label;

    /// <summary>The event at the other end: the id this row jumps to.</summary>
    public uint TargetId { get; } = targetId;

    /// <summary>Type, id and summary of the event at the other end.</summary>
    public string Description { get; } = description;

    /// <summary>
    /// False when the <c>chainTrigger</c> means this edge is never followed.
    /// </summary>
    /// <remarks>
    /// Stored and dead is a real and common state — see the remarks on <see cref="EventChainLinks"/>
    /// — and it is the explanation for most "my branch never fires" reports.
    /// </remarks>
    public bool IsTaken { get; } = taken;

    /// <summary>False when the id names no event: a dangling chain.</summary>
    public bool IsBroken { get; } = !resolves;

    [RelayCommand]
    private void Follow()
    {
        if (!IsBroken)
        {
            goTo(TargetId);
        }
    }
}
