using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the text the journal screen shows.</summary>
public class JournalScreenTests
{
    private static JournalEntry Entry(string text, int key = 1) => new(key, key, text);

    [Fact]
    public void An_empty_journal_produces_nothing_at_all()
    {
        // Not an empty line: FormatJournalText returns before formatting when the count is zero.
        Assert.Equal(string.Empty, JournalScreen.Text([]));
    }

    [Fact]
    public void One_entry_is_itself_with_no_separator()
    {
        Assert.Equal("A dwarf sold us a map.",
                     JournalScreen.Text([Entry("A dwarf sold us a map.")]));
    }

    [Fact]
    public void Entries_are_joined_by_a_colour_reset_and_a_blank_line()
    {
        Assert.Equal("first" + JournalScreen.Separator + "second",
                     JournalScreen.Text([Entry("first"), Entry("second", 2)]));

        // The \b is the journal's own colour tag, cleared between entries so one cannot leak its
        // colour into the next.
        Assert.Equal("\b\n\n", JournalScreen.Separator);
    }

    [Fact]
    public void An_empty_entry_is_skipped_but_still_counted()
    {
        // count only advances for entries that had text; the separator goes on while
        // count < GetCount(), which is the whole list. So a journal ending in empty entries puts
        // a separator after its last real one.
        string text = JournalScreen.Text([Entry("first"), Entry("", 2)]);

        Assert.Equal("first" + JournalScreen.Separator, text);
    }

    [Fact]
    public void An_empty_entry_between_two_real_ones_leaves_one_separator_each()
    {
        string text = JournalScreen.Text([Entry("first"), Entry("", 2), Entry("third", 3)]);

        Assert.Equal("first" + JournalScreen.Separator + "third" + JournalScreen.Separator, text);
    }

    [Fact]
    public void The_menu_is_the_five_the_screen_offers()
    {
        Assert.Equal(["NEXT", "PREV", "FIRST", "LAST", "EXIT"],
                     JournalScreen.Menu.Select(m => m.Label));
        Assert.Equal(4, JournalScreen.Exit);
    }
}
