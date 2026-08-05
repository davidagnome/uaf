using UAF.Media;

namespace UAF.Media.Tests;

/// <summary>
/// Covers the journal's own paging, which is not the text box's.
/// </summary>
/// <remarks>
/// The reference gives the journal six methods of its own rather than reusing the box ones with a
/// different count, and the difference is real: these step blindly where the box methods read the
/// lines they are stepping over.
/// </remarks>
public class JournalBoxTests
{
    private static TextDisplayData WithLines(int count)
    {
        var data = new TextDisplayData();
        for (int i = 0; i < count; i++)
        {
            data.Add(new TextLine(System.Text.Encoding.ASCII.GetBytes($"line {i}"), false));
        }
        return data;
    }

    private const int Box = TextDisplayData.JournalLinesPerBox;

    [Fact]
    public void The_box_is_twenty_lines()
    {
        Assert.Equal(20, Box);
    }

    [Fact]
    public void One_short_box_is_both_the_first_and_the_last()
    {
        var data = WithLines(5);

        Assert.True(data.IsFirstJournalBox);
        Assert.True(data.IsLastJournalBox);
    }

    [Fact]
    public void Paging_steps_a_whole_box_at_a_time()
    {
        var data = WithLines(Box * 3);

        Assert.Equal(0, data.CurrentLine);
        data.NextJournalBox();
        Assert.Equal(Box, data.CurrentLine);
        data.NextJournalBox();
        Assert.Equal(Box * 2, data.CurrentLine);

        data.PrevJournalBox();
        Assert.Equal(Box, data.CurrentLine);
    }

    [Fact]
    public void The_ends_stop_rather_than_wrapping()
    {
        var data = WithLines(Box * 2);

        data.PrevJournalBox();
        Assert.Equal(0, data.CurrentLine);

        data.LastJournalBox();
        int last = data.CurrentLine;
        Assert.True(data.IsLastJournalBox);

        data.NextJournalBox();
        Assert.True(data.CurrentLine >= last);
    }

    [Fact]
    public void The_last_box_shows_the_final_lines_rather_than_starting_a_new_one()
    {
        // numLines - 20, so a journal of 25 lines opens showing lines 5 to 24 and not line 20
        // alone. The newest entry is what a player came for.
        var data = WithLines(25);
        data.LastJournalBox();

        Assert.Equal(5, data.CurrentLine);
    }

    [Fact]
    public void An_empty_journal_stays_at_the_first_line()
    {
        // The reference's LastJournalBox floors at zero and then re-tests currLine >= numLines,
        // which with no lines at all is 0 >= 0 -- putting the line back to -20. Nothing else can
        // reach that, and reading before the start of the list is what it would do.
        var data = WithLines(0);
        data.LastJournalBox();

        Assert.Equal(0, data.CurrentLine);
        Assert.True(data.IsFirstJournalBox);
        Assert.True(data.IsLastJournalBox);
    }

    [Fact]
    public void Paging_does_not_stop_at_a_wait_marker()
    {
        // The box methods stop early at a /N; the journal's do not, because what it shows is many
        // entries concatenated rather than one authored passage.
        var data = new TextDisplayData();
        for (int i = 0; i < Box * 2; i++)
        {
            data.Add(new TextLine(System.Text.Encoding.ASCII.GetBytes($"line {i}"),
                                  WaitForReturn: i == 3));
        }

        data.NextJournalBox();

        Assert.Equal(Box, data.CurrentLine);
    }
}
