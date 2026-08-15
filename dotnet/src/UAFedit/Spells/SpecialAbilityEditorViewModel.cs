using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Data;

namespace UAFedit.Spells;

/// <summary>
/// One special ability: a name, and the bag of scripts, parameters, tables and constants it carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>An ability is a bag, not a record with fields.</b> Nothing in the format says which entries an
/// ability has — a design invents the names and the engine looks them up by string
/// (<c>RunGlobalScript</c>, <c>Shared/Specab.cpp:2097</c>). So this editor is a list of entries with
/// no schema behind it, and the only structure worth showing is the four kinds.
/// </para>
/// <para>
/// <b><c>name</c> is not one of the entries.</b> The parser pulls it out of the bag and makes it the
/// ability's identity, exactly as the reference deletes it after reading
/// (<see cref="SpecialAbilitiesFile"/>) — so renaming here is renaming the ability, and the entry
/// list never shows a row called "name".
/// </para>
/// </remarks>
public sealed partial class SpecialAbilityEditorViewModel : EditableViewModel, IDisposable
{
    private readonly SpecialAbility original;

    public SpecialAbilityEditorViewModel(SpecialAbility ability)
    {
        ArgumentNullException.ThrowIfNull(ability);

        original = ability;
        Name = ability.Name;

        Entries.CollectionChanged += OnEntriesChanged;
        Fill(ability);

        ResetDirty();
    }

    [ObservableProperty]
    private string name = string.Empty;

    /// <summary>Every entry, in file order. Kinds are interleaved; the file does not group them.</summary>
    public ObservableCollection<SpecialAbilityEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    private SpecialAbilityEntryViewModel? selectedEntry;

    /// <summary>A one-line summary of the bag, for the master list.</summary>
    public string Summary =>
        $"{Count(SpecialAbilityEntryKind.Script)} scripts, "
        + $"{Count(SpecialAbilityEntryKind.Variable)} parameters, "
        + $"{Count(SpecialAbilityEntryKind.IntegerTable)} tables, "
        + $"{Count(SpecialAbilityEntryKind.Constant)} constants";

    public int ScriptCount => Count(SpecialAbilityEntryKind.Script);

    /// <summary>Entries that would not survive a write and a read. Usually none.</summary>
    public IReadOnlyList<SpecialAbilityEntryViewModel> Unfaithful =>
        [.. Entries.Where(e => !e.IsFaithful)];

    public int Count(SpecialAbilityEntryKind kind) => Entries.Count(e => e.Kind == kind);

    /// <summary>
    /// Compiles every script this ability carries, answering how many failed.
    /// </summary>
    /// <remarks>
    /// This is <c>Test All Special Abilities</c> narrowed to one ability. Note that it keeps going
    /// past a failure: the engine does too — a script that will not compile is logged, marked
    /// <c>SPECAB_SCRIPTERROR</c> and skipped, and the ability's other scripts still run.
    /// </remarks>
    public int CompileScripts()
    {
        int failed = 0;

        foreach (var entry in Entries.Where(e => e.IsScript))
        {
            entry.Compile();
            if (!entry.Diagnostics.Succeeded)
            {
                failed++;
            }
        }

        return failed;
    }

    [RelayCommand]
    private void CompileAllScripts() => CompileScripts();

    /// <summary>Adds an empty entry and selects it.</summary>
    /// <remarks>
    /// It starts as a <see cref="SpecialAbilityEntryKind.Constant"/> with no name, which is the one
    /// combination that is faithful while empty — a bracketed kind needs a name before its key is
    /// three characters long and reads back as itself.
    /// </remarks>
    [RelayCommand]
    private void AddEntry()
    {
        var entry = new SpecialAbilityEntryViewModel(
            new SpecialAbilityEntry(string.Empty, string.Empty,
                                    SpecialAbilityEntryKind.Constant), Name);

        Entries.Add(entry);
        SelectedEntry = entry;
    }

    [RelayCommand]
    private void RemoveEntry()
    {
        if (SelectedEntry is { } entry)
        {
            Entries.Remove(entry);
            SelectedEntry = null;
        }
    }

    /// <summary>The edited ability.</summary>
    public SpecialAbility ToAbility() => new(Name, [.. Entries.Select(e => e.ToEntry())]);

    /// <summary>Throws away every edit, including added and removed entries.</summary>
    public void Revert()
    {
        Name = original.Name;
        Clear();
        Fill(original);
        SelectedEntry = null;
        ResetDirty();
    }

    /// <remarks>
    /// Selection is the view's, and the derived counts fire off the entry list rather than off an
    /// edit of their own.
    /// </remarks>
    protected override bool IsEdit(string? propertyName) =>
        propertyName is not (nameof(SelectedEntry) or nameof(Summary) or nameof(ScriptCount)
                             or nameof(Unfaithful));

    private void Fill(SpecialAbility ability)
    {
        foreach (var entry in ability.Entries)
        {
            Entries.Add(new SpecialAbilityEntryViewModel(entry, ability.Name));
        }
    }

    private void Clear()
    {
        foreach (var entry in Entries)
        {
            entry.PropertyChanged -= OnEntryChanged;
        }

        Entries.Clear();
    }

    /// <remarks>
    /// <b>An ability is dirty when one of its entries is</b>, and nothing else propagates that: an
    /// entry is its own <see cref="EditableViewModel"/> and the collection only reports adds and
    /// removes. Without this, editing a script's text would leave the ability looking untouched.
    /// </remarks>
    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var entry in e.OldItems?.OfType<SpecialAbilityEntryViewModel>() ?? [])
        {
            entry.PropertyChanged -= OnEntryChanged;
        }

        foreach (var entry in e.NewItems?.OfType<SpecialAbilityEntryViewModel>() ?? [])
        {
            entry.PropertyChanged += OnEntryChanged;
        }

        IsDirty = true;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ScriptCount));
        OnPropertyChanged(nameof(Unfaithful));
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditableViewModel.IsDirty)
            && sender is SpecialAbilityEntryViewModel { IsDirty: true })
        {
            IsDirty = true;
        }

        if (e.PropertyName is nameof(SpecialAbilityEntryViewModel.Kind)
                           or nameof(SpecialAbilityEntryViewModel.Name))
        {
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(ScriptCount));
            OnPropertyChanged(nameof(Unfaithful));
        }
    }

    public void Dispose()
    {
        Entries.CollectionChanged -= OnEntriesChanged;
        Clear();
    }
}
