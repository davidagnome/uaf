using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAFcore;

namespace UAFedit.Levels;

/// <summary>
/// The Level menu: the design's levels, and whichever one is open.
/// </summary>
/// <remarks>
/// <para>
/// <c>ID_VIEW_LEVELDATA</c> (<c>MainFrm.cpp:1638</c>) puts up <c>CSelectLevel</c>, a modal list of
/// all 255 slots; picking one loads it into the file-scope <c>levelData</c> and sets
/// <c>globalData.currLevel</c>. Here the list and the level are on screen together, because there
/// is no reason for a chooser to be modal and every reason for it not to be — the original's own
/// comment says its dialog "is not supposed to exit until a valid 'used' level is chosen"
/// (<c>MainFrm.cpp:1645</c>) and wraps the whole thing in a <c>while (!ok)</c>.
/// </para>
/// <para>
/// <b>The list is of files, not of the 255 slots.</b> The original lists every slot whether or not
/// a file exists, because it can create one; this editor reads and does not yet write, so a row for
/// a level that is not there would be a row that does nothing. Stats rows with no file are surfaced
/// separately as <see cref="Orphans"/> — that is the same information, and it is the case the
/// original pops an error box about.
/// </para>
/// </remarks>
public sealed partial class LevelsViewModel : ObservableObject
{
    private readonly LoadedDesign design;

    /// <summary>Opens the Level menu over a design.</summary>
    /// <param name="readFiles">
    /// Whether the list reads every <c>.lvl</c> for its true extent and counts. See
    /// <see cref="LevelCatalog.Build"/>; off makes the list cheap and leaves the file-derived
    /// columns blank.
    /// </param>
    public LevelsViewModel(LoadedDesign design, bool readFiles = true)
    {
        ArgumentNullException.ThrowIfNull(design);

        this.design = design;
        Catalog = LevelCatalog.Build(design, readFiles);
        Levels = [.. Catalog.Entries];
        Orphans = [.. Catalog.Orphans];

        // The design's own start level, when it has one on disk. The original opens on
        // globalData.currLevel, which is the same thing on a freshly loaded design.
        SelectedLevel = Catalog.ByNumber(design.Globals.StartLevel + 1) ?? Levels.FirstOrDefault();
    }

    /// <summary>The pairing of files to stats rows, which is the whole point of the namespace.</summary>
    public LevelCatalog Catalog { get; }

    /// <summary>Every level file the design ships, in directory order.</summary>
    public ObservableCollection<LevelCatalogEntry> Levels { get; }

    /// <summary>
    /// Stats rows with no <c>.lvl</c> behind them.
    /// </summary>
    /// <remarks>
    /// The original clears their <c>used</c> flag on sight and reports them as
    /// "The LVL files for the following levels are missing" (<c>SelectLevel.cpp:256</c>). Nothing
    /// here modifies the design, so they are simply listed.
    /// </remarks>
    public ObservableCollection<LevelCatalogOrphan> Orphans { get; }

    /// <summary>The level whose panel is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Panel))]
    [NotifyPropertyChangedFor(nameof(Status))]
    private LevelCatalogEntry? selectedLevel;

    private LevelPanelViewModel? panel;

    /// <summary>
    /// The open level, built on demand.
    /// </summary>
    /// <remarks>
    /// Building it re-reads the <c>.lvl</c> — <see cref="LoadedDesign.Level"/> holds no cache — so
    /// this is deliberately not done while merely moving through the list in a keyboard-driven
    /// selection. It is cached against the entry so that reading the property repeatedly, which
    /// bindings do, costs one read.
    /// </remarks>
    public LevelPanelViewModel? Panel
    {
        get
        {
            if (SelectedLevel is not { } entry)
            {
                return null;
            }

            if (panel?.Entry != entry)
            {
                panel = new LevelPanelViewModel(design, entry);
            }

            return panel;
        }
    }

    /// <summary>What the list's footer says.</summary>
    public string Status
    {
        get
        {
            var parts = new List<string>
            {
                $"{Levels.Count} level file{(Levels.Count == 1 ? string.Empty : "s")}",
            };

            // numLevels is recomputed from the used flags whenever the original's chooser closes
            // (SelectLevel.cpp), so a disagreement means the design was last written by something
            // else -- this port included.
            if (Catalog.DeclaredLevelCount != Levels.Count)
            {
                parts.Add($"table declares {Catalog.DeclaredLevelCount}");
            }

            if (Orphans.Count > 0)
            {
                parts.Add($"{Orphans.Count} level{(Orphans.Count == 1 ? string.Empty : "s")} "
                          + "in the table with no file");
            }

            int mismatched = Levels.Count(e => !e.AgreesWithFileName);
            if (mismatched > 0)
            {
                parts.Add($"{mismatched} file{(mismatched == 1 ? string.Empty : "s")} "
                          + "record a different level number than the name");
            }

            int unreadable = Levels.Count(e => !e.IsReadable);
            if (unreadable > 0)
            {
                parts.Add($"{unreadable} could not be read whole");
            }

            if (SelectedLevel is { } entry)
            {
                parts.Insert(0, $"Level {entry.Number} (position {entry.Position})");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Selects a level by its number — the one-based number in its file name.</summary>
    [RelayCommand]
    public void SelectByNumber(int number)
    {
        if (Catalog.ByNumber(number) is { } entry)
        {
            SelectedLevel = entry;
        }
    }
}
