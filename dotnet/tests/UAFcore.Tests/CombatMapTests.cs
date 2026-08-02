using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the combat terrain grid and its primitives (<c>Drawtile.cpp</c>).
/// </summary>
public class CombatMapTests
{
    /// <summary>A map whose every square is the dungeon floor tile, so nothing blocks.</summary>
    private static CombatMap OpenMap(int width = 25, int height = 25)
    {
        var map = new CombatMap(width, height);
        map.FillHoles();
        return map;
    }

    [Fact]
    public void Extents_are_clamped_the_way_config_clamps_them()
    {
        // Globals.cpp:2868 applies max(25, w) then min(500, w). A design asking for a 4x4 combat
        // map gets 25x25, and one asking for 10000 gets 500.
        Assert.Equal(25, new CombatMap(4, 4).Width);
        Assert.Equal(500, new CombatMap(10000, 10000).Height);
        Assert.Equal(50, new CombatMap().Width);
    }

    [Fact]
    public void An_ungenerated_map_is_impassable_everywhere()
    {
        // Empty squares fail HaveMovability's `cell >= 1` guard. This is why every generator
        // fills holes before anyone walks on the map, and getting it backwards would let
        // combatants wander off the terrain.
        var map = new CombatMap(25, 25);
        Assert.True(map.IsEmpty(10, 10));
        Assert.False(map.IsPassable(10, 10));

        map.FillHoles();
        Assert.False(map.IsEmpty(10, 10));
        Assert.True(map.IsPassable(10, 10));
    }

    [Fact]
    public void A_disabled_tile_is_silently_ignored_rather_than_placed()
    {
        // Dungeon tile 26 has enabled = 0, and the conversion tables still name disabled tiles.
        // SetDungeon drops the write; throwing would turn normal generation into an error.
        var map = new CombatMap(25, 25);
        Assert.False(CombatTiles.Dungeon[26].Enabled);

        map.SetTile(5, 5, 26);
        Assert.True(map.IsEmpty(5, 5));

        map.SetTile(5, 5, 25);
        Assert.Equal(25, map.CellAt(5, 5));
    }

    [Fact]
    public void Out_of_range_tile_indices_are_ignored()
    {
        var map = new CombatMap(25, 25);
        map.SetTile(5, 5, 0);
        map.SetTile(5, 5, -1);
        map.SetTile(5, 5, 999);
        Assert.True(map.IsEmpty(5, 5));
    }

    [Fact]
    public void Fits_measures_the_whole_icon_not_just_its_corner()
    {
        // Combat icons run up to 4x4 and the coordinate is the top-left corner, so a monster at
        // the last column does not fit even though its corner is on the map.
        var map = OpenMap(25, 25);
        Assert.True(map.Fits(24, 24, 1, 1));
        Assert.False(map.Fits(24, 24, 2, 2));
        Assert.True(map.Fits(21, 21, 4, 4));
        Assert.False(map.Fits(22, 22, 4, 4));
    }

    [Fact]
    public void Obstacle_reports_off_map_before_wall_and_wall_before_occupied()
    {
        // The order is load-bearing: the round state machine wants to know which of the three it
        // hit, not merely that the square is unavailable.
        var map = OpenMap(25, 25);
        map.CombatantCount = 4;

        Assert.Equal(ObstacleType.None, map.Obstacle(10, 10));
        Assert.Equal(ObstacleType.OffMap, map.Obstacle(-1, 10));
        Assert.Equal(ObstacleType.OffMap, map.Obstacle(25, 10));

        // Tile 1 is impassable in the dungeon table.
        map.SetTile(11, 10, 1);
        Assert.Equal(ObstacleType.Wall, map.Obstacle(11, 10));

        map.Place(12, 10, combatant: 2);
        Assert.Equal(ObstacleType.Occupied, map.Obstacle(12, 10));

        // A wall the caller is standing on still reads as a wall: the occupancy test runs second.
        map.Place(11, 10, combatant: 3);
        Assert.Equal(ObstacleType.Wall, map.Obstacle(11, 10));
    }

    [Fact]
    public void An_occupant_can_be_ignored_so_a_combatant_does_not_block_itself()
    {
        var map = OpenMap(25, 25);
        map.CombatantCount = 4;
        map.Place(10, 10, combatant: 2);

        Assert.Equal(ObstacleType.Occupied, map.Obstacle(10, 10));
        Assert.Equal(ObstacleType.None, map.Obstacle(10, 10, ignoreCombatant: 2));
        Assert.Equal(ObstacleType.Occupied, map.Obstacle(10, 10, ignoreCombatant: 3));

        // checkOccupants=false skips the test entirely -- getHallWidth measures terrain only.
        Assert.Equal(ObstacleType.None, map.Obstacle(10, 10, checkOccupants: false));
    }

    [Fact]
    public void A_multi_square_icon_occupies_every_square_it_covers()
    {
        var map = OpenMap(25, 25);
        map.CombatantCount = 4;
        map.Place(10, 10, combatant: 1, width: 2, height: 3);

        for (int y = 10; y < 13; y++)
        {
            for (int x = 10; x < 12; x++)
            {
                Assert.Equal(1, map.OccupantAt(x, y));
            }
        }

        Assert.Equal(CombatMap.NoDude, map.OccupantAt(12, 10));
        Assert.Equal(CombatMap.NoDude, map.OccupantAt(10, 13));

        map.Remove(10, 10, 2, 3);
        Assert.Equal(CombatMap.NoDude, map.OccupantAt(10, 10, 2, 3));
    }

    [Fact]
    public void The_dying_layer_is_separate_so_a_corpse_and_a_combatant_can_share_a_square()
    {
        // Dying combatants are drawn before live ones and do not block movement, which is why
        // TERRAIN_CELL carries two indices rather than one.
        var map = OpenMap(25, 25);
        map.CombatantCount = 4;

        map.PlaceDying(10, 10, combatant: 1);
        Assert.Equal(1, map.DyingAt(10, 10));
        Assert.Equal(CombatMap.NoDude, map.OccupantAt(10, 10));
        Assert.Equal(ObstacleType.None, map.Obstacle(10, 10));

        map.Place(10, 10, combatant: 2);
        Assert.Equal(1, map.DyingAt(10, 10));
        Assert.Equal(2, map.OccupantAt(10, 10));
    }

    [Fact]
    public void A_stale_occupant_index_is_cleared_rather_than_merely_skipped()
    {
        // getCombatantInCell writes NO_DUDE back when the stored index is past the combatant
        // count (Drawtile.cpp:1531), so a combatant removed from the list stops blocking. This is
        // a read that mutates -- easy to port as a plain guard and lose.
        var map = OpenMap(25, 25);
        map.CombatantCount = 2;
        map.Place(10, 10, combatant: 7);

        Assert.Equal(CombatMap.NoDude, map.OccupantAt(10, 10));

        // Raising the count must not resurrect it: the first read cleared the square.
        map.CombatantCount = 10;
        Assert.Equal(CombatMap.NoDude, map.OccupantAt(10, 10));
    }

    [Fact]
    public void Distance_is_euclidean_and_rounded_not_chebyshev()
    {
        // floor(d + 0.5) over the true distance. A diagonal neighbour is 1 away, but two diagonal
        // steps are 3 rather than 2 -- every combat range check depends on this rounding.
        Assert.Equal(0, CombatMap.Distance(5, 5, 5, 5));
        Assert.Equal(1, CombatMap.Distance(5, 5, 6, 5));
        Assert.Equal(1, CombatMap.Distance(5, 5, 6, 6));    // sqrt(2) = 1.41 -> 1
        Assert.Equal(3, CombatMap.Distance(5, 5, 7, 7));    // sqrt(8) = 2.83 -> 3
        Assert.Equal(4, CombatMap.Distance(5, 5, 8, 8));    // sqrt(18) = 4.24 -> 4
        Assert.Equal(5, CombatMap.Distance(0, 0, 3, 4));
        Assert.Equal(CombatMap.Distance(1, 2, 9, 8), CombatMap.Distance(9, 8, 1, 2));
    }

    [Fact]
    public void Line_of_sight_distinguishes_a_wall_from_the_edge_of_the_map()
    {
        // A square that blocks sight because it holds a wall reflects; one that is off-map or
        // has no terrain does not. Spell bouncing needs the difference.
        var map = OpenMap(25, 25);

        Assert.True(map.IsSeeThrough(10, 10, out bool reflects));
        Assert.False(reflects);

        map.SetTile(10, 10, 1);     // impassable, not see-through
        Assert.False(map.IsSeeThrough(10, 10, out reflects));
        Assert.True(reflects);

        Assert.False(map.IsSeeThrough(-1, 10, out reflects));
        Assert.False(reflects);
    }

    [Fact]
    public void Find_empty_cell_returns_the_starting_square_when_it_is_already_free()
    {
        var map = OpenMap(25, 25);
        int x = 10, y = 10;
        Assert.True(CombatMapGenerator.FindEmptyCell(map, ref x, ref y));
        Assert.Equal((10, 10), (x, y));
    }

    [Fact]
    public void Find_empty_cell_widens_its_search_until_something_fits()
    {
        var map = OpenMap(25, 25);
        map.CombatantCount = 1;

        // Wall off everything within one square of the start.
        for (int j = 9; j <= 11; j++)
        {
            for (int i = 9; i <= 11; i++)
            {
                map.SetTile(i, j, 1);
            }
        }

        int x = 10, y = 10;
        Assert.True(CombatMapGenerator.FindEmptyCell(map, ref x, ref y));
        Assert.NotEqual((10, 10), (x, y));
        Assert.Equal(ObstacleType.None, map.Obstacle(x, y));
        Assert.InRange(CombatMap.Distance(10, 10, x, y), 1, 3);
    }

    [Fact]
    public void Find_empty_cell_reports_failure_on_a_map_with_no_room()
    {
        var map = new CombatMap(25, 25);   // never filled, so every square is impassable
        int x = 10, y = 10;
        Assert.False(CombatMapGenerator.FindEmptyCell(map, ref x, ref y));
    }

    [Fact]
    public void Find_empty_cell_skips_a_sealed_pocket_when_asked_for_reachability()
    {
        // The reference rejects a square the party cannot walk to, via pathMgr.GetPath. Without
        // the rule the nearest free square wins even when it is walled off; with it, the search
        // keeps going.
        var map = OpenMap(25, 25);

        // A solid 5x5 block with one hole at its centre. Starting on the block's own edge, the
        // hole is the only free square within a radius of one, so an unfiltered search must pick
        // it -- which is what makes the filtered search's refusal meaningful.
        for (int j = 3; j <= 7; j++)
        {
            for (int i = 3; i <= 7; i++)
            {
                if ((i, j) != (5, 5)) { map.SetTile(i, j, 1); }
            }
        }

        int x = 5, y = 4;      // a wall, so the search starts looking around
        Assert.True(CombatMapGenerator.FindEmptyCell(map, ref x, ref y));
        Assert.Equal((5, 5), (x, y));       // without the rule, the pocket is nearest

        x = 5; y = 4;
        Assert.True(CombatMapGenerator.FindEmptyCell(map, ref x, ref y,
                                                     reachableFrom: (20, 20)));
        Assert.NotEqual((5, 5), (x, y));    // with it, the pocket is refused
        Assert.Equal(ObstacleType.None, map.Obstacle(x, y));
    }
}
