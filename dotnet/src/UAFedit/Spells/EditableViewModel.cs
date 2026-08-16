using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UAFedit.Spells;

/// <summary>
/// A view model over one record, which knows whether it has been edited.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dirtiness is derived from change notification rather than written into every setter.</b> A
/// spell has thirty-odd editable fields (<see cref="SpellEditorViewModel"/>); hand-writing a
/// <c>IsDirty = true</c> into each is thirty chances to forget one, and a forgotten one is a
/// silently discarded edit. Overriding <see cref="OnPropertyChanged"/> catches every generated
/// setter at once, including ones added later.
/// </para>
/// <para>
/// <b>The cost is that view state has to opt out</b> — a selection or a search box raises change
/// notification exactly as an edit does. <see cref="IsEdit"/> is where a derived class names the
/// properties that are not record content. Getting that list wrong makes the editor claim unsaved
/// changes after a click, which is why it is an explicit list rather than a heuristic.
/// </para>
/// <para>
/// A constructor should assign through the properties and then call <see cref="ResetDirty"/>: the
/// alternative, assigning the generated backing fields directly to dodge notification, works but
/// trips the toolkit's own analyzer, and this project builds with warnings as errors.
/// </para>
/// </remarks>
public abstract partial class EditableViewModel : ObservableObject
{
    /// <summary>Whether anything has been changed since the record was loaded or reverted.</summary>
    [ObservableProperty]
    private bool isDirty;

    /// <summary>
    /// Whether a change to this property means the record was edited.
    /// </summary>
    /// <remarks>Override to exclude selection, filters and anything else the view owns.</remarks>
    protected virtual bool IsEdit(string? propertyName) => true;

    /// <summary>Declares the current values to be the unedited ones.</summary>
    protected void ResetDirty() => IsDirty = false;

    /// <summary>
    /// Declares the current values saved, so <see cref="IsDirty"/> reads false again.
    /// </summary>
    /// <remarks>
    /// The same thing as <see cref="ResetDirty"/> and public, because the caller that knows a save
    /// succeeded is never the record itself. It must be called <b>after</b> the write, not before:
    /// the writers refuse shapes they cannot reproduce, and a record marked clean by a save that
    /// then threw is an edit the user has lost without being told.
    /// </remarks>
    public void AcceptChanges() => ResetDirty();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        ArgumentNullException.ThrowIfNull(e);

        if (e.PropertyName != nameof(IsDirty) && IsEdit(e.PropertyName))
        {
            IsDirty = true;
        }
    }
}
