using UAF.Serialization;
using UAFcore;

namespace UAFedit.Databases;

/// <summary>
/// The Database &gt; Edit Monsters… editor: <c>monsters.dat</c> as a searchable list plus a detail
/// form.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <c>items.dat</c> there is no trailing list — the payload is a count and the records
/// (<c>Monster.cpp:1023</c>) — so
/// <see cref="DatabaseEditorViewModel{TEditor,TRecord}.Records"/> is the whole product and there is
/// no <c>Database</c> property to forget.
/// </para>
/// <para>
/// <b>Bound to the collection it mutates.</b> The original's <c>AddToAvailList</c> read the
/// <i>global</i> monster database rather than the pointer it had been handed
/// (<c>MonsterEditor.cpp:259</c>), which was harmless from the main menu and wrong from the
/// import-a-text-file path that pointed it at a temporary.
/// </para>
/// </remarks>
public sealed class MonsterDatabaseViewModel
    : DatabaseEditorViewModel<MonsterEditorViewModel, MonsterRecord>
{
    private readonly IReadOnlyList<string> classes;

    public MonsterDatabaseViewModel(LoadedDesign design)
        : this(Monsters(design), ClassNames(design))
    {
    }

    public MonsterDatabaseViewModel(IReadOnlyList<MonsterRecord>? monsters,
                                    IReadOnlyList<string>? knownClasses)
        : base(monsters ?? [])
    {
        IsReadable = monsters is not null;
        classes = knownClasses ?? [];

        Sort = Sorts[0];
        Refresh();
    }

    /// <summary>False when <c>monsters.dat</c> came back null rather than empty.</summary>
    public bool IsReadable { get; }

    public override IReadOnlyList<RecordSort<MonsterEditorViewModel>> Sorts { get; } =
    [
        new("Name", (a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase)),
        new("Hit dice", (a, b) => a.HitDice.CompareTo(b.HitDice)),
        new("Armour class", (a, b) => a.ArmorClass.CompareTo(b.ArmorClass)),
        new("THAC0", (a, b) => a.Thac0.CompareTo(b.Thac0)),
        new("Experience", (a, b) => a.ExperienceValue.CompareTo(b.ExperienceValue)),
        new("Attacks", (a, b) => a.Attacks.Count.CompareTo(b.Attacks.Count)),
        new("Class", (a, b) => string.Compare(a.ClassChoice?.Value, b.ClassChoice?.Value,
                                              StringComparison.OrdinalIgnoreCase)),
    ];

    protected override string NewName => "New Monster";

    protected override MonsterEditorViewModel NewEditor(MonsterRecord record) =>
        new(record, classes);

    protected override MonsterRecord NewRecord(string name) =>
        MonsterEditorViewModel.NewRecord(name);

    protected override MonsterRecord Rename(MonsterRecord record, string name)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record with { Name = name };
    }

    /// <remarks>
    /// Name and class, because a design's monsters are routinely near-duplicates of each other and
    /// the class is what a search for "the caster version" is actually after.
    /// </remarks>
    protected override bool Matches(MonsterEditorViewModel editor, string search)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return editor.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (editor.ClassChoice?.Value.Contains(search, StringComparison.OrdinalIgnoreCase)
                ?? false);
    }

    private static IReadOnlyList<MonsterRecord>? Monsters(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design.Monsters;
    }

    private static IReadOnlyList<string> ClassNames(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design.Classes is { } map ? [.. map.Keys] : [];
    }
}
