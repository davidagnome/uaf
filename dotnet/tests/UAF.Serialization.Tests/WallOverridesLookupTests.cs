using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reading one square out of a level's override table.
/// </summary>
/// <remarks>
/// <b>Built by hand rather than read from the corpus, deliberately.</b> A level only carries this
/// table from design version 5.0, and neither corpus design is that new — so a test that looked for
/// a shipped override would find none and pass without asserting anything. The sparseness this
/// covers is real all the same: it is what <c>$GetWall</c> and its four siblings read through.
/// </remarks>
public class WallOverridesLookupTests
{
    /// <summary>A cell with one value set, and the rest left as "nothing here".</summary>
    private static CellOverride Cell(params (int Kind, int Facing, byte Value)[] set)
    {
        var bytes = new byte[CellOverride.Size];
        Array.Fill(bytes, (byte)255);

        foreach (var (kind, facing, value) in set)
        {
            bytes[(kind * CellOverride.Facings) + facing] = value;
        }

        return new CellOverride(bytes);
    }

    /// <summary>A table with a single populated row.</summary>
    private static WallOverrides Table(int rowNumber, params CellOverride[] cells) =>
        new([new WallOverrideEntry(rowNumber, new RowOverrides(cells.Length, [.. cells]))]);

    /// <summary>A square that was set reads back, for the right layer and side.</summary>
    [Fact]
    public void A_set_square_reads_back()
    {
        var table = Table(3, Cell(), Cell((2, 1, 40)));

        Assert.Equal((byte)40, table.At(2, 1, 3, 1));

        // Not the neighbouring layer, side, column or row.
        Assert.Null(table.At(3, 1, 3, 1));
        Assert.Null(table.At(2, 1, 3, 2));
        Assert.Null(table.At(2, 0, 3, 1));
        Assert.Null(table.At(2, 1, 4, 1));
    }

    /// <summary>
    /// A row the table does not carry is a miss, not an error.
    /// </summary>
    /// <remarks>
    /// The table is sparse by row: a level 30 squares tall may carry two rows. Every other row
    /// reads as nothing.
    /// </remarks>
    [Fact]
    public void An_absent_row_is_a_miss()
    {
        var table = Table(3, Cell((0, 0, 7)));

        Assert.Equal((byte)7, table.At(0, 0, 3, 0));
        Assert.Null(table.At(0, 0, 0, 0));
        Assert.Null(table.At(0, 0, 99, 0));
    }

    /// <summary>
    /// A present row can be shorter than the level is wide, and reading past its end is a miss.
    /// </summary>
    /// <remarks>
    /// <b>The sparseness that is easy to miss.</b> <c>SetMapOverride</c> grows a row only as far as
    /// the furthest column anyone has written, so a row on a 20-wide level may hold three cells. An
    /// implementation that indexed by x without checking the row's own length would read off the
    /// end of the array.
    /// </remarks>
    [Fact]
    public void Reading_past_a_short_rows_end_is_a_miss()
    {
        var table = Table(0, Cell((0, 0, 5)), Cell((0, 0, 6)));

        Assert.Equal((byte)5, table.At(0, 0, 0, 0));
        Assert.Equal((byte)6, table.At(0, 1, 0, 0));

        // The row holds two cells; the third is not there.
        Assert.Null(table.At(0, 2, 0, 0));
        Assert.Null(table.At(0, 500, 0, 0));

        // And a negative column does not index backwards into the array.
        Assert.Null(table.At(0, -1, 0, 0));
    }

    /// <summary>
    /// A row written as absent carries no cells and reads as nothing.
    /// </summary>
    /// <remarks>
    /// An entry whose row number is -1 is a placeholder that keeps its position on the wire. It is
    /// in <c>Entries</c> but must not appear in <c>Rows</c>, or a lookup would find a row with no
    /// cells behind it.
    /// </remarks>
    [Fact]
    public void An_absent_entry_is_not_a_row()
    {
        var table = new WallOverrides([
            new WallOverrideEntry(-1, null),
            new WallOverrideEntry(1, new RowOverrides(1, [Cell((0, 0, 9))])),
        ]);

        Assert.Single(table.Rows);
        Assert.Equal((byte)9, table.At(0, 0, 1, 0));
        Assert.Null(table.At(0, 0, -1, 0));
    }

    /// <summary>
    /// 255 is "nothing here", not the value 255.
    /// </summary>
    /// <remarks>
    /// <c>Clear()</c> memsets a cell to <c>0xFF</c>, so an untouched square is all 255s. A reader
    /// that treated it as a value would put picture 255 on every wall of the level.
    /// </remarks>
    [Fact]
    public void The_sentinel_is_not_a_value()
    {
        var table = Table(0, Cell((0, 0, 255), (0, 1, 254)));

        Assert.Null(table.At(0, 0, 0, 0));

        // One below it is an ordinary value, so the sentinel is exact rather than a threshold.
        Assert.Equal((byte)254, table.At(0, 0, 0, 1));
    }
}
