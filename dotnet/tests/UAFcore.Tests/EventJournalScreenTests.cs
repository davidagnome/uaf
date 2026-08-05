using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Drives the journal screen through the runner — the paging, the ends, and where it goes back to.
/// </summary>
public class EventJournalScreenTests
{
    private const uint Key = 0xFF000000;

    private static readonly TextBoxMetrics Box = new(18, 328, 400, 96, 6);

    private static readonly MenuAnchors Anchors =
        new((16, 460), (200, 200), (20, 328), (16, 460));

    private static BitmapFont Font()
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (10, 16));

        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, sheetHeight, SurfaceKind.Font);
        sheet.Fill(Key);
        sheet.ColorKey = Key;

        return new BitmapFont(new FontAtlas(sheet, glyphs));
    }

    private static CampEvent Camp() =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                (int)EventType.Camp, 1, 0, 0, ChainEventHappen: 77, ChainEventNotHappen: 0,
                "You make camp.", "", "", []),
            0);

    private const int CampJournal = 8;

    /// <summary>Enough entries to fill several boxes.</summary>
    private static List<JournalEntry> Entries(int count) =>
        [.. Enumerable.Range(1, count)
             .Select(i => new JournalEntry(i, i, $"Entry {i}: something happened that day."))];

    private static EventRunner Started(IReadOnlyList<JournalEntry>? journal = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            PartyJournal = () => journal ?? [],
        };

        runner.Begin(Camp(), Font(), Box, Anchors);
        return runner;
    }

    private static void Press(EventRunner runner, VirtualKey key) =>
        runner.Handle(InputEvent.KeyDown(key));

    private static void Choose(EventRunner runner, int item)
    {
        for (int i = 0; i < runner.Menu.Count && runner.Menu.ActiveItem != item; i++)
        {
            Press(runner, VirtualKey.Right);
        }

        Assert.Equal(item, runner.Menu.ActiveItem);
        Press(runner, VirtualKey.Return);
    }

    [Fact]
    public void An_empty_journal_leaves_the_entry_dark()
    {
        var runner = Started();

        Assert.False(runner.Menu.Items[CampJournal].Enabled);
    }

    [Fact]
    public void An_entry_lights_it_up_and_opens_the_screen()
    {
        var runner = Started(Entries(3));

        Assert.True(runner.Menu.Items[CampJournal].Enabled);
        Choose(runner, CampJournal);

        Assert.NotNull(runner.JournalText);
        Assert.Equal(["NEXT", "PREV", "FIRST", "LAST", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void The_screen_opens_on_the_last_box()
    {
        // OnInitialEvent formats and then calls LastJournalBox: a player opening the journal wants
        // what just happened, not the first thing that ever did.
        var runner = Started(Entries(60));
        Choose(runner, CampJournal);

        Assert.True(runner.JournalText!.IsLastJournalBox);
        Assert.False(runner.JournalText.IsFirstJournalBox);
    }

    [Fact]
    public void Next_is_dark_at_the_end_and_prev_at_the_start()
    {
        var runner = Started(Entries(60));
        Choose(runner, CampJournal);

        Assert.False(runner.Menu.Items[JournalScreen.Next].Enabled);
        Assert.True(runner.Menu.Items[JournalScreen.Previous].Enabled);

        Choose(runner, JournalScreen.First);

        Assert.True(runner.Menu.Items[JournalScreen.Next].Enabled);
        Assert.False(runner.Menu.Items[JournalScreen.Previous].Enabled);
    }

    [Fact]
    public void First_and_last_jump_rather_than_stepping()
    {
        var runner = Started(Entries(60));
        Choose(runner, CampJournal);

        Choose(runner, JournalScreen.First);
        Assert.Equal(0, runner.JournalText!.CurrentLine);

        Choose(runner, JournalScreen.Last);
        Assert.True(runner.JournalText.IsLastJournalBox);
    }

    [Fact]
    public void Paging_steps_a_box_and_back()
    {
        var runner = Started(Entries(60));
        Choose(runner, CampJournal);
        Choose(runner, JournalScreen.First);

        Choose(runner, JournalScreen.Next);
        Assert.Equal(TextDisplayData.JournalLinesPerBox, runner.JournalText!.CurrentLine);

        Choose(runner, JournalScreen.Previous);
        Assert.Equal(0, runner.JournalText.CurrentLine);
    }

    [Fact]
    public void Exiting_puts_the_camp_menu_back()
    {
        var runner = Started(Entries(3));
        Choose(runner, CampJournal);
        Choose(runner, JournalScreen.Exit);

        Assert.Null(runner.JournalText);
        Assert.Equal(12, runner.Menu.Count);
        Assert.Equal("SAVE", BitmapFont.Decode(runner.Menu.Items[0].Text));
    }

    [Fact]
    public void Escape_exits_too()
    {
        var runner = Started(Entries(3));
        Choose(runner, CampJournal);
        Press(runner, VirtualKey.Escape);

        Assert.Null(runner.JournalText);
        Assert.Equal(12, runner.Menu.Count);
    }
}
