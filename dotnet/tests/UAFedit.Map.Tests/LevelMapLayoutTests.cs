using UAF.Common;
using UAF.Serialization;
using UAFcore;
using UAFedit.Map;

namespace UAFedit.Map.Tests;

/// <summary>
/// The viewport: scrolling, zoom, tiling and hit testing, on a hand-built level.
/// </summary>
/// <remarks>
/// Deliberately not corpus-backed. These are properties of the layout arithmetic and a synthetic
/// 4×3 level makes the torus wrapping legible in a way a 60×60 dungeon does not.
/// </remarks>
public class LevelMapLayoutTests
{
    /// <summary>A cell with one wall index and one blockage on every side.</summary>
    private static AreaMapCell Cell(byte wall = 0, byte blockage = 0, byte zone = 0,
                                    bool hasEvent = false) =>
        new(Background: 0, ShowDistantBackground: false, DistantBackgroundInBands: false,
            NorthBg: 1, EastBg: 2, SouthBg: 3, WestBg: 4,
            Zone: zone, EventExists: hasEvent,
            Walls: [wall, wall, wall, wall],
            Blockage: [blockage, blockage, blockage, blockage]);

    /// <summary>A minimal level: a grid, and empty everything else.</summary>
    private static LevelFile Level(byte width, byte height, Func<int, int, AreaMapCell> cell)
    {
        var cells = new List<AreaMapCell>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells.Add(cell(x, y));
            }
        }

        return new LevelFile(
            new DesignVersion(5.29), width, height, cells, Level: 1,
            EventCount: 0, Events: [], Entries: [],
            Zones: new ZoneData([], string.Empty), Attributes: [], StepEvents: [],
            WallSets: [], BackgroundSets: [], BlockageKeys: []);
    }

    private static LevelMapLayout Layout(int width = 4, int height = 3) =>
        new(width, height, MapCellGeometry.Default);

    /// <summary>A cell's screen size is the geometry scaled, and nothing else.</summary>
    [Fact]
    public void Zoom_scales_the_cell_and_the_content()
    {
        var layout = Layout() with { Zoom = 2 };

        Assert.Equal(32, layout.CellWidth);
        Assert.Equal(32, layout.CellHeight);
        Assert.Equal(4 * 32, layout.ContentWidth);
        Assert.Equal(3 * 32, layout.ContentHeight);
    }

    /// <summary>Zoom is clamped rather than rejected, so a binding cannot put the view in a bad state.</summary>
    [Fact]
    public void Zoom_is_clamped_to_the_views_range()
    {
        Assert.Equal(LevelMapLayout.MaxZoom, (Layout() with { Zoom = 1000 }).Zoom);
        Assert.Equal(LevelMapLayout.MinZoom, (Layout() with { Zoom = 0 }).Zoom);
        Assert.Equal(1.0, (Layout() with { Zoom = double.NaN }).Zoom);
    }

    /// <summary>The tiled map has no edge to scroll off; the untiled one stops at its extent.</summary>
    [Fact]
    public void Tiling_decides_whether_scroll_is_clamped()
    {
        var tiled = Layout() with { Tile = true };
        Assert.Equal((-7.0, 99.0), tiled.ClampScroll(-7, 99, 160, 160));

        var bounded = Layout() with { Tile = false };
        var (x, y) = bounded.ClampScroll(-7, 99, 32, 32);

        // 4 cells wide, 2 visible, so the furthest left column is 2.
        Assert.Equal(0, x);
        Assert.Equal(1, y);
    }

    /// <summary>A level smaller than the viewport does not scroll at all.</summary>
    [Fact]
    public void A_level_smaller_than_the_viewport_does_not_scroll()
    {
        var bounded = Layout() with { Tile = false };
        Assert.Equal((0.0, 0.0), bounded.ClampScroll(3, 3, 1000, 1000));
    }

    /// <summary>The visible set wraps onto the torus, so the same cell appears more than once.</summary>
    [Fact]
    public void Tiling_repeats_the_level_across_the_viewport()
    {
        var layout = Layout() with { Zoom = 1, Tile = true };

        // 4 cells wide at 16 pixels; a 160-pixel viewport shows ten columns, so column 4 is
        // column 0 again.
        var visible = layout.Visible(160, 16).ToList();
        var row = visible.Where(v => v.Row == 0).ToList();

        Assert.Contains(row, v => v.Column == 0 && v.X == 0);
        Assert.Contains(row, v => v.Column == 4 && v.X == 0);
        Assert.Contains(row, v => v.Column == 5 && v.X == 1);
    }

    /// <summary>Without tiling the cells outside the level are simply absent.</summary>
    [Fact]
    public void Without_tiling_the_level_does_not_repeat()
    {
        var layout = Layout() with { Zoom = 1, Tile = false };

        Assert.All(layout.Visible(160, 160),
                   v => Assert.True(v.Column >= 0 && v.Column < 4 && v.Row >= 0 && v.Row < 3));
    }

    /// <summary>Scrolling by a fraction of a cell still shows the partly-visible one.</summary>
    [Fact]
    public void A_fractional_scroll_keeps_the_clipped_cell()
    {
        var layout = Layout(width: 40, height: 40) with { Zoom = 1, ScrollX = 0.5, Tile = false };
        var row = layout.Visible(32, 16).Where(v => v.Row == 0).ToList();

        Assert.Contains(row, v => v.Column == 0);
        Assert.Equal(-8, row.Single(v => v.Column == 0).Left);
        Assert.Contains(row, v => v.Column == 2);
    }

    /// <summary>Hit testing inverts the layout: a cell's own pixels come back as that cell.</summary>
    [Fact]
    public void A_point_inside_a_cell_hit_tests_to_it()
    {
        var layout = Layout(width: 40, height: 40) with { Zoom = 2, ScrollX = 3, ScrollY = 7 };
        var (left, top) = layout.Origin(10, 12);

        var hit = layout.HitTest(left + 1, top + 1);
        Assert.Equal(10, hit.X);
        Assert.Equal(12, hit.Y);
    }

    /// <summary>
    /// The offset a hit reports is in the geometry's pixel space, not the screen's.
    /// </summary>
    /// <remarks>
    /// Which is what makes the side test zoom-independent — a click a third of the way across the
    /// cell picks the same side at every zoom.
    /// </remarks>
    [Fact]
    public void The_hit_offset_is_unzoomed()
    {
        foreach (double zoom in new[] { 0.5, 1.0, 2.0, 4.0 })
        {
            var layout = Layout(width: 40, height: 40) with { Zoom = zoom };
            var hit = layout.HitTest(layout.CellWidth * 0.5, layout.CellHeight * 0.1);

            Assert.Equal(8, hit.OffsetX);
            Assert.Equal(Facing.North, hit.Side);
        }
    }

    /// <summary>A hit past the level's edge wraps, because the level does.</summary>
    [Fact]
    public void A_hit_past_the_edge_wraps_onto_the_torus()
    {
        var layout = Layout(width: 4, height: 3) with { Zoom = 1 };

        var hit = layout.HitTest(4 * 16 + 8, 3 * 16 + 8);
        Assert.Equal(0, hit.X);
        Assert.Equal(0, hit.Y);

        var negative = layout.HitTest(-8, -8);
        Assert.Equal(3, negative.X);
        Assert.Equal(2, negative.Y);
    }

    /// <summary>Scrolling to show a visible cell moves nothing.</summary>
    [Fact]
    public void Scrolling_to_a_visible_cell_is_a_no_op()
    {
        var layout = Layout(width: 40, height: 40) with { Zoom = 1, ScrollX = 5, ScrollY = 5 };
        var moved = layout.ScrollToShow(7, 7, 160, 160);

        Assert.Equal(5, moved.ScrollX);
        Assert.Equal(5, moved.ScrollY);
    }

    /// <summary>Scrolling to a cell off each edge brings it just inside.</summary>
    [Fact]
    public void Scrolling_to_an_offscreen_cell_brings_it_just_into_view()
    {
        var layout = Layout(width: 40, height: 40) with { Zoom = 1, ScrollX = 5, ScrollY = 5 };

        // Ten cells fit in 160 pixels, so showing column 20 puts the left edge at 11.
        Assert.Equal(11, layout.ScrollToShow(20, 5, 160, 160).ScrollX);

        // And a cell behind the left edge scrolls back to it exactly.
        Assert.Equal(2, layout.ScrollToShow(2, 5, 160, 160).ScrollX);
    }

    /// <summary>The model wraps reads, so a draw loop never has to bounds-check.</summary>
    [Fact]
    public void The_model_wraps_reads_onto_the_torus()
    {
        var model = new LevelMapModel(Level(4, 3, (x, y) => Cell(wall: (byte)(x + 1))));

        Assert.Equal(1, model.At(0, 0).North.WallIndex);
        Assert.Equal(1, model.At(4, 0).North.WallIndex);
        Assert.Equal(4, model.At(-1, 0).North.WallIndex);
        Assert.Equal((3, 2), model.Wrap(-1, -1));
    }

    /// <summary>
    /// Backgrounds are stored in compass order, unlike walls, and the model must not permute them.
    /// </summary>
    /// <remarks>
    /// The cell above carries 1/2/3/4 for north/east/south/west. If the model applied the wall
    /// permutation here, east and south would swap and the map would draw the wrong backdrop
    /// colours — a mistake with no other symptom, since both are just colours.
    /// </remarks>
    [Fact]
    public void Backgrounds_are_not_permuted()
    {
        var model = new LevelMapModel(Level(2, 2, (_, _) => Cell()));
        var cell = model.At(0, 0);

        Assert.Equal(1, cell.North.Background);
        Assert.Equal(2, cell.East.Background);
        Assert.Equal(3, cell.South.Background);
        Assert.Equal(4, cell.West.Background);
    }

    /// <summary>A level whose grid is shorter than its extent is refused rather than faulting later.</summary>
    [Fact]
    public void A_short_grid_is_refused_at_construction()
    {
        var truncated = new LevelFile(
            new DesignVersion(5.29), 4, 3, [Cell()], Level: 1,
            EventCount: 0, Events: [], Entries: [],
            Zones: new ZoneData([], string.Empty), Attributes: [], StepEvents: [],
            WallSets: [], BackgroundSets: [], BlockageKeys: []);

        Assert.Throws<ArgumentException>(() => new LevelMapModel(truncated));
    }
}
