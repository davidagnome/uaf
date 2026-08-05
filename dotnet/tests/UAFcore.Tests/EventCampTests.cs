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

    /// <summary>
    /// Walks to <paramref name="item"/> and commits it.
    /// </summary>
    /// <remarks>
    /// <b>Steps until it arrives rather than counting.</b> The encamp menu darkens entries now —
    /// TALK without a label, JOURNAL with nothing in it — and the menu skips over dark ones, so N
    /// presses do not advance N places. Failing here means the entry is unreachable, which is
    /// worth knowing loudly.
    /// </remarks>
    private static EventStep Choose(EventRunner runner, int item)
    {
        for (int i = 0; i < runner.Menu.Count && runner.Menu.ActiveItem != item; i++)
        {
            Press(runner, VirtualKey.Right);
        }

        Assert.Equal(item, runner.Menu.ActiveItem);
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
            TalkForActive = () => new EventRunner.TalkOption(42, "GREET", false),
            IsValidEvent = _ => true,
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        var step = Choose(runner, 7);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(42u, step.ChainTo);
    }

    [Fact]
    public void Talk_needs_a_label_as_well_as_an_event()
    {
        // The label is what the entry is renamed to (changeMenuItem(8, dude.TalkLabel)), so a
        // character with an event and no label would leave a nameless entry. The reference
        // darkens it instead, and the two conditions are one rule.
        var runner = new EventRunner
        {
            TalkEventOfActive = () => 42,
            TalkForActive = () => new EventRunner.TalkOption(42, "", false),
            IsValidEvent = _ => true,
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.False(runner.Menu.Items[7].Enabled);
    }

    [Fact]
    public void Talk_with_no_talk_event_is_dark_rather_than_doing_nothing()
    {
        // The dispatch still has its DO_NOTHING fallback and OnUpdateUI makes it unreachable, so
        // what a player sees is a dark entry. The fallback is kept because the two could disagree
        // -- a mouse click, a shortcut key -- and then the screen has to stay up.
        var runner = new EventRunner
        {
            TalkEventOfActive = () => 0,
            TalkForActive = () => new EventRunner.TalkOption(0, "GREET", false),
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.False(runner.Menu.Items[7].Enabled);
    }

    [Fact]
    public void Talk_takes_the_characters_own_word_as_its_label()
    {
        // changeMenuItem(8, dude.TalkLabel) renames the entry, so the bar shows what the design
        // wrote rather than "TALK".
        var runner = new EventRunner
        {
            TalkEventOfActive = () => 42,
            TalkForActive = () => new EventRunner.TalkOption(42, "PARLEY", false),
            IsValidEvent = _ => true,
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.Equal("PARLEY", BitmapFont.Decode(runner.Menu.Items[7].Text));
    }

    [Fact]
    public void A_character_silenced_by_its_status_cannot_talk()
    {
        // DisableTalkIfDead against a status that is not Okay -- a third condition, applied after
        // the event and the label.
        var runner = new EventRunner
        {
            TalkEventOfActive = () => 42,
            TalkForActive = () => new EventRunner.TalkOption(42, "GREET", Silenced: true),
            IsValidEvent = _ => true,
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.False(runner.Menu.Items[7].Enabled);
    }

    [Fact]
    public void Save_and_load_open_the_slot_screens_and_come_back_to_camp()
    {
        // The party menu pushed these already; camp pushes the same two, and closing one has to
        // rebuild the camp bar rather than dropping the player into a menu they never opened.
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            SaveSlotsAvailable = () => SaveSlots.Under(null),
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        Choose(runner, 0);                          // SAVE
        Assert.NotNull(runner.Slots);
        Assert.True(runner.SlotsForSaving);

        Press(runner, VirtualKey.Escape);           // the slot screen's EXIT
        Assert.Null(runner.Slots);
        Assert.Equal(12, runner.Menu.Count);
        Assert.Equal("SAVE", BitmapFont.Decode(runner.Menu.Items[0].Text));

        Choose(runner, 1);                          // LOAD
        Assert.NotNull(runner.Slots);
        Assert.False(runner.SlotsForSaving);
    }

    [Fact]
    public void A_zone_that_forbids_magic_darkens_it()
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            ZoneHere = () => new EventRunner.ZoneRules(AllowsMagic: false, AllowsResting: true),
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.False(runner.Menu.Items[3].Enabled);
        Assert.True(runner.Menu.Items[4].Enabled);
    }

    [Fact]
    public void A_no_rest_zone_darkens_FIX_but_not_a_camp_an_event_pushed()
    {
        // The asymmetry is the reference's: FIX is dark whatever pushed the camp, REST only when
        // the camp came from the world. An event that camps the party can rest them somewhere
        // they could not have chosen to.
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            ZoneHere = () => new EventRunner.ZoneRules(AllowsMagic: true, AllowsResting: false),
        };
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.False(runner.Menu.Items[6].Enabled);       // FIX
        Assert.True(runner.Menu.Items[4].Enabled);        // REST, because an event pushed this

        runner.CampPushedByEvent = false;
        runner.Begin(Camp(), Font(), Box, Anchors);

        Assert.False(runner.Menu.Items[4].Enabled);
    }

    [Theory]
    [InlineData(6, "FIX")]
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
    public void Yes_opens_the_party_menu()
    {
        // The hall has no screen of its own past the question: YES pushes MAIN_MENU_DATA, the
        // game's own top-level menu, with the hall as its parent. See EventPartyMenuTests.
        var runner = new EventRunner();
        runner.Begin(Hall(), Font(), Box, Anchors);

        var step = Choose(runner, 0);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.True(runner.PartyMenuOpen);
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
