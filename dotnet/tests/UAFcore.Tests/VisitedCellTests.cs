using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers which squares the party has stood on — what an automap draws and what a savegame
/// carries.
/// </summary>
public class VisitedCellTests
{
    [Fact]
    public void Nothing_is_visited_to_begin_with()
    {
        var visited = new VisitedCells();

        Assert.False(visited.IsVisited(0, 5, 5));
        Assert.Equal(0, visited.CountOn(0));
        Assert.Empty(visited.ToRecords());
    }

    [Fact]
    public void A_visited_square_stays_visited_and_its_neighbours_do_not()
    {
        var visited = new VisitedCells();
        visited.SetVisited(0, 5, 5);

        Assert.True(visited.IsVisited(0, 5, 5));
        Assert.False(visited.IsVisited(0, 6, 5));
        Assert.False(visited.IsVisited(0, 5, 6));
        Assert.Equal(1, visited.CountOn(0));
    }

    [Fact]
    public void The_bitmaps_are_per_level()
    {
        var visited = new VisitedCells();
        visited.SetVisited(3, 5, 5);

        Assert.True(visited.IsVisited(3, 5, 5));
        Assert.False(visited.IsVisited(4, 5, 5));
    }

    [Fact]
    public void The_row_stride_is_the_formats_width_not_the_levels()
    {
        // The bitmap is always 100 wide because SetVisited allocates a fixed TAG_LIST_2D without
        // asking the level how big it is. Getting the stride wrong wraps rows into each other,
        // which looks like a working automap with ghosts on it -- so this asserts on the bit that
        // moves rather than on a neighbouring square, which would pass under several strides.
        var visited = new VisitedCells();
        visited.SetVisited(0, 0, 1);

        // (0,1) is flat index 100, which is bit 4 of byte 12.
        byte[] bitmap = visited.ToRecords()[0].Bitmap;
        Assert.Equal(0b0001_0000, bitmap[12]);
        Assert.All(bitmap.Where((_, i) => i != 12), b => Assert.Equal(0, b));
        Assert.Equal(1, visited.CountOn(0));
    }

    [Fact]
    public void Every_square_of_a_level_can_be_marked()
    {
        var visited = new VisitedCells();
        for (int y = 0; y < VisitedCells.Height; y++)
        {
            for (int x = 0; x < VisitedCells.Width; x++)
            {
                visited.SetVisited(0, x, y);
            }
        }

        Assert.Equal(VisitedCells.Width * VisitedCells.Height, visited.CountOn(0));
    }

    // ---- the edges -----------------------------------------------------------------------------

    [Fact]
    public void A_square_off_the_map_reads_as_visited_on_a_level_that_has_been_entered()
    {
        // "outside boundaries is tagged" -- which keeps the border from drawing as unexplored.
        var visited = new VisitedCells();
        visited.SetVisited(0, 5, 5);

        Assert.True(visited.IsVisited(0, -1, 5));
        Assert.True(visited.IsVisited(0, VisitedCells.Width, 5));
        Assert.True(visited.IsVisited(0, 5, VisitedCells.Height));
    }

    [Fact]
    public void The_same_square_off_the_map_reads_as_unvisited_on_a_level_that_has_not()
    {
        // IsVisited checks for a missing bitmap first, so the out-of-bounds rule never runs. Both
        // answers are the reference's, and they disagree with each other.
        var visited = new VisitedCells();

        Assert.False(visited.IsVisited(0, -1, 5));
    }

    [Fact]
    public void Marking_off_the_map_still_creates_the_levels_bitmap()
    {
        // SetVisited allocates before it range-checks, so the level ends up in a savegame with an
        // empty bitmap rather than absent.
        var visited = new VisitedCells();
        visited.SetVisited(0, -1, -1);

        Assert.Equal(0, visited.CountOn(0));
        Assert.Single(visited.ToRecords());
    }

    [Fact]
    public void Level_255_cannot_hold_visited_squares()
    {
        // VISIT_DATA is a fixed array tested with level >= MAX_LEVELS, while EVENT_TRIGGER_DATA is
        // a CArray that grows -- which is what lets global events record at 255 and means nothing
        // can ever mark a square visited there.
        var visited = new VisitedCells();
        visited.SetVisited(EventTriggerFlags.GlobalLevel, 5, 5);

        Assert.False(visited.IsVisited(EventTriggerFlags.GlobalLevel, 5, 5));
        Assert.Empty(visited.ToRecords());
    }

    [Fact]
    public void A_negative_level_is_ignored()
    {
        var visited = new VisitedCells();
        visited.SetVisited(-1, 5, 5);

        Assert.False(visited.IsVisited(-1, 5, 5));
        Assert.Empty(visited.ToRecords());
    }

    // ---- projecting to and from a savegame ------------------------------------------------------

    [Fact]
    public void A_bitmap_is_one_byte_longer_than_the_squares_need()
    {
        // TAG_LIST's constructor adds the +1 unconditionally, so a writer that computed a tight
        // size would be one byte short of what the reader expects.
        Assert.Equal(1251, VisitedCells.BitmapBytes);

        var visited = new VisitedCells();
        visited.SetVisited(0, 0, 0);

        Assert.Equal(VisitedCells.BitmapBytes, visited.ToRecords()[0].Bitmap.Length);
    }

    [Fact]
    public void The_records_are_sparse_and_carry_their_own_level_numbers()
    {
        // Unlike the trigger flags, nothing here is positional: VISIT_DATA writes a (level, count)
        // pair per slot and the reader drops the empty ones.
        var visited = new VisitedCells();
        visited.SetVisited(7, 1, 1);

        var records = visited.ToRecords();

        Assert.Single(records);
        Assert.Equal(7, records[0].Level);
    }

    [Fact]
    public void The_squares_survive_a_round_trip_through_the_savegame_shape()
    {
        var before = new VisitedCells();
        before.SetVisited(0, 0, 0);
        before.SetVisited(0, 99, 99);
        before.SetVisited(2, 40, 17);

        var after = VisitedCells.FromRecords(before.ToRecords());

        Assert.True(after.IsVisited(0, 0, 0));
        Assert.True(after.IsVisited(0, 99, 99));
        Assert.True(after.IsVisited(2, 40, 17));
        Assert.False(after.IsVisited(0, 40, 17));
        Assert.Equal(2, after.CountOn(0));
        Assert.Equal(1, after.CountOn(2));
    }

    [Fact]
    public void A_short_bitmap_loses_the_squares_it_cannot_describe_not_the_level()
    {
        var records = new List<VisitedLevel> { new(0, [0b0000_0001]) };

        var visited = VisitedCells.FromRecords(records);

        Assert.True(visited.IsVisited(0, 0, 0));
        Assert.Equal(1, visited.CountOn(0));
    }

    [Fact]
    public void A_record_naming_an_impossible_level_is_dropped()
    {
        var records = new List<VisitedLevel> { new(VisitedCells.MaxLevels, [1]) };

        Assert.Empty(VisitedCells.FromRecords(records).ToRecords());
    }

    // ---- what this unblocks ---------------------------------------------------------------------

    [Fact]
    public void Visited_squares_are_no_longer_on_the_list_of_what_a_save_cannot_carry()
    {
        Assert.DoesNotContain("visited squares", SaveGameProjection.Untracked);
        Assert.Contains("the journal", SaveGameProjection.Untracked);
    }
}
