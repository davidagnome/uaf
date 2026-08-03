using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the camp and training-hall screens, and the party-backup they share on close.
/// </summary>
/// <remarks>
/// Both are <b>outer</b> screens over inner ones this port has not built — camp's twelve entries
/// push six separate event classes, and the training hall's YES pushes the character-picking menu
/// that does the actual training. What is covered here is the shell and the entries that do run;
/// the rest is named rather than silently doing nothing.
/// </remarks>
public class EventCampTests
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

    private static GameEventBase Base(EventType type, int chain = 77) => new(
        new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
        new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        (int)type, 1, 0, 0, ChainEventHappen: chain, ChainEventNotHappen: 0,
        "You make camp.", "", "", []);

    private static CampEvent Camp(int forceExit = 0) =>
        new(Base(EventType.Camp), forceExit);

    private static TrainingHallEvent Hall(int forceExit = 0) =>
        new(Base(EventType.TrainingHallEvent), forceExit, [], 0);

    private static EventStep Press(EventRunner runner, VirtualKey key) =>
        runner.Handle(InputEvent.KeyDown(key));

    private static EventStep Choose(EventRunner runner, int item)
    {
        for (int i = 0; i < item; i++)
        {
            Press(runner, VirtualKey.Right);
        }
        return Press(runner, VirtualKey.Return);
    }

    // ---- camp ----------------------------------------------------------------------------------

    [Fact]
    public void The_camp_screen_offers_the_encamp_menu()
    {
        // CAMP_EVENT_DATA has no screen of its own -- it pushes ENCAMP_MENU_DATA immediately, so
        // what the player sees is entirely the inner menu.
        var runner = new EventRunner();
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.Equal(
            ["SAVE", "LOAD", "VIEW", "MAGIC", "REST", "ALTER", "FIX", "TALK", "JOURNAL", "ZAP",
             "EXIT", "QUIT"],
            runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void Exit_runs_the_camps_chain()
    {
        var runner = new EventRunner();
        runner.Begin(Camp(), Font(), Box, Anchors);

        var step = Choose(runner, EventRunner.CampExit);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(77u, step.ChainTo);
    }

    [Fact]
    public void Force_exit_asks_the_host_to_step_the_party_back()
    {
        // What forceExit actually means: not "leave immediately" but "step off the square on the
        // way out", so the party does not stand in the doorway re-triggering the event.
        var runner = new EventRunner();
        runner.Begin(Camp(forceExit: 1), Font(), Box, Anchors);
        Choose(runner, EventRunner.CampExit);

        Assert.True(runner.BackupRequested);
    }

    [Fact]
    public void A_camp_without_force_exit_leaves_the_party_where_it_is()
    {
        var runner = new EventRunner();
        runner.Begin(Camp(forceExit: 0), Font(), Box, Anchors);
        Choose(runner, EventRunner.CampExit);

        Assert.False(runner.BackupRequested);
    }

    [Fact]
    public void Zap_shows_its_debug_text_and_stays_on_screen()
    {
        var runner = new EventRunner();
        runner.Begin(Camp(), Font(), Box, Anchors);

        var step = Choose(runner, 9);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Contains("SHAZAM", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    [Fact]
    public void Talk_chains_to_the_active_characters_talk_event()
    {
        var runner = new EventRunner
        {
            TalkEventOfActive = () => 42,
            IsValidEvent = _ => true,
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        var step = Choose(runner, 7);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(42u, step.ChainTo);
    }

    [Fact]
    public void Talk_with_no_talk_event_leaves_the_player_camped()
    {
        // DO_NOTHING_EVENT again: the screen stays up rather than falling back on the chain.
        var runner = new EventRunner { TalkEventOfActive = () => 0 };
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.Equal(EventStepKind.Running, Choose(runner, 7).Kind);
        Assert.True(runner.IsActive);
    }

    [Theory]
    [InlineData(0, "SAVE")]
    [InlineData(1, "LOAD")]
    [InlineData(3, "MAGIC")]
    [InlineData(4, "REST")]
    [InlineData(5, "ALTER")]
    [InlineData(6, "FIX")]
    [InlineData(8, "JOURNAL")]
    [InlineData(11, "QUIT")]
    public void The_entries_that_push_unbuilt_screens_are_named(int item, string label)
    {
        // Named rather than silently doing nothing -- the difference is the whole signal while the
        // executor is being built out.
        var runner = new EventRunner();
        runner.Begin(Camp(), Font(), Box, Anchors);

        var step = Choose(runner, item);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Contains(label, runner.Unimplemented);
    }

    // ---- the training hall ---------------------------------------------------------------------

    [Fact]
    public void The_training_hall_asks_a_yes_no()
    {
        var runner = new EventRunner();
        runner.Begin(Hall(), Font(), Box, Anchors);

        Assert.Equal(["YES", "NO"], runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void No_backs_the_party_up_and_chains()
    {
        var runner = new EventRunner();
        runner.Begin(Hall(forceExit: 1), Font(), Box, Anchors);

        var step = Choose(runner, 1);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(77u, step.ChainTo);
        Assert.True(runner.BackupRequested);
    }

    [Fact]
    public void Yes_names_the_training_menu_this_port_has_not_built()
    {
        var runner = new EventRunner();
        runner.Begin(Hall(), Font(), Box, Anchors);

        var step = Choose(runner, 0);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Contains("TRAINING", runner.Unimplemented);
    }

    [Fact]
    public void The_backup_flag_does_not_leak_into_the_next_event()
    {
        var runner = new EventRunner();
        runner.Begin(Camp(forceExit: 1), Font(), Box, Anchors);
        Choose(runner, EventRunner.CampExit);
        Assert.True(runner.BackupRequested);

        runner.Begin(Camp(forceExit: 0), Font(), Box, Anchors);
        Assert.False(runner.BackupRequested);
    }
}
