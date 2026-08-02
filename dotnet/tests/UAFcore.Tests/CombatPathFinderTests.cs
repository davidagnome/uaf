using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the combat pathfinder (<c>CPathFinder::GeneratePath</c>, <c>path.cpp:566</c>).
/// </summary>
/// <remarks>
/// The A* implementation above it in the same file is dead — <c>OLDPATH</c> is commented out at
/// <c>path.h:97</c> — so none of this covers that one.
/// </remarks>
public class CombatPathFinderTests
{
    private static CombatMap OpenMap(int width = 25, int height = 25)
    {
        var map = new CombatMap(width, height);
        map.FillHoles();
        return map;
    }

    /// <summary>Asserts a path is a legal walk from start to target.</summary>
    private static void AssertWalkable(CombatMap map, CombatPath path,
                                       int startX, int startY, int targetX, int targetY)
    {
        Assert.NotEmpty(path.Steps);

        var previous = (X: startX, Y: startY);
        foreach (var step in path.Steps)
        {
            int gap = Math.Max(Math.Abs(step.X - previous.X), Math.Abs(step.Y - previous.Y));
            Assert.True(gap == 1, $"step from {previous} to {step} is not to a neighbour");
            Assert.True(map.IsPassable(step.X, step.Y), $"step {step} is not passable");
            previous = step;
        }

        Assert.Equal((targetX, targetY), previous);
    }

    [Fact]
    public void A_path_across_open_ground_is_as_short_as_the_geometry_allows()
    {
        // Diagonals are legal and cost 15 against 10 for an orthogonal step, so two orthogonals
        // (20) never beat one diagonal. The shortest walk is therefore the Chebyshev distance.
        var map = OpenMap();
        var finder = new CombatPathFinder(map);

        var path = finder.To(5, 5, 15, 9);
        Assert.NotNull(path);
        AssertWalkable(map, path, 5, 5, 15, 9);
        Assert.Equal(10, path.StepCount);
    }

    [Fact]
    public void The_starting_square_is_not_part_of_the_path()
    {
        // The reference walks parents back and stops when it reaches the source, so the first
        // entry is the first square to move into. A caller that assumed otherwise would burn a
        // movement point standing still.
        var map = OpenMap();
        var path = new CombatPathFinder(map).To(5, 5, 8, 5);

        Assert.NotNull(path);
        Assert.DoesNotContain((5, 5), path.Steps);
        Assert.Equal((8, 5), path.Steps[^1]);
        Assert.Equal(3, path.StepCount);
    }

    [Fact]
    public void A_wall_is_routed_around_rather_than_through()
    {
        var map = OpenMap();
        for (int y = 3; y <= 12; y++)
        {
            map.SetTile(10, y, 1);          // a wall with a gap below it
        }

        var finder = new CombatPathFinder(map);
        var path = finder.To(5, 5, 15, 5);

        Assert.NotNull(path);
        AssertWalkable(map, path, 5, 5, 15, 5);
        Assert.DoesNotContain(path.Steps, s => s.X == 10 && s.Y is >= 3 and <= 12);
    }

    [Fact]
    public void A_sealed_target_has_no_route()
    {
        var map = OpenMap();
        for (int y = 8; y <= 12; y++)
        {
            for (int x = 8; x <= 12; x++)
            {
                if (x == 8 || x == 12 || y == 8 || y == 12)
                {
                    map.SetTile(x, y, 1);
                }
            }
        }

        Assert.Null(new CombatPathFinder(map).To(2, 2, 10, 10));

        // And the square just outside the box is still reachable, so the box is what blocked it.
        Assert.NotNull(new CombatPathFinder(map).To(2, 2, 13, 13));
    }

    [Fact]
    public void Being_already_there_returns_no_path_rather_than_an_empty_one()
    {
        // The reference returns the same -1 for "already there" as for "no route"
        // (path.cpp:919), so callers that care have to ask IsAlreadyWithin first.
        var map = OpenMap();
        var finder = new CombatPathFinder(map);

        Assert.Null(finder.To(5, 5, 5, 5));
        Assert.True(finder.IsAlreadyWithin(5, 5, 5, 5, 5, 5));
        Assert.False(finder.IsAlreadyWithin(5, 5, 6, 5, 6, 5));
    }

    [Fact]
    public void A_destination_rectangle_is_reached_by_touching_any_of_it()
    {
        var map = OpenMap();
        var finder = new CombatPathFinder(map);

        var path = finder.To(2, 2, destLeft: 10, destTop: 10, destRight: 14, destBottom: 14);
        Assert.NotNull(path);

        var end = path.Destination!.Value;
        Assert.InRange(end.X, 10, 14);
        Assert.InRange(end.Y, 10, 14);
    }

    [Fact]
    public void Moving_the_origin_point_demands_the_corner_not_an_overlap()
    {
        // With moveOriginPoint the rectangle collapses to its top-left, and the reference ignores
        // destRight/destBottom entirely in that mode (path.cpp:607).
        var map = OpenMap();
        var finder = new CombatPathFinder(map) { PathWidth = 2, PathHeight = 2 };

        var overlap = finder.To(2, 2, 10, 10, 14, 14, moveOriginPoint: false);
        var corner = finder.To(2, 2, 10, 10, 14, 14, moveOriginPoint: true);

        Assert.NotNull(overlap);
        Assert.NotNull(corner);
        Assert.Equal((10, 10), corner.Destination);

        // A 2x2 mover overlaps the rectangle one square earlier than its origin reaches the corner.
        Assert.True(overlap.StepCount < corner.StepCount);
    }

    [Fact]
    public void A_large_mover_will_not_fit_through_a_one_square_gap()
    {
        var map = OpenMap();
        for (int y = 0; y < 25; y++)
        {
            if (y != 12) { map.SetTile(10, y, 1); }
        }

        // A 1x1 slips through the gap; a 2x2 cannot, because its footprint would overlap the wall.
        Assert.NotNull(new CombatPathFinder(map).To(5, 12, 15, 12));
        Assert.Null(new CombatPathFinder(map) { PathWidth = 2, PathHeight = 2 }.To(5, 12, 15, 12));
    }

    [Fact]
    public void Occupants_block_a_route_unless_the_caller_says_otherwise()
    {
        var map = OpenMap();
        map.CombatantCount = 30;
        for (int y = 0; y < 25; y++)
        {
            if (y != 12) { map.SetTile(10, y, 1); }
        }
        map.Place(10, 12, combatant: 1);        // plug the one gap with a body

        Assert.Null(new CombatPathFinder(map).To(5, 12, 15, 12));
        Assert.NotNull(new CombatPathFinder(map) { OccupantsBlock = false }.To(5, 12, 15, 12));

        // Ignoring the combatant standing in the gap has the same effect as not checking at all.
        Assert.NotNull(new CombatPathFinder(map) { IgnoreCombatant = 1 }.To(5, 12, 15, 12));
    }

    [Fact]
    public void Reachability_agrees_with_an_independent_flood_fill()
    {
        // The strongest check available without an oracle: the search and a plain 8-way BFS over
        // the same passability must agree about which squares can be reached. A pathfinder that
        // gives up early is otherwise very hard to notice -- it just makes monsters idle.
        var map = OpenMap(30, 30);
        var random = new Random(20260801);
        for (int y = 0; y < 30; y++)
        {
            for (int x = 0; x < 30; x++)
            {
                if ((x, y) != (1, 1) && random.Next(100) < 28)
                {
                    map.SetTile(x, y, 1);
                }
            }
        }

        var reachable = new bool[30, 30];
        var queue = new Queue<(int X, int Y)>();
        reachable[1, 1] = true;
        queue.Enqueue((1, 1));
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if ((dx == 0 && dy == 0) || !map.Contains(nx, ny)
                        || reachable[nx, ny] || !map.IsPassable(nx, ny))
                    {
                        continue;
                    }
                    reachable[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        var finder = new CombatPathFinder(map);
        int compared = 0, blocked = 0;
        for (int y = 0; y < 30; y++)
        {
            for (int x = 0; x < 30; x++)
            {
                if ((x, y) == (1, 1)) { continue; }

                compared++;
                if (!reachable[x, y]) { blocked++; }

                var path = finder.To(1, 1, x, y);
                Assert.True(reachable[x, y] == (path is not null),
                            $"({x},{y}): flood fill says {reachable[x, y]}, finder says {path is not null}");

                if (path is not null)
                {
                    AssertWalkable(map, path, 1, 1, x, y);
                }
            }
        }

        // The maze has to be a real one, or this proves nothing.
        Assert.True(blocked > 20, $"only {blocked} of {compared} squares were unreachable");
    }
}
