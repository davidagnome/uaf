using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events;

/// <summary>One entry of the level picker.</summary>
/// <param name="Index">
/// The index into <see cref="LoadedDesign.LevelFiles"/>, which is <b>not</b> the level number:
/// designs ship gaps, and Case.dsn's tenth file is <c>Level255.lvl</c>.
/// </param>
public sealed record EventLevelChoice(int Index, string Label);

/// <summary>
/// The Level menu's Event Editor (<c>ID_VIEW_EVENTS</c>, <c>CEventViewer</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here writes a byte.</b> Events arrive through <see cref="LoadedDesign.Level"/> and
/// edits stay in <see cref="EditedEvents"/>; there is no call to a writer anywhere in this
/// namespace. That is deliberate for now — the round trip is the serialization layer's contract to
/// keep, and an editor that saved before that was proven would corrupt designs quietly.
/// </para>
/// <para>
/// <b>The unit of work is one level.</b> Chain ids are level-local: a chain target is resolved
/// against the level's own event list (<c>GameEventList::GetEvent</c>), so an id means nothing
/// outside the level that stored it. Switching levels therefore discards the current selection and
/// rebuilds the graph, and edits to a level are kept per level in
/// <see cref="EditedEvents"/>.
/// </para>
/// <para>
/// <b>Dirtiness is computed, not flagged.</b> The event records are C# <c>record</c>s, so an event
/// edited and edited back compares equal to the one that was loaded and the level goes clean again.
/// That holds because a <c>with</c> expression shares the list references it did not touch — the
/// generated <c>Equals</c> compares <c>IReadOnlyList</c> members by reference, which is exactly
/// right here and would be wrong if anything mutated a list in place. Nothing does; see
/// <c>EventDetailFields.Replace</c>.
/// </para>
/// </remarks>
public sealed partial class EventEditorViewModel : ObservableObject, IEventFieldHost
{
    private readonly LoadedDesign? design;

    /// <summary>The bodies as loaded, for the selected level — the baseline dirtiness is against.</summary>
    private IReadOnlyList<IGameEvent> original = [];

    /// <summary>Opens the editor over a design, showing its first level.</summary>
    public EventEditorViewModel(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);

        this.design = design;

        var files = design.LevelFiles;
        for (int i = 0; i < files.Count; i++)
        {
            Levels.Add(new EventLevelChoice(i, Path.GetFileNameWithoutExtension(files[i])));
        }

        SelectedLevel = Levels.FirstOrDefault();
    }

    /// <summary>
    /// Opens the editor over one already-read level.
    /// </summary>
    /// <remarks>
    /// The seam the tests use, and the reason none of them need a design on disk to exercise
    /// selection, editing or chain navigation.
    /// </remarks>
    public EventEditorViewModel(LevelFile level, string label = "Level")
    {
        ArgumentNullException.ThrowIfNull(level);

        Levels.Add(new EventLevelChoice(0, label));
        selectedLevel = Levels[0];
        Load(level);
    }

    /// <summary>Every level file in the design, in name order.</summary>
    public ObservableCollection<EventLevelChoice> Levels { get; } = [];

    [ObservableProperty]
    private EventLevelChoice? selectedLevel;

    partial void OnSelectedLevelChanged(EventLevelChoice? value)
    {
        if (design is null || value is null)
        {
            return;
        }

        var level = design.Level(value.Index);
        if (level is null)
        {
            // Level() returns null on an event type the port cannot read, because there is no way
            // to step over a body of unknown length. Saying so beats an empty list that looks like
            // a level with no events.
            Reset();
            Status = $"{value.Label} could not be read — it contains an event type this port "
                     + "does not know.";
            return;
        }

        Load(level);
    }

    /// <summary>The events of the selected level, in wire order.</summary>
    public ObservableCollection<EventListItemViewModel> Events { get; } = [];

    [ObservableProperty]
    private EventListItemViewModel? selectedEvent;

    partial void OnSelectedEventChanged(EventListItemViewModel? value) => Rebuild();

    /// <summary>Identity and placement — mostly read-only.</summary>
    public ObservableCollection<EventFieldViewModel> IdentityFields { get; } = [];

    /// <summary>Trigger, once-only, chain trigger and the two chain ids.</summary>
    public ObservableCollection<EventFieldViewModel> ControlFields { get; } = [];

    /// <summary>The trigger's operands, greyed when the trigger does not read them.</summary>
    public ObservableCollection<EventFieldViewModel> TriggerFields { get; } = [];

    /// <summary>The three display strings, which is where nearly all event content lives.</summary>
    public ObservableCollection<EventFieldViewModel> TextFields { get; } = [];

    /// <summary>The selected event's own fields.</summary>
    public ObservableCollection<EventFieldViewModel> DetailFields { get; } = [];

    /// <summary>The selected event's nested blocks — options, steps, monsters, transfers.</summary>
    public ObservableCollection<EventFieldGroupViewModel> DetailGroups { get; } = [];

    /// <summary>The attribute lists, which carry three of the trigger's operands.</summary>
    public ObservableCollection<EventAttributeViewModel> Attributes { get; } = [];

    /// <summary>Events this one can hand control to.</summary>
    public ObservableCollection<EventChainReferenceViewModel> Outgoing { get; } = [];

    /// <summary>Events that can hand control to this one.</summary>
    public ObservableCollection<EventChainReferenceViewModel> Incoming { get; } = [];

    /// <summary>False when the selected event ends its run.</summary>
    /// <remarks>
    /// A property rather than a <c>Count</c> binding so the empty case has somewhere to say what it
    /// means: "nothing" is a different fact for the two directions.
    /// </remarks>
    public bool HasOutgoing => Outgoing.Count > 0;

    /// <summary>False when nothing chains here — a map-triggered event, or an unreachable one.</summary>
    public bool HasIncoming => Incoming.Count > 0;

    /// <summary>False when neither attribute list carries anything.</summary>
    public bool HasAttributes => Attributes.Count > 0;

    [ObservableProperty]
    private string status = "No level selected.";

    /// <summary>True while any event of the selected level differs from what was read.</summary>
    [ObservableProperty]
    private bool isDirty;

    /// <summary>
    /// The events as they now stand, edits included.
    /// </summary>
    /// <remarks>
    /// The point of the whole exercise: a plain list of records a writer could take, in the same
    /// order they were read, with no view-model types in it.
    /// </remarks>
    public IReadOnlyList<IGameEvent> EditedEvents => [..Events.Select(e => e.Body)];

    /// <summary>The events that have actually changed, for a save that wants to be selective.</summary>
    public IReadOnlyList<IGameEvent> ChangedEvents =>
        [..Events.Where(e => e.IsModified).Select(e => e.Body)];

    /// <summary>The event being edited, or null when nothing is selected.</summary>
    public IGameEvent? Current => SelectedEvent?.Body;

    /// <summary>Takes an edited record and re-derives everything that depends on it.</summary>
    public void Apply(IGameEvent updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        if (SelectedEvent is not { } row)
        {
            return;
        }

        row.Body = updated;

        // Records compare structurally, so an edit undone leaves the row clean again.
        row.IsModified = row.Index >= original.Count || !Equals(original[row.Index], updated);
        IsDirty = Events.Any(e => e.IsModified);

        RefreshRelevance();
        RebuildChains();
    }

    /// <summary>Whether an id names an event of this level.</summary>
    public bool Resolves(uint id) => id > 0 && Events.Any(e => e.Id == id);

    /// <summary>
    /// Selects the event with an id.
    /// </summary>
    /// <remarks>
    /// <b>Ids are not guaranteed unique.</b> Nothing in the format enforces it and the engine's own
    /// lookup takes the first match (<see cref="EventLookup.ById"/>), so this does too rather than
    /// pretending the question has one answer.
    /// </remarks>
    public void GoTo(uint id)
    {
        if (Events.FirstOrDefault(e => e.Id == id) is { } target)
        {
            SelectedEvent = target;
        }
    }

    [RelayCommand]
    private void FollowChain(uint id) => GoTo(id);

    /// <summary>Reads a level in and replaces everything derived from the old one.</summary>
    private void Load(LevelFile level)
    {
        Reset();

        original = level.Events;

        // Entries carries the wire order including bodyless tags; Events drops them. Walking
        // Entries and skipping the nulls keeps the declared ordinal beside each body, which is the
        // only place the two copies of the event type can be compared.
        int index = 0;
        foreach (var entry in level.Entries)
        {
            if (entry.Body is { } body)
            {
                Events.Add(new EventListItemViewModel(index++, body, entry.Type));
            }
        }

        Status = $"{Events.Count} events, {level.Width} x {level.Height}";
        SelectedEvent = Events.FirstOrDefault();
    }

    private void Reset()
    {
        Events.Clear();
        original = [];
        SelectedEvent = null;
        IsDirty = false;
        Rebuild();
    }

    /// <summary>Builds every pane for the newly selected event.</summary>
    private void Rebuild()
    {
        IdentityFields.Clear();
        ControlFields.Clear();
        TriggerFields.Clear();
        TextFields.Clear();
        DetailFields.Clear();
        DetailGroups.Clear();
        Attributes.Clear();

        if (Current is not { } body)
        {
            RebuildChains();
            return;
        }

        Fill(IdentityFields, EventHeaderFields.Identity);
        Fill(ControlFields, EventHeaderFields.Control);
        Fill(TriggerFields, EventHeaderFields.Trigger);
        Fill(TextFields, EventHeaderFields.Texts);

        var detail = EventDetailFields.For(body);
        Fill(DetailFields, detail.Fields);

        foreach (var group in detail.Groups)
        {
            DetailGroups.Add(new EventFieldGroupViewModel(
                group.Label, [..group.Fields.Select(f => EventFieldViewModel.Create(f, this))]));
        }

        foreach (var attribute in body.Base.Control.Attributes)
        {
            Attributes.Add(new EventAttributeViewModel("Control", attribute));
        }

        foreach (var attribute in body.Base.Attributes)
        {
            Attributes.Add(new EventAttributeViewModel("Event", attribute));
        }

        OnPropertyChanged(nameof(HasAttributes));

        RefreshRelevance();
        RebuildChains();
    }

    private void Fill(ObservableCollection<EventFieldViewModel> target,
                      IReadOnlyList<EventFieldSpec> specs)
    {
        foreach (var spec in specs)
        {
            target.Add(EventFieldViewModel.Create(spec, this));
        }
    }

    /// <summary>Greys the trigger operands the current trigger does not read.</summary>
    private void RefreshRelevance()
    {
        if (Current is not { } body)
        {
            return;
        }

        var trigger = (EventTriggerType)body.Base.Control.EventTrigger;

        foreach (var field in TriggerFields)
        {
            field.IsRelevant = EventHeaderFields.RelevantTo(trigger, field.Label);
            field.Refresh();
        }

        foreach (var field in ControlFields)
        {
            field.Refresh();
        }
    }

    /// <summary>
    /// Rebuilds both directions of the chain graph around the selection.
    /// </summary>
    /// <remarks>
    /// The reverse edges are found by re-walking every event's links. That is
    /// <c>O(events × links)</c> per selection change — about 40,000 comparisons on Case.dsn's
    /// largest level — and it is what the original does too, in <c>DumpEventText</c. An index would
    /// have to be invalidated on every chain edit, which is the case that matters least and is
    /// hardest to get right.
    /// </remarks>
    private void RebuildChains()
    {
        Outgoing.Clear();
        Incoming.Clear();

        if (SelectedEvent is not { } selected)
        {
            OnPropertyChanged(nameof(HasOutgoing));
            OnPropertyChanged(nameof(HasIncoming));
            return;
        }

        foreach (var link in EventChainLinks.Of(selected.Body))
        {
            Outgoing.Add(Reference(link.Label, link.Target, link.Taken));
        }

        foreach (var candidate in Events)
        {
            if (ReferenceEquals(candidate, selected))
            {
                // An event chaining to itself is a real, and infinite, thing a design can express;
                // it is listed under Outgoing and left out of Incoming rather than shown twice.
                continue;
            }

            foreach (var link in EventChainLinks.Of(candidate.Body))
            {
                if (link.Target == selected.Id)
                {
                    Incoming.Add(new EventChainReferenceViewModel(
                        link.Label, candidate.Id, Describe(candidate), link.Taken, true, GoTo));
                }
            }
        }

        OnPropertyChanged(nameof(HasOutgoing));
        OnPropertyChanged(nameof(HasIncoming));
    }

    private EventChainReferenceViewModel Reference(string label, uint target, bool taken)
    {
        var row = Events.FirstOrDefault(e => e.Id == target);

        return new EventChainReferenceViewModel(
            label, target,
            row is null ? $"{target} — no such event" : Describe(row),
            taken, row is not null, GoTo);
    }

    private static string Describe(EventListItemViewModel row)
    {
        string summary = row.Summary;

        return summary.Length > 0
            ? $"{row.TypeName} ({row.Id}) — {summary}"
            : $"{row.TypeName} ({row.Id})";
    }
}

/// <summary>
/// One ASL entry of an event or its control block.
/// </summary>
/// <remarks>
/// <b>Not decoration.</b> Three of the trigger's operands live here and nowhere else: <c>gender</c>,
/// <c>specialItem</c> and <c>specialKey</c> are moved into <c>eventcontrol_asl</c> under the keys
/// <c>Gen</c>, <c>SpIt</c> and <c>SpKy</c> before writing and pulled back after reading
/// (<c>PreSerialize</c>, <c>Shared/GameEvent.cpp:1318</c>), and they are read back with
/// <c>atoi</c> — so a missing or non-numeric value is silently 0.
/// </remarks>
public sealed class EventAttributeViewModel(string owner, AslEntry entry)
{
    /// <summary>"Control" or "Event" — which of the two lists this came from.</summary>
    public string Owner { get; } = owner;

    public string Key { get; } = entry.Key;

    public string Value { get; } = entry.Value;

    /// <summary>The known keys, spelled out where one is recognised.</summary>
    public string Meaning { get; } = entry.Key switch
    {
        "Gen" => "gender trigger",
        "SpIt" => "special item trigger",
        "SpKy" => "special key trigger",
        _ => string.Empty,
    };
}
