using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;
using UAFcore;

namespace UAFedit.Spells;

/// <summary>What the master list can be ordered by.</summary>
/// <remarks>
/// <b><see cref="Database"/> is the design's own order and is not a sort at all.</b> It is what the
/// reference showed, and it is the only ordering that survives being written back — a spell's
/// position in <c>spells.dat</c> is its identity to anything holding an index, so seeing that order
/// has to stay possible.
/// </remarks>
public enum SpellSort
{
    Database,
    Name,
    School,
    Level,
    CastCost,
}

/// <summary>
/// The spell database: a searchable, sortable list with one spell's detail form beside it.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>ID_EDIT_SPELLS</c> — the reference's <c>CSpellDBDlgEx</c>
/// (<c>UAFWinEd/SpellDBDlgEx.cpp</c>), which was a single modal dialog holding both the list and
/// the fields. Splitting it into a master/detail pane is the only structural change: everything the
/// dialog showed is here, and nothing it did not show has been invented.
/// </para>
/// <para>
/// <b>Nothing here writes.</b> <see cref="EditedSpells"/> is the database as edited, ready for
/// <c>SpellRecordWriter</c>; whether a given design's version can be written back at all is that
/// writer's question (<c>SpellRecordWriter.CanWrite</c>), not this one's.
/// </para>
/// </remarks>
public sealed partial class SpellDatabaseViewModel : ObservableObject, IDisposable
{
    private readonly List<SpellEditorViewModel> all = [];

    /// <param name="design">
    /// The open design. Its <c>Baseclasses</c> supply the caster list; a design whose
    /// <c>baseclasses.dat</c> could not be read still edits, with each spell offering only the
    /// casters it already names.
    /// </param>
    public SpellDatabaseViewModel(LoadedDesign design)
        : this(design?.Spells ?? [],
               design?.Baseclasses is { } classes
                   ? [.. classes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)]
                   : [])
    {
        ArgumentNullException.ThrowIfNull(design);

        IsReadable = design.Spells is not null;
        Status = IsReadable
            ? $"{all.Count} spells."
            : "spells.dat is missing, or is a shape this port declines to read.";
    }

    /// <summary>Builds the editor over records directly, for tests and for reuse.</summary>
    public SpellDatabaseViewModel(IReadOnlyList<SpellRecord> spells,
                                  IReadOnlyList<string> designBaseclasses)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(designBaseclasses);

        foreach (var spell in spells)
        {
            var editor = new SpellEditorViewModel(spell, designBaseclasses);
            editor.PropertyChanged += OnSpellChanged;
            all.Add(editor);
        }

        // The reference builds its School combo from the distinct values in the loaded database
        // rather than from any list the design declares -- there is no such list.
        Schools =
        [
            .. all.Select(s => s.SchoolId)
                  .Where(s => s.Length > 0)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(s => s, StringComparer.OrdinalIgnoreCase),
        ];

        ApplyView();
        SelectedSpell = Spells.FirstOrDefault();
        Status = $"{all.Count} spells.";
    }

    /// <summary>False when the design's <c>spells.dat</c> came back null rather than empty.</summary>
    public bool IsReadable { get; } = true;

    /// <summary>The spells matching <see cref="Search"/>, in <see cref="Sort"/> order.</summary>
    public ObservableCollection<SpellEditorViewModel> Spells { get; } = [];

    /// <summary>Every distinct school in the database, for the detail form's suggestions.</summary>
    public IReadOnlyList<string> Schools { get; } = [];

    [ObservableProperty]
    private SpellEditorViewModel? selectedSpell;

    /// <summary>Matches on name, school and caster list.</summary>
    [ObservableProperty]
    private string search = string.Empty;

    [ObservableProperty]
    private SpellSort sort = SpellSort.Database;

    [ObservableProperty]
    private bool sortDescending;

    [ObservableProperty]
    private string status = string.Empty;

    /// <summary>The last <see cref="CompileAllScripts"/> result, or null if it has not been run.</summary>
    [ObservableProperty]
    private ScriptCompileReport? report;

    /// <summary>Whether any spell has been edited.</summary>
    [ObservableProperty]
    private bool isDirty;

    public int Count => all.Count;

    /// <summary>Every sort option, for a picker.</summary>
    public static IReadOnlyList<SpellSort> Sorts { get; } = [.. Enum.GetValues<SpellSort>()];

    /// <summary>
    /// The whole database, edits included — always in the design's own order.
    /// </summary>
    /// <remarks>
    /// <b>Not the order <see cref="Spells"/> is showing.</b> A spell's position in the file is what
    /// an index into the database means, so a sorted view must never become a sorted file. Sorting
    /// this list would silently renumber every spell reference in the design.
    /// </remarks>
    public IReadOnlyList<SpellRecord> EditedSpells => [.. all.Select(s => s.ToRecord())];

    /// <summary>Just the spells that were touched.</summary>
    public IReadOnlyList<SpellEditorViewModel> Edited => [.. all.Where(s => s.IsDirty)];

    /// <summary>
    /// Declares every spell saved, so the database reads clean again.
    /// </summary>
    /// <remarks>
    /// For a caller that has just written <c>spells.dat</c>. Clearing each spell's own flag is
    /// what makes <see cref="Edited"/> empty afterwards — setting only the database's flag would
    /// leave every spell still claiming to be unsaved.
    /// </remarks>
    public void AcceptChanges()
    {
        foreach (var spell in all)
        {
            spell.AcceptChanges();
        }

        IsDirty = false;
    }

    partial void OnSearchChanged(string value)
    {
        _ = value;
        ApplyView();
    }

    partial void OnSortChanged(SpellSort value)
    {
        _ = value;
        ApplyView();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        _ = value;
        ApplyView();
    }

    /// <summary>
    /// Compiles every non-empty script on every spell.
    /// </summary>
    /// <remarks>
    /// A plain method with the command wrapped round it, following <c>MainWindowViewModel.Open</c>:
    /// this is the part worth testing and it has to run without an application.
    /// </remarks>
    public ScriptCompileReport CompileAllScripts()
    {
        var failures = new List<ScriptFailure>();
        int scripts = 0;
        int spells = 0;

        foreach (var spell in all)
        {
            bool any = false;

            foreach (var script in spell.Scripts.Where(s => !s.IsEmpty))
            {
                any = true;
                scripts++;
                script.Compile();

                if (!script.Diagnostics.Succeeded)
                {
                    failures.Add(new ScriptFailure(
                        spell.Name, script.Name, script.Diagnostics.Summary));
                }
            }

            if (any)
            {
                spells++;
            }
        }

        var built = new ScriptCompileReport(spells, scripts, failures);
        Report = built;
        Status = built.Summary("spells");
        return built;
    }

    /// <remarks>
    /// Off the UI thread: a design's spell scripts are hundreds of full lex-and-compile passes.
    /// </remarks>
    [RelayCommand]
    private async Task CompileAllAsync()
    {
        Status = "Compiling…";
        await Task.Run(CompileAllScripts).ConfigureAwait(true);
    }

    [RelayCommand]
    private void RevertSpell() => SelectedSpell?.Revert();

    private void ApplyView()
    {
        var previous = SelectedSpell;

        Spells.Clear();

        foreach (var spell in Ordered(all.Where(Matches)))
        {
            Spells.Add(spell);
        }

        // Keep the selection across a filter or sort change, so the detail pane does not blank out
        // while the user is typing a search that still matches what they were looking at.
        SelectedSpell = previous is not null && Spells.Contains(previous)
            ? previous
            : Spells.FirstOrDefault();
    }

    private IEnumerable<SpellEditorViewModel> Ordered(IEnumerable<SpellEditorViewModel> spells)
    {
        if (Sort == SpellSort.Database)
        {
            // Reversing the file order is still the file order read backwards, which is a useful
            // thing to ask for and costs nothing.
            return SortDescending ? spells.Reverse() : spells;
        }

        IOrderedEnumerable<SpellEditorViewModel> ordered = Sort switch
        {
            SpellSort.Name => Order(spells, s => s.Name),
            SpellSort.School => Order(spells, s => s.SchoolId),
            SpellSort.Level => Order(spells, s => s.Level),
            _ => Order(spells, s => s.CastCost),
        };

        // A stable second key, so two spells of the same level do not swap places on a redraw.
        return ordered.ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    private IOrderedEnumerable<SpellEditorViewModel> Order<TKey>(
        IEnumerable<SpellEditorViewModel> spells, Func<SpellEditorViewModel, TKey> key) =>
        SortDescending ? spells.OrderByDescending(key) : spells.OrderBy(key);

    private bool Matches(SpellEditorViewModel spell)
    {
        if (Search.Length == 0)
        {
            return true;
        }

        return spell.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || spell.SchoolId.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || spell.BaseclassSummary.Contains(Search, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSpellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditableViewModel.IsDirty)
            && sender is SpellEditorViewModel)
        {
            IsDirty = all.Exists(s => s.IsDirty);
        }
    }

    public void Dispose()
    {
        foreach (var spell in all)
        {
            spell.PropertyChanged -= OnSpellChanged;
        }

        all.Clear();
        Spells.Clear();
    }
}
