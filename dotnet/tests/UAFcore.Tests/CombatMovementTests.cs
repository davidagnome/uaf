using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers stepping a combatant across the grid
/// (<c>MoveCombatant</c> / <c>TakeNextStep</c>, <c>Combatant.cpp:9293</c>, <c>:4026</c>).
/// </summary>
public class CombatMovementTests
{
    private static CombatMap OpenMap(int size = 25)
    {
        var map = new CombatMap(size, size);
        map.FillHoles();
        map.CombatantCount = 8;
        return map;
    }

    private static Combatant Walker(CombatMap map, int x, int y, int index = 0,
                                    int maxMovement = 12)
    {
        var c = new Combatant(index, isFriendly: true, new CombatantIcon(1, 1), $"c{index}")
        {
            X = x,
            Y = y,
            MaxMovement = maxMovement,
        };
        map.Place(x, y, index);
        return c;
    }

    [Theory]
    [InlineData(5, 5, 5, 4, PathDirection.North)]
    [InlineData(5, 5, 5, 6, PathDirection.South)]
    [InlineData(5, 5, 6, 5, PathDirection.East)]
    [InlineData(5, 5, 4, 5, PathDirection.West)]
    [InlineData(5, 5, 4, 4, PathDirection.NorthWest)]
    [InlineData(5, 5, 6, 4, PathDirection.NorthEast)]
    [InlineData(5, 5, 4, 6, PathDirection.SouthWest)]
    [InlineData(5, 5, 6, 6, PathDirection.SouthEast)]
    [InlineData(5, 5, 5, 5, PathDirection.None)]
    public void Directions_are_read_off_the_two_coordinates(int fx, int fy, int tx, int ty,
                                                            PathDirection expected)
    {
        Assert.Equal(expected, CombatMovement.DirectionTo(fx, fy, tx, ty));
    }

    [Fact]
    public void Facing_only_ever_flips_east_or_west()
    {
        // The icon is a sprite that mirrors horizontally, so a north or south step leaves the
        // facing alone -- the reference's default arm says so in as many words.
        var map = OpenMap();
        var c = Walker(map, 5, 5);
        c.Facing = Facing.East;

        CombatMovement.Face(c, PathDirection.North);
        Assert.Equal(Facing.East, c.Facing);                    // unchanged
        Assert.Equal(PathDirection.North, c.MoveDirection);      // but the step is recorded

        CombatMovement.Face(c, PathDirection.SouthWest);
        Assert.Equal(Facing.West, c.Facing);

        CombatMovement.Face(c, PathDirection.NorthEast);
        Assert.Equal(Facing.East, c.Facing);

        CombatMovement.Face(c, PathDirection.South);
        Assert.Equal(Facing.East, c.Facing);                    // still unchanged
    }

    [Fact]
    public void An_orthogonal_step_costs_one()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5);

        Assert.Equal(MoveOutcome.Moved, CombatMovement.Step(c, map, 6, 5));
        Assert.Equal(1, c.Movement);
        Assert.Equal((6, 5), (c.X, c.Y));
        Assert.True(c.DidMove);
    }

    [Fact]
    public void Every_second_diagonal_is_free()
    {
        // Diagonals cost 2, 1, 2, 1... averaging 1.5 -- the AD&D rule made integral, and the same
        // 1.5 the pathfinder charges (15 against 10), so the two agree.
        var map = OpenMap();
        var c = Walker(map, 5, 5, maxMovement: 99);

        CombatMovement.Step(c, map, 6, 6);
        Assert.Equal(2, c.Movement);

        CombatMovement.Step(c, map, 7, 7);
        Assert.Equal(3, c.Movement);        // the second diagonal cost 1

        CombatMovement.Step(c, map, 8, 8);
        Assert.Equal(5, c.Movement);

        CombatMovement.Step(c, map, 9, 9);
        Assert.Equal(6, c.Movement);

        Assert.Equal(4, c.DiagonalMoves);
    }

    [Fact]
    public void Orthogonal_steps_do_not_disturb_the_diagonal_alternation()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5, maxMovement: 99);

        CombatMovement.Step(c, map, 6, 6);      // diagonal 1: costs 2
        CombatMovement.Step(c, map, 7, 6);      // orthogonal: costs 1
        CombatMovement.Step(c, map, 8, 7);      // diagonal 2: costs 1

        Assert.Equal(4, c.Movement);
        Assert.Equal(2, c.DiagonalMoves);
    }

    [Fact]
    public void A_combatant_out_of_movement_stays_put()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5, maxMovement: 2);

        Assert.Equal(MoveOutcome.Moved, CombatMovement.Step(c, map, 6, 5));
        Assert.Equal(MoveOutcome.Moved, CombatMovement.Step(c, map, 7, 5));
        Assert.Equal(MoveOutcome.None, CombatMovement.Step(c, map, 8, 5));

        Assert.Equal((7, 5), (c.X, c.Y));
        Assert.Equal(2, c.Movement);
    }

    [Fact]
    public void A_diagonal_that_would_overrun_the_allowance_is_refused()
    {
        // The affordability test is `spent < max - (cost - 1)`, i.e. spent + cost <= max. With one
        // point left a diagonal costing 2 does not fit, but an orthogonal does.
        var map = OpenMap();
        var c = Walker(map, 5, 5, maxMovement: 1);

        Assert.Equal(MoveOutcome.None, CombatMovement.Step(c, map, 6, 6));
        Assert.Equal((5, 5), (c.X, c.Y));

        Assert.Equal(MoveOutcome.Moved, CombatMovement.Step(c, map, 6, 5));
        Assert.Equal(1, c.Movement);
    }

    [Fact]
    public void A_wall_stops_the_step_without_spending_movement()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5);
        map.SetTile(6, 5, 1);

        Assert.Equal(MoveOutcome.None, CombatMovement.Step(c, map, 6, 5));
        Assert.Equal((5, 5), (c.X, c.Y));
        Assert.Equal(0, c.Movement);
    }

    [Fact]
    public void Stepping_into_somebody_attacks_them_instead()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5);
        map.Place(6, 5, combatant: 3);

        var outcome = CombatMovement.Step(c, map, 6, 5, canAttack: (_, _) => true);

        Assert.Equal(MoveOutcome.Attacked, outcome);
        Assert.Equal(3, c.Target);
        Assert.Equal(CombatantState.Attacking, c.State);
        Assert.Equal((5, 5), (c.X, c.Y));       // did not move into the square
        Assert.Equal(0, c.Movement);
    }

    [Fact]
    public void Somebody_in_the_way_who_cannot_be_attacked_just_blocks()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5);
        map.Place(6, 5, combatant: 3);

        Assert.Equal(MoveOutcome.None,
                     CombatMovement.Step(c, map, 6, 5, canAttack: (_, _) => false));
        Assert.Equal(CombatMap.NoDude, c.Target);
    }

    [Fact]
    public void With_no_movement_left_an_attack_is_still_allowed_into_an_occupied_square()
    {
        // That is what allowZeroMoveAttack is for: a combatant that has walked up to an enemy and
        // run out of movement can still swing.
        var map = OpenMap();
        var c = Walker(map, 5, 5, maxMovement: 0);
        map.Place(6, 5, combatant: 3);

        Assert.Equal(MoveOutcome.None, CombatMovement.Step(c, map, 6, 5,
                                                           canAttack: (_, _) => true));

        Assert.Equal(MoveOutcome.Attacked,
                     CombatMovement.Step(c, map, 6, 5, allowZeroMoveAttack: true,
                                         canAttack: (_, _) => true));
    }

    [Fact]
    public void Stepping_off_the_map_is_fleeing()
    {
        // Not a failed move: the reference sets Fled, bumps the flee counter and ends the turn.
        // A caller treating this as an error loses the only way out of a fight.
        var map = OpenMap();
        var c = Walker(map, 0, 5);

        Assert.Equal(MoveOutcome.Fled, CombatMovement.Step(c, map, -1, 5));
        Assert.Equal(CharacterStatus.Fled, c.Status);
        Assert.True(c.TurnIsDone);
        Assert.False(c.IsOnCombatMap());
    }

    [Fact]
    public void Stepping_where_you_already_are_does_nothing()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5);

        Assert.Equal(MoveOutcome.None, CombatMovement.Step(c, map, 5, 5));
        Assert.Equal(0, c.Movement);
        Assert.Equal(0, c.DiagonalMoves);
    }

    [Fact]
    public void The_grid_follows_the_combatant()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5, index: 2);
        Assert.Equal(2, map.OccupantAt(5, 5));

        CombatMovement.Step(c, map, 6, 5);

        Assert.Equal(CombatMap.NoDude, map.OccupantAt(5, 5));
        Assert.Equal(2, map.OccupantAt(6, 5));
    }

    [Fact]
    public void A_path_is_walked_down_to_empty()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5, maxMovement: 99);
        var path = new List<(int X, int Y)> { (6, 5), (7, 5), (8, 5) };

        Assert.Equal(MoveOutcome.Moved, CombatMovement.TakeNextStep(c, map, path));
        Assert.Equal(2, path.Count);

        while (path.Count > 0)
        {
            Assert.Equal(MoveOutcome.Moved, CombatMovement.TakeNextStep(c, map, path));
        }

        Assert.Equal((8, 5), (c.X, c.Y));
        Assert.Equal(MoveOutcome.None, CombatMovement.TakeNextStep(c, map, path));
    }

    [Fact]
    public void A_route_found_by_the_pathfinder_can_be_walked_to_its_end()
    {
        // The two halves together: the search produces a route and movement consumes it. Both
        // sides existed before this; nothing had ever joined them up.
        var map = OpenMap(30);
        for (int y = 3; y <= 20; y++)
        {
            map.SetTile(12, y, 1);          // a wall with a gap below it
        }

        var c = Walker(map, 5, 10, maxMovement: 99);
        var route = new CombatPathFinder(map) { IgnoreCombatant = c.Index }.To(5, 10, 20, 10);
        Assert.NotNull(route);

        var remaining = route.Steps.ToList();
        while (remaining.Count > 0)
        {
            var outcome = CombatMovement.TakeNextStep(c, map, remaining);
            Assert.Equal(MoveOutcome.Moved, outcome);
        }

        Assert.Equal((20, 10), (c.X, c.Y));
        Assert.Equal(c.Index, map.OccupantAt(20, 10));

        // The route went round the wall, so it cost more than the 15 squares of straight line.
        Assert.True(c.Movement > 15, $"walked the route for only {c.Movement} points");
    }

    [Fact]
    public void A_new_round_gives_the_allowance_back()
    {
        var map = OpenMap();
        var c = Walker(map, 5, 5, maxMovement: 3);
        c.TurnIsDone = true;

        CombatMovement.Step(c, map, 6, 6);
        Assert.Equal(2, c.Movement);
        Assert.Equal(1, c.DiagonalMoves);

        c.TurnIsDone = true;
        c.BeginRound(attacksThisRound: 1);

        Assert.Equal(0, c.Movement);
        Assert.Equal(0, c.DiagonalMoves);
    }
}
