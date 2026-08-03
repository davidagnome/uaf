using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the party menu the training hall pushes over itself — which entries light up, how the
/// keys are split, and what TRAIN does.
/// </summary>
/// <remarks>
/// <b>This is the game's own top-level menu, borrowed.</b> The same twelve entries run at startup
/// with no parent; the difference is entirely in what is selectable, so the enable rules are the
/// substance of the screen rather than a detail of it.
/// </remarks>
public class EventPartyMenuTests
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

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static TrainingHallEvent Hall(int forceExit = 0) =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                NoPic, NoPic, (int)EventType.TrainingHallEvent, 1, 0, 0,
                ChainEventHappen: 55, ChainEventNotHappen: 0, "Train here?", "", "", []),
            forceExit, [new TrainableBaseclass("fighter", 1, 20, "")], Cost: 100);

    /// <summary>The training hall's own menu is YES / NO; YES is 0.</summary>
    private const int HallYes = 0;

    private static EventRunner Started(bool canTrain = true,
                                       Func<TrainingHallEvent, TrainingOutcome>? train = null,
                                       Action? onTab = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            CanTrainHere = _ => canTrain,
            ApplyTraining = train ?? (_ => TrainingOutcome.Refused(TrainingRefusal.NotReady)),
            TabParty = onTab ?? (() => { }),
        };
        runner.Begin(Hall(), Font(), Box, Anchors);
        return runner;
    }

    /// <summary>
    /// Walks the menu to <paramref name="item"/> and commits it.
    /// </summary>
    /// <remarks>
    /// <b>Steps until it arrives rather than counting.</b> This is the first screen with disabled
    /// entries, and the menu skips them — so N presses do not advance N places, and a helper that
    /// counts lands somewhere else entirely. Failing here means the entry is unreachable, which
    /// is worth knowing loudly rather than asserting against whatever was selected instead.
    /// </remarks>
    private static EventStep Choose(EventRunner runner, int item)
    {
        // The party menu is vertical, so it moves on Down rather than Right.
        var key = runner.PartyMenuOpen ? VirtualKey.Down : VirtualKey.Right;

        for (int i = 0; i < runner.Menu.Count && runner.Menu.ActiveItem != item; i++)
        {
            runner.Handle(InputEvent.KeyDown(key));
        }

        Assert.Equal(item, runner.Menu.ActiveItem);
        return runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
    }

    private static string[] Labels(EventRunner runner) =>
        [.. runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text))];

    private const int Train = 3;
    private const int ChangeClass = 4;
    private const int View = 5;
    private const int Begin = 10;
    private const int Exit = 11;

    [Fact]
    public void Saying_yes_at_the_hall_opens_the_party_menu()
    {
        var runner = Started();
        Choose(runner, HallYes);

        Assert.True(runner.PartyMenuOpen);
        Assert.Equal(12, runner.Menu.Count);
        Assert.Equal("ADD CHARACTER", Labels(runner)[0]);
        Assert.Equal("EXIT FROM GAME", Labels(runner)[Exit]);
    }

    [Fact]
    public void The_live_table_leads_with_ADD_not_CREATE()
    {
        // Two twelve-entry lists sit in the source, one commented "original order". The commented
        // one leads with CREATE; the live one does not.
        var runner = Started();
        Choose(runner, HallYes);

        Assert.Equal("ADD CHARACTER", Labels(runner)[0]);
        Assert.Equal("CREATE CHARACTER", Labels(runner)[6]);
    }

    [Fact]
    public void Train_lights_up_only_when_this_hall_will_teach_this_character()
    {
        var willing = Started(canTrain: true);
        Choose(willing, HallYes);
        Assert.True(willing.Menu.Items[Train].Enabled);

        var wont = Started(canTrain: false);
        Choose(wont, HallYes);
        Assert.False(wont.Menu.Items[Train].Enabled);
    }

    [Fact]
    public void Change_class_is_dark_because_the_port_cannot_answer_it()
    {
        var runner = Started();
        Choose(runner, HallYes);

        Assert.False(runner.Menu.Items[ChangeClass].Enabled);
    }

    [Fact]
    public void A_dark_entry_cannot_be_selected()
    {
        // The menu skips disabled entries when moving, so CHANGE CLASS is unreachable rather
        // than merely inert.
        var runner = Started(canTrain: false);
        Choose(runner, HallYes);

        for (int i = 0; i < 20; i++)
        {
            runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
            Assert.NotEqual(ChangeClass, runner.Menu.ActiveItem);
            Assert.NotEqual(Train, runner.Menu.ActiveItem);
        }
    }

    [Fact]
    public void The_menu_takes_the_vertical_keys_and_the_party_takes_the_horizontal_ones()
    {
        // VMenuHPartyKeyboardAction -- the mirror image of the inventory, which splits them the
        // other way round.
        int tabs = 0;
        var runner = Started(onTab: () => tabs++);
        Choose(runner, HallYes);

        int before = runner.Menu.ActiveItem;
        runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
        Assert.NotEqual(before, runner.Menu.ActiveItem);
        Assert.Equal(0, tabs);

        int afterDown = runner.Menu.ActiveItem;
        runner.Handle(InputEvent.KeyDown(VirtualKey.Right));
        Assert.Equal(afterDown, runner.Menu.ActiveItem);
        Assert.Equal(1, tabs);
    }

    [Fact]
    public void Changing_character_recomputes_what_is_lit()
    {
        // TAB moves who is standing at the counter, and the new one may not be trainable.
        bool trainable = true;
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            CanTrainHere = _ => trainable,
            TabParty = () => trainable = false,
        };
        runner.Begin(Hall(), Font(), Box, Anchors);
        Choose(runner, HallYes);
        Assert.True(runner.Menu.Items[Train].Enabled);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Right));

        Assert.False(runner.Menu.Items[Train].Enabled);
    }

    [Fact]
    public void View_shows_the_sheet_the_host_builds()
    {
        var runner = Started();
        runner.ActiveCharacterSheet = () => new CharacterSheet(
            Name: "Aramil", Gender: "MALE", Age: "20 YEARS", Status: "OKAY",
            Alignment: "TRUE NEUTRAL", Race: "HUMAN", Class: "FIGHTER", Level: "LEVEL 1",
            Hits: "10", MaxHits: "/10", ExperienceLines: ["FIGHTER 4000"],
            Abilities: ["12", "12", "12", "12", "12", "12"], Coins: []);
        Choose(runner, HallYes);

        Choose(runner, View);

        Assert.NotNull(runner.Stats);
        Assert.True(runner.PartyMenuOpen);
    }

    [Fact]
    public void Training_announces_what_changed()
    {
        var outcome = new TrainingOutcome(
            TrainingRefusal.None, [new LevelGain("fighter", 1, 2, 7)],
            ["Aramil IS NOW A 2 LEVEL fighter"], 100);

        var runner = Started(train: _ => outcome);
        Choose(runner, HallYes);

        Choose(runner, Train);

        Assert.Same(outcome, runner.LastTraining);
        Assert.Contains("IS NOW A 2 LEVEL", BitmapFont.Decode(runner.Text.Lines[0].Text));
        Assert.True(runner.PartyMenuOpen);
    }

    [Fact]
    public void A_refused_session_says_nothing_and_leaves_the_screen_up()
    {
        var runner = Started(train: _ => TrainingOutcome.Refused(TrainingRefusal.CannotAfford));
        Choose(runner, HallYes);

        Choose(runner, Train);

        Assert.Equal(TrainingRefusal.CannotAfford, runner.LastTraining!.Refusal);
        Assert.True(runner.PartyMenuOpen);
    }

    [Fact]
    public void Begin_adventuring_leaves_the_hall_behind()
    {
        // The hall's OnReturnToTopOfQueue backs the party up and chains the moment the menu pops,
        // so popping back to it and finishing it are the same thing.
        var runner = Started();
        Choose(runner, HallYes);

        var step = Choose(runner, Begin);

        Assert.False(runner.PartyMenuOpen);
        Assert.Equal(55u, step.ChainTo);
    }

    [Fact]
    public void Exit_from_game_leaves_the_same_way()
    {
        var runner = Started();
        Choose(runner, HallYes);

        Choose(runner, Exit);

        Assert.False(runner.PartyMenuOpen);
    }

    [Fact]
    public void Escape_takes_the_exit()
    {
        var runner = Started();
        Choose(runner, HallYes);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Escape));

        Assert.False(runner.PartyMenuOpen);
    }

    [Fact]
    public void A_hall_that_forces_a_backup_still_does_so_through_the_menu()
    {
        var runner = new EventRunner { IsValidEvent = _ => true, CanTrainHere = _ => false };
        runner.Begin(Hall(forceExit: 1), Font(), Box, Anchors);
        Choose(runner, HallYes);

        Choose(runner, Begin);

        Assert.True(runner.BackupRequested);
    }

    [Fact]
    public void The_unbuilt_entries_are_named()
    {
        var runner = Started();
        Choose(runner, HallYes);

        Choose(runner, 0);                       // ADD CHARACTER

        Assert.Contains("ADD CHARACTER", runner.Unimplemented);
        Assert.True(runner.PartyMenuOpen);
    }

    [Fact]
    public void The_party_menu_does_not_leak_into_the_next_event()
    {
        var runner = Started();
        Choose(runner, HallYes);
        Assert.True(runner.PartyMenuOpen);

        runner.Begin(Hall(), Font(), Box, Anchors);

        Assert.False(runner.PartyMenuOpen);
        Assert.Null(runner.LastTraining);
    }

    // ---- the save and load slot screens --------------------------------------------------------

    private const int Load = 8;
    private const int Save = 9;

    private static IReadOnlyList<SaveSlot> Occupied(params int[] indices) =>
        [.. Enumerable.Range(0, SaveSlots.Count).Select(
            i => new SaveSlot(i, SaveSlots.Letter(i), SaveSlots.FileName(i),
                              indices.Contains(i)))];

    private static EventRunner AtSlots(bool saving, IReadOnlyList<SaveSlot>? slots = null,
                                       Func<int, string?>? save = null,
                                       Func<int, string?>? load = null)
    {
        var runner = Started();
        runner.SaveSlotsAvailable = () => slots ?? Occupied();
        runner.SaveToSlot = save ?? (_ => null);
        runner.LoadFromSlot = load ?? (_ => null);

        Choose(runner, HallYes);
        Choose(runner, saving ? Save : Load);
        return runner;
    }

    [Fact]
    public void Both_slot_screens_show_the_same_eleven_entries()
    {
        // SaveMenuData and LoadMenuData point at one shared array, so they cannot drift apart.
        var saving = AtSlots(saving: true);
        var loading = AtSlots(saving: false, slots: Occupied(0));

        Assert.Equal(Labels(saving), Labels(loading));
        Assert.Equal(["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "EXIT"], Labels(saving));
    }

    [Fact]
    public void The_save_screen_says_which_way_round_it_is()
    {
        var runner = AtSlots(saving: true);

        Assert.True(runner.SlotsOpen);
        Assert.True(runner.SlotsForSaving);
        Assert.Contains("SAVE GAME INTO", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    [Fact]
    public void The_load_screen_says_so_when_there_is_nothing_to_load()
    {
        var runner = AtSlots(saving: false, slots: Occupied());

        Assert.Contains("NO SAVED GAMES", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    [Fact]
    public void The_load_screen_names_the_choice_when_there_is_one()
    {
        var runner = AtSlots(saving: false, slots: Occupied(3));

        Assert.Contains("LOAD GAME FROM", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    [Fact]
    public void Loading_darkens_every_empty_slot()
    {
        var runner = AtSlots(saving: false, slots: Occupied(2, 5));

        Assert.True(runner.Menu.Items[2].Enabled);
        Assert.True(runner.Menu.Items[5].Enabled);
        Assert.False(runner.Menu.Items[0].Enabled);
        Assert.False(runner.Menu.Items[9].Enabled);
        Assert.True(runner.Menu.Items[SaveSlots.Exit].Enabled);   // EXIT is always available
    }

    [Fact]
    public void Saving_over_an_occupied_slot_is_offered_without_comment()
    {
        // No "are you sure": the save screen darkens nothing and asks nothing.
        var runner = AtSlots(saving: true, slots: Occupied(0, 1, 2));

        Assert.All(runner.Menu.Items, i => Assert.True(i.Enabled));
    }

    [Fact]
    public void Choosing_a_slot_returns_to_the_party_menu()
    {
        // Both screens pop unconditionally -- a failed save returns just the same, so there is no
        // retry loop and a player who picks a slot always lands back where they came from.
        int? chosen = null;
        var runner = AtSlots(saving: true, save: slot => { chosen = slot; return null; });

        Choose(runner, 4);

        Assert.Equal(4, chosen);
        Assert.False(runner.SlotsOpen);
        Assert.True(runner.PartyMenuOpen);
        Assert.Equal("ADD CHARACTER", Labels(runner)[0]);
    }

    [Fact]
    public void Leaving_the_slot_screen_saves_nothing()
    {
        bool asked = false;
        var runner = AtSlots(saving: true, save: _ => { asked = true; return null; });

        Choose(runner, SaveSlots.Exit);

        Assert.False(asked);
        Assert.False(runner.SlotsOpen);
        Assert.True(runner.PartyMenuOpen);
    }

    [Fact]
    public void Escape_leaves_the_slot_screen_rather_than_the_party_menu()
    {
        var runner = AtSlots(saving: true);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Escape));

        Assert.False(runner.SlotsOpen);
        Assert.True(runner.PartyMenuOpen);
    }

    [Fact]
    public void A_refusal_is_shown_rather_than_swallowed()
    {
        var runner = AtSlots(saving: true, save: _ => "NOT YET");

        Choose(runner, 0);

        Assert.Equal("NOT YET", runner.SlotMessage);
        Assert.Contains("NOT YET", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    [Fact]
    public void Loading_asks_the_host_for_the_slot_the_player_chose()
    {
        int? chosen = null;
        var runner = AtSlots(saving: false, slots: Occupied(6),
                             load: slot => { chosen = slot; return null; });

        Choose(runner, 6);

        Assert.Equal(6, chosen);
    }

    [Fact]
    public void The_slot_screen_does_not_leak_into_the_next_event()
    {
        var runner = AtSlots(saving: true, save: _ => "NOT YET");
        Choose(runner, 0);

        runner.Begin(Hall(), Font(), Box, Anchors);

        Assert.False(runner.SlotsOpen);
        Assert.Null(runner.SlotMessage);
    }
}
