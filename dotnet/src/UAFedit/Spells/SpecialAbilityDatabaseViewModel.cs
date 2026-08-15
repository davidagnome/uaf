using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Data;
using UAFcore;

namespace UAFedit.Spells;

/// <summary>
/// The design's special abilities: the master list, and one ability's entries beside it.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>ID_EDIT_SPECIAL_ABILITIES</c> (<c>UAFWinEd/ChooseSpeclAbDlg.cpp</c>). Everything it
/// shows comes from <see cref="LoadedDesign.SpecialAbilities"/>, which reads
/// <c>Data/specialAbilities.txt</c> — a line-oriented text file, not the archive format, and the
/// only database in the design that is not binary.
/// </para>
/// <para>
/// <b>Nothing here writes.</b> The edits live in the view models and are readable through
/// <see cref="EditedAbilities"/>; putting the file back is a separate job, and it is a real one —
/// the writer has to re-bracket names, prefix continuation lines with <c>-</c> and avoid emitting a
/// line the reader would take for a comment or a delimiter.
/// </para>
/// </remarks>
public sealed partial class SpecialAbilityDatabaseViewModel : ObservableObject, IDisposable
{
    private readonly List<SpecialAbilityEditorViewModel> all = [];

    public SpecialAbilityDatabaseViewModel(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);

        foreach (var ability in design.SpecialAbilities)
        {
            var editor = new SpecialAbilityEditorViewModel(ability);
            editor.PropertyChanged += OnAbilityChanged;
            all.Add(editor);
        }

        ApplyFilter();
        SelectedAbility = Abilities.FirstOrDefault();
        Status = $"{all.Count} special abilities.";
    }

    /// <summary>Builds the editor over abilities directly, for tests and for reuse.</summary>
    public SpecialAbilityDatabaseViewModel(IEnumerable<SpecialAbility> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        foreach (var ability in abilities)
        {
            var editor = new SpecialAbilityEditorViewModel(ability);
            editor.PropertyChanged += OnAbilityChanged;
            all.Add(editor);
        }

        ApplyFilter();
        SelectedAbility = Abilities.FirstOrDefault();
        Status = $"{all.Count} special abilities.";
    }

    /// <summary>The abilities matching <see cref="Search"/>, in file order.</summary>
    /// <remarks>
    /// File order, not alphabetical: a design's abilities are related to their neighbours far more
    /// often than to their alphabetical neighbours, and the file is the only ordering the design
    /// itself chose.
    /// </remarks>
    public ObservableCollection<SpecialAbilityEditorViewModel> Abilities { get; } = [];

    [ObservableProperty]
    private SpecialAbilityEditorViewModel? selectedAbility;

    /// <summary>
    /// Filters by ability name, and by entry name too.
    /// </summary>
    /// <remarks>
    /// <b>Searching the entry names matters more than searching the ability names.</b> What a
    /// maintainer is usually looking for is which ability defines a given hook — the ability's own
    /// name is often a design's private label, while the entry's name is what the engine looks up.
    /// </remarks>
    [ObservableProperty]
    private string search = string.Empty;

    [ObservableProperty]
    private string status = string.Empty;

    /// <summary>The last <see cref="CompileAllScripts"/> result, or null if it has not been run.</summary>
    [ObservableProperty]
    private ScriptCompileReport? report;

    public int Count => all.Count;

    /// <summary>Whether any ability has been edited.</summary>
    [ObservableProperty]
    private bool isDirty;

    /// <summary>Every ability, edits included — the design's list as it now stands.</summary>
    /// <remarks>
    /// Rebuilt on each read rather than cached: the whole point of it is to be current, and it is
    /// read once when something wants to save, not per frame.
    /// </remarks>
    public IReadOnlyList<SpecialAbility> EditedAbilities => [.. all.Select(a => a.ToAbility())];

    /// <summary>Just the abilities that were touched.</summary>
    public IReadOnlyList<SpecialAbilityEditorViewModel> Edited => [.. all.Where(a => a.IsDirty)];

    partial void OnSearchChanged(string value)
    {
        _ = value;
        ApplyFilter();
    }

    /// <summary>
    /// Compiles every script in every ability — the port of <c>Test All Special Abilities</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A plain method with the command wrapped round it</b>, following
    /// <c>MainWindowViewModel.Open</c>: this is the part worth testing, and it must be reachable
    /// without an application running.
    /// </para>
    /// <para>
    /// Every script is compiled even after one fails, because that is the question being asked —
    /// the design wants to know which of its scripts are broken, not that one of them is.
    /// </para>
    /// </remarks>
    public ScriptCompileReport CompileAllScripts()
    {
        var failures = new List<ScriptFailure>();
        int scripts = 0;
        int abilities = 0;

        foreach (var ability in all)
        {
            bool any = false;

            foreach (var entry in ability.Entries.Where(e => e.IsScript))
            {
                any = true;
                scripts++;
                entry.Compile();

                if (!entry.Diagnostics.Succeeded)
                {
                    failures.Add(new ScriptFailure(
                        ability.Name, entry.Name, entry.Diagnostics.Summary));
                }
            }

            if (any)
            {
                abilities++;
            }
        }

        var built = new ScriptCompileReport(abilities, scripts, failures);
        Report = built;
        Status = built.Summary("abilities");
        return built;
    }

    /// <remarks>
    /// Off the UI thread: a shipped design carries hundreds of scripts and each one is a full
    /// lex-and-compile pass, which is long enough to freeze a window.
    /// </remarks>
    [RelayCommand]
    private async Task CompileAllAsync()
    {
        Status = "Compiling…";
        await Task.Run(CompileAllScripts).ConfigureAwait(true);
    }

    [RelayCommand]
    private void RevertAbility() => SelectedAbility?.Revert();

    private void ApplyFilter()
    {
        var previous = SelectedAbility;

        Abilities.Clear();

        foreach (var ability in all.Where(Matches))
        {
            Abilities.Add(ability);
        }

        // Keeping the selection through a filter change is what stops the detail pane blanking out
        // while the user types a search that still matches what they were looking at.
        SelectedAbility = previous is not null && Abilities.Contains(previous)
            ? previous
            : Abilities.FirstOrDefault();
    }

    private bool Matches(SpecialAbilityEditorViewModel ability)
    {
        if (Search.Length == 0)
        {
            return true;
        }

        return ability.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || ability.Entries.Any(
                e => e.Name.Contains(Search, StringComparison.OrdinalIgnoreCase));
    }

    private void OnAbilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditableViewModel.IsDirty)
            && sender is SpecialAbilityEditorViewModel)
        {
            IsDirty = all.Exists(a => a.IsDirty);
        }
    }

    public void Dispose()
    {
        foreach (var ability in all)
        {
            ability.PropertyChanged -= OnAbilityChanged;
            ability.Dispose();
        }

        all.Clear();
        Abilities.Clear();
    }
}
