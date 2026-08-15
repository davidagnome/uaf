using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UAFedit.Databases;

/// <summary>
/// One record of a database, unpacked into editable fields and packed back on demand.
/// </summary>
/// <typeparam name="TRecord">
/// The immutable record type from <c>UAF.Serialization</c>. It must be an <c>IEquatable</c>
/// record, because <see cref="IsDirty"/> is a comparison rather than a flag anybody sets.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Dirtiness is derived, not tracked.</b> The serialization records are C# <c>record</c>s with
/// value equality, so "has this been edited?" can be answered by rebuilding the record and
/// comparing it to the one that was loaded. That makes an edit-then-undo land back on clean
/// without any undo stack, and it makes the flag impossible to leave stale — the failure mode a
/// hand-maintained <c>m_bModified</c> has. The MFC original set its flag in every handler
/// (<c>ItemDB.cpp</c>'s <c>SetModifiedFlag</c> calls) and got it wrong in the places that wrote a
/// member directly.
/// </para>
/// <para>
/// <b>The cost of that choice is <see cref="Canonical"/>.</b> Record equality compares a
/// collection member by <i>reference</i>, not by contents — <c>ItemTail.UsableByBaseclass</c> and
/// <c>MonsterRecord.Attacks</c> are both <c>IReadOnlyList&lt;T&gt;</c>. So a <see cref="Build"/>
/// that allocated a fresh list every call would report every record dirty from the moment it
/// loaded, and one that mutated a list in place would report none of them. Every editable
/// collection therefore goes through <see cref="Canonical"/>, which hands back the original
/// instance when the contents match.
/// </para>
/// </remarks>
public abstract class RecordEditorViewModel<TRecord> : ObservableObject
    where TRecord : class, IEquatable<TRecord>
{
    /// <summary>
    /// The properties that are computed from the others, and so must not re-trigger themselves.
    /// </summary>
    private readonly string[] derived;

    private TRecord original;

    /// <param name="alsoDerived">
    /// Further computed properties of the concrete form — cross-field warnings, mostly. They join
    /// the set that every field change raises, and the set that is guarded against re-entry.
    /// </param>
    protected RecordEditorViewModel(TRecord record, params string[] alsoDerived)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(alsoDerived);

        original = record;
        derived =
        [
            nameof(Record), nameof(IsDirty), nameof(Title), nameof(Subtitle), nameof(Original),
            .. alsoDerived,
        ];
    }

    /// <summary>The record as it was loaded, or as it was at the last <see cref="AcceptChanges"/>.</summary>
    public TRecord Original => original;

    /// <summary>The record as the form currently describes it.</summary>
    /// <remarks>
    /// Rebuilt on every read. That is affordable — a record is a couple of dozen fields and this is
    /// read at user speed — and it removes the class of bug where a stale cached projection is
    /// handed to a writer.
    /// </remarks>
    public TRecord Record => Build();

    public bool IsDirty => !Build().Equals(original);

    /// <summary>What the master list shows for this record.</summary>
    public abstract string Title { get; }

    /// <summary>A second line for the master list — enough to tell near-duplicates apart.</summary>
    public abstract string Subtitle { get; }

    /// <summary>Packs the editable fields back into a record.</summary>
    protected abstract TRecord Build();

    /// <summary>Unpacks a record into the editable fields.</summary>
    protected abstract void Load(TRecord record);

    /// <summary>Throws away every edit, back to <see cref="Original"/>.</summary>
    public void Revert()
    {
        Load(original);
        RaiseDerived();
    }

    /// <summary>
    /// Treats the current state as the saved state, so <see cref="IsDirty"/> reads false again.
    /// </summary>
    /// <remarks>
    /// For a caller that has written the database out. Nothing here writes a file — the record this
    /// produces is the whole product, and what happens to it is the writer's business.
    /// </remarks>
    public void AcceptChanges()
    {
        original = Build();
        RaiseDerived();
    }

    /// <summary>
    /// The edited collection, or the original instance when the edit landed back on its contents.
    /// </summary>
    /// <remarks>
    /// See the class remarks: without this, <see cref="IsDirty"/> would be a reference comparison
    /// wearing a value comparison's clothes. <c>SequenceEqual</c> is exact here because every
    /// element type involved is itself a record or a string.
    /// </remarks>
    protected static IReadOnlyList<T> Canonical<T>(IReadOnlyList<T> edited, IReadOnlyList<T> loaded)
    {
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentNullException.ThrowIfNull(loaded);

        return edited.SequenceEqual(loaded) ? loaded : edited;
    }

    /// <summary>
    /// The value an <c>int</c>-typed <c>BOOL</c> takes when a checkbox is ticked or cleared.
    /// </summary>
    /// <remarks>
    /// <b>These fields are <c>int</c> in the port on purpose.</b> The reference declares
    /// <c>Cursed</c>, <c>IsNonLethal</c>, <c>UseHitDice</c> and the two "can be" flags as
    /// <c>BOOL</c>, and this codebase has designs that store values other than 0 and 1 in a
    /// <c>BOOL</c> (see <c>ItemScalars</c>' remarks on <c>AutoDarkenAmount</c>). Ticking a box that
    /// is already set therefore leaves the stored value alone rather than normalising it to 1; only
    /// clearing and re-ticking loses a non-canonical truth, and that is a deliberate edit.
    /// </remarks>
    protected static int Flag(int current, bool on) => on ? (current != 0 ? current : 1) : 0;

    protected void RaiseDerived()
    {
        foreach (string name in derived)
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>
    /// Turns any field change into a change of the derived properties.
    /// </summary>
    /// <remarks>
    /// The alternative is <c>[NotifyPropertyChangedFor]</c> on every one of the ~50 fields these
    /// forms carry, which is a list that silently goes stale the first time somebody adds a field
    /// and forgets the attribute. The guard on <see cref="Derived"/> is what stops the recursion.
    /// </remarks>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnPropertyChanged(e);

        if (e.PropertyName is null || Array.IndexOf(derived, e.PropertyName) >= 0)
        {
            return;
        }

        RaiseDerived();
    }
}
