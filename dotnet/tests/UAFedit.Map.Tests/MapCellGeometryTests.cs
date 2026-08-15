using UAFcore;
using UAFedit.Map;

namespace UAFedit.Map.Tests;

/// <summary>
/// The cell layout: which offset a side takes, and where a click lands.
/// </summary>
/// <remarks>
/// No design needed — this is the table from <c>DlgPicture.cpp:815</c> and the hit test from
/// <c>UAFWinEdView.cpp:565</c>, both of which are constants.
/// </remarks>
public class MapCellGeometryTests
{
    /// <summary>
    /// The offset table is grouped in storage order, so east is the third group and south the
    /// second.
    /// </summary>
    [Fact]
    public void The_side_groups_follow_the_wall_arrays_permutation()
    {
        Assert.Equal(0, MapCellGeometry.GroupOf(Facing.North));
        Assert.Equal(1, MapCellGeometry.GroupOf(Facing.South));
        Assert.Equal(2, MapCellGeometry.GroupOf(Facing.East));
        Assert.Equal(3, MapCellGeometry.GroupOf(Facing.West));
    }

    /// <summary>
    /// Each side's dashes sit against that side's edge — the check that would have caught reading
    /// the table as N/E/S/W.
    /// </summary>
    [Fact]
    public void Each_sides_dashes_lie_against_that_edge()
    {
        var g = MapCellGeometry.Default;

        foreach (var segment in new[] { MapSegment.Start, MapSegment.Middle, MapSegment.End })
        {
            Assert.Equal(0, g.SegmentRect(Facing.North, segment).Top);
            Assert.Equal(g.SquareHeight, g.SegmentRect(Facing.South, segment).Bottom);
            Assert.Equal(g.SquareWidth, g.SegmentRect(Facing.East, segment).Right);
            Assert.Equal(0, g.SegmentRect(Facing.West, segment).Left);
        }
    }

    /// <summary>Horizontal sides get wide flat dashes, vertical ones narrow tall dashes.</summary>
    [Fact]
    public void Dash_shape_follows_the_sides_orientation()
    {
        var g = MapCellGeometry.Default;

        var north = g.SegmentRect(Facing.North, MapSegment.Start);
        var east = g.SegmentRect(Facing.East, MapSegment.Start);

        Assert.Equal(4, north.Width);
        Assert.Equal(2, north.Height);
        Assert.Equal(2, east.Width);
        Assert.Equal(4, east.Height);
    }

    /// <summary>The three dashes of a side run in order along it and do not overlap.</summary>
    [Fact]
    public void The_three_dashes_run_along_the_side_without_overlapping()
    {
        var g = MapCellGeometry.Default;

        var (a, b, c) = (g.SegmentRect(Facing.North, MapSegment.Start),
                         g.SegmentRect(Facing.North, MapSegment.Middle),
                         g.SegmentRect(Facing.North, MapSegment.End));

        Assert.True(a.Right <= b.Left);
        Assert.True(b.Right <= c.Left);

        var (d, e, f) = (g.SegmentRect(Facing.West, MapSegment.Start),
                         g.SegmentRect(Facing.West, MapSegment.Middle),
                         g.SegmentRect(Facing.West, MapSegment.End));

        Assert.True(d.Bottom <= e.Top);
        Assert.True(e.Bottom <= f.Top);
    }

    /// <summary>The blockage mark sits inside the wall it belongs to, never outside the cell.</summary>
    [Fact]
    public void Blockage_marks_sit_inside_their_wall()
    {
        var g = MapCellGeometry.Default;
        var square = g.Square;

        foreach (var facing in LevelMapPainter.DrawOrder)
        {
            var wall = g.SegmentRect(facing, MapSegment.Middle);
            var mark = g.SegmentRect(facing, MapSegment.Obstruction);

            Assert.True(mark.Left >= square.Left && mark.Right <= square.Right);
            Assert.True(mark.Top >= square.Top && mark.Bottom <= square.Bottom);

            // Inside means towards the centre, which is a different axis per side.
            switch (facing)
            {
                case Facing.North: Assert.True(mark.Top >= wall.Bottom); break;
                case Facing.South: Assert.True(mark.Bottom <= wall.Top); break;
                case Facing.East: Assert.True(mark.Left <= wall.Left); break;
                default: Assert.True(mark.Right >= wall.Right); break;
            }
        }
    }

    /// <summary>The arrow overhangs the centre block, which is the surprising part of the table.</summary>
    [Fact]
    public void The_arrow_shares_the_centre_blocks_corner_and_not_its_size()
    {
        var g = MapCellGeometry.Default;

        Assert.Equal((g.CenterRect().Left, g.CenterRect().Top),
                     (g.ArrowRect().Left, g.ArrowRect().Top));
        Assert.True(g.ArrowRect().Width > g.CenterRect().Width);
    }

    /// <summary>A click in each quarter of the cell picks that quarter's side.</summary>
    [Theory]
    [InlineData(8, 1, Facing.North)]
    [InlineData(15, 8, Facing.East)]
    [InlineData(8, 15, Facing.South)]
    [InlineData(1, 8, Facing.West)]
    [InlineData(0, 0, Facing.North)]      // the top-left corner is on the main diagonal
    [InlineData(15, 15, Facing.East)]     // the bottom-right corner, the case the -1 fixed
    [InlineData(15, 0, Facing.North)]     // top-right: inside both diagonals, so north wins
    [InlineData(0, 15, Facing.West)]
    public void A_click_picks_the_nearest_side(int dx, int dy, Facing expected) =>
        Assert.Equal(expected, MapCellGeometry.Default.SideAt(dx, dy));

    /// <summary>
    /// The two diagonals disagree about their own boundary, which is faithful and easy to lose.
    /// </summary>
    /// <remarks>
    /// On the anti-diagonal the north/east half rounds to north and the south/west half to south;
    /// on the main diagonal past the centre the answer is east, not north, because of the
    /// <c>dy-dx-1</c> the original grew.
    /// </remarks>
    [Fact]
    public void The_diagonals_break_their_own_ties_asymmetrically()
    {
        var g = MapCellGeometry.Default;

        // dx + dy == 16 exactly, once on each side of the main diagonal.
        Assert.Equal(Facing.North, g.SideAt(10, 6));
        Assert.Equal(Facing.South, g.SideAt(6, 10));

        // dx == dy, before and after the centre.
        Assert.Equal(Facing.North, g.SideAt(4, 4));
        Assert.Equal(Facing.East, g.SideAt(12, 12));
    }

    /// <summary>Every point of the cell resolves to a side; none of the arithmetic falls through.</summary>
    [Fact]
    public void Every_point_in_the_cell_picks_some_side()
    {
        var g = MapCellGeometry.Default;
        var seen = new HashSet<Facing>();

        for (int y = 0; y < g.SquareHeight; y++)
        {
            for (int x = 0; x < g.SquareWidth; x++)
            {
                seen.Add(g.SideAt(x, y));
            }
        }

        Assert.Equal(4, seen.Count);
    }
}
