using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UAFedit.Databases;

/// <summary>One way of ordering the master list.</summary>
/// <param name="Compare">
/// Always ascending; <see cref="DatabaseEditorViewModel{TEditor,TRecord}.SortDescending"/> inverts
/// it. Keeping the comparison one-directional is what stops a descending sort from also reversing
/// the tie-break, which is how "sort by AC" ends up shuffling equal-AC monsters on every click.
/// </param>
public sealed record RecordSort<TEditor>(string Label, Comparison<TEditor> Compare);

/// <summary>
/// The master half of a database editor: a searchable, sortable list of records, one of which is
/// selected into the detail form.
/// </summary>
/// <remarks>
/// <para>
/// The Avalonia replacement for <c>CItemEditor</c> / <c>CMonsterEditor</c>
/// (<c>UAFWinEd/ItemEditor.cpp</c>, <c>UAFWinEd/MonsterEditor.cpp</c>), which were sortable
/// <c>SysListView32</c>s over the live global database.
/// </para>
/// <para>
/// <b>Selection is an object, not an index.</b> Both originals stored the array index in the list
/// item's <c>lParam</c> and re-derived the current record from the selection on every command —
/// and then, in three places, keyed off the row's <i>text</i> instead (<c>ItemEditor.cpp:220</c>,
/// <c>:435</c>, <c>:496</c>). With a sortable list those two index spaces differ, which is the
/// mechanism behind the original's delete-rebuilds-everything workaround. Holding the editor view
/// model itself removes the question.
/// </para>
/// <para>
/// <b>Nothing here writes a file.</b> <see cref="Records"/> is the product; a caller that has a
/// writer runs it and then calls <see cref="AcceptChanges"/>.
/// </para>
/// </remarks>
public abstract partial class DatabaseEditorViewModel<TEditor, TRecord> : ObservableObject
    where TEditor : RecordEditorViewModel<TRecord>
    where TRecord : class, IEquatable<TRecord>
{
    private readonly List<TEditor> all = [];
    private bool structureChanged;

    protected DatabaseEditorViewModel(IEnumerable<TRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            all.Add(Track(NewEditor(record)));
        }
    }

    /// <summary>The records currently passing <see cref="Search"/>, in <see cref="Sort"/> order.</summary>
    public ObservableCollection<TEditor> Visible { get; } = [];

    /// <summary>Every record, in database order — the order a writer must emit them in.</summary>
    public IReadOnlyList<TEditor> All => all;

    /// <summary>
    /// The edited database, ready for a writer.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="All"/> rather than from <see cref="Visible"/>, because a filtered or
    /// re-sorted view is a view — emitting it would silently drop every record the search box was
    /// hiding.
    /// </remarks>
    public IReadOnlyList<TRecord> Records => [.. all.Select(e => e.Record)];

    /// <summary>Whether anything has been edited, added or deleted since the last accept.</summary>
    public bool IsDirty => structureChanged || all.Exists(e => e.IsDirty);

    public int Count => all.Count;

    /// <summary>How many records differ from what was loaded.</summary>
    public int DirtyCount => all.Count(e => e.IsDirty);

    /// <summary>
    /// The ids that more than one record answers to.
    /// </summary>
    /// <remarks>
    /// <b>A real state in shipped designs, not a hypothetical.</b> Both originals checked for a
    /// duplicate name when <i>adding</i> a record and never when renaming one — <c>SetItem</c> and
    /// <c>SetMonster</c> both locate by the <b>old</b> id and then assign the whole record over it
    /// (<c>Items.cpp:6007</c>, <c>Monster.cpp</c>'s equivalent), with no uniqueness test. So
    /// renaming A to B's name produced two records with one name, of which every subsequent
    /// lookup found only the first. Surfacing it is cheaper than preventing it, and preventing it
    /// would make an already-broken design uneditable.
    /// </remarks>
    public IReadOnlyList<string> DuplicateNames =>
    [
        .. all.GroupBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
              .Where(g => g.Count() > 1)
              .Select(g => g.Key)
              .Order(StringComparer.OrdinalIgnoreCase),
    ];

    [ObservableProperty]
    private TEditor? selected;

    /// <summary>Matched against <see cref="Matches"/>; empty shows everything.</summary>
    [ObservableProperty]
    private string search = string.Empty;

    [ObservableProperty]
    private RecordSort<TEditor>? sort;

    [ObservableProperty]
    private bool sortDescending;

    /// <summary>The orderings the list offers. Supplied by the concrete database.</summary>
    public abstract IReadOnlyList<RecordSort<TEditor>> Sorts { get; }

    /// <summary>Wraps one record in its detail form.</summary>
    protected abstract TEditor NewEditor(TRecord record);

    /// <summary>A blank record, with the reference's own defaults rather than zeros.</summary>
    protected abstract TRecord NewRecord(string name);

    /// <summary>What the Add command calls the first new record.</summary>
    protected abstract string NewName { get; }

    /// <summary>Whether a record survives the current search text.</summary>
    protected abstract bool Matches(TEditor editor, string search);

    /// <summary>
    /// Rebuilds <see cref="Visible"/> from the current search and sort, keeping the selection.
    /// </summary>
    /// <remarks>
    /// <b>Not re-run when a record is renamed.</b> Re-sorting on every keystroke in the name box
    /// would walk the selected row out from under the cursor. The list therefore lags a rename
    /// until the next search or sort change, which is the lesser of the two surprises.
    /// </remarks>
    public void Refresh()
    {
        var kept = Selected;

        IEnumerable<TEditor> query = search.Length == 0
            ? all
            : all.Where(e => Matches(e, search));

        var ordered = query.ToList();
        if (Sort is { } sorting)
        {
            ordered.Sort(sorting.Compare);
            if (SortDescending)
            {
                ordered.Reverse();
            }
        }

        Visible.Clear();
        foreach (var editor in ordered)
        {
            Visible.Add(editor);
        }

        Selected = kept is not null && ordered.Contains(kept) ? kept : ordered.FirstOrDefault();
    }

    /// <summary>Treats the current state as saved, for a caller that has written the records out.</summary>
    public void AcceptChanges()
    {
        foreach (var editor in all)
        {
            editor.AcceptChanges();
        }

        structureChanged = false;
        RaiseDatabaseState();
    }

    /// <summary>Appends a new record and selects it.</summary>
    [RelayCommand]
    public void Add()
    {
        var editor = Track(NewEditor(NewRecord(UnusedName(NewName))));
        all.Add(editor);
        structureChanged = true;

        Refresh();
        Selected = editor;
        RaiseDatabaseState();
    }

    /// <summary>
    /// Appends a copy of the selected record under a fresh name.
    /// </summary>
    /// <remarks>
    /// The original's Paste overwrote the destination and kept <i>its</i> name
    /// (<c>ItemEditor.cpp:482</c>), so a copy was a two-step dance through a file-static clipboard.
    /// A duplicate is what that dance was for.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void Duplicate()
    {
        if (Selected is not { } source)
        {
            return;
        }

        var editor = Track(NewEditor(Rename(source.Record, UnusedName(source.Title))));
        all.Insert(all.IndexOf(source) + 1, editor);
        structureChanged = true;

        Refresh();
        Selected = editor;
        RaiseDatabaseState();
    }

    /// <summary>Copies a record under a new id. Only the id changes.</summary>
    protected abstract TRecord Rename(TRecord record, string name);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void Delete()
    {
        if (Selected is not { } victim)
        {
            return;
        }

        victim.PropertyChanged -= OnRecordChanged;
        all.Remove(victim);
        structureChanged = true;

        Refresh();
        RaiseDatabaseState();
    }

    /// <summary>Throws away the selected record's edits.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void RevertSelected() => Selected?.Revert();

    public bool HasSelection => Selected is not null;

    /// <summary>
    /// A name no record holds, by appending <c>/2</c>, <c>/3</c>, … to the one asked for.
    /// </summary>
    /// <remarks>
    /// The suffix is the reference's own convention for disambiguating a pasted record — the
    /// commented-out block at <c>MonsterEditor.cpp:344</c> built <c>"&lt;Name&gt;/&lt;n&gt;"</c>
    /// before somebody replaced it with "keep the destination's name". Matching it keeps generated
    /// ids recognisable to anyone who knows the format.
    /// </remarks>
    protected string UnusedName(string wanted)
    {
        var taken = all.Select(e => e.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(wanted))
        {
            return wanted;
        }

        for (int n = 2; ; n++)
        {
            string candidate = $"{wanted}/{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private TEditor Track(TEditor editor)
    {
        editor.PropertyChanged += OnRecordChanged;
        return editor;
    }

    private void OnRecordChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordEditorViewModel<TRecord>.IsDirty)
                           or nameof(RecordEditorViewModel<TRecord>.Title))
        {
            RaiseDatabaseState();
        }
    }

    private void RaiseDatabaseState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtyCount));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(DuplicateNames));
    }

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnSortChanged(RecordSort<TEditor>? value) => Refresh();

    partial void OnSortDescendingChanged(bool value) => Refresh();

    partial void OnSelectedChanged(TEditor? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        DuplicateCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RevertSelectedCommand.NotifyCanExecuteChanged();
    }
}
