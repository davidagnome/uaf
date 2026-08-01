using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the monster-placement turtle interpreter and the approach-direction cursor
/// (<c>Combatants.cpp:2749</c>, <c>:3124</c>).
/// </summary>
public class MonsterPlacementTests
{
    private static CombatMap OpenMap(int width = 50, int height = 50)
    {
        var map = new CombatMap(width, height);
        map.FillHoles();
        return map;
    }

    /// <summary>An arrangement with <paramref name="monsters"/> monsters all on one side.</summary>
    private static MonsterArrangement Arrange(int monsters, int direction, int partyX = 25,
                                              int partyY = 25)
    {
        var state = new MonsterArrangement { PartyX = partyX, PartyY = partyY };
        state.Activate(monsters);
        for (int i = 0; i < monsters; i++)
        {
            state.Slots[i].DirectionFromParty = direction;
        }
        state.CountByDirection[direction] = monsters;
        state.BeginDirection(direction);
        return state;
    }

    private static CombatantIcon[] Icons(int n, int w = 1, int h = 1) =>
        [.. Enumerable.Repeat(new CombatantIcon(w, h), n)];

    [Fact]
    public void The_built_in_programs_are_the_ones_the_shipped_script_names()
    {
        // Read off reference/dc-default/databases/specialAbilities/CombatPlacement.txt and the
        // defaultGlobalScripts table at Specab.cpp:2081. The script branches on
        // $GET_PARTY_FACING() >= 2, which is south or west.
        Assert.Equal("bPV500E", TurtlePlacement.Default(EncounterDistance.UpClose, Facing.North));
        Assert.Equal("FbPV500E", TurtlePlacement.Default(EncounterDistance.UpClose, Facing.South));
        Assert.Equal("9FbPV500E", TurtlePlacement.Default(EncounterDistance.Nearby, Facing.East));
        Assert.Equal("10FbPV500E", TurtlePlacement.Default(EncounterDistance.Nearby, Facing.West));
        Assert.Equal("16FbPV500E", TurtlePlacement.Default(EncounterDistance.FarAway, Facing.North));
        Assert.Equal("17FbPV500E", TurtlePlacement.Default(EncounterDistance.FarAway, Facing.South));
    }

    [Theory]
    // Forward is the direction the monsters are coming from, in the sheared frame.
    [InlineData(0, -9, -9)]     // north
    [InlineData(1, 9, 0)]       // east
    [InlineData(2, 9, 9)]       // south
    [InlineData(3, -9, 0)]      // west
    public void A_digit_prefix_repeats_a_move(int direction, int expectedX, int expectedY)
    {
        var state = Arrange(0, direction);
        TurtlePlacement.Run("9F", state, OpenMap(), []);

        Assert.Equal(expectedX, state.TurtleX);
        Assert.Equal(expectedY, state.TurtleY);
    }

    [Fact]
    public void A_bare_move_steps_once_and_the_count_resets_after_it()
    {
        var state = Arrange(0, 1);              // east: F is (+1, 0)
        TurtlePlacement.Run("FF", state, OpenMap(), []);
        Assert.Equal(2, state.TurtleX);

        state = Arrange(0, 1);
        TurtlePlacement.Run("3FF", state, OpenMap(), []);
        Assert.Equal(4, state.TurtleX);          // 3 then 1, not 3 then 3
    }

    [Fact]
    public void Multi_digit_counts_accumulate()
    {
        var state = Arrange(0, 1);
        TurtlePlacement.Run("17F", state, OpenMap(), []);
        Assert.Equal(17, state.TurtleX);
    }

    [Fact]
    public void Moving_the_turtles_row_carries_its_column_with_it()
    {
        // MoveTurtleY holds x - y constant, because the combat map is rotated 45 degrees and the
        // placement limits are expressed in that sheared coordinate. See MonsterArrangement.
        var state = Arrange(0, 0);
        state.TurtleX = 5;
        state.TurtleY = 3;

        state.MoveTurtleY(7);
        Assert.Equal(7, state.TurtleY);
        Assert.Equal(9, state.TurtleX);
        Assert.Equal(5 - 3, state.TurtleX - state.TurtleY);

        state.MoveTurtleX(1);
        Assert.Equal(1, state.TurtleX);
        Assert.Equal(7, state.TurtleY);
    }

    [Fact]
    public void P_plants_the_next_monster_where_the_turtle_stands()
    {
        var map = OpenMap();
        map.CombatantCount = 2;
        var state = Arrange(2, 1);              // east

        TurtlePlacement.Run("3FP", state, map, Icons(2));

        Assert.True(state.Slots[0].IsPlaced);
        Assert.Equal((28, 25), (state.Slots[0].PlaceX, state.Slots[0].PlaceY));
        Assert.Equal(0, map.OccupantAt(28, 25));
        Assert.False(state.Slots[1].IsPlaced);   // only one P, so only one monster
    }

    [Fact]
    public void P_reports_a_zero_when_there_is_nobody_left_to_place()
    {
        var map = OpenMap();
        var state = Arrange(0, 1);
        Assert.Equal("0", TurtlePlacement.Run("P", state, map, []));
    }

    [Fact]
    public void E_places_up_to_its_count_and_stops()
    {
        var map = OpenMap();
        map.CombatantCount = 5;
        var state = Arrange(5, 1);

        TurtlePlacement.Run("3E", state, map, Icons(5));

        Assert.Equal(3, state.Slots.Count(s => s.IsPlaced));
    }

    [Fact]
    public void E_fills_outward_from_the_turtle_without_stacking()
    {
        var map = OpenMap();
        map.CombatantCount = 8;
        var state = Arrange(8, 1);

        TurtlePlacement.Run("5F500E", state, map, Icons(8));

        var placed = state.Slots.Where(s => s.IsPlaced)
                                .Select(s => (s.PlaceX, s.PlaceY)).ToList();
        Assert.Equal(8, placed.Count);
        Assert.Equal(8, placed.Distinct().Count());

        // The turtle sat at (30,25); everything must land within the search radius of it.
        Assert.All(placed, p => Assert.InRange(
            Math.Max(Math.Abs(p.PlaceX - 30), Math.Abs(p.PlaceY - 25)), 0, map.Width / 4));
    }

    [Fact]
    public void The_back_limit_keeps_monsters_on_their_own_side()
    {
        // 'b' for a northern approach pins LimitMaxY at the turtle's row, so nothing may be placed
        // south of it. That is what stops a northern group appearing behind the party.
        var map = OpenMap();
        map.CombatantCount = 6;
        var state = Arrange(6, 0);

        TurtlePlacement.Run("b500E", state, map, Icons(6));

        Assert.Equal(0, state.LimitMaxY);
        Assert.All(state.Slots.Where(s => s.IsPlaced),
                   s => Assert.True(s.PlaceY - 25 <= 0, $"placed at y={s.PlaceY}, south of the limit"));
    }

    [Fact]
    public void The_east_and_west_limits_bound_the_sheared_axis_not_the_column()
    {
        // For an eastern approach 'b' writes LimitMinX from turtleX - turtleY, and PlantCombatant
        // compares relX - relY against it. Reading either as a plain column puts monsters on the
        // diagonal.
        var state = Arrange(1, 1);
        state.TurtleX = 6;
        state.TurtleY = 2;

        TurtlePlacement.Run("b", state, OpenMap(), Icons(1));
        Assert.Equal(4, state.LimitMinX);
    }

    [Fact]
    public void V_requires_line_of_sight_to_something_already_placed()
    {
        var map = OpenMap();
        map.CombatantCount = 4;

        // Nothing on the map at all: with V set, no square can see a placed combatant, so nothing
        // is placed. Without it, everything is.
        var withSight = Arrange(4, 1);
        TurtlePlacement.Run("V500E", withSight, map, Icons(4));
        Assert.All(withSight.Slots, s => Assert.False(s.IsPlaced));

        var withoutSight = Arrange(4, 1);
        TurtlePlacement.Run("500E", withoutSight, OpenMap(), Icons(4));
        Assert.All(withoutSight.Slots, s => Assert.True(s.IsPlaced));
    }

    [Fact]
    public void The_query_command_reports_what_is_under_the_turtle()
    {
        var map = OpenMap();
        map.CombatantCount = 1;
        var state = Arrange(1, 1);

        Assert.Equal("n", TurtlePlacement.Run("?", state, map, Icons(1)));

        map.SetTile(28, 25, 1);                  // impassable
        Assert.Equal("w", TurtlePlacement.Run("3F?", Arrange(1, 1), map, Icons(1)));

        // Far enough east to leave a 50-wide map.
        Assert.Equal("i", TurtlePlacement.Run("30F?", Arrange(1, 1), map, Icons(1)));
    }

    [Fact]
    public void The_turtle_position_stack_is_two_deep()
    {
        var state = Arrange(0, 1);
        TurtlePlacement.Run("3Fu5Fu2Foo", state, OpenMap(), []);

        // Pushed at 3 and at 8, moved to 10, then popped twice back to 3.
        Assert.Equal(3, state.TurtleX);
    }

    [Fact]
    public void An_unknown_command_reports_an_error_character_and_carries_on()
    {
        var state = Arrange(0, 1);
        Assert.Equal("e", TurtlePlacement.Run("3FzF", state, OpenMap(), []));
        Assert.Equal(4, state.TurtleX);      // both moves still happened
    }

    [Theory]
    [InlineData(EncounterDirection.North, new[] { 0, 0, 0, 0 })]
    [InlineData(EncounterDirection.East, new[] { 1, 1, 1, 1 })]
    [InlineData(EncounterDirection.NorthSouth, new[] { 0, 2, 0, 2 })]
    [InlineData(EncounterDirection.EastWest, new[] { 1, 3, 1, 3 })]
    [InlineData(EncounterDirection.NorthSouthEast, new[] { 0, 1, 2, 0 })]
    [InlineData(EncounterDirection.NorthWestEast, new[] { 3, 0, 1, 3 })]
    public void Monsters_are_dealt_round_robin_across_the_permitted_sides(
        EncounterDirection allowed, int[] expected)
    {
        // The cycles are transcribed, not derived: NorthSouthEast runs N->E->S->N while
        // NorthWestEast runs W->N->E->W, which no naming rule would give you.
        var approach = new MonsterApproach(allowed, Facing.North);
        Assert.Equal(expected, expected.Select(_ => approach.Next()).ToArray());
    }

    [Fact]
    public void Any_starts_from_the_way_the_party_faces_then_cycles()
    {
        var approach = new MonsterApproach(EncounterDirection.Any, Facing.South);
        Assert.Equal([2, 3, 0, 1, 2], Enumerable.Range(0, 5).Select(_ => approach.Next()).ToArray());
    }

    [Fact]
    public void In_front_always_means_the_way_the_party_faces()
    {
        var approach = new MonsterApproach(EncounterDirection.InFront, Facing.West);
        Assert.Equal([3, 3, 3], Enumerable.Range(0, 3).Select(_ => approach.Next()).ToArray());
    }
}
