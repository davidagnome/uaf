using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Drives the REST screen through the runner: setting a duration, engaging, and being interrupted.
/// </summary>
/// <remarks>
/// <b>REST is the first screen that needs a cycle</b> — the reference's <c>OnCycle</c> runs
/// whether or not the player pressed anything, and it is what lets a rest pass time on its own.
/// </remarks>
public class EventRestTests
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

    private const int CampRest = 4;

    private static EventRunner Started(Action<int>? clock = null, Func<uint>? restEvent = null,
                                       Action<int, bool>? processTime = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            AdvanceClock = clock,
            RestEventThisMinute = restEvent,
            ProcessTime = processTime,
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
    public void Camp_opens_the_rest_screen()
    {
        var runner = Started();
        Choose(runner, CampRest);

        Assert.True(runner.RestOpen);
        Assert.False(runner.RestEngaged);
        Assert.Equal(["REST", "DAYS", "HOURS", "MINS", "ADD", "SUB", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    /// <summary>Selects a field and adjusts it with the keys, which do not move the cursor.</summary>
    private static void Set(EventRunner runner, int field, int presses)
    {
        Choose(runner, field);

        for (int i = 0; i < presses; i++)
        {
            Press(runner, VirtualKey.Add);
        }
    }

    [Fact]
    public void Add_and_sub_act_on_whatever_the_cursor_last_passed_over()
    {
        // A real wart, and the reference's: walking the cursor from HOURS to ADD passes over MINS,
        // and the form re-syncs to the menu on every keypress -- so pressing ADD there adds a
        // minute. Only the field immediately before ADD can be reached this way; anything else
        // has to be adjusted with the + and - keys.
        var runner = Started();
        Choose(runner, CampRest);

        Choose(runner, EventRunner.RestHours);
        Choose(runner, EventRunner.RestAdd);

        Assert.Equal(0, runner.RestLeft.Hours);
        Assert.Equal(1, runner.RestLeft.Minutes);

        Choose(runner, EventRunner.RestSub);
        Assert.Equal(0, runner.RestLeft.Minutes);
    }

    [Fact]
    public void The_keys_adjust_the_field_the_cursor_is_on()
    {
        var runner = Started();
        Choose(runner, CampRest);

        Set(runner, EventRunner.RestHours, 2);

        Assert.Equal(2, runner.RestLeft.Hours);

        Press(runner, VirtualKey.Subtract);
        Assert.Equal(1, runner.RestLeft.Hours);
    }

    [Fact]
    public void Moving_the_cursor_onto_a_field_selects_it()
    {
        // The reference re-syncs the form to the menu after every keypress, so walking the cursor
        // onto MINS activates that field without a second press.
        var runner = Started();
        Choose(runner, CampRest);

        while (runner.Menu.ActiveItem != EventRunner.RestMins)
        {
            Press(runner, VirtualKey.Right);
        }

        Press(runner, VirtualKey.Up);            // the arrow keys adjust rather than move
        Assert.Equal(1, runner.RestLeft.Minutes);
    }

    [Fact]
    public void The_arrow_keys_adjust_and_never_reach_the_menu()
    {
        var runner = Started();
        Choose(runner, CampRest);
        Choose(runner, EventRunner.RestDays);

        int before = runner.Menu.ActiveItem;
        Press(runner, VirtualKey.Up);
        Press(runner, VirtualKey.Add);

        Assert.Equal(before, runner.Menu.ActiveItem);
        Assert.Equal(2, runner.RestLeft.Days);
    }

    [Fact]
    public void Nothing_ticks_until_rest_is_pressed()
    {
        int minutes = 0;
        var runner = Started(clock: m => minutes += m);

        Choose(runner, CampRest);
        Set(runner, EventRunner.RestHours, 1);

        runner.Cycle();
        runner.Cycle();

        Assert.Equal(0, minutes);
        Assert.False(runner.RestEngaged);
    }

    [Fact]
    public void Resting_passes_time_a_delta_at_a_time()
    {
        int minutes = 0;
        var runner = Started(clock: m => minutes += m);

        Choose(runner, CampRest);
        Set(runner, EventRunner.RestHours, 1);
        Choose(runner, EventRunner.RestBegin);

        Assert.True(runner.RestEngaged);

        runner.Cycle();

        // An hour left steps fifteen minutes a cycle.
        Assert.Equal(15, minutes);
        Assert.Equal(45, runner.RestLeft.TotalMinutes);
    }

    [Fact]
    public void A_rest_runs_down_to_nothing_and_hands_the_screen_back()
    {
        int minutes = 0;
        var runner = Started(clock: m => minutes += m);

        Choose(runner, CampRest);
        Set(runner, EventRunner.RestHours, 1);
        Choose(runner, EventRunner.RestBegin);

        for (int i = 0; i < 100 && runner.RestEngaged; i++)
        {
            runner.Cycle();
        }

        Assert.False(runner.RestEngaged);
        Assert.True(runner.RestOpen);                     // back to setting a duration
        Assert.Equal(60, minutes);                        // exactly the hour, not a minute more
        Assert.Equal(0, runner.RestLeft.TotalMinutes);
        Assert.Equal(EventRunner.RestBegin, runner.Menu.ActiveItem);
    }

    [Fact]
    public void A_rest_event_interrupts_and_replaces_the_screen()
    {
        int minutes = 0;
        int checks = 0;
        var runner = Started(clock: m => minutes += m,
                             restEvent: () => ++checks == 3 ? 99u : 0u);

        Choose(runner, CampRest);
        Set(runner, EventRunner.RestDays, 1);             // a whole day
        Choose(runner, EventRunner.RestBegin);

        var step = runner.Cycle();

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(99u, step.ChainTo);
        Assert.False(runner.RestOpen);
        Assert.False(runner.RestEngaged);

        // It stopped on the minute the event fired rather than finishing the step.
        Assert.Equal(3, minutes);
    }

    [Fact]
    public void The_zone_is_asked_once_per_minute_and_not_once_per_cycle()
    {
        // The counter the reference keeps is per minute, so a coarse delta still gives the zone
        // every minute it is owed -- otherwise a long rest would roll far fewer times than it
        // should.
        int checks = 0;
        var runner = Started(clock: _ => { }, restEvent: () => { checks++; return 0u; });

        Choose(runner, CampRest);
        Set(runner, EventRunner.RestHours, 1);
        Choose(runner, EventRunner.RestBegin);

        runner.Cycle();

        Assert.Equal(15, checks);
    }

    [Fact]
    public void The_time_pass_runs_once_a_cycle_with_the_whole_step()
    {
        // Not once per minute: the reference's minute loop only advances the clock and the
        // rest-event counter, and ProcessTimeSensitiveData runs after it. That is what makes the
        // auto-heal at most one point a cycle however long the step was.
        var calls = new List<(int Minutes, bool Resting)>();
        var runner = Started(clock: _ => { }, processTime: (m, r) => calls.Add((m, r)));

        Choose(runner, CampRest);
        Set(runner, EventRunner.RestHours, 1);
        Choose(runner, EventRunner.RestBegin);

        runner.Cycle();

        Assert.Equal([(15, true)], calls);
    }

    [Fact]
    public void An_interrupted_rest_still_counts_the_minutes_it_managed()
    {
        var calls = new List<(int Minutes, bool Resting)>();
        int checks = 0;
        var runner = Started(clock: _ => { },
                             restEvent: () => ++checks == 4 ? 99u : 0u,
                             processTime: (m, r) => calls.Add((m, r)));

        Choose(runner, CampRest);
        Set(runner, EventRunner.RestDays, 1);
        Choose(runner, EventRunner.RestBegin);

        runner.Cycle();

        Assert.Equal([(4, true)], calls);
    }

    [Fact]
    public void Exiting_leaves_for_camp()
    {
        var runner = Started();
        Choose(runner, CampRest);
        Choose(runner, EventRunner.RestExit);

        Assert.False(runner.RestOpen);
        Assert.Equal(12, runner.Menu.Count);
        Assert.Equal("SAVE", BitmapFont.Decode(runner.Menu.Items[0].Text));
    }

    [Fact]
    public void A_rest_of_no_time_finishes_on_the_first_cycle()
    {
        int minutes = 0;
        var runner = Started(clock: m => minutes += m);

        Choose(runner, CampRest);
        Choose(runner, EventRunner.RestBegin);

        runner.Cycle();

        Assert.False(runner.RestEngaged);
        Assert.Equal(0, minutes);
    }
}
