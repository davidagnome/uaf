using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAF.Serialization;
using UAFcore;
using UAFedit.Map;

namespace UAFedit.Levels;

/// <summary>
/// One level, open: the map, the tables that go with it, and the state the map view binds to.
/// </summary>
/// <remarks>
/// <para>
/// The Avalonia stand-in for <c>CUAFWinEdView</c> (<c>UAFWinEdView.cpp</c>) minus its tools. The
/// drawing is <see cref="LevelMapView"/>'s and the data is <see cref="LevelMapModel"/>'s; what this
/// adds is the wiring the original keeps in file-scope globals — <c>currLevel</c>, <c>currX</c>,
/// <c>currY</c>, <c>currFacing</c>, <c>currZone</c>, <c>currWallSlot</c>, <c>currBlockage</c>,
/// <c>currEntryPoint</c> — as properties of one level's panel, so two levels can be open without
/// them fighting.
/// </para>
/// <para>
/// <b>Built from a catalog entry rather than an index.</b> The map needs the grid, which is read by
/// position in <see cref="LoadedDesign.LevelFiles"/>, and the entry points, which are read by level
/// number minus one — <see cref="LevelCatalogEntry"/> is what knows both. Passing an index and using
/// it for both is the bug this whole namespace is arranged to avoid.
/// </para>
/// </remarks>
public sealed partial class LevelPanelViewModel : ObservableObject
{
    /// <summary>Fired when the panel wants the map scrolled to a square.</summary>
    /// <remarks>
    /// Scrolling needs the viewport's size, which only the control has
    /// (<see cref="LevelMapView.ScrollToShow"/>). An event keeps the view model free of it; the
    /// alternative — a viewport size pushed down into the view model — puts layout in the wrong
    /// place and is wrong for one frame after every resize.
    /// </remarks>
    public event EventHandler<MapPoint>? ScrollRequested;

    /// <summary>Opens a level for editing. <paramref name="entry"/> must carry a readable file.</summary>
    public LevelPanelViewModel(LoadedDesign design, LevelCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        Palette = MapPalette.FromConfig(design.Config);
        Geometry = MapCellGeometry.FromConfig(design.Config);

        var level = design.Level(entry.Position);

        if (level is not null)
        {
            // Taken from the file just read rather than from the entry, which carries it only when
            // the catalog was built with readFiles on. The panel always opens the file, so it
            // always knows -- and a level list built cheaply should still be able to warn here.
            StoredNumber = level.Level + 1;

            // The design's start square belongs on the map only when it is on THIS level, and
            // startLevel is a stats index -- the same zero-based number the entry carries.
            var start = design.Globals.StartLevel == entry.StatsIndex
                ? ((int)design.Globals.StartX, (int)design.Globals.StartY)
                : ((int, int)?)null;

            Model = new LevelMapModel(level, entry.Stats, start)
            {
                UseWallIndex = HasGlobalFlag(design, "UseWallIndex"),
                UseDoorAndOverlayIndex = HasGlobalFlag(design, "UseDoorAndOverlayIndex"),
            };

            WallSlots = new WallSlotsViewModel(Model, Palette);
            Zones = new ZonesViewModel(Model, Palette);
        }

        EntryPoints = new EntryPointsViewModel(entry.Stats);
    }

    /// <summary>The map's display modes, for a picker to bind to.</summary>
    public static IReadOnlyList<MapDisplayMode> DisplayModes { get; } =
        Enum.GetValues<MapDisplayMode>();

    /// <summary>Which level this is, with all three of its numbers.</summary>
    public LevelCatalogEntry Entry { get; }

    /// <summary>
    /// The map, or null when the level file could not be read whole.
    /// </summary>
    /// <remarks>
    /// <see cref="LoadedDesign.Level"/> gives up on an event type this port has no reader for, and
    /// the wall and zone tables sit after the event list — so there is no partial map to draw. The
    /// entry points still work, because they come from <c>game.dat</c>.
    /// </remarks>
    public LevelMapModel? Model { get; }

    public MapPalette Palette { get; }

    public MapCellGeometry Geometry { get; }

    /// <summary>The level's eight entry points. Present even when the map is not.</summary>
    public EntryPointsViewModel EntryPoints { get; }

    /// <summary>The level's wall-set table, or null when the file could not be read.</summary>
    public WallSlotsViewModel? WallSlots { get; }

    /// <summary>The level's zone table, or null when the file could not be read.</summary>
    public ZonesViewModel? Zones { get; }

    /// <summary>Whether there is a map to draw.</summary>
    public bool HasMap => Model is not null;

    /// <summary>
    /// The heading: the level's number, its name, and where its file sits.
    /// </summary>
    /// <remarks>
    /// The position is shown alongside the number on purpose. They are equal on a design numbered
    /// without holes and differ on one with them, and a user who has to reason about which is which
    /// — because a teleport event names one and a stack trace names the other — cannot do it from a
    /// UI that shows only one.
    /// </remarks>
    public string Title =>
        $"Level {Entry.Number} — {(Entry.Name.Length > 0 ? Entry.Name : "(unnamed)")}";

    /// <inheritdoc cref="LevelCatalogEntry.StoredNumber"/>
    public int? StoredNumber { get; }

    /// <inheritdoc cref="LevelCatalogEntry.AgreesWithFileName"/>
    public bool AgreesWithFileName => StoredNumber is null || StoredNumber == Entry.Number;

    /// <inheritdoc cref="Title"/>
    public string Subtitle =>
        $"{Entry.FileName} · file {Entry.Position + 1} of the design · stats[{Entry.StatsIndex}]"
        + (AgreesWithFileName
            ? string.Empty
            : $" · file records itself as level {StoredNumber}");

    /// <summary>What the map's cell centres show.</summary>
    [ObservableProperty]
    private MapDisplayMode mode = MapDisplayMode.Walls;

    /// <summary>Screen pixels per geometry pixel. Two, as the map view defaults.</summary>
    [ObservableProperty]
    private double zoom = 2.0;

    [ObservableProperty]
    private double scrollX;

    [ObservableProperty]
    private double scrollY;

    /// <summary>Whether the level repeats past its edges. On, because a level really is a torus.</summary>
    [ObservableProperty]
    private bool tile = true;

    /// <summary>Whether the 5.x per-cell override tables are applied.</summary>
    /// <remarks>
    /// Off by default, matching the editor rather than the engine — see
    /// <see cref="LevelMapModel"/>. The switch has no effect on a level whose stats carry no
    /// override table, which is every level of every design read so far.
    /// </remarks>
    [ObservableProperty]
    private bool showOverrides;

    /// <summary><c>currX</c> / <c>currY</c>: the square the tools would act on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionDescription))]
    private MapPoint? selectedCell;

    /// <summary><c>currFacing</c>: the side of it the wall tools would act on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionDescription))]
    private Facing selectedSide = Facing.East;

    /// <summary>Whether the level's stats carry a per-cell override table at all.</summary>
    public bool HasOverrides => Entry.Stats?.Overrides is not null;

    /// <summary>
    /// Everything the selected square holds, as one line.
    /// </summary>
    /// <remarks>
    /// The original's equivalent is spread across the status bar, the wall dialog's radio group and
    /// the map's own colours. One line is more useful in a port whose map has no art behind it.
    /// </remarks>
    public string SelectionDescription
    {
        get
        {
            if (Model is not { } model || SelectedCell is not { } point)
            {
                return string.Empty;
            }

            var cell = model.At(point.X, point.Y);
            var side = cell.Side(SelectedSide);

            var parts = new List<string>
            {
                $"({cell.X}, {cell.Y}) {SelectedSide}",
                side.WallIndex > MapPalette.NoWall
                    ? $"wall {side.WallIndex}"
                      + (cell.HasDoorGap(SelectedSide, model.WallSets) ? " (door)" : string.Empty)
                    : "no wall",
                $"blockage {side.Blockage}",
                $"backdrop {side.Background}",
                $"zone {cell.Zone}",
            };

            if (cell.HasEvent)
            {
                parts.Add("event");
            }

            if (cell.IsEntryPoint)
            {
                parts.Add($"entry point {cell.EntryPointIndex + 1}");
            }

            if (cell.IsStartLocation)
            {
                parts.Add("party start");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Scrolls the map to the entry point the entry-point editor has selected.</summary>
    [RelayCommand]
    public void ShowSelectedEntryPoint()
    {
        var point = EntryPoints.Selected;
        var target = new MapPoint(point.X, point.Y);

        SelectedCell = target;
        Mode = MapDisplayMode.EntryPoints;
        ScrollRequested?.Invoke(this, target);
    }

    /// <summary>Scrolls the map to the design's start square, when it is on this level.</summary>
    [RelayCommand]
    public void ShowStartLocation()
    {
        if (Model?.StartLocation is not { } start)
        {
            return;
        }

        var target = new MapPoint(start.X, start.Y);
        SelectedCell = target;
        Mode = MapDisplayMode.StartLocation;
        ScrollRequested?.Invoke(this, target);
    }

    /// <summary>Brings the selected square back into view without moving it.</summary>
    [RelayCommand]
    public void ShowSelectedCell()
    {
        if (SelectedCell is { } point)
        {
            ScrollRequested?.Invoke(this, point);
        }
    }

    /// <summary>Whether the design sets a global ASL flag.</summary>
    /// <remarks>
    /// <b>Presence, not value</b> — <c>useWallIndex = (global_asl.Find("UseWallIndex") == NULL) ? 0
    /// : 1</c> (<c>Level.cpp:3098</c>). A design that set the key to "0" would still have the flag
    /// on.
    /// </remarks>
    private static bool HasGlobalFlag(LoadedDesign design, string key) =>
        design.Globals.Attributes.Any(
            a => string.Equals(a.Key, key, StringComparison.Ordinal));

    private LevelMapModel? overridden;

    partial void OnShowOverridesChanged(bool value) => OnPropertyChanged(nameof(EffectiveModel));

    /// <summary>
    /// The model the map draws, honouring <see cref="ShowOverrides"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="LevelMapModel.ShowOverrides"/> is <c>init</c>-only, so the flag is applied by
    /// rebuilding. That is cheap — the model holds a reference to the already-read level and
    /// computes per cell — and it keeps the map layer immutable, which is what makes it testable.
    /// </para>
    /// <para>
    /// The rebuilt model is kept rather than made fresh on each read. The map rebinds whenever this
    /// changes identity, so an <c>init</c>-only property behind a computed getter is exactly the
    /// shape that redraws the whole level on every unrelated notification.
    /// </para>
    /// </remarks>
    public LevelMapModel? EffectiveModel
    {
        get
        {
            if (Model is null || !ShowOverrides)
            {
                return Model;
            }

            return overridden ??= new LevelMapModel(Model.Level, Entry.Stats, Model.StartLocation)
            {
                ShowOverrides = true,
                UseWallIndex = Model.UseWallIndex,
                UseDoorAndOverlayIndex = Model.UseDoorAndOverlayIndex,
            };
        }
    }
}
