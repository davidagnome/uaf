using UAF.Common;
using UAF.Serialization;
using UAFcore;
using UAFedit.Map;

namespace UAFedit.Map.Tests;

/// <summary>
/// What each mode draws, on cells built to say one thing each.
/// </summary>
public class LevelMapPainterTests
{
    private static WallSetSlot Slot(string wall = "wall.pcx", string door = "") =>
        new(WallFile: wall, DoorFile: door, OverlayFile: string.Empty, SoundFile: string.Empty,
            AreaViewFile: string.Empty, Used: 1, DoorFirst: 0, DrawAreaView: 0,
            UnlockSpellId: string.Empty, BlendOverlay: 0, BlendAmount: 0);

    /// <summary>Wall sets 0..2: the unused sentinel, a plain wall, and a doored one.</summary>
    private static IReadOnlyList<WallSetSlot> WallSets =>
        [Slot(wall: string.Empty), Slot(), Slot(door: "door.pcx")];

    private static LevelMapCell Cell(
        int north = 0, int east = 0, int south = 0, int west = 0,
        BlockageType blockage = BlockageType.Open, byte zone = 0, bool hasEvent = false,
        bool startLocation = false, int entryPoint = -1) =>
        new(0, 0,
            new LevelMapSide(north, blockage, 1),
            new LevelMapSide(east, blockage, 2),
            new LevelMapSide(south, blockage, 3),
            new LevelMapSide(west, blockage, 4),
            zone, hasEvent, entryPoint >= 0, entryPoint, startLocation);

    private static LevelMapPainter Painter(MapDisplayMode mode = MapDisplayMode.Walls) =>
        new(MapCellGeometry.Default, MapPalette.Default) { Mode = mode };

    /// <summary>An empty cell is a fill and four corner dots, and nothing else.</summary>
    [Fact]
    public void An_empty_cell_is_a_fill_and_four_dots()
    {
        var marks = Painter().Marks(Cell(), WallSets).ToList();

        Assert.Equal(5, marks.Count);
        Assert.Equal(MapMarkKind.Fill, marks[0].Kind);
        Assert.Equal(4, marks.Count(m => m.Kind == MapMarkKind.Corner));
    }

    /// <summary>The fill comes first, so the corners and walls sit on top of it.</summary>
    [Fact]
    public void The_fill_is_drawn_before_everything_it_would_cover()
    {
        var marks = Painter().Marks(Cell(north: 1, east: 1, south: 1, west: 1), WallSets).ToList();

        Assert.Equal(MapMarkKind.Fill, marks[0].Kind);
        Assert.True(marks.FindIndex(m => m.Kind == MapMarkKind.Corner)
                    < marks.FindIndex(m => m.Kind == MapMarkKind.Wall));
    }

    /// <summary>Wall slot 0 means no wall, not "wall set 0".</summary>
    [Fact]
    public void Wall_slot_zero_draws_nothing()
    {
        Assert.Empty(Painter().WallMarks(Cell(north: 0), Facing.North, WallSets));
        Assert.NotEmpty(Painter().WallMarks(Cell(north: 1), Facing.North, WallSets));
    }

    /// <summary>A plain wall is three dashes; a doored one leaves the middle out.</summary>
    [Fact]
    public void A_door_is_drawn_as_a_gap_in_the_wall()
    {
        var solid = Painter().WallMarks(Cell(north: 1), Facing.North, WallSets).ToList();
        var doored = Painter().WallMarks(Cell(north: 2), Facing.North, WallSets).ToList();

        Assert.Equal(3, solid.Count);
        Assert.Equal(2, doored.Count);

        // The one missing is the middle, not an end.
        var middle = MapCellGeometry.Default.SegmentRect(Facing.North, MapSegment.Middle);
        Assert.Contains(solid, m => m.Rect == middle);
        Assert.DoesNotContain(doored, m => m.Rect == middle);
    }

    /// <summary>
    /// A wall index past the end of the design's table still draws — solid, since it names no door.
    /// </summary>
    /// <remarks>
    /// A design really can reference a wall set it does not carry; the engine warns and draws
    /// nothing, and the editor's 2-D map draws the colour anyway. Keeping the mark is the editor's
    /// answer: the author has to be able to see the wall in order to fix it.
    /// </remarks>
    [Fact]
    public void A_wall_index_past_the_table_draws_solid()
    {
        var marks = Painter().WallMarks(Cell(north: 99), Facing.North, WallSets).ToList();
        Assert.Equal(3, marks.Count);
    }

    /// <summary>Each side's wall is drawn against its own edge.</summary>
    [Fact]
    public void Each_side_draws_against_its_own_edge()
    {
        var painter = Painter();
        var cell = Cell(north: 1, east: 1, south: 1, west: 1);
        var square = MapCellGeometry.Default.Square;

        Assert.All(painter.WallMarks(cell, Facing.North, WallSets),
                   m => Assert.Equal(0, m.Rect.Top));
        Assert.All(painter.WallMarks(cell, Facing.South, WallSets),
                   m => Assert.Equal(square.Bottom, m.Rect.Bottom));
        Assert.All(painter.WallMarks(cell, Facing.East, WallSets),
                   m => Assert.Equal(square.Right, m.Rect.Right));
        Assert.All(painter.WallMarks(cell, Facing.West, WallSets),
                   m => Assert.Equal(0, m.Rect.Left));
    }

    /// <summary>An open side draws no blockage mark; every other blockage does.</summary>
    [Theory]
    [InlineData(BlockageType.Open, 0)]
    [InlineData(BlockageType.OpenSecret, 4)]
    [InlineData(BlockageType.Blocked, 4)]
    [InlineData(BlockageType.LockedKey8, 4)]
    public void Only_open_sides_draw_no_blockage(BlockageType blockage, int expected)
    {
        var marks = Painter().CenterMarks(Cell(blockage: blockage)).ToList();
        Assert.Equal(expected, marks.Count(m => m.Kind == MapMarkKind.Blockage));
    }

    /// <summary>
    /// The blockage palette's two special cases really are special.
    /// </summary>
    /// <remarks>
    /// Blocked and FalseDoor jump to the two brightest slots; everything above them shifts down two.
    /// A straight lookup would give Blocked slot 2's green and put the eight keyed locks on top of
    /// the ordinary wall colours.
    /// </remarks>
    [Fact]
    public void The_blockage_palette_is_permuted()
    {
        var palette = MapPalette.Default;

        Assert.Equal(MapPalette.DefaultColors[14], palette.Obstruction(BlockageType.Blocked));
        Assert.Equal(MapPalette.DefaultColors[15], palette.Obstruction(BlockageType.FalseDoor));
        Assert.Equal(MapPalette.DefaultColors[2], palette.Obstruction(BlockageType.Locked));
        Assert.Equal(MapPalette.DefaultColors[13], palette.Obstruction(BlockageType.LockedKey8));
        Assert.Equal(MapPalette.DefaultColors[1], palette.Obstruction(BlockageType.OpenSecret));
    }

    /// <summary>Event mode marks the squares carrying events and no others.</summary>
    [Fact]
    public void Event_mode_marks_only_squares_with_events()
    {
        var painter = Painter(MapDisplayMode.Events);

        Assert.Empty(painter.CenterMarks(Cell(hasEvent: false)));
        Assert.Single(painter.CenterMarks(Cell(hasEvent: true)));
    }

    /// <summary>Zone mode marks every square, because zone 0 is a zone.</summary>
    [Fact]
    public void Zone_mode_marks_every_square()
    {
        var painter = Painter(MapDisplayMode.Zones);

        Assert.Single(painter.CenterMarks(Cell(zone: 0)));
        Assert.Single(painter.CenterMarks(Cell(zone: 5)));
        Assert.Equal(MapPalette.DefaultColors[5],
                     painter.CenterMarks(Cell(zone: 5)).Single().Color);
    }

    /// <summary>Zone and entry-point modes swap the cell's backdrop for the dark red.</summary>
    [Fact]
    public void Zone_and_entry_point_modes_use_a_different_backdrop()
    {
        var modal = MapPalette.DefaultColors[LevelMapPainter.ModalCellColor];
        var empty = MapPalette.DefaultColors[LevelMapPainter.EmptyCellColor];

        Assert.Equal(modal, Painter(MapDisplayMode.Zones).Marks(Cell(), WallSets).First().Color);
        Assert.Equal(modal,
                     Painter(MapDisplayMode.EntryPoints).Marks(Cell(), WallSets).First().Color);
        Assert.Equal(empty, Painter(MapDisplayMode.Walls).Marks(Cell(), WallSets).First().Color);
        Assert.Equal(empty, Painter(MapDisplayMode.Events).Marks(Cell(), WallSets).First().Color);
    }

    /// <summary>Background mode draws the four per-side slots, skipping slot 0.</summary>
    [Fact]
    public void Background_mode_draws_the_four_side_slots()
    {
        var painter = Painter(MapDisplayMode.Backgrounds);
        var marks = painter.CenterMarks(Cell()).ToList();

        Assert.Equal(4, marks.Count);
        Assert.All(marks, m => Assert.Equal(MapMarkKind.Background, m.Kind));

        // They land in the obstruction slots, which the mode has to itself.
        Assert.Equal(MapCellGeometry.Default.SegmentRect(Facing.North, MapSegment.Obstruction),
                     marks[0].Rect);
    }

    /// <summary>Start-location and entry-point modes mark only their own squares.</summary>
    [Fact]
    public void The_marker_modes_mark_only_their_own_squares()
    {
        Assert.Empty(Painter(MapDisplayMode.StartLocation).CenterMarks(Cell()));
        Assert.Single(Painter(MapDisplayMode.StartLocation)
                      .CenterMarks(Cell(startLocation: true)));

        Assert.Empty(Painter(MapDisplayMode.EntryPoints).CenterMarks(Cell()));
        Assert.Equal(MapPalette.DefaultColors[3],
                     Painter(MapDisplayMode.EntryPoints)
                         .CenterMarks(Cell(entryPoint: 3)).Single().Color);
    }

    /// <summary>Walls are drawn in every mode, not only the wall mode.</summary>
    [Fact]
    public void Walls_are_drawn_in_every_mode()
    {
        foreach (var mode in Enum.GetValues<MapDisplayMode>())
        {
            var marks = Painter(mode).Marks(Cell(north: 1), WallSets);
            Assert.Contains(marks, m => m.Kind == MapMarkKind.Wall);
        }
    }

    /// <summary>
    /// The eight entry points default to (0,0), which puts a marker on nearly every level's corner.
    /// </summary>
    /// <remarks>
    /// Faithful and worth pinning: an author seeing a dot at 0,0 on a level they placed no entry
    /// point on is looking at the fixed table's padding, and a "fix" that hid it would also hide a
    /// real entry point placed there.
    /// </remarks>
    [Fact]
    public void The_unused_entry_point_slots_land_on_the_origin()
    {
        var stats = new LevelStats(
            Height: 2, Width: 2, Used: 1, Overland: 0, AreaViewStyle: 0, Name: "test",
            EntryPoints: [new EntryPoint(0, 0), new EntryPoint(1, 1)],
            StepSound: string.Empty, BumpSound: string.Empty, Sounds: null,
            Overrides: null, Contents: null, Attributes: []);

        var cells = Enumerable.Range(0, 4).Select(_ => new AreaMapCell(
            0, false, false, 0, 0, 0, 0, 0, false, [0, 0, 0, 0], [0, 0, 0, 0])).ToList();

        var level = new LevelFile(
            new DesignVersion(5.29), 2, 2, cells, Level: 1, EventCount: 0, Events: [], Entries: [],
            Zones: new ZoneData([], string.Empty), Attributes: [], StepEvents: [],
            WallSets: [], BackgroundSets: [], BlockageKeys: []);

        var model = new LevelMapModel(level, stats);

        Assert.Equal(0, model.EntryPointAt(0, 0));
        Assert.Equal(1, model.EntryPointAt(1, 1));
        Assert.Equal(-1, model.EntryPointAt(1, 0));
    }
}
