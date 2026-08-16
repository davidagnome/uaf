using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;

namespace UAFedit.Events;

/// <summary>What a field needs from the editor around it.</summary>
/// <remarks>
/// Deliberately tiny. A field reads the event it is bound to, hands back a new one, and — for a
/// chain — asks whether an id resolves and jumps to it. Everything else about the editor is none of
/// its business, which is what lets the field view models be constructed and driven in a test with
/// no window.
/// </remarks>
public interface IEventFieldHost
{
    /// <summary>The event currently being edited.</summary>
    IGameEvent? Current { get; }

    /// <summary>Takes an edited event in place of <see cref="Current"/>.</summary>
    void Apply(IGameEvent updated);

    /// <summary>Whether an id names an event in this level.</summary>
    bool Resolves(uint id);

    /// <summary>Selects the event with this id.</summary>
    void GoTo(uint id);
}

/// <summary>
/// One row of the detail pane.
/// </summary>
/// <remarks>
/// <para>
/// <b>The view model stores no value.</b> Every read goes to the record through the spec's getter
/// and every write produces a new record; <see cref="Refresh"/> only tells the binding to ask
/// again. That is what makes an edit that failed to parse visible — the box snaps back to what the
/// record still says, rather than holding text nothing accepted.
/// </para>
/// <para>
/// The subclasses exist so the view can pick a template by type with <c>x:DataType</c> on each,
/// rather than one template full of mutually exclusive <c>IsVisible</c> bindings.
/// </para>
/// </remarks>
public abstract partial class EventFieldViewModel : ObservableObject
{
    private protected EventFieldViewModel(EventFieldSpec spec, IEventFieldHost host)
    {
        Spec = spec;
        Host = host;
    }

    private protected EventFieldSpec Spec { get; }

    private protected IEventFieldHost Host { get; }

    public string Label => Spec.Label;

    /// <summary>False for a field the record carries but nothing here can change.</summary>
    public bool IsEditable => Spec.Write is not null;

    /// <summary>
    /// False when the selected trigger does not read this field.
    /// </summary>
    /// <remarks>
    /// Shown greyed rather than hidden. The original hides irrelevant controls <i>and clears the
    /// fields behind them</i> as a side effect of redrawing (<c>SetControlStates</c>,
    /// <c>EventViewer.cpp:2967</c>), so a designer who set an item and then changed the trigger
    /// loses it silently. Greying keeps the value and still says it is not being read.
    /// </remarks>
    [ObservableProperty]
    private bool isRelevant = true;

    /// <summary>The stored value as text.</summary>
    private protected string Raw =>
        Host.Current is { } body ? Spec.Read(body) : string.Empty;

    /// <summary>Applies an edit, if the field takes one.</summary>
    private protected void Write(string text)
    {
        if (Spec.Write is null || Host.Current is not { } body)
        {
            return;
        }

        Host.Apply(Spec.Write(body, text));
        Refresh();
    }

    /// <summary>Re-reads everything this row shows.</summary>
    public abstract void Refresh();

    /// <summary>The row for a spec, of whatever kind.</summary>
    public static EventFieldViewModel Create(EventFieldSpec spec, IEventFieldHost host)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(host);

        return spec.Kind switch
        {
            EventFieldKind.Flag => new EventFlagFieldViewModel(spec, host),
            EventFieldKind.Choice => new EventChoiceFieldViewModel(spec, host),
            EventFieldKind.Chain => new EventChainFieldViewModel(spec, host),
            EventFieldKind.Info => new EventInfoFieldViewModel(spec, host),
            _ => new EventTextFieldViewModel(spec, host),
        };
    }
}

/// <summary>A string or a number — both edited in a box.</summary>
/// <remarks>
/// Numbers share this rather than getting a spinner because the spec already clamps on write, and
/// because several of them are only nominally numeric: an entry point of -1 means "none" and a
/// quest stage of 65001 means "failed".
/// </remarks>
public sealed partial class EventTextFieldViewModel(EventFieldSpec spec, IEventFieldHost host)
    : EventFieldViewModel(spec, host)
{
    /// <summary>True for one of the three display texts or a GPDL body.</summary>
    public bool IsMultiline => Spec.Kind == EventFieldKind.Paragraph;

    public string Value
    {
        get => Raw;
        set => Write(value);
    }

    public override void Refresh() => OnPropertyChanged(nameof(Value));
}

/// <summary>A <c>BOOL</c> or a bit of a mask.</summary>
public sealed partial class EventFlagFieldViewModel(EventFieldSpec spec, IEventFieldHost host)
    : EventFieldViewModel(spec, host)
{
    public bool IsChecked
    {
        get => Raw == "1";
        set => Write(value ? "1" : "0");
    }

    public override void Refresh() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>An ordinal picked from one of <see cref="EventCatalog"/>'s tables.</summary>
/// <remarks>
/// <b>A stored value the table does not cover is added to it rather than dropped.</b> Several
/// tables are shorter than their enum — <c>eventDistType</c> has six members and three labels — and
/// a combo that silently selected nothing would let the next write destroy a value the design
/// depends on.
/// </remarks>
public sealed partial class EventChoiceFieldViewModel : EventFieldViewModel
{
    public EventChoiceFieldViewModel(EventFieldSpec spec, IEventFieldHost host)
        : base(spec, host)
    {
        Choices = [..spec.Choices ?? []];
    }

    public ObservableCollection<EventChoice> Choices { get; }

    public EventChoice? Selected
    {
        get
        {
            if (!int.TryParse(Raw, out int value))
            {
                return null;
            }

            if (Choices.FirstOrDefault(c => c.Value == value) is { } known)
            {
                return known;
            }

            var unlisted = new EventChoice(value, $"{value} (not in table)");
            Choices.Add(unlisted);

            return unlisted;
        }

        set
        {
            if (value is not null)
            {
                Write(value.Value.ToString());
            }
        }
    }

    public override void Refresh() => OnPropertyChanged(nameof(Selected));
}

/// <summary>Another event's id, with a jump to it.</summary>
public sealed partial class EventChainFieldViewModel(EventFieldSpec spec, IEventFieldHost host)
    : EventFieldViewModel(spec, host)
{
    public string Value
    {
        get => Raw;
        set => Write(value);
    }

    /// <summary>The id, or 0 for "none".</summary>
    private uint Target => uint.TryParse(Raw, out uint id) ? id : 0;

    /// <summary>False when the id names no event in this level — a broken chain.</summary>
    /// <remarks>
    /// Not an error at run time: the engine pushes a do-nothing event and carries on
    /// (<c>RunEvent.cpp:13224</c>). It is nearly always a mistake in the design, though, which is
    /// why the original offers to repair dangling ids while building its tree
    /// (<c>EventViewer.cpp:2709</c>).
    /// </remarks>
    public bool IsBroken => Target > 0 && !Host.Resolves(Target);

    public bool CanFollow => Target > 0 && Host.Resolves(Target);

    [RelayCommand]
    private void Follow()
    {
        if (CanFollow)
        {
            Host.GoTo(Target);
        }
    }

    public override void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsBroken));
        OnPropertyChanged(nameof(CanFollow));
    }
}

/// <summary>Something the pane shows and does not edit.</summary>
public sealed partial class EventInfoFieldViewModel(EventFieldSpec spec, IEventFieldHost host)
    : EventFieldViewModel(spec, host)
{
    public string Value => Raw;

    public override void Refresh() => OnPropertyChanged(nameof(Value));
}

/// <summary>A named block of rows — one nested record, or one section of the original's dialog.</summary>
public sealed class EventFieldGroupViewModel(string label,
                                             IReadOnlyList<EventFieldViewModel> fields)
{
    public string Label { get; } = label;

    public IReadOnlyList<EventFieldViewModel> Fields { get; } = fields;
}
