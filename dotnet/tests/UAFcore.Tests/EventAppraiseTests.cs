using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Drives the appraise screens through the runner, from the temple.</summary>
public class EventAppraiseTests
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

    private static TempleEvent Temple() =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                NoPic, NoPic, (int)EventType.TempleEvent, 1, 0, 0,
                ChainEventHappen: 55, ChainEventNotHappen: 0,
                "Welcome.", "What do you want?", "", []),
            ForceExit: 0, AllowDonations: 1, CostFactor: 10, MaxLevel: 9,
            DonationTrigger: 100, DonationChain: 0, new SpellBook(0, []), TotalDonation: 0);

    private const int TempleHeal = 0;
    private const int HealAppraise = 6;

    private static EventRunner Started(int gems = 2, int jewels = 1,
                                       int value = 40,
                                       Action<Valuable, int, Appraised>? apply = null)
    {
        int gemsLeft = gems;
        int jewelsLeft = jewels;

        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            AppraiseKind = kind => kind == Valuable.Gem
                ? ("STONES", gemsLeft)
                : ("TRINKETS", jewelsLeft),
            TakeForAppraisal = kind =>
            {
                if (kind == Valuable.Gem) { gemsLeft--; } else { jewelsLeft--; }
                return value;
            },
            ApplyAppraisal = apply,
        };

        runner.Begin(Temple(), Font(), Box, Anchors);
        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));   // past the welcome
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

    private static void OpenIt(EventRunner runner)
    {
        Choose(runner, TempleHeal);
        Choose(runner, HealAppraise);
    }

    private static string[] Labels(EventRunner runner) =>
        [.. runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text))];

    [Fact]
    public void The_temples_fix_asks_for_the_temple_environment()
    {
        // The same call camp's FIX makes, with the one argument that changes who casts: camp draws
        // on the party's own memorised spells, the temple on a bishop it synthesises.
        var asked = new List<FixEnvironment>();

        var runner = Started();
        runner.ApplyFix = where => { asked.Add(where); return []; };

        Choose(runner, TempleHeal);
        Choose(runner, 2);                          // FIX

        Assert.Equal([FixEnvironment.Temple], asked);
        Assert.Equal(8, runner.Menu.Count);          // still the heal menu -- no screen is pushed
    }

    [Fact]
    public void The_entries_are_named_by_the_design()
    {
        // A design calling them STONES and TRINKETS says so on the bar.
        var runner = Started();
        OpenIt(runner);

        Assert.True(runner.AppraiseOpen);
        Assert.Equal(["STONES", "TRINKETS", "EXIT"], Labels(runner));
    }

    [Fact]
    public void A_kind_the_party_has_none_of_is_dark()
    {
        var runner = Started(gems: 0);
        OpenIt(runner);

        Assert.False(runner.Menu.Items[0].Enabled);
        Assert.True(runner.Menu.Items[1].Enabled);
    }

    [Fact]
    public void A_temple_appraises_both_kinds_whatever_its_design_says()
    {
        // The constructor's two flags default to TRUE and the temple pushes the screen without
        // them, so nothing about a temple can darken a kind -- only the purse can. The shop is
        // the one service that passes them; see EventBuyTests.
        var runner = Started(gems: 5, jewels: 3);
        OpenIt(runner);

        Assert.True(runner.Menu.Items[0].Enabled);
        Assert.True(runner.Menu.Items[1].Enabled);
    }

    [Fact]
    public void Choosing_a_kind_takes_it_out_of_the_purse_and_values_it()
    {
        var runner = Started(gems: 2, value: 40);
        OpenIt(runner);

        Choose(runner, 0);

        Assert.NotNull(runner.Appraising);
        Assert.Equal(Valuable.Gem, runner.Appraising!.Value.Kind);
        Assert.Equal(40, runner.Appraising.Value.Value);
        Assert.Equal(["SELL", "KEEP"], Labels(runner));
    }

    [Fact]
    public void Selling_reports_the_decision_and_the_value()
    {
        var decisions = new List<(Valuable, int, Appraised)>();
        var runner = Started(value: 40, apply: (k, v, d) => decisions.Add((k, v, d)));

        OpenIt(runner);
        Choose(runner, 0);
        Press(runner, VirtualKey.Return);          // SELL, where the cursor starts

        Assert.Equal([(Valuable.Gem, 40, Appraised.Sell)], decisions);
    }

    [Fact]
    public void Keeping_reports_the_other_decision()
    {
        var decisions = new List<(Valuable, int, Appraised)>();
        var runner = Started(value: 40, apply: (k, v, d) => decisions.Add((k, v, d)));

        OpenIt(runner);
        Choose(runner, 0);
        Choose(runner, 1);                         // KEEP

        Assert.Equal([(Valuable.Gem, 40, Appraised.Keep)], decisions);
    }

    [Fact]
    public void Deciding_returns_to_the_picker_with_one_fewer_left()
    {
        var runner = Started(gems: 2, apply: (_, _, _) => { });

        OpenIt(runner);
        Choose(runner, 0);
        Press(runner, VirtualKey.Return);

        Assert.Null(runner.Appraising);
        Assert.True(runner.AppraiseOpen);
        Assert.True(runner.Menu.Items[0].Enabled);   // one gem still to go

        Choose(runner, 0);
        Press(runner, VirtualKey.Return);

        Assert.False(runner.Menu.Items[0].Enabled);  // and now none
    }

    [Fact]
    public void Leaving_returns_to_the_screen_that_opened_it()
    {
        var runner = Started();
        OpenIt(runner);

        Press(runner, VirtualKey.Escape);

        Assert.False(runner.AppraiseOpen);
        Assert.Equal(8, runner.Menu.Count);          // the heal menu
    }
}
