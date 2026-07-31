using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers <see cref="WallResolver"/> against a hand-built map, where every wall index is known.
/// </summary>
/// <remarks>
/// Synthetic rather than a real design, deliberately. The wall-set table sits after the event list
/// in a <c>.lvl</c> file, so reading it needs a decoder for every event subclass — and that
/// dispatcher is still in the serialization tests rather than the library. Until it moves, the
/// resolver can be verified but not yet wired to a design's own wall art.
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

    private static WallResolver Resolver(Map map) =>
        new(map, [Set("first"), Set("second"), Set("third")]);

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

        // Index 1 is the design's *first* wall set, not its second. An off-by-one here shifts
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

        var resolver = new WallResolver(new Map(2, 2, cells), [Set("only")]);
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
}
