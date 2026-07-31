using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers <see cref="WallResolver"/> against a hand-built map, where every wall index is known.
/// </summary>
/// <remarks>
/// Synthetic on purpose: a hand-built map is the only way to know what every index should resolve
/// to, so these pin the rules. <c>GameTests</c> checks the same resolver against a real design's
/// levels, where the assertion is that no index is out of range and no wall names art the design
/// does not ship.
/// </remarks>
public class WallResolverTests
{
    private static AreaMapCell Cell(params byte[] walls) =>
        new(0, false, false, 0, 0, 0, 0, 0, false, walls, [0, 0, 0, 0]);

    private static WallSetSlot Set(string name) =>
        new($"{name}_wall.png", $"{name}_door.png", $"{name}_overlay.png", string.Empty,
            string.Empty, 1, 0, 0, string.Empty, 0, 0);

    /// <summary>A 4×4 map where cell (1,1) has a different wall on each face.</summary>
    private static Map BuildMap()
    {
        var cells = new AreaMapCell[16];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = Cell(0, 0, 0, 0);
        }

        // North=1, East=2, South=0 (none), West=3.
        cells[(1 * 4) + 1] = Cell(1, 2, 0, 3);
        return new Map(4, 4, cells);
    }

    /// <summary>
    /// Wall sets indexed directly, with a blank slot 0 — the shape a real level actually has.
    /// </summary>
    /// <remarks>
    /// The fixture used to omit slot 0 and rely on an index-minus-one in the resolver. That
    /// matched a real design only by accident, because its first few entries name the same file.
    /// </remarks>
    private static WallResolver Resolver(Map map) =>
        new(map, [Set("unused"), Set("first"), Set("second"), Set("third")]);

    [Fact]
    public void A_slot_resolves_to_the_wall_index_on_the_cells_facing()
    {
        var map = BuildMap();
        var resolver = Resolver(map);

        // Party at (1,2) facing north, so slot 9 -- one step forward -- is cell (1,1).
        var view = ViewMap.For(1, 2, Facing.North, 4, 4);
        Assert.Equal((1, 1), view[9]);

        Assert.Equal(1, resolver.IndexAt(view, 9, Facing.North));
        Assert.Equal(2, resolver.IndexAt(view, 9, Facing.East));
        Assert.Equal(3, resolver.IndexAt(view, 9, Facing.West));
    }

    [Fact]
    public void Index_zero_means_no_wall_rather_than_the_first_wall_set()
    {
        var map = BuildMap();
        var resolver = Resolver(map);
        var view = ViewMap.For(1, 2, Facing.North, 4, 4);

        // The south face of (1,1) is 0. Treating that as a valid entry would paper the level in
        // whatever art sits in the first slot.
        Assert.Equal(WallResolver.NoWall, resolver.IndexAt(view, 9, Facing.South));
        Assert.False(resolver.HasWall(view, 9, Facing.South));
        Assert.Null(resolver.ArtFor(view, 9, Facing.South, WallLayer.Wall));
    }

    [Fact]
    public void Wall_sets_are_indexed_from_one_because_zero_is_the_sentinel()
    {
        var map = BuildMap();
        var resolver = Resolver(map);
        var view = ViewMap.For(1, 2, Facing.North, 4, 4);

        // Index 1 addresses entry 1, not entry 0. The table is the full MAX_WALLSETS array with
        // slot 0 present and unused, so no adjustment is applied -- an off-by-one here shifts
        // every wall in the level to its neighbour's texture, which reads as bad art rather than
        // a bad index.
        Assert.Equal("first_wall.png", resolver.ArtFor(view, 9, Facing.North, WallLayer.Wall));
        Assert.Equal("second_wall.png", resolver.ArtFor(view, 9, Facing.East, WallLayer.Wall));
        Assert.Equal("third_wall.png", resolver.ArtFor(view, 9, Facing.West, WallLayer.Wall));
    }

    [Theory]
    [InlineData(WallLayer.Wall, "first_wall.png")]
    [InlineData(WallLayer.Door, "first_door.png")]
    [InlineData(WallLayer.Overlay, "first_overlay.png")]
    public void Each_layer_pulls_a_different_picture_from_the_same_set(WallLayer layer,
                                                                        string expected)
    {
        var map = BuildMap();
        var resolver = Resolver(map);
        var view = ViewMap.For(1, 2, Facing.North, 4, 4);

        // getWallSurface, getDoorSurface and getOverlaySurface differ only in this.
        Assert.Equal(expected, resolver.ArtFor(view, 9, Facing.North, layer));
    }

    [Fact]
    public void An_index_beyond_the_engine_limit_degrades_to_no_wall_and_is_reported()
    {
        var cells = new AreaMapCell[4];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = Cell(200, 0, 0, 0);      // above MAX_WALLSETS
        }

        var resolver = new WallResolver(new Map(2, 2, cells), [Set("only")]);
        var view = ViewMap.For(0, 0, Facing.North, 2, 2);

        // The original logs "Bogus wall slot num" and resets to 0, so the frame still draws.
        Assert.Equal(WallResolver.NoWall, resolver.IndexAt(view, 12, Facing.North));
        Assert.NotEmpty(resolver.Warnings);
        Assert.Contains("200", resolver.Warnings[0]);
    }

    [Fact]
    public void An_index_the_design_has_no_wall_set_for_is_reported_separately()
    {
        var cells = new AreaMapCell[4];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = Cell(3, 0, 0, 0);        // in range, but the design declares one set
        }

        var resolver = new WallResolver(new Map(2, 2, cells), [Set("unused")]);
        var view = ViewMap.For(0, 0, Facing.North, 2, 2);

        // In range for the engine but past the end of this design's table -- a different fault
        // from an impossible index, and worth telling apart when diagnosing a design.
        Assert.Equal(3, resolver.IndexAt(view, 12, Facing.North));
        Assert.Null(resolver.ArtFor(view, 12, Facing.North, WallLayer.Wall));
        Assert.Contains("no wall set", resolver.Warnings[0]);
    }

    [Fact]
    public void Only_the_unwrapped_outer_slots_can_fall_outside_the_map()
    {
        var map = BuildMap();
        var resolver = Resolver(map);
        var view = ViewMap.For(0, 0, Facing.North, 4, 4);

        // 0..12 are wrapped onto the torus, so they always name a real cell.
        for (int slot = 0; slot < ViewMap.WrappedSlots; slot++)
        {
            Assert.True(resolver.CellExists(view, slot), $"slot {slot} should be on the map");
        }

        // 13 and 14 are left unwrapped precisely so this can be false -- it is the occlusion
        // tests' "there is no there there".
        Assert.False(resolver.CellExists(view, 13));
    }

    [Fact]
    public void A_slot_outside_the_map_resolves_to_nothing_rather_than_throwing()
    {
        var map = BuildMap();
        var resolver = Resolver(map);
        var view = ViewMap.For(0, 0, Facing.North, 4, 4);

        Assert.Equal(WallResolver.NoWall, resolver.IndexAt(view, 13, Facing.North));
        Assert.Equal(WallResolver.NoWall, resolver.IndexAt(view, 99, Facing.North));
        Assert.False(resolver.CellExists(view, -1));
    }

    [Fact]
    public void The_pass_table_matches_what_the_square_routines_do()
    {
        // Transcribed, not derived -- square 10 uses D while 11 uses C, and squares 7, 8 and 9 all
        // draw their front face from H but pair it with N, O, and F+G respectively.
        var passes = ViewportRenderer.SquarePasses;

        Assert.Equal(10, passes.Count);

        // 13 and 14 are single front passes gated on the layout, not occlusion tests.
        Assert.Single(passes[13]);
        Assert.Single(passes[14]);
        Assert.Contains(13, ViewportRenderer.SevenDistantWallOnly);
        Assert.Contains(14, ViewportRenderer.SevenDistantWallOnly);

        // Square 12 is the party's own cell: the front wall you face plus both near side walls.
        Assert.Equal(3, passes[12].Length);
        Assert.Equal(DrawSlot.E, passes[12][0].Slot);
        Assert.Equal(DrawSlot.A, passes[12][1].Slot);
        Assert.Equal(DrawSlot.B, passes[12][2].Slot);
        Assert.Equal(DrawSlot.D, passes[10].Single().Slot);
        Assert.Equal(DrawSlot.C, passes[11].Single().Slot);

        // Square 9 is the only three-pass square: the cell straight ahead shows its front wall and
        // both side walls.
        Assert.Equal(3, passes[9].Length);
        Assert.Equal(DrawSlot.H, passes[9][0].Slot);
        Assert.Equal(ViewportRenderer.PassDirection.Front, passes[9][0].Direction);
        Assert.Equal(DrawSlot.F, passes[9][1].Slot);
        Assert.Equal(ViewportRenderer.PassDirection.Left, passes[9][1].Direction);
        Assert.Equal(DrawSlot.G, passes[9][2].Slot);
        Assert.Equal(ViewportRenderer.PassDirection.Right, passes[9][2].Direction);

        // 7 and 8 are mirror images of each other, one side each.
        Assert.Equal(ViewportRenderer.PassDirection.Left, passes[7][1].Direction);
        Assert.Equal(ViewportRenderer.PassDirection.Right, passes[8][1].Direction);

        // The eight squares with occlusion tests are deliberately absent.
        foreach (int square in new[] { 0, 1, 2, 3, 4 })
        {
            Assert.False(passes.ContainsKey(square), $"square {square} should need its own routine");
        }
    }

    [Fact]
    public void Squares_13_and_14_draw_nothing_in_the_five_wall_layout()
    {
        var config = UAF.Data.DesignConfig.Parse(BuildFormatConfig());
        var format = WallFormatReader.ReadAll(config)[0];
        Assert.Equal(5, format.DistantWallCount);

        var renderer = new ViewportRenderer(format);
        var map = BuildMap();
        var screen = new UAF.Media.Surface(64, 64);
        screen.Fill(0xFF123456);

        // The gate is a return, not an occlusion test: in the narrower layout these squares do not
        // exist, and their viewport coordinates are not even read.
        renderer.RenderSquare(screen, ViewMap.For(1, 1, Facing.North, 4, 4), Resolver(map),
                              Facing.North, 13, 0, 0, _ => null);

        Assert.All(screen.Pixels, p => Assert.Equal(0xFF123456u, p));
    }

    [Fact]
    public void A_square_needing_its_own_routine_is_refused_rather_than_approximated()
    {
        var config = UAF.Data.DesignConfig.Parse(BuildFormatConfig());
        var renderer = new ViewportRenderer(WallFormatReader.ReadAll(config)[0]);
        var map = BuildMap();

        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderSquare(
            new UAF.Media.Surface(64, 64), ViewMap.For(1, 1, Facing.North, 4, 4), Resolver(map),
            Facing.North, square: 4, 0, 0, _ => null));
    }

    [Fact]
    public void Draw_slots_are_one_based_against_the_format_rectangles()
    {
        var config = UAF.Data.DesignConfig.Parse(BuildFormatConfig());
        var format = WallFormatReader.ReadAll(config)[0];
        var renderer = new ViewportRenderer(format);

        // DRAW_A_WALL is 1 and addresses rect 0 -- GetWidth does Type-- before indexing. Using the
        // enum value directly would draw a 32-pixel side wall where a 112-pixel front wall goes.
        Assert.Equal(format.SlotRects[0], renderer.SlotRect(DrawSlot.A));
        Assert.Equal(format.SlotRects[15], renderer.SlotRect(DrawSlot.P));
        Assert.Equal(format.SlotRects[4].Width, renderer.SlotWidth(DrawSlot.E));
    }

    [Fact]
    public void The_far_sliver_is_drawn_when_slot_13_falls_off_the_map()
    {
        // Every wall index zero, so the only thing that can trigger the test is slot 13 having no
        // cell at all -- which is exactly what the unwrapped outer slots exist to express.
        var cells = new AreaMapCell[16];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = Cell(0, 0, 0, 0);
        }

        var map = new Map(4, 4, cells);
        var resolver = new WallResolver(map, [Set("only")]);
        var view = ViewMap.For(0, 0, Facing.North, 4, 4);

        Assert.False(resolver.CellExists(view, 13));
        Assert.True(ViewportRenderer.ShouldDrawFarSliver(
            view, resolver, Facing.North, Facing.West, Facing.East));
    }

    [Fact]
    public void The_far_sliver_is_skipped_when_nothing_occludes_it()
    {
        // A map big enough that slot 13 lands inside it, and no walls anywhere: all four
        // disjuncts false, so the sliver is not drawn.
        var cells = new AreaMapCell[400];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = Cell(0, 0, 0, 0);
        }

        var map = new Map(20, 20, cells);
        var resolver = new WallResolver(map, [Set("only")]);
        var view = ViewMap.For(10, 10, Facing.North, 20, 20);

        Assert.True(resolver.CellExists(view, 13));
        Assert.False(ViewportRenderer.ShouldDrawFarSliver(
            view, resolver, Facing.North, Facing.West, Facing.East));
    }

    [Fact]
    public void Any_one_of_the_four_conditions_is_enough()
    {
        var cells = new AreaMapCell[400];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = Cell(0, 0, 0, 0);
        }

        // A wall on the left face of slot 0 alone must trigger it. Slot 0 from (10,10) facing
        // north is two forward and two left, i.e. (8,8).
        var view = ViewMap.For(10, 10, Facing.North, 20, 20);
        Assert.Equal((8, 8), view[0]);

        cells[(8 * 20) + 8] = Cell(0, 0, 0, 1);        // west face has wall 1
        var resolver = new WallResolver(new Map(20, 20, cells), [Set("only")]);

        Assert.True(resolver.HasWall(view, 0, Facing.West));
        Assert.True(ViewportRenderer.ShouldDrawFarSliver(
            view, resolver, Facing.North, Facing.West, Facing.East));
    }

    /// <summary>A minimal one-band wall format, so the renderer can be built without a design.</summary>
    private static string[] BuildFormatConfig()
    {
        var lines = new List<string> { "MAX_ALTERNATE_WALL_FORMATS = 1",
                                       "WIDTH_WALL_FORMAT_1 = 480",
                                       "HEIGHT_WALL_FORMAT_1 = 360",
                                       "NUM_DISTANT_WALLS_1 = 5" };

        // Widths that mirror the real thing: E is the 112-wide front wall, the rest are narrower.
        for (int slot = 0; slot < WallFormat.MaxSlotTypes; slot++)
        {
            char letter = WallFormat.SlotLetter(slot);
            int width = slot == 4 ? 112 : slot == 7 ? 48 : slot >= 8 ? 16 : 32;
            lines.Add($"{letter}1_WALL_RECT = 0,0,{width},211");
            lines.Add($"{letter}1_OFF = 0,0");
        }

        for (int i = 0; i < 13; i++)
        {
            lines.Add($"VIEWPORT_COORD_{i}_1 = {i * 4},0");
        }

        return [.. lines];
    }
}
