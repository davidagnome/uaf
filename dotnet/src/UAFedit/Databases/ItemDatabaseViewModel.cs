using UAF.Serialization;
using UAFcore;

namespace UAFedit.Databases;

/// <summary>
/// The Database &gt; Edit Items… editor: <c>items.dat</c> as a searchable list plus a detail form.
/// </summary>
/// <remarks>
/// <para>
/// Constructed from a <see cref="LoadedDesign"/> and nothing else, so the editor reads no bytes of
/// its own — the same contract <c>DesignInspector</c> keeps. A design whose <c>items.dat</c> is
/// missing or is a shape this port declines gives <see cref="IsReadable"/> false and an empty list
/// rather than a throw, because "no item database" is an ordinary design state.
/// </para>
/// <para>
/// <b>The ammo-type list is part of the file and is carried through.</b> It sits after the records
/// rather than inside them (<c>Items.cpp:3091</c>), so it is easy to lose: a writer handed only
/// <see cref="DatabaseEditorViewModel{TEditor,TRecord}.Records"/> would emit a database whose
/// launchers no longer match their ammunition. <see cref="Database"/> is what a save should take.
/// </para>
/// </remarks>
public sealed class ItemDatabaseViewModel
    : DatabaseEditorViewModel<ItemEditorViewModel, ItemRecord>
{
    private readonly IReadOnlyList<string> baseclasses;
    private readonly IReadOnlyList<string> loadedAmmoTypes;

    public ItemDatabaseViewModel(LoadedDesign design)
        : this(Database(design), BaseclassNames(design))
    {
    }

    public ItemDatabaseViewModel(ItemDatabase? database, IReadOnlyList<string>? knownBaseclasses)
        : base(database?.Items ?? [])
    {
        IsReadable = database is not null;
        loadedAmmoTypes = database?.AmmoTypes ?? [];
        baseclasses = knownBaseclasses ?? [];

        Sort = Sorts[0];
        Refresh();
    }

    /// <summary>False when <c>items.dat</c> came back null — missing, or a shape this port refuses.</summary>
    public bool IsReadable { get; }

    /// <summary>
    /// The ammunition families already in the file, offered as suggestions.
    /// </summary>
    /// <remarks>
    /// A suggestion list, not a constraint: the original's combo was <c>CBS_DROPDOWN</c> and
    /// accepted anything typed. Matching between a launcher and its ammunition is an <b>exact</b>
    /// string comparison, so a typo here is silent and total — the bow simply never finds an arrow.
    /// <para>
    /// Fixed for the lifetime of the view model. The original grew the <i>global</i> list as a side
    /// effect of <c>UpdateData</c> — before the user had pressed OK, and surviving Cancel
    /// (<c>ItemDBDlg.cpp:384</c>). <see cref="Database"/> instead unions the types in use at the
    /// moment a save asks for them, which cannot leak an abandoned edit.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> AmmoTypes => loadedAmmoTypes;

    /// <summary>
    /// The whole edited <c>items.dat</c> payload: every record, plus the trailing ammo-type list.
    /// </summary>
    /// <remarks>
    /// The ammo list is the file's, <b>plus</b> any family an item now names that it did not
    /// contain. Nothing is removed: the reference only drops a type once no item uses it
    /// (<c>Items.cpp:6141</c>), and a design is free to keep a type in reserve — pruning here would
    /// delete data on behalf of a user who only opened the editor.
    /// </remarks>
    public ItemDatabase Database
    {
        get
        {
            var records = Records;
            var types = new List<string>(loadedAmmoTypes);
            var seen = types.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                if (record.Scalars.AmmoType is { Length: > 0 } type && seen.Add(type))
                {
                    types.Add(type);
                }
            }

            return new ItemDatabase(records, types);
        }
    }

    public override IReadOnlyList<RecordSort<ItemEditorViewModel>> Sorts { get; } =
    [
        new("Name", (a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase)),
        new("Display name",
            (a, b) => string.Compare(a.IdName, b.IdName, StringComparison.OrdinalIgnoreCase)),
        new("Slot", (a, b) => string.Compare(a.SlotChoiceValue?.Label, b.SlotChoiceValue?.Label,
                                             StringComparison.OrdinalIgnoreCase)),
        new("Weapon type", (a, b) => a.WeaponTypeChoice?.Value.CompareTo(
                                         b.WeaponTypeChoice?.Value ?? 0) ?? 0),
        new("Cost", (a, b) => a.Cost.CompareTo(b.Cost)),
        new("Encumbrance", (a, b) => a.Encumbrance.CompareTo(b.Encumbrance)),
    ];

    protected override string NewName => "New Item";

    protected override ItemEditorViewModel NewEditor(ItemRecord record) =>
        new(record, baseclasses);

    protected override ItemRecord NewRecord(string name) => ItemEditorViewModel.NewRecord(name);

    /// <remarks>
    /// <b>Only <c>m_uniqueName</c> changes.</b> The original's Paste renamed the unique name and
    /// left the display name as the source's (<c>ItemEditor.cpp:482</c>), so a duplicated item
    /// looked identical in the inventory and could only be told apart by the id nothing shows.
    /// </remarks>
    protected override ItemRecord Rename(ItemRecord record, string name)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record with { Names = record.Names with { UniqueName = name, IdName = name } };
    }

    protected override bool Matches(ItemEditorViewModel editor, string search)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return editor.UniqueName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || editor.IdName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static ItemDatabase? Database(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design.Items;
    }

    /// <summary>
    /// The design's baseclass ids, which is what an item's usability list names.
    /// </summary>
    /// <remarks>
    /// The map is keyed by <c>BaseclassRecord.Name</c> (<c>LoadedDesign.LoadBaseclasses</c>), and
    /// that is the id an item stores — not the <c>Tag</c>, which is the shorter label the character
    /// sheet prints.
    /// </remarks>
    private static IReadOnlyList<string> BaseclassNames(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design.Baseclasses is { } map ? [.. map.Keys] : [];
    }
}
