using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the squares an area spell covers.</summary>
public class SpellAreaTests
{
    private const int Map = 30;

    private static HashSet<(int, int)> Rect(int x0, int y0, int dirX, int dirY,
                                            int width, int height, bool forceNonZero = true) =>
        [.. SpellArea.Rectangle(x0, y0, dirX, dirY, width, height, forceNonZero, Map, Map)];

    /// <summary>Every square of an axis-aligned box, for comparing against.</summary>
    private static HashSet<(int, int)> Box(int left, int top, int right, int bottom)
    {
        var cells = new HashSet<(int, int)>();
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                cells.Add((x, y));
            }
        }

        return cells;
    }

    // ---- the rectangle -------------------------------------------------------------------------

    [Fact]
    public void A_one_by_one_rectangle_is_just_the_target()
    {
        Assert.Equal([(5, 5)], Rect(5, 5, dirX: 1, dirY: 0, width: 1, height: 1));
    }

    [Fact]
    public void The_target_is_always_included_whatever_the_tests_would_say()
    {
        // The flood seeds with it and marks it visited before the loop, so it is never tested.
        Assert.Contains((5, 5), Rect(5, 5, 1, 0, width: 1, height: 1));
    }

    [Fact]
    public void An_odd_rectangle_facing_east_is_centred_on_the_target()
    {
        // Width is across (y), height is along (x). 3 across and 5 along.
        var cells = Rect(10, 10, dirX: 1, dirY: 0, width: 3, height: 5);

        Assert.Equal(Box(8, 9, 12, 11), cells);
    }

    [Fact]
    public void Facing_south_swaps_which_axis_width_and_height_measure()
    {
        // Same 3 across and 5 along, now rotated a quarter turn.
        var cells = Rect(10, 10, dirX: 0, dirY: 1, width: 3, height: 5);

        Assert.Equal(Box(9, 8, 11, 12), cells);
    }

    [Fact]
    public void An_even_extent_straddles_the_target_one_square_off_centre()
    {
        // The quarter-square nudge puts the centre just past the target's own corner, so the extra
        // row and column go to the +y and +x side.
        var cells = Rect(10, 10, dirX: 1, dirY: 0, width: 2, height: 2);

        Assert.Equal(Box(10, 10, 11, 11), cells);
    }

    [Fact]
    public void Facing_west_is_the_same_shape_as_facing_east()
    {
        // The tests are on squared distances, so the sign of the direction cannot matter.
        Assert.Equal(Rect(10, 10, 1, 0, 3, 5), Rect(10, 10, -1, 0, 3, 5));
    }

    [Fact]
    public void On_a_diagonal_width_measures_along_the_cast_and_height_across_it()
    {
        // The reference's two tests use the direction vector reflected rather than rotated. That
        // gives the perpendicular for the four cardinal directions and the wrong axis for the
        // diagonals -- contradicting its own header comment, and reproduced. Facing southeast, the
        // wide-and-shallow 8x2 band runs *along* the diagonal for three steps and only one step
        // across it, which is what the cardinal directions would give for 2x8.
        var band = Rect(10, 10, dirX: 1, dirY: 1, width: 8, height: 2);

        Assert.Contains((11, 11), band);          // along the cast
        Assert.Contains((13, 13), band);          // still along it, three steps out
        Assert.DoesNotContain((14, 14), band);    // width 8 runs out here
        Assert.Contains((11, 10), band);          // one step off the axis
        Assert.DoesNotContain((12, 10), band);    // height 2 allows no more
    }

    [Fact]
    public void A_diagonal_area_only_one_square_thick_collapses_to_the_target()
    {
        // The flood is four-connected (deltax/deltay are the four cardinals) but a thin diagonal
        // strip is only diagonally connected, so the flood cannot leave the seed. The squares that
        // would pass both tests are unreachable and simply never appear. The reference has the
        // same hole -- an area spell cast diagonally with height 1 hits one square.
        Assert.Equal([(10, 10)], Rect(10, 10, dirX: 1, dirY: 1, width: 8, height: 1));
    }

    [Fact]
    public void A_zero_extent_yields_nothing_unless_it_is_forced_up()
    {
        Assert.Empty(Rect(5, 5, 1, 0, width: 0, height: 3, forceNonZero: false));
        Assert.NotEmpty(Rect(5, 5, 1, 0, width: 0, height: 3, forceNonZero: true));
    }

    [Fact]
    public void A_rectangle_is_clipped_at_the_map_edge()
    {
        // The flood refuses to step outside, so a cast near the corner simply covers less.
        var cells = SpellArea.Rectangle(0, 0, 1, 0, width: 5, height: 5,
                                        forceNonZero: true, Map, Map);

        Assert.All(cells, c => Assert.True(c.X >= 0 && c.Y >= 0));
        Assert.Contains((0, 0), cells);
    }

    [Fact]
    public void No_square_is_reported_twice()
    {
        var cells = SpellArea.Rectangle(10, 10, 1, 0, 5, 5, true, Map, Map);

        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void A_direction_of_nothing_is_treated_as_east()
    {
        Assert.Equal(Rect(10, 10, 1, 0, 3, 5), Rect(10, 10, 0, 0, 3, 5));
    }

    // ---- the circle ----------------------------------------------------------------------------

    [Fact]
    public void A_radius_of_zero_covers_only_the_target()
    {
        // radius*2|1 is 1, so a 1x1 square, and the target survives the prune at distance zero.
        Assert.Equal([(10, 10)], SpellArea.Circle(10, 10, radius: 0, Map, Map));
    }

    [Fact]
    public void A_radius_of_one_covers_the_target_and_its_eight_neighbours()
    {
        // The side is 3, and every square of a 3x3 rounds to distance 1 or less -- a diagonal
        // neighbour is sqrt(2), which floors to 1.
        var cells = SpellArea.Circle(10, 10, radius: 1, Map, Map);

        Assert.Equal(Box(9, 9, 11, 11), [.. cells]);
    }

    [Fact]
    public void A_radius_of_two_gives_a_five_wide_square_with_its_corners_off()
    {
        // radius*2|1 = 5, so 5x5 before pruning. The corners are at sqrt(8) = 2.83, which rounds
        // to 3 and is dropped; everything else survives.
        var cells = SpellArea.Circle(10, 10, radius: 2, Map, Map).ToHashSet();

        Assert.Equal(21, cells.Count);
        Assert.DoesNotContain((8, 8), cells);
        Assert.DoesNotContain((12, 12), cells);
        Assert.Contains((8, 10), cells);
        Assert.Contains((10, 12), cells);
    }

    [Fact]
    public void A_negative_radius_covers_nothing_at_all()
    {
        // Not even the target: the reference returns before the square is built.
        Assert.Empty(SpellArea.Circle(10, 10, radius: -1, Map, Map));
    }

    [Fact]
    public void The_circle_is_symmetric_despite_the_rectangle_it_is_built_from_not_being()
    {
        // The odd side makes the quarter-square nudge cancel, so unlike an even rectangle the
        // circle really is centred.
        var cells = SpellArea.Circle(10, 10, radius: 3, Map, Map).ToHashSet();

        foreach (var (x, y) in cells)
        {
            Assert.Contains((20 - x, 20 - y), cells);
        }
    }

    // ---- the cone ------------------------------------------------------------------------------

    [Fact]
    public void A_cone_starts_at_the_target_and_widens_away_from_the_caster()
    {
        // Caster at (10,10) aiming at (12,10), reaching 3 further and 3 wide at the far end.
        // The apex is the target, so the squares between caster and target are not in it, and the
        // half-width grows with distance: 0.5 one step out, 1.0 two steps out.
        var cone = SpellArea.Cone(10, 10, 12, 10, length: 3, width: 3,
                                  forceNonZero: true, Map, Map).ToHashSet();

        Assert.Equal(
            [(12, 10), (13, 10), (14, 9), (14, 10), (14, 11)],
            cone);
    }

    [Fact]
    public void The_squares_between_the_caster_and_the_target_are_not_in_the_cone()
    {
        var cone = SpellArea.Cone(10, 10, 12, 10, 3, 3, true, Map, Map);

        Assert.DoesNotContain((10, 10), cone);
        Assert.DoesNotContain((11, 10), cone);
    }

    [Fact]
    public void A_cone_cast_on_the_casters_own_square_produces_nothing()
    {
        // The direction comes from dividing by the caster-to-target distance. The reference has no
        // guard and gets a NaN triangle that contains nothing; the result is the same.
        Assert.Empty(SpellArea.Cone(10, 10, 10, 10, 5, 5, true, Map, Map));
    }

    [Fact]
    public void A_cone_points_wherever_the_caster_is_relative_to_the_target()
    {
        // Same geometry rotated: caster below, aiming up.
        var cone = SpellArea.Cone(10, 12, 10, 10, length: 3, width: 3,
                                  forceNonZero: true, Map, Map).ToHashSet();

        Assert.Equal(
            [(10, 10), (10, 9), (9, 8), (10, 8), (11, 8)],
            cone);
    }

    [Fact]
    public void A_zero_length_or_width_cone_covers_nothing_unless_forced_up()
    {
        Assert.Empty(SpellArea.Cone(10, 10, 12, 10, 0, 3, forceNonZero: false, Map, Map));
        Assert.Empty(SpellArea.Cone(10, 10, 12, 10, 3, 0, forceNonZero: false, Map, Map));
        Assert.NotEmpty(SpellArea.Cone(10, 10, 12, 10, 0, 0, forceNonZero: true, Map, Map));
    }

    [Fact]
    public void A_cone_is_clipped_to_the_map()
    {
        var cone = SpellArea.Cone(2, 2, 1, 2, length: 10, width: 6, forceNonZero: true,
                                  Map, Map);

        Assert.All(cone, c => Assert.True(c.X >= 0 && c.Y >= 0 && c.X < Map && c.Y < Map));
    }

    // ---- the triangle test ---------------------------------------------------------------------

    [Fact]
    public void A_degenerate_triangle_contains_nothing_at_all()
    {
        // Not even its own vertices: the zero-area test returns before anything else.
        Assert.False(SpellArea.IsPointInTriangle(1, 1, 0, 0, 1, 1, 2, 2, onLine: 7));
    }

    [Fact]
    public void The_online_flags_pick_which_edges_count_as_inside()
    {
        // (2, 0) sits on the edge from (0,0) to (4,0), which is the p1-p2 edge -- flag 4.
        Assert.True(SpellArea.IsPointInTriangle(2, 0, 0, 0, 4, 0, 0, 4, onLine: 4));
        Assert.False(SpellArea.IsPointInTriangle(2, 0, 0, 0, 4, 0, 0, 4, onLine: 3));
    }

    // ---- the lines -----------------------------------------------------------------------------

    [Fact]
    public void A_straight_line_covers_every_square_between_its_ends()
    {
        Assert.Equal([(10, 10), (11, 10), (12, 10), (13, 10)],
                     SpellArea.Line(10, 10, 13, 10, Map, Map));
    }

    [Fact]
    public void A_perfect_diagonal_line_steps_one_square_at_a_time()
    {
        Assert.Equal([(10, 10), (11, 11), (12, 12), (13, 13)],
                     SpellArea.Line(10, 10, 13, 13, Map, Map));
    }

    [Fact]
    public void A_shallow_line_covers_more_squares_than_a_tile_walk_would()
    {
        // Bresenham runs at 48x resolution over world coordinates and each step is quantised back,
        // so both squares at a diagonal transition get visited. A tile-resolution walk from (10,10)
        // to (12,11) gives (10,10), (11,11), (12,11) and never enters (11,10); the pixel walk picks
        // it up on the way through.
        var line = SpellArea.Line(10, 10, 12, 11, Map, Map);

        Assert.Equal([(10, 10), (11, 10), (11, 11), (12, 11)], line);
    }

    [Fact]
    public void A_steep_line_takes_the_other_corner_at_each_transition()
    {
        // The mirror of the shallow case, stepping in y rather than x. A tile walk would give
        // (10,10), (11,11), (11,12); the pixel walk adds (10,11).
        var line = SpellArea.Line(10, 10, 11, 12, Map, Map);

        Assert.Equal([(10, 10), (10, 11), (11, 11), (11, 12)], line);
    }

    [Fact]
    public void A_line_of_no_length_is_the_one_square()
    {
        Assert.Equal([(10, 10)], SpellArea.Line(10, 10, 10, 10, Map, Map));
    }

    [Fact]
    public void A_line_stops_at_the_map_edge_rather_than_skipping_past_it()
    {
        // The callback returns false outside the map, which terminates the walk.
        var line = SpellArea.Line(2, 2, 2, 40, Map, Map);

        Assert.All(line, c => Assert.True(c.Y < Map));
        Assert.Equal((2, Map - 1), line[^1]);
    }

    [Fact]
    public void No_square_of_a_line_is_reported_twice()
    {
        var line = SpellArea.Line(3, 3, 20, 7, Map, Map);

        Assert.Equal(line.Count, line.Distinct().Count());
    }

    // ---- combatants ----------------------------------------------------------------------------

    [Fact]
    public void An_area_catches_whoever_stands_in_it()
    {
        // CombatantCount gates the occupancy layer: an index at or above it is treated as stale
        // and cleared, so a map built directly by a test has to say how many combatants exist.
        var map = new CombatMap(Map, Map) { CombatantCount = 10 };
        map.Place(10, 10, combatant: 3);
        map.Place(12, 10, combatant: 7);
        map.Place(20, 20, combatant: 9);

        var caught = SpellArea.CombatantsIn(map, SpellArea.Circle(11, 10, 1, Map, Map));

        // The order is the flood's, not the combatant list's: the target square first, then north,
        // east, south, west outward. That is what decides which of two targets a spell resolves
        // against first, so it is asserted rather than sorted away.
        Assert.Equal([7, 3], caught);
    }

    [Fact]
    public void A_large_monster_is_caught_once_however_many_of_its_squares_are_covered()
    {
        var map = new CombatMap(Map, Map) { CombatantCount = 10 };
        map.Place(10, 10, combatant: 4, width: 2, height: 2);

        var caught = SpellArea.CombatantsIn(map, SpellArea.Circle(10, 10, 2, Map, Map));

        Assert.Equal([4], caught);
    }

    [Fact]
    public void An_empty_area_catches_nobody()
    {
        var map = new CombatMap(Map, Map) { CombatantCount = 10 };

        Assert.Empty(SpellArea.CombatantsIn(map, SpellArea.Circle(10, 10, 3, Map, Map)));
    }
}
