using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The two line-of-sight walks, and the two calls built on them.
/// </summary>
/// <remarks>
/// Drawn by hand rather than taken from a design: the interesting cases are a wall in exactly the
/// wrong place, and no shipped map is guaranteed to have one.
/// </remarks>
public class GpdlLineOfSightTests
{
    /// <summary>
    /// A map drawn as rows of text: <c>.</c> is clear floor, <c>#</c> is wall, <c>' '</c> is a
    /// square with no terrain at all.
    /// </summary>
    /// <remarks>
    /// <b>The blank is not the same as the floor</b>, and that is the point of having it: the two
    /// walks disagree about a square with no terrain in it, so a grid that only had walls and
    /// floors could not show the difference.
    /// </remarks>
    private sealed class Grid(params string[] rows) : IGpdlSightMap
    {
        public bool Contains(int x, int y) =>
            y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length;

        public bool HasTerrain(int x, int y) => Contains(x, y) && rows[y][x] != ' ';

        public bool SeeThrough(int x, int y) => Contains(x, y) && rows[y][x] == '.';
    }

    /// <summary>An open floor with nothing in the way.</summary>
    private static Grid Open() => new(
        ".....",
        ".....",
        ".....",
        ".....",
        ".....");

    /// <summary>A clear line is clear, in every direction.</summary>
    [Theory]
    [InlineData(0, 0, 4, 0)]
    [InlineData(4, 0, 0, 0)]
    [InlineData(0, 0, 0, 4)]
    [InlineData(0, 4, 0, 0)]
    [InlineData(0, 0, 4, 4)]
    [InlineData(4, 4, 0, 0)]
    [InlineData(4, 0, 0, 4)]
    [InlineData(0, 4, 4, 0)]
    [InlineData(0, 0, 4, 1)]
    [InlineData(0, 0, 1, 4)]
    public void An_open_floor_is_clear_in_every_direction(int x0, int y0, int x1, int y1)
    {
        Assert.True(GpdlLineOfSight.IsClear(Open(), x0, y0, x1, y1));
        Assert.True(GpdlLineOfSight.HasVisibility(Open(), x0, y0, x1, y1));
    }

    /// <summary>A wall across the middle blocks both walks.</summary>
    [Fact]
    public void A_wall_between_two_points_blocks_both_walks()
    {
        var grid = new Grid(
            ".....",
            ".....",
            "#####",
            ".....",
            ".....");

        Assert.False(GpdlLineOfSight.IsClear(grid, 2, 0, 2, 4));
        Assert.False(GpdlLineOfSight.HasVisibility(grid, 2, 0, 2, 4));

        // And along a row that avoids it, both are clear.
        Assert.True(GpdlLineOfSight.IsClear(grid, 0, 1, 4, 1));
        Assert.True(GpdlLineOfSight.HasVisibility(grid, 0, 1, 4, 1));
    }

    /// <summary>
    /// Both endpoints are skipped, so a combatant standing in a wall can still see out.
    /// </summary>
    /// <remarks>
    /// The Bresenham walk tests neither end. That is not an oversight — a combatant occupies its
    /// own square, and testing it would make everybody blind.
    /// </remarks>
    [Fact]
    public void The_endpoints_are_not_tested()
    {
        var grid = new Grid(
            "#....",
            ".....",
            "....#");

        Assert.True(GpdlLineOfSight.HasVisibility(grid, 0, 0, 4, 2));
    }

    /// <summary>
    /// The two walks disagree about a square with no terrain in it.
    /// </summary>
    /// <remarks>
    /// <b>The clearest sign they are different algorithms and not one refactored twice.</b> The
    /// octant walk only blocks on a square it can positively identify as opaque, so an empty square
    /// is clear; the Bresenham walk requires a terrain index of at least one, so an empty square is
    /// blocked. A map with blank squares between two combatants is fully sighted by one and fully
    /// blind by the other.
    /// </remarks>
    [Fact]
    public void The_two_walks_disagree_about_an_empty_square()
    {
        var grid = new Grid(
            ".....",
            "     ",
            ".....");

        Assert.True(GpdlLineOfSight.IsClear(grid, 2, 0, 2, 2));
        Assert.False(GpdlLineOfSight.HasVisibility(grid, 2, 0, 2, 2));
    }

    /// <summary>
    /// And about a square off the map.
    /// </summary>
    /// <remarks>
    /// The octant walk starts its flags at zero and only sets them for a square it can read, so
    /// off-map reads as clear. <c>HaveVisibility</c> returns false for anything out of bounds.
    /// </remarks>
    [Fact]
    public void The_two_walks_disagree_about_a_square_off_the_map()
    {
        var grid = new Grid("...");

        // A line that leaves the map and comes back.
        Assert.True(GpdlLineOfSight.IsClear(grid, 0, 0, 2, 0));
        Assert.True(GpdlLineOfSight.IsClear(grid, -3, 0, 5, 0));

        Assert.False(GpdlLineOfSight.HasVisibility(grid, -3, 0, 5, 0));
    }

    /// <summary>
    /// The octant walk tests both squares a diagonal passes between.
    /// </summary>
    /// <remarks>
    /// <b>So a diagonal cannot slip through the corner between two walls</b> — the thing a naive
    /// single-square Bresenham lets through, and the reason the engine has the second algorithm at
    /// all.
    /// </remarks>
    [Fact]
    public void A_diagonal_does_not_slip_between_two_corners()
    {
        // Walls at (1,0) and (0,1): the diagonal from (0,0) to (2,2) passes BETWEEN them without
        // ever entering either, so nothing is on the line itself.
        var grid = new Grid(
            ".#.",
            "#..",
            "...");

        Assert.False(GpdlLineOfSight.IsClear(grid, 0, 0, 2, 2));

        // The same diagonal on an open floor is clear, so it is the pinch and not the direction.
        Assert.True(GpdlLineOfSight.IsClear(new Grid("...", "...", "..."), 0, 0, 2, 2));

        // And this is exactly what the other walk lets through: its Bresenham line steps from
        // (0,0) to (1,1) to (2,2) and never looks at either wall. Both endpoints are skipped, so
        // the only square it tests is the clear one in the middle.
        Assert.True(GpdlLineOfSight.HasVisibility(grid, 0, 0, 2, 2));
    }

    /// <summary>A point sees itself.</summary>
    [Fact]
    public void A_point_sees_itself()
    {
        Assert.True(GpdlLineOfSight.IsClear(Open(), 2, 2, 2, 2));
        Assert.True(GpdlLineOfSight.HasVisibility(Open(), 2, 2, 2, 2));
    }

    /// <summary>Sight is symmetric — the octant decomposition is what makes it so.</summary>
    [Fact]
    public void Sight_is_symmetric()
    {
        var grid = new Grid(
            ".....",
            "..#..",
            ".....",
            "..#..",
            ".....");

        for (int y0 = 0; y0 < 5; y0++)
        {
            for (int x0 = 0; x0 < 5; x0++)
            {
                for (int y1 = 0; y1 < 5; y1++)
                {
                    for (int x1 = 0; x1 < 5; x1++)
                    {
                        Assert.Equal(GpdlLineOfSight.IsClear(grid, x0, y0, x1, y1),
                                     GpdlLineOfSight.IsClear(grid, x1, y1, x0, y0));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The distance is truncated, not rounded.
    /// </summary>
    /// <remarks>
    /// The reference assigns a <c>float</c> square root to an <c>int</c>, so a diagonal of two
    /// squares — 2.83 — reads as 2, and a script comparing against a radius is comparing against a
    /// slightly generous one.
    /// </remarks>
    [Theory]
    [InlineData(0, 0, 3, 0, 3)]
    [InlineData(0, 0, 0, 4, 4)]
    [InlineData(0, 0, 2, 2, 2)]
    [InlineData(0, 0, 3, 4, 5)]
    [InlineData(2, 2, 2, 2, 0)]
    public void The_distance_is_truncated(int x0, int y0, int x1, int y1, int expected)
    {
        var grid = new Grid(
            ".....",
            ".....",
            ".....",
            ".....",
            ".....");

        Assert.Equal(expected, GpdlLineOfSight.Distance(grid, x0, y0, x1, y1));
    }

    /// <summary>
    /// A blocked line answers a very large number, not a negative one.
    /// </summary>
    /// <remarks>
    /// So a script asking "is this closer than N?" answers no for an unseen target, which is the
    /// useful default — and one adding distances up gets nonsense.
    /// </remarks>
    [Fact]
    public void A_blocked_line_answers_a_very_large_number()
    {
        var grid = new Grid(
            ".....",
            "#####",
            ".....");

        Assert.Equal(999999, GpdlLineOfSight.NotVisible);
        Assert.Equal(GpdlLineOfSight.NotVisible,
                     GpdlLineOfSight.Distance(grid, 2, 0, 2, 2));
    }

    /// <summary>The call reaches the host with its four coordinates in order.</summary>
    /// <remarks>
    /// <b><c>$IsLineOfSight</c> declares FIVE parameters and the reference reads four.</b> The
    /// fifth is popped and never looked at, so it has no meaning at all — but it still has to be
    /// written, and it still has to be popped or the stack is left one deep.
    /// </remarks>
    [Fact]
    public void The_call_reaches_the_host_with_four_of_its_five_arguments()
    {
        var host = new SightHost();
        var compiler = new GpdlCompiler();

        Assert.True(compiler.Compile(
            """$PUBLIC $FUNC f() { $RETURN $IsLineOfSight("1", "2", "3", "4", "99"); } f;""") == 0,
            string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);

        Assert.NotEqual(string.Empty, vm.Execute("f"));
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        Assert.Equal((1, 2, 3, 4), host.Asked);
    }

    /// <summary>And the fifth argument is required, however meaningless.</summary>
    [Fact]
    public void The_meaningless_fifth_argument_is_still_required()
    {
        var compiler = new GpdlCompiler();

        Assert.NotEqual(0, compiler.Compile(
            """$PUBLIC $FUNC f() { $RETURN $IsLineOfSight("1", "2", "3", "4"); } f;"""));
    }

    private sealed class SightHost : GpdlUnhostedEnvironment
    {
        public (int X0, int Y0, int X1, int Y1) Asked { get; private set; }

        public override bool IsLineOfSight(int x0, int y0, int x1, int y1)
        {
            Asked = (x0, y0, x1, y1);
            return true;
        }
    }
}
