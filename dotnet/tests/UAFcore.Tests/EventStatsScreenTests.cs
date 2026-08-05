using UAF.Common;
using UAF.Media;
using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Drives the stats screen through the runner — the keys it takes, and where it goes when it ends.
/// </summary>
/// <remarks>
/// <b>The same screen serves the generator and the party menu's MODIFY</b>, so both routes are
/// exercised here; what differs is only what happens on ACCEPT.
/// </remarks>
public class EventStatsScreenTests
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

    private static TrainingHallEvent Hall() =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                NoPic, NoPic, (int)EventType.TrainingHallEvent, 1, 0, 0,
                ChainEventHappen: 55, ChainEventNotHappen: 0, "Train here?", "", "", []),
            0, [new TrainableBaseclass("fighter", 1, 20, "")], Cost: 100);

    private const int HallYes = 0;
    private const int PartyModify = 2;
    private const int PartyCreate = 6;

    private static AbilityScores Even(int score = 12) =>
        new(score, 0, score, score, score, score, score);

    private static StatsScreen Screen(Action<StatsScreen>? accept = null,
                                      Func<(AbilityScores?, int)>? reroll = null) =>
        new(Even(), maxHitPoints: 10,
            _ => new AbilityLimits(3, 0, 18, 0),
            scores => scores,
            () => 74,
            scores => scores.Constitution,
            reroll,
            accept);

    private static EventRunner Started(Func<CharacterCreation, StatsScreen?>? forCreation = null,
                                       Func<StatsScreen?>? forMember = null,
                                       Action? onTab = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            CanTrainHere = _ => false,
            TabParty = onTab ?? (() => { }),
            StatsForCreation = forCreation,
            StatsForActiveMember = forMember,
            CreationChoicesFor = making => making.Step switch
            {
                CreationStep.Race => [new CreationChoice("Human", "HUMAN")],
                CreationStep.Gender => CreationChoices.Genders,
                CreationStep.Class => [new CreationChoice("Fighter", "FIGHTER")],
                CreationStep.Alignment => CreationChoices.Alignments,
                _ => [],
            },
        };

        runner.Begin(Hall(), Font(), Box, Anchors);
        return runner;
    }

    private static EventStep Choose(EventRunner runner, int item)
    {
        var key = runner.PartyMenuOpen ? VirtualKey.Down : VirtualKey.Right;

        for (int i = 0; i < runner.Menu.Count && runner.Menu.ActiveItem != item; i++)
        {
            runner.Handle(InputEvent.KeyDown(key));
        }

        Assert.Equal(item, runner.Menu.ActiveItem);
        return runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
    }

    private static void Press(EventRunner runner, VirtualKey key) =>
        runner.Handle(InputEvent.KeyDown(key));

    // ---- MODIFY ----------------------------------------------------------------------------------

    [Fact]
    public void Modify_puts_the_stats_screen_up_over_the_party_menu()
    {
        var runner = Started(forMember: () => Screen());
        Choose(runner, HallYes);
        Choose(runner, PartyModify);

        Assert.NotNull(runner.ChoosingStats);
        Assert.Equal(["REROLL", "ACCEPT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void Modify_with_nobody_to_modify_says_so_rather_than_opening_an_empty_screen()
    {
        var runner = Started(forMember: () => null);
        Choose(runner, HallYes);
        Choose(runner, PartyModify);

        Assert.Null(runner.ChoosingStats);
        Assert.Contains("MODIFY", runner.Unimplemented);
    }

    [Fact]
    public void Tab_moves_the_highlight_and_not_the_party()
    {
        // CHOOSESTATS_MENU_DATA is one of the few screens whose OnKeypress does not open with
        // TABParty, so TAB means something different here than on every other screen.
        int tabbedParty = 0;
        var runner = Started(forMember: () => Screen(), onTab: () => tabbedParty++);

        Choose(runner, HallYes);
        Choose(runner, PartyModify);

        Press(runner, VirtualKey.Tab);

        Assert.Equal(0, runner.ChoosingStats!.Highlighted);
        Assert.Equal(0, tabbedParty);
    }

    [Fact]
    public void The_numeric_keypad_keys_adjust_and_the_row_ones_do_not()
    {
        // KC_PLUS and KC_MINUS map from VK_ADD and VK_SUBTRACT (Getinput.cpp:566). The OEM keys on
        // the number row go through as KC_NUM and reach the menu instead.
        var runner = Started(forMember: () => Screen());
        Choose(runner, HallYes);
        Choose(runner, PartyModify);

        Press(runner, VirtualKey.Tab);
        Press(runner, VirtualKey.Subtract);
        Assert.Equal(11, runner.ChoosingStats!.Scores.Strength);

        Press(runner, VirtualKey.Add);
        Assert.Equal(12, runner.ChoosingStats.Scores.Strength);

        Press(runner, VirtualKey.Minus);
        Assert.Equal(12, runner.ChoosingStats.Scores.Strength);
    }

    [Fact]
    public void Up_and_down_never_reach_the_menu()
    {
        // The reference sets its "handled" bit before consulting whether anything moved, so the
        // keys are swallowed even when the adjustment refused.
        var runner = Started(forMember: () => Screen());
        Choose(runner, HallYes);
        Choose(runner, PartyModify);

        int before = runner.Menu.ActiveItem;
        Press(runner, VirtualKey.Up);
        Press(runner, VirtualKey.Down);

        Assert.Equal(before, runner.Menu.ActiveItem);
    }

    [Fact]
    public void Accepting_writes_back_and_returns_to_the_party_menu()
    {
        StatsScreen? accepted = null;
        var runner = Started(forMember: () => Screen(accept: s => accepted = s));

        Choose(runner, HallYes);
        Choose(runner, PartyModify);

        Press(runner, VirtualKey.Tab);
        Press(runner, VirtualKey.Down);          // strength to 11

        Press(runner, VirtualKey.Right);         // REROLL -> ACCEPT
        Press(runner, VirtualKey.Return);

        Assert.NotNull(accepted);
        Assert.Equal(11, accepted!.Scores.Strength);
        Assert.Null(runner.ChoosingStats);
        Assert.True(runner.PartyMenuOpen);
        Assert.Equal(12, runner.Menu.Count);
    }

    [Fact]
    public void Escape_accepts_rather_than_abandoning()
    {
        // Item 2 is "don't re-roll", and it is where the escape key lands -- there is no third
        // entry that throws the character away.
        StatsScreen? accepted = null;
        var runner = Started(forMember: () => Screen(accept: s => accepted = s));

        Choose(runner, HallYes);
        Choose(runner, PartyModify);
        Press(runner, VirtualKey.Escape);

        Assert.NotNull(accepted);
        Assert.Null(runner.ChoosingStats);
    }

    [Fact]
    public void Rerolling_replaces_the_scores_and_stays_on_the_screen()
    {
        int rolls = 0;
        var runner = Started(forMember: () => Screen(
            reroll: () => { rolls++; return (Even(15), 9); }));

        Choose(runner, HallYes);
        Choose(runner, PartyModify);

        Press(runner, VirtualKey.Return);        // REROLL is where the cursor starts

        Assert.Equal(1, rolls);
        Assert.NotNull(runner.ChoosingStats);
        Assert.Equal(15, runner.ChoosingStats!.Scores.Strength);
    }

    // ---- the generator ---------------------------------------------------------------------------

    /// <summary>Walks the wizard's four picker steps, which is what reaches the stats screen.</summary>
    private static void RunToStats(EventRunner runner)
    {
        Choose(runner, HallYes);
        Choose(runner, PartyCreate);

        for (int step = 0; step < 4; step++)
        {
            // SELECT is item 0 on the picker menu, and the cursor starts there.
            Assert.Equal(0, runner.Menu.ActiveItem);
            runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
        }
    }

    [Fact]
    public void The_generator_reaches_the_stats_screen_after_alignment()
    {
        var runner = Started(forCreation: _ => Screen());
        RunToStats(runner);

        Assert.NotNull(runner.ChoosingStats);
        Assert.NotNull(runner.Creating);
        Assert.Equal(CreationStep.Stats, runner.Creating!.Step);
    }

    [Fact]
    public void Accepting_moves_the_wizard_on_to_the_name()
    {
        var runner = Started(forCreation: _ => Screen());
        RunToStats(runner);

        Press(runner, VirtualKey.Right);
        Press(runner, VirtualKey.Return);

        Assert.Null(runner.ChoosingStats);
        Assert.Equal(CreationStep.Name, runner.Creating!.Step);
    }

    [Fact]
    public void A_host_with_no_stats_screen_skips_the_step_rather_than_stranding_the_wizard()
    {
        var runner = Started(forCreation: null);
        RunToStats(runner);

        Assert.Null(runner.ChoosingStats);
        Assert.Equal(CreationStep.Name, runner.Creating!.Step);
    }
}
