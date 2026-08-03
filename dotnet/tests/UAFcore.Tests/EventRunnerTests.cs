using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers event chaining and the events that run on text and menus.
/// </summary>
/// <remarks>
/// The events are built by hand rather than read from a design, because the interesting cases —
/// an option flagged absent, a chain naming a missing event, a question with no options at all —
/// are ones no shipped design happens to contain in a reachable place. The readers are already
/// proven against 6,234 real events; what needs proving here is what the engine does with one.
/// </remarks>
public class EventRunnerTests
{
    private const uint Key = 0xFF000000;
    private const uint Ink = 0xFFFFFFFF;

    private static BitmapFont Font(int advance = 10, int height = 16)
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (advance, height));

        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, sheetHeight, SurfaceKind.Font);
        sheet.Fill(Key);
        sheet.ColorKey = Key;

        foreach (var glyph in glyphs)
        {
            for (int y = glyph.Source.Top; y < glyph.Source.Bottom; y++)
            {
                for (int x = glyph.Source.Left + 1; x < glyph.Source.Right; x++)
                {
                    sheet[x, y] = Ink;
                }
            }
        }

        return new BitmapFont(new FontAtlas(sheet, glyphs));
    }

    private static readonly TextBoxMetrics Box = new(18, 328, 400, 96, 6);

    private static readonly MenuAnchors Anchors =
        new((16, 460), (200, 200), (20, 328), (16, 460));

    private static EventControl Control(ChainTrigger trigger = ChainTrigger.Always) =>
        new(0, 0, 0, (int)trigger, (int)EventTriggerType.Always, string.Empty, 0, 0, 0,
            string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static GameEventBase Base(EventType type, uint id = 1, string text = "",
                                      int onHappened = 0, int onNotHappened = 0,
                                      ChainTrigger trigger = ChainTrigger.Always) =>
        new(Control(trigger), NoPic, NoPic, (int)type, id, 0, 0,
            onHappened, onNotHappened, text, string.Empty, string.Empty, []);

    /// <summary>An empty <c>PIC_DATA</c>; none of these events' art is exercised here.</summary>
    private static readonly PicRecord NoPic =
        new(0, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    // ---- chaining ----------------------------------------------------------------------------

    [Fact]
    public void If_happened_chains_only_when_the_event_ran()
    {
        var source = Base(EventType.TextStatement, onHappened: 7, onNotHappened: 9,
                          trigger: ChainTrigger.IfHappened);

        Assert.Equal(7u, EventChain.Next(source, happened: true));
        Assert.Null(EventChain.Next(source, happened: false));
    }

    [Fact]
    public void If_not_happened_chains_only_when_the_event_was_suppressed()
    {
        var source = Base(EventType.TextStatement, onHappened: 7, onNotHappened: 9,
                          trigger: ChainTrigger.IfNotHappened);

        Assert.Null(EventChain.Next(source, happened: true));
        Assert.Equal(9u, EventChain.Next(source, happened: false));
    }

    [Fact]
    public void Always_takes_the_happened_target_on_both_paths()
    {
        // RunEvent.cpp:910 -- the not-happened path under AlwaysChain reads chainEventHappen, not
        // chainEventNotHappen. It reads like a typo and a design relying on it would follow a
        // different route if it were "corrected", so it is transcribed.
        var source = Base(EventType.TextStatement, onHappened: 7, onNotHappened: 9,
                          trigger: ChainTrigger.Always);

        Assert.Equal(7u, EventChain.Next(source, happened: true));
        Assert.Equal(7u, EventChain.Next(source, happened: false));
    }

    [Fact]
    public void Event_zero_can_never_be_chained_to()
    {
        // Both paths guard on > 0, so a design cannot chain to the event with id 0.
        var source = Base(EventType.TextStatement, onHappened: 0, trigger: ChainTrigger.Always);

        Assert.Null(EventChain.Next(source, happened: true));
        Assert.Null(EventChain.Next(source, happened: false));
    }

    [Fact]
    public void An_event_can_be_found_by_id_even_when_it_sits_at_no_reachable_cell()
    {
        // Chain targets resolve by id, which is how designs use off-map events as subroutines.
        var lookup = new EventLookup([
            new TextEvent(Base(EventType.TextStatement, id: 1), 0, 0, 0, 0, ""),
            new TextEvent(Base(EventType.TextStatement, id: 42), 0, 0, 0, 0, ""),
        ]);

        Assert.Equal(42u, lookup.ById(42)?.Base.Id);
        Assert.Null(lookup.ById(99));
    }

    // ---- the presented events ----------------------------------------------------------------

    private static EventStep Begin(EventRunner runner, IGameEvent gameEvent) =>
        runner.Begin(gameEvent, Font(), Box, Anchors);

    [Fact]
    public void A_text_statement_shows_its_text_and_a_two_entry_bar()
    {
        var runner = new EventRunner();
        var step = Begin(runner, new TextEvent(
            Base(EventType.TextStatement, text: "A cold wind rises."), 0, 0, 0, 0, ""));

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Equal(["A cold wind rises."],
                     runner.Text.Lines.Select(l => BitmapFont.Decode(l.Text)));

        // TextEventData's own entries and shortcut indices: X of EXIT, N of ENTER.
        Assert.Equal(["EXIT", "PRESS ENTER TO CONTINUE"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
        Assert.Equal([1, 7], runner.Menu.Items.Select(i => i.ShortcutIndex));
    }

    [Fact]
    public void Return_ends_a_text_statement_and_follows_its_chain()
    {
        var runner = new EventRunner();
        Begin(runner, new TextEvent(
            Base(EventType.TextStatement, text: "Hello.", onHappened: 12), 0, 0, 0, 0, ""));

        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(12u, step.ChainTo);
        Assert.False(runner.IsActive);
    }

    [Fact]
    public void Long_text_pages_before_the_event_finishes()
    {
        // The box is 6 lines; 40 short words at 400px wrap past that, so the first Return has to
        // turn the page rather than end the event.
        var runner = new EventRunner();
        string prose = string.Join(' ', Enumerable.Repeat("word", 200));
        Begin(runner, new TextEvent(
            Base(EventType.TextStatement, text: prose, onHappened: 3), 0, 0, 0, 0, ""));

        Assert.True(runner.Text.NumLines > 6);

        Assert.Equal(EventStepKind.Running,
                     runner.Handle(InputEvent.KeyDown(VirtualKey.Return)).Kind);
        Assert.True(runner.IsActive);
        Assert.Equal(6, runner.Text.CurrentLine);
    }

    [Fact]
    public void Yes_and_no_chain_to_their_own_targets()
    {
        foreach (var (moves, expected) in new (int Moves, uint Expected)[] { (0, 21u), (1, 22u) })
        {
            var runner = new EventRunner();
            Begin(runner, new YesNoEvent(
                Base(EventType.QuestionYesNo, text: "Enter?"), 0, 0, 21, 22));

            Assert.Equal(["YES", "NO"],
                         runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));

            for (int i = 0; i < moves; i++)
            {
                runner.Handle(InputEvent.KeyDown(VirtualKey.Right));
            }

            var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
            Assert.Equal(expected, step.ChainTo);
        }
    }

    [Fact]
    public void A_question_list_keeps_a_slot_for_every_button_so_indices_stay_aligned()
    {
        // Empty labels become disabled " " placeholders, which is what lets the original index
        // straight into buttons[UserResult-1]. Adding only the non-empty ones picks the wrong
        // option the moment a design leaves a gap.
        var runner = new EventRunner();
        Begin(runner, new QuestionEvent(
            Base(EventType.QuestionList, text: "Which way?"), "Choose:", 3,
            [
                new QuestionOption("North", 1, 0, 31),
                new QuestionOption(string.Empty, 0, 0, 0),
                new QuestionOption("South", 1, 0, 33),
            ]));

        Assert.Equal(EventRunner.MaxButtons, runner.Menu.Count);
        Assert.True(runner.Menu.Items[0].Enabled);
        Assert.False(runner.Menu.Items[1].Enabled);
        Assert.True(runner.Menu.Items[2].Enabled);

        // Down from slot 0 skips the empty slot and lands on "South", whose chain is 33.
        runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
        Assert.Equal(2, runner.Menu.ActiveItem);
        Assert.Equal(33u, runner.Handle(InputEvent.KeyDown(VirtualKey.Return)).ChainTo);
    }

    [Fact]
    public void An_option_flagged_absent_keeps_its_slot_but_cannot_be_chosen()
    {
        var runner = new EventRunner();
        Begin(runner, new QuestionEvent(
            Base(EventType.QuestionList), "Choose:", 2,
            [
                new QuestionOption("Open", 1, 0, 41),
                new QuestionOption("Steal", 0, 0, 42),
            ]));

        Assert.Equal(2, runner.Menu.Items.Count(i => BitmapFont.Decode(i.Text) != " "));
        Assert.False(runner.Menu.Items[1].Enabled);

        // Down cannot reach it, so the selection stays put.
        runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
        Assert.Equal(0, runner.Menu.ActiveItem);
    }

    [Fact]
    public void A_question_with_no_options_chains_straight_through_without_drawing()
    {
        // if (count == 0) ChainHappened() -- the event is over before a frame is drawn.
        var runner = new EventRunner();
        var step = Begin(runner, new QuestionEvent(
            Base(EventType.QuestionList, onHappened: 8), "Choose:", 0, []));

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(8u, step.ChainTo);
        Assert.False(runner.IsActive);
    }

    [Fact]
    public void A_shortcut_letter_picks_an_option_outright()
    {
        var runner = new EventRunner();
        Begin(runner, new QuestionEvent(
            Base(EventType.QuestionList), "Choose:", 2,
            [
                new QuestionOption("North", 1, 0, 31),
                new QuestionOption("South", 1, 0, 33),
            ]));

        // First letters are unique across the enabled entries, so they become shortcuts -- and one
        // keystroke both selects and confirms.
        var step = runner.Handle(InputEvent.Text('s'));
        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(33u, step.ChainTo);
    }

    [Fact]
    public void The_two_question_forms_differ_in_where_they_sit_and_how_they_flow()
    {
        var list = new EventRunner();
        Begin(list, new QuestionEvent(Base(EventType.QuestionList), "Choose:", 1,
                                      [new QuestionOption("Yes", 1, 0, 1)]));

        var buttons = new EventRunner();
        Begin(buttons, new QuestionEvent(Base(EventType.QuestionButton), string.Empty, 1,
                                         [new QuestionOption("Yes", 1, 0, 1)]));

        Assert.Equal(MenuOrientation.Vertical, list.Menu.Orientation);
        Assert.Equal((20, 328), (list.Menu.StartX, list.Menu.StartY));
        Assert.Equal(2, list.Menu.ItemSeparation);

        Assert.Equal(MenuOrientation.Horizontal, buttons.Menu.Orientation);
        Assert.Equal((16, 460), (buttons.Menu.StartX, buttons.Menu.StartY));
        Assert.Equal(7, buttons.Menu.ItemSeparation);
    }

    [Fact]
    public void Only_the_list_form_carries_a_title()
    {
        // QUESTION_BUTTON_DATA is the list form without the title string -- reading one as the
        // other loses or invents a counted string, and it shows up here as a stray heading.
        var list = new EventRunner();
        Begin(list, new QuestionEvent(Base(EventType.QuestionList), "Choose:", 1,
                                      [new QuestionOption("Yes", 1, 0, 1)]));
        Assert.NotNull(list.Menu.Title);

        var buttons = new EventRunner();
        Begin(buttons, new QuestionEvent(Base(EventType.QuestionButton), string.Empty, 1,
                                         [new QuestionOption("Yes", 1, 0, 1)]));
        Assert.Null(buttons.Menu.Title);
    }

    [Fact]
    public void An_npc_shows_its_line_and_a_single_press_enter_entry()
    {
        var runner = new EventRunner();
        Begin(runner, new NpcSaysEvent(
            Base(EventType.NPCSays, text: "Well met."), string.Empty, 0, string.Empty, 0, 0));

        Assert.Equal(["Well met."],
                     runner.Text.Lines.Select(l => BitmapFont.Decode(l.Text)));
        Assert.Equal(["PRESS ENTER TO CONTINUE"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void An_event_this_port_does_not_run_is_named_rather_than_silently_skipped()
    {
        // PlayMovie, which is blocked on the FFmpeg adapter rather than merely unported -- this
        // test has now had to move twice as Camp and then Vault started running, so it wants a
        // type that is waiting on a whole subsystem.
        var runner = new EventRunner();
        var step = Begin(runner, new PlayMovieEvent(Base(EventType.PlayMovieEvent), "", 0));

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.NotNull(runner.Unimplemented);
        Assert.Contains("PlayMovie", runner.Unimplemented, StringComparison.Ordinal);
    }

    // ---- treasure ------------------------------------------------------------------------------

    private static TreasureEvent Treasure(params string[] itemIds)
    {
        var items = itemIds
            .Select((id, i) => new ItemInstance(i, id, 0, 0, 1, 0, 0, 0, 0))
            .ToArray();

        return new TreasureEvent(
            Base(EventType.GiveTreasure, text: "You Have Found Treasure!"),
            new MoneySack([0, 0, 0, 0, 0], [], []),
            new ItemList(items, new ReadyItems([])),
            SilentGiveToActiveChar: 0);
    }

    [Fact]
    public void A_treasure_event_shows_its_list_and_the_six_entry_bar()
    {
        var runner = new EventRunner { ItemNames = id => id == "Glaive" ? "Noble Glaive" : null };

        var step = runner.Begin(Treasure("Glaive", "Arrow"), Font(), TextBoxMetrics.Default,
                                Anchors);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Equal(6, runner.Menu.Count);
        Assert.NotNull(runner.Items);

        // The resolver supplies the display name; an id it does not know falls back to the id
        // itself rather than showing an empty row.
        var rows = runner.Items!.Form;
        Assert.Equal("Noble Glaive", rows.Field(ItemsFormFields.Name)!.Text);
        Assert.Equal("Arrow",
                     rows.Field(ItemsFormFields.Name + ItemsFormFields.RowOffset(1))!.Text);
    }

    [Fact]
    public void Exit_finishes_a_treasure_event_without_taking_anything()
    {
        var runner = new EventRunner();
        runner.Begin(Treasure("Glaive"), Font(), TextBoxMetrics.Default, Anchors);

        // EXIT is the last of the six, and arrows skip the disabled entries -- POOL, SHARE and
        // DETECT are all greyed out here -- so stepping right lands on it in two moves, not five.
        // Its shortcut is the surer route and is what a player would use.
        var step = runner.Handle(InputEvent.Text('X'));

        Assert.NotEqual(EventStepKind.Running, step.Kind);
        Assert.False(runner.TakeRequested);
    }

    [Fact]
    public void Take_finishes_the_event_and_asks_the_host_to_hand_it_over()
    {
        // The runner owns no party, so it records the request and the host acts on it.
        var runner = new EventRunner();
        runner.Begin(Treasure("Glaive"), Font(), TextBoxMetrics.Default, Anchors);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Right));   // VIEW -> TAKE
        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.NotEqual(EventStepKind.Running, step.Kind);
        Assert.True(runner.TakeRequested);
    }

    [Fact]
    public void An_option_this_port_has_not_built_names_itself_and_leaves_the_event_open()
    {
        var runner = new EventRunner();
        runner.Begin(Treasure("Glaive"), Font(), TextBoxMetrics.Default, Anchors);

        // VIEW is first, and opens a character screen that does not exist yet.
        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.False(runner.TakeRequested);
        Assert.Contains("VIEW", runner.Unimplemented ?? "", StringComparison.Ordinal);
    }

    private static CharacterSheet TestSheet() => new(
        Name: "Sherlas of Hemlock", Gender: "MALE", Age: "17 YEARS", Status: "OKAY",
        Alignment: "TRUE NEUTRAL", Race: "HUMAN", Class: "RANGER", Level: "LEVEL 3",
        Hits: "18", MaxHits: "/22",
        ExperienceLines: ["RANGER 8000"],
        Abilities: ["18/75", "12", "9", "14", "16", "11"],
        Coins: []);

    [Fact]
    public void View_opens_the_active_characters_sheet_over_the_treasure_list()
    {
        var runner = new EventRunner { ActiveCharacterSheet = TestSheet };
        runner.Begin(Treasure("Glaive"), Font(), TextBoxMetrics.Default, Anchors);

        Assert.Null(runner.Stats);
        Assert.NotNull(runner.Items);

        // VIEW is first, so a bare Return opens it.
        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.NotNull(runner.Stats);
        Assert.Null(runner.Unimplemented);
        Assert.Equal("Sherlas of Hemlock",
                     runner.Stats!.Form.Field(CharStatsFields.Name)!.Text);
        Assert.Equal("18/75", runner.Stats.Form.Field(CharStatsFields.AbilityValues[0])!.Text);
    }

    [Fact]
    public void The_next_commit_puts_the_sheet_away_rather_than_choosing_anything()
    {
        var runner = new EventRunner { ActiveCharacterSheet = TestSheet };
        runner.Begin(Treasure("Glaive"), Font(), TextBoxMetrics.Default, Anchors);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));   // VIEW
        Assert.NotNull(runner.Stats);

        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        // Back on the menu, with the event still running and nothing taken.
        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Null(runner.Stats);
        Assert.False(runner.TakeRequested);
    }

    [Fact]
    public void View_reports_itself_when_there_is_nobody_to_show()
    {
        // No host resolver -- the runner has no party of its own, so it says so rather than
        // opening an empty sheet.
        var runner = new EventRunner();
        runner.Begin(Treasure("Glaive"), Font(), TextBoxMetrics.Default, Anchors);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Null(runner.Stats);
        Assert.Contains("VIEW", runner.Unimplemented ?? "", StringComparison.Ordinal);
    }

    // ---- random events -------------------------------------------------------------------------

    private static RandomEvent Random(uint onHappened, params (uint Chain, byte Chance)[] branches) =>
        new(Base(EventType.RandomEvent, text: "Something stirs.", onHappened: (int)onHappened),
            [.. branches.Select(b => new RandomBranch(b.Chain, b.Chance))]);

    [Fact]
    public void A_random_event_presents_before_it_rolls()
    {
        // It looks like a text statement until Return, which is the point: nothing on screen says
        // which way it went.
        var runner = new EventRunner { ChooseRandomBranch = _ => 55u };

        var step = Begin(runner, Random(0, (55, 100)));

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Null(runner.Unimplemented);
    }

    [Fact]
    public void Return_replaces_the_random_event_with_the_branch_it_rolled()
    {
        var runner = new EventRunner { ChooseRandomBranch = _ => 77u };
        Begin(runner, Random(onHappened: 5, (77, 100)));

        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        // The rolled branch, not the event's own chain -- a random event replaces itself.
        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(77u, step.ChainTo);
    }

    [Fact]
    public void A_random_event_with_nothing_to_roll_falls_back_on_its_own_chain()
    {
        var runner = new EventRunner { ChooseRandomBranch = _ => null };
        Begin(runner, Random(onHappened: 5));

        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(5u, step.ChainTo);
    }

    // ---- the branch roll itself ------------------------------------------------------------------

    private static uint? Pick(int roll, params (uint Chain, byte Chance)[] branches) =>
        RandomEventChoice.Pick(Random(0, branches), _ => true, _ => roll);

    [Theory]
    [InlineData(1, 10u)]
    [InlineData(30, 10u)]                                // the boundary belongs to the earlier one
    [InlineData(31, 20u)]
    [InlineData(100, 20u)]
    public void The_roll_walks_the_running_total(int roll, uint expected)
    {
        Assert.Equal(expected, Pick(roll, (10, 30), (20, 70)));
    }

    [Fact]
    public void The_die_is_sized_to_the_total_rather_than_to_a_hundred()
    {
        // Chances of 1, 2 and 3 give sixths. Normalising to a percentage would change the odds of
        // every design that does not happen to add up to 100.
        int sides = 0;
        RandomEventChoice.Pick(Random(0, (10, 1), (20, 2), (30, 3)), _ => true,
                               n => { sides = n; return 1; });

        Assert.Equal(6, sides);
    }

    [Fact]
    public void A_branch_naming_an_event_the_level_lacks_takes_no_share_of_the_odds()
    {
        // The weight is removed from the total, so the survivors keep their relative odds rather
        // than the dead branch's share vanishing into a dead end.
        int sides = 0;
        var chosen = RandomEventChoice.Pick(
            Random(0, (10, 50), (99, 50)),
            id => id != 99,
            n => { sides = n; return n; });

        Assert.Equal(50, sides);
        Assert.Equal(10u, chosen);
    }

    [Fact]
    public void A_branch_with_no_chance_is_not_eligible_however_valid_it_is()
    {
        Assert.Null(RandomEventChoice.Pick(Random(0, (10, 0)), _ => true, _ => 1));
    }

    [Fact]
    public void An_event_with_no_branches_at_all_picks_nothing()
    {
        Assert.Null(RandomEventChoice.Pick(Random(0), _ => true, _ => 1));
    }

    // ---- special items -------------------------------------------------------------------------

    private static SpecialItemEvent Special(uint onHappened = 4) =>
        new(Base(EventType.SpecialItem, text: "A chest stands open.",
                 onHappened: (int)onHappened),
            [new SpecialObjectEvent(SpecialItems.ItemFlag, SpecialItems.Give, 1, 0)],
            ForceExit: 0, WaitForReturn: 0);

    [Fact]
    public void A_special_item_event_gives_nothing_until_return_is_pressed()
    {
        // The reference applies the list in OnKeypress, not OnInitialEvent. A run abandoned before
        // Return leaves the party without the item, which matters when progress is gated on it.
        SpecialItemEvent? applied = null;
        var runner = new EventRunner { ApplySpecialItems = e => applied = e };

        Begin(runner, Special());

        Assert.Null(applied);
        Assert.Null(runner.Unimplemented);
    }

    // ---- damage and healing --------------------------------------------------------------------

    [Fact]
    public void A_damage_event_hurts_nobody_until_return_is_pressed()
    {
        // Both GIVE_DAMAGE_DATA and HEAL_PARTY_DATA do all their work in OnKeypress, so a run
        // abandoned before the commit leaves the party untouched.
        DamageEvent? applied = null;
        var runner = new EventRunner { ApplyDamage = e => applied = e };
        var damage = new DamageEvent(Base(EventType.Damage, text: "Darts fly out."),
                                     1, 100, 6, 1, 0, 0, 15, 0, 0, 1, 0);

        Begin(runner, damage);
        Assert.Null(applied);
        Assert.Null(runner.Unimplemented);

        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Same(damage, applied);
        Assert.NotEqual(EventStepKind.Running, step.Kind);
    }

    [Fact]
    public void A_heal_event_takes_the_same_path()
    {
        HealPartyEvent? applied = null;
        var runner = new EventRunner { ApplyHeal = e => applied = e };
        var heal = new HealPartyEvent(Base(EventType.HealParty, text: "You feel better.",
                                           onHappened: 6),
                                      1, 0, 0, 100, 1, 10, 0);

        Begin(runner, heal);
        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Same(heal, applied);
        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(6u, step.ChainTo);
    }

    [Fact]
    public void Tab_cycles_the_party_and_never_reaches_the_menu()
    {
        // TABParty is the first line of every OnKeypress (RunEvent.cpp:792) and returns before the
        // menu sees the key -- so TAB can never also move a selection. That is what makes "who
        // tries" and "who pays" answerable without a selection screen.
        int cycled = 0;
        var runner = new EventRunner { TabParty = () => cycled++ };
        Begin(runner, new YesNoEvent(Base(EventType.QuestionYesNo, text: "Enter?"), 0, 0, 21, 22));

        int before = runner.Menu.ActiveItem;
        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Tab));

        Assert.Equal(1, cycled);
        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Equal(before, runner.Menu.ActiveItem);
    }

    [Fact]
    public void Return_applies_the_list_and_then_chains()
    {
        SpecialItemEvent? applied = null;
        var runner = new EventRunner { ApplySpecialItems = e => applied = e };
        var special = Special(onHappened: 4);
        Begin(runner, special);

        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Same(special, applied);
        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(4u, step.ChainTo);
    }
}
