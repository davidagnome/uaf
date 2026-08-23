using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;
using UAFcore;
using UAFedit.CrossReference;
using UAFedit.Databases;
using UAFedit.Events;
using UAFedit.Globals;
using UAFedit.Levels;
using UAFedit.Spells;

namespace UAFedit.ViewModels;

/// <summary>
/// The shell's panes, in tab order.
/// </summary>
/// <remarks>
/// The order is the reference's menu order — the design itself, then the Level menu, then the
/// Database menu — rather than the order they were ported in.
/// </remarks>
public enum EditorPane
{
    /// <summary>The read-only navigation tree and record list.</summary>
    Design,

    /// <summary>The design's own settings — game.dat's GLOBAL_STATS.</summary>
    Settings,

    /// <summary>The Level menu.</summary>
    Levels,

    /// <summary>The event editor.</summary>
    Events,

    /// <summary>Database &gt; Edit Items.</summary>
    Items,

    /// <summary>Database &gt; Edit Monsters.</summary>
    Monsters,

    /// <summary>Database &gt; Edit Spells.</summary>
    Spells,

    /// <summary>Database &gt; Edit Special Abilities.</summary>
    Abilities,

    /// <summary>Tools &gt; Cross Reference.</summary>
    CrossReference,
}

/// <summary>
/// The shell: a design, its navigation tree, and the record list of whatever is selected.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opening is <see cref="Open"/>, a plain method, and the command is a wrapper round it.</b>
/// That is what lets the whole inspector be tested against a real design with no window, no
/// display and no Avalonia application at all — the same separation that made
/// <see cref="LoadedDesign"/> testable where <c>OpenDesign</c> was not (docs/PORTING-PLAN.md §7
/// Phase 0). The two things the view must supply, a folder picker and a way to close, are
/// delegates the view sets.
/// </para>
/// </remarks>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string BaseTitle = "Dungeon Craft Editor";

    private LoadedDesign? design;

    /// <summary>The tree's roots — one design at a time, so exactly one until it opens another.</summary>
    public ObservableCollection<DesignNodeViewModel> Roots { get; } = [];

    /// <summary>
    /// Asks the user for a design directory, or null if they cancelled.
    /// </summary>
    /// <remarks>
    /// Set by the view, because a folder picker needs a <c>TopLevel</c>. Left null in tests, which
    /// call <see cref="Open"/> directly.
    /// </remarks>
    public Func<Task<string?>>? ChooseFolder { get; set; }

    /// <summary>What <c>File &gt; Exit</c> does. Set by the view; closing a window is its business.</summary>
    public Action? RequestExit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Columns))]
    [NotifyPropertyChangedFor(nameof(Rows))]
    private DesignNodeViewModel? selectedNode;

    [ObservableProperty]
    private string status = "No design open. File > Open Design… to browse one.";

    /// <summary>The columns of the selected category, or none.</summary>
    public IReadOnlyList<RecordColumn> Columns => SelectedNode?.Table.Columns ?? [];

    /// <summary>
    /// The rows of the selected category, built on this read.
    /// </summary>
    /// <remarks>
    /// Reading this is what materialises a category — see <see cref="RecordTable"/>. Selecting a
    /// node is therefore the work, not opening the design.
    /// </remarks>
    public IReadOnlyList<RecordRow> Rows => SelectedNode?.Table.Rows ?? [];

    public string Title => design is null ? BaseTitle : $"{BaseTitle} — {DesignName}";

    private string DesignName { get; set; } = string.Empty;

    /// <summary>
    /// Which pane is open. Setting it is what builds that pane.
    /// </summary>
    /// <remarks>
    /// Bound to the tab strip, so a tab the user never visits costs nothing — see
    /// <see cref="Show"/> for why that matters.
    /// </remarks>
    [ObservableProperty]
    private EditorPane selectedPane;

    /// <summary>The design's settings, once its tab has been opened.</summary>
    [ObservableProperty]
    private DesignGlobalsViewModel? settingsPane;

    /// <summary>The Level menu, once its tab has been opened.</summary>
    [ObservableProperty]
    private LevelsViewModel? levelsPane;

    /// <summary>The event editor, once its tab has been opened.</summary>
    [ObservableProperty]
    private EventEditorViewModel? eventsPane;

    /// <summary>The item database editor, once its tab has been opened.</summary>
    [ObservableProperty]
    private ItemDatabaseViewModel? itemsPane;

    /// <summary>The monster database editor, once its tab has been opened.</summary>
    [ObservableProperty]
    private MonsterDatabaseViewModel? monstersPane;

    /// <summary>The spell database editor, once its tab has been opened.</summary>
    [ObservableProperty]
    private SpellDatabaseViewModel? spellsPane;

    /// <summary>The special-ability database editor, once its tab has been opened.</summary>
    [ObservableProperty]
    private SpecialAbilityDatabaseViewModel? abilitiesPane;

    /// <summary>The cross-reference pane, once its tab has been opened.</summary>
    /// <remarks>
    /// Building it costs nothing — the sweep is a button, not a constructor, because it reads
    /// every level in the design.
    /// </remarks>
    [ObservableProperty]
    private CrossReferenceViewModel? crossReferencePane;

    partial void OnSelectedPaneChanged(EditorPane value) => Show(value);

    /// <summary>
    /// Builds the pane behind a tab, the first time that tab is opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lazy because two of them read the whole design off disk.</b>
    /// <see cref="LevelsViewModel"/> reads every <c>.lvl</c> for its extent and counts, and
    /// <see cref="EventEditorViewModel"/> reads every level again for its events. Building all six
    /// on open would make <c>File &gt; Open</c> take as long as the slowest pane on a design the
    /// user may only want the map from. This is the same bargain
    /// <see cref="RecordTable"/> already strikes for the tree's categories.
    /// </para>
    /// <para>
    /// A pane that throws leaves its tab empty and says so on the status line, rather than taking
    /// the window down: one unreadable database should not cost the user the other five.
    /// </para>
    /// </remarks>
    public void Show(EditorPane pane)
    {
        if (design is not { } open)
        {
            return;
        }

        try
        {
            switch (pane)
            {
                case EditorPane.Settings:
                    SettingsPane ??= Watch(new DesignGlobalsViewModel(open));
                    break;
                case EditorPane.Levels:
                    LevelsPane ??= new LevelsViewModel(open);
                    break;
                case EditorPane.Events:
                    EventsPane ??= Watch(new EventEditorViewModel(open));
                    break;
                case EditorPane.Items:
                    ItemsPane ??= Watch(new ItemDatabaseViewModel(open));
                    break;
                case EditorPane.Monsters:
                    MonstersPane ??= Watch(new MonsterDatabaseViewModel(open));
                    break;
                case EditorPane.Spells:
                    SpellsPane ??= Watch(new SpellDatabaseViewModel(open));
                    break;
                case EditorPane.Abilities:
                    AbilitiesPane ??= Watch(new SpecialAbilityDatabaseViewModel(open));
                    break;
                case EditorPane.CrossReference:
                    CrossReferencePane ??= new CrossReferenceViewModel(open);
                    break;
                default:
                    break;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException
                                    or NotSupportedException or InvalidOperationException
                                    or ArgumentException)
        {
            Status = $"Could not open the {pane} pane: {e.Message}";
        }
    }

    /// <summary>
    /// Subscribes to a savable pane so <see cref="IsDirty"/> follows it.
    /// </summary>
    /// <remarks>
    /// Without this the File &gt; Save item would only re-evaluate when something else happened to
    /// raise a property — so the first edit in a pane would not enable it.
    /// </remarks>
    private T Watch<T>(T pane) where T : INotifyPropertyChanged
    {
        pane.PropertyChanged += OnPaneChanged;
        return pane;
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The database panes raise IsDirty; the event pane raises both that and the level-level
        // flag, because a stashed level is unsaved work even when the open one is clean.
        if (e.PropertyName is nameof(IsDirty)
                           or nameof(EventEditorViewModel.HasUnsavedLevels))
        {
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>Drops every built pane, disposing the ones that hold anything.</summary>
    private void ClosePanes()
    {
        foreach (var pane in new INotifyPropertyChanged?[]
                 { ItemsPane, MonstersPane, SpellsPane, EventsPane, AbilitiesPane,
                   SettingsPane })
        {
            if (pane is not null)
            {
                pane.PropertyChanged -= OnPaneChanged;
            }
        }

        (SpellsPane as IDisposable)?.Dispose();
        (AbilitiesPane as IDisposable)?.Dispose();

        SettingsPane = null;
        CrossReferencePane = null;
        LevelsPane = null;
        EventsPane = null;
        ItemsPane = null;
        MonstersPane = null;
        SpellsPane = null;
        AbilitiesPane = null;

        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>
    /// Opens a design directory and rebuilds the tree, answering whether it opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both decoders are left null.</b> <see cref="LoadedDesign.Open"/> takes an optional image
    /// decoder and font rasteriser; the inspector draws no art and needs no text measured, and
    /// passing null costs it nothing — <c>ImageLoader</c> substitutes a null decoder and
    /// <see cref="LoadedDesign.Font"/> answers null, which is the same degradation contract the
    /// engine accepts for a design whose art is missing. The editor stays free of SDL as a result,
    /// which is the point of <c>UAFcore.App</c> being a separate project.
    /// </para>
    /// <para>
    /// A directory that is not a design is an ordinary outcome — a user picking the wrong folder —
    /// so the failure is a status line rather than a throw. The previously open design is kept
    /// until the new one has actually loaded.
    /// </para>
    /// </remarks>
    public bool Open(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        LoadedDesign opened;
        try
        {
            // Editor, not the default Engine: the two readers differ only in what they accept
            // below 0.998101, and an editor that refused a legacy design could not upgrade it --
            // which is the one thing the reference's editor exists to do that the engine does not.
            opened = LoadedDesign.Open(root, role: ArchiveRole.Editor);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                    or InvalidDataException or EndOfStreamException
                                    or NotSupportedException or InvalidOperationException
                                    or ArgumentException)
        {
            Status = $"Could not open '{root}': {e.Message}";
            return false;
        }

        // The panes hold records read out of the design being replaced, so they go first.
        ClosePanes();
        design?.Dispose();
        design = opened;
        SelectedPane = EditorPane.Design;

        DesignName = string.IsNullOrWhiteSpace(opened.Name)
            ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar,
                                            Path.AltDirectorySeparatorChar))
            : opened.Name;

        var categories = DesignInspector.Categories(opened);

        SelectedNode = null;
        Roots.Clear();
        Roots.Add(DesignNodeViewModel.Root(DesignName, categories));

        // The first category rather than the design's own node: the root has no records, and a
        // right pane that opens empty reads as a failure to load.
        SelectedNode = categories.Count > 0 ? categories[0] : Roots[0];

        Status = $"{DesignName} — version {opened.Globals.Version.Value.ToString("0.####")}, "
               + $"{categories.Count(c => c.IsReadable)} of {categories.Count} categories readable";

        OnPropertyChanged(nameof(Title));
        return true;
    }

    /// <summary>Whether any built pane holds an edit that is not on disk.</summary>
    public bool IsDirty =>
        (ItemsPane?.IsDirty ?? false)
        || (MonstersPane?.IsDirty ?? false)
        || (SpellsPane?.IsDirty ?? false)
        || (EventsPane?.HasUnsavedLevels ?? false)
        || (AbilitiesPane?.IsDirty ?? false)
        || (SettingsPane?.IsDirty ?? false);

    /// <summary>
    /// Writes every pane that has been edited back to the design folder.
    /// </summary>
    /// <returns>What happened, for the status line.</returns>
    /// <remarks>
    /// <para>
    /// <b>Only a pane that was edited is written.</b> Not an optimisation: the writers refuse
    /// records below 0.998101 rather than guessing at them, so writing an untouched database would
    /// turn opening a legacy design and changing nothing into a refusal — or, worse on a design
    /// they accept, silently upgrade files the user never opened.
    /// </para>
    /// <para>
    /// <b>Each file is written on its own and a failure stops the rest.</b> The alternative —
    /// carrying on after one refusal — leaves the design half upgraded, which is the state hardest
    /// to reason about afterwards. What has already been written stays written; each file is
    /// individually complete, because <see cref="DesignSaver"/> stages every one of them.
    /// </para>
    /// <para>
    /// Panes accept their changes only once their file is safely in place, so a refusal leaves the
    /// editor still holding the edit and still reading as dirty.
    /// </para>
    /// </remarks>
    public string Save()
    {
        if (design is not { } open)
        {
            return "No design is open.";
        }

        var written = new List<string>();
        var refused = new List<string>();
        bool versionMoved = false;

        try
        {
            if (SettingsPane is { IsDirty: true } settings)
            {
                // The pane saves itself: it read game.dat whole, events included, and it is the
                // only thing holding those events.
                settings.Save();
                written.Add("game.dat");

                // And the binary ability database beside it, because writing game.dat is what
                // creates the requirement: the reference editor refuses a design at 5.26 with no
                // specialAbilities.dat ("Unable to open special abilities db file ... error 2",
                // then "Failed to load design data file"). The template ships only the .txt, so a
                // design saved without this loads in the port and not in the reference.
                if (Try(() => DesignSaver.SaveSpecialAbilityDatabase(
                                  open.Root, open.SpecialAbilities)))
                {
                    written.Add("specialAbilities.dat");
                }
                else
                {
                    // Best-effort, and the failure is worth naming rather than throwing. The .txt
                    // is where the scripts authoritatively live; the .dat is the binary form the
                    // reference wants beside it. A design whose text file has a key the binary ASL
                    // cannot round-trip -- SomethingWild has exactly one, class_Druid, whose script
                    // declaration carries no '=' on its first line so the parser splits on a later
                    // one and the key ends up with a CRLF in it -- cannot have a faithful .dat, and
                    // losing the whole save over it would be much worse.
                    refused.Add("specialAbilities.dat");
                }

                // And every other database, edited or not. Writing game.dat moves the design's
                // version stamp, and a database left at its old shape underneath a new stamp is
                // not merely stale -- the reference reads the stamp first and then cannot read
                // the file at all. Its log on a half-saved design says "Loading monster DB
                // version: 0.9150250" under a 5.26 game.dat, and "Unable to load race data file"
                // on a races.dat it had read happily moments before at the old version.
                //
                // The ones the writers still refuse (races, baseclass, classes at their legacy
                // container shapes) cannot be brought along, and that is exactly what is left of
                // the warnings the reference shows on such a design.
                //
                // Done at the END of the save, after the panes: a pane holds edits the design does
                // not, so writing from the design first would only be overwritten a moment later.
                versionMoved = true;
            }

            if (ItemsPane is { IsDirty: true } items)
            {
                DesignSaver.SaveItems(open.Root, items.Database);
                items.AcceptChanges();
                written.Add("items.dat");
            }

            if (MonstersPane is { IsDirty: true } monsters)
            {
                DesignSaver.SaveMonsters(open.Root, monsters.Records);
                monsters.AcceptChanges();
                written.Add("monsters.dat");
            }

            if (SpellsPane is { IsDirty: true } spells)
            {
                DesignSaver.SaveSpells(open.Root, spells.EditedSpells);
                spells.AcceptChanges();
                written.Add("spells.dat");
            }

            if (AbilitiesPane is { IsDirty: true } abilities)
            {
                DesignSaver.SaveSpecialAbilities(open.Root, abilities.EditedAbilities);
                written.Add("specialAbilities.txt");

                // Both shapes, from the same list -- see DesignSaver.SaveSpecialAbilityDatabase.
                if (Try(() => DesignSaver.SaveSpecialAbilityDatabase(
                                  open.Root, abilities.EditedAbilities)))
                {
                    written.Add("specialAbilities.dat");
                }
                else
                {
                    refused.Add("specialAbilities.dat");
                }

                abilities.AcceptChanges();
            }

            if (EventsPane is { HasUnsavedLevels: true } events)
            {
                // The pane keys its edits by position in LevelFiles, not by level number: a
                // level's number is its index plus one and designs ship gaps, so the file name has
                // to come from the list rather than be derived from the key.
                var files = open.LevelFiles;

                foreach (var (index, level) in events.EditedLevels)
                {
                    if (index < 0 || index >= files.Count)
                    {
                        continue;
                    }

                    DesignSaver.SaveLevel(open.Root, files[index], level);
                    written.Add(Path.GetFileName(files[index]));
                }

                events.AcceptChanges();
            }

            if (versionMoved)
            {
                written.AddRange(Coherent(open, written));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                    or NotSupportedException or InvalidDataException
                                    or InvalidOperationException or ArgumentException)
        {
            OnPropertyChanged(nameof(IsDirty));
            return written.Count == 0
                ? $"Nothing was saved: {e.Message}"
                : $"Saved {string.Join(", ", written)}, then stopped: {e.Message}";
        }

        OnPropertyChanged(nameof(IsDirty));

        string note = refused.Count == 0
            ? string.Empty
            : $" Could not write {string.Join(", ", refused)} — see the log.";

        return written.Count == 0
            ? (refused.Count == 0 ? "Nothing has changed." : $"Nothing was written.{note}")
            : $"Saved {string.Join(", ", written)}.{note}";
    }

    /// <summary>
    /// Writes the databases a version change drags along, skipping any a pane already wrote.
    /// </summary>
    /// <remarks>
    /// A refusal here is reported rather than thrown: one database the writers cannot take should
    /// not undo the ones they can, and the design is already more coherent for each that lands.
    /// </remarks>
    private IEnumerable<string> Coherent(LoadedDesign open, List<string> already)
    {
        var done = new List<string>();

        if (!already.Contains("items.dat") && open.Items is { } items)
        {
            if (Try(() => DesignSaver.SaveItems(open.Root, items)))
            {
                done.Add("items.dat");
            }
        }

        if (!already.Contains("monsters.dat") && open.Monsters is { } monsters)
        {
            if (Try(() => DesignSaver.SaveMonsters(open.Root, monsters)))
            {
                done.Add("monsters.dat");
            }
        }

        if (!already.Contains("spells.dat") && open.Spells is { } spells)
        {
            if (Try(() => DesignSaver.SaveSpells(open.Root, spells)))
            {
                done.Add("spells.dat");
            }
        }

        done.AddRange(CoherentLevels(open, already));

        return done;
    }

    /// <summary>
    /// Rewrites every level the version change left behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A level is as much a versioned file as a database, and leaving one stale is what the
    /// reference offers to fix on load.</b> A design created from the template carries its
    /// <c>Level001.lvl</c> at 0.915; the first save moves the stamp to 5.26 and, without this, the
    /// editor then opens it and asks whether to convert the level file to the latest format — the
    /// same incoherence already fixed for items, monsters and spells, in the one file kind that was
    /// missed.
    /// </para>
    /// <para>
    /// <b>Every refusal is silent by design.</b> A level below 0.998101 whose events still carry
    /// unresolvable numeric keys cannot be written at all
    /// (<see cref="UAF.Serialization.GameEventWriter.CanWrite"/>), and the right answer there is to
    /// leave the file exactly as it was rather than half-write it. The level stays readable in its
    /// old form, which is what the reference's own conversion prompt exists to handle.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> CoherentLevels(LoadedDesign open, List<string> already)
    {
        var files = open.LevelFiles;

        for (int index = 0; index < files.Count; index++)
        {
            string name = Path.GetFileName(files[index]);

            if (already.Contains(name) || open.Level(index) is not { } level)
            {
                continue;
            }

            if (Try(() => DesignSaver.SaveLevel(open.Root, name, level)))
            {
                yield return name;
            }
        }
    }

    /// <summary>Runs a write, answering whether it went rather than letting a refusal escape.</summary>
    private static bool Try(Action write)
    {
        try
        {
            write();
            return true;
        }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException
                                    or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [RelayCommand]
    private void SaveDesign() => Status = Save();

    /// <summary>
    /// Copies the template into a folder and opens the copy.
    /// </summary>
    /// <returns>What happened, for the status line.</returns>
    /// <remarks>
    /// <b>The template is copied, never opened in place.</b> Save writes back to the folder a
    /// design was opened from, so opening the template directly would overwrite it the first time
    /// anybody saved — see <see cref="DesignTemplate"/>.
    /// </remarks>
    public string New(string destination)
    {
        try
        {
            DesignTemplate.CreateAt(destination);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                    or InvalidOperationException or ArgumentException)
        {
            return $"Could not create a design in '{destination}': {e.Message}";
        }

        return Open(destination)
            ? $"New design created in '{destination}'."
            : Status;                            // Open already said why
    }

    [RelayCommand]
    private async Task NewDesignAsync()
    {
        if (ChooseFolder is null)
        {
            Status = "No folder picker is available.";
            return;
        }

        if (await ChooseFolder().ConfigureAwait(true) is { } destination)
        {
            Status = New(destination);
        }
    }

    [RelayCommand]
    private async Task OpenDesignAsync()
    {
        if (ChooseFolder is null)
        {
            Status = "No folder picker is available.";
            return;
        }

        if (await ChooseFolder().ConfigureAwait(true) is { } root)
        {
            Open(root);
        }
    }

    [RelayCommand]
    private void Exit() => RequestExit?.Invoke();

    public void Dispose()
    {
        ClosePanes();
        design?.Dispose();
        design = null;
    }
}
