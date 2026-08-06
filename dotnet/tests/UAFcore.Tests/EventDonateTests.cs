using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Drives the temple's DONATE and GIVE screens through the runner.</summary>
public class EventDonateTests
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

    private static TempleEvent Temple(int allowDonations = 1, int trigger = 100,
                                      uint chain = 0, int total = 0) =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                NoPic, NoPic, (int)EventType.TempleEvent, 1, 0, 0,
                ChainEventHappen: 55, ChainEventNotHappen: 0,
                "Welcome to the temple.", "What do you want?", "", []),
            ForceExit: 0, allowDonations, CostFactor: 0, MaxLevel: 9,
            trigger, chain, new SpellBook(0, []), total);

    private const int TempleHeal = 0;
    private const int TempleDonate = 1;
    private const int TempleExit = 6;

    private static EventRunner Started(TempleEvent? temple = null, int maximum = 500,
                                       Func<int, int>? apply = null)
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            DonationMaximum = () => maximum,
            ApplyDonation = apply ?? (amount => amount),
        };

        runner.Begin(temple ?? Temple(), Font(), Box, Anchors);

        // The temple opens on its welcome; Return moves to the menu.
        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
        return runner;
    }

    private static void Press(EventRunner runner, VirtualKey key) =>
        runner.Handle(InputEvent.KeyDown(key));

    private static void Digit(EventRunner runner, char digit) =>
        runner.Handle(new InputEvent
        {
            Kind = InputEventKind.KeyDown,
            Key = VirtualKey.D0 + (digit - '0'),
            Character = digit,
        });

    private static EventStep Choose(EventRunner runner, int item)
    {
        for (int i = 0; i < runner.Menu.Count && runner.Menu.ActiveItem != item; i++)
        {
            Press(runner, VirtualKey.Right);
        }

        Assert.Equal(item, runner.Menu.ActiveItem);
        return runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
    }

    [Fact]
    public void Heal_opens_its_own_menu()
    {
        var runner = Started();
        Choose(runner, TempleHeal);

        Assert.True(runner.HealOpen);
        Assert.Equal(["CAST", "VIEW", "FIX", "TAKE", "POOL", "SHARE", "APPR", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void Cast_opens_the_priced_list()
    {
        var runner = Started();
        runner.TempleSpellsFor = () => [new TempleSpell("cure", "Cure Light Wounds", 1, 50)];

        Choose(runner, TempleHeal);
        Choose(runner, EventRunner.HealCast);

        Assert.NotNull(runner.TempleCasting);
        Assert.Equal(50, runner.TempleCasting![0].Cost);
        Assert.Equal(["CAST", "NEXT", "PREV", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void The_cast_list_takes_the_vertical_keys()
    {
        var runner = Started();
        runner.TempleSpellsFor = () =>
            [new TempleSpell("a", "A", 1, 10), new TempleSpell("b", "B", 1, 20)];

        Choose(runner, TempleHeal);
        Choose(runner, EventRunner.HealCast);

        Press(runner, VirtualKey.Down);
        Assert.Equal(1, runner.TempleSpellIndex);

        Press(runner, VirtualKey.Down);
        Assert.Equal(0, runner.TempleSpellIndex);          // wraps
    }

    [Fact]
    public void The_casting_itself_is_named_rather_than_done()
    {
        // The reference synthesises a max-level bishop and casts through the ordinary spell
        // machinery -- the same layer FIX waits on.
        var runner = Started();
        runner.TempleSpellsFor = () => [new TempleSpell("cure", "Cure", 1, 50)];

        Choose(runner, TempleHeal);
        Choose(runner, EventRunner.HealCast);
        Press(runner, VirtualKey.Return);

        Assert.Contains("casting", runner.Unimplemented);
    }

    [Fact]
    public void Leaving_the_cast_list_returns_to_heal_and_then_to_the_temple()
    {
        var runner = Started();
        runner.TempleSpellsFor = () => [new TempleSpell("cure", "Cure", 1, 50)];

        Choose(runner, TempleHeal);
        Choose(runner, EventRunner.HealCast);
        Press(runner, VirtualKey.Escape);

        Assert.Null(runner.TempleCasting);
        Assert.True(runner.HealOpen);
        Assert.Equal(8, runner.Menu.Count);

        Choose(runner, EventRunner.HealExit);

        Assert.False(runner.HealOpen);
        Assert.Equal(7, runner.Menu.Count);
    }

    [Fact]
    public void The_temple_menu_follows_its_welcome()
    {
        var runner = Started();

        Assert.Equal(["HEAL", "DONATE", "VIEW", "TAKE", "POOL", "SHARE", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void Donate_opens_its_own_menu()
    {
        var runner = Started();
        Choose(runner, TempleDonate);

        Assert.True(runner.DonateOpen);
        Assert.Equal(["TAKE", "POOL", "SHARE", "APPR", "GIVE", "EXIT"],
                     runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));
    }

    [Fact]
    public void A_temple_that_takes_no_donations_says_so_rather_than_opening()
    {
        var runner = Started(Temple(allowDonations: 0));
        Choose(runner, TempleDonate);

        Assert.False(runner.DonateOpen);
        Assert.Contains("DONATE", runner.Unimplemented);
    }

    [Fact]
    public void Give_takes_digits()
    {
        var runner = Started();
        Choose(runner, TempleDonate);
        Choose(runner, EventRunner.DonateGive);

        Assert.NotNull(runner.Giving);

        Digit(runner, '2');
        Digit(runner, '5');

        Assert.Equal(25, runner.Giving!.Value.Amount);
    }

    [Fact]
    public void More_than_the_party_has_snaps_to_the_maximum()
    {
        var runner = Started(maximum: 30);
        Choose(runner, TempleDonate);
        Choose(runner, EventRunner.DonateGive);

        Digit(runner, '9');
        Digit(runner, '9');

        Assert.Equal(30, runner.Giving!.Value.Amount);
    }

    [Fact]
    public void Backspace_takes_a_digit_off()
    {
        var runner = Started();
        Choose(runner, TempleDonate);
        Choose(runner, EventRunner.DonateGive);

        Digit(runner, '1');
        Digit(runner, '2');
        Press(runner, VirtualKey.Backspace);

        Assert.Equal(1, runner.Giving!.Value.Amount);
    }

    [Fact]
    public void Return_hands_the_money_over_and_goes_back()
    {
        int given = -1;
        var runner = Started(apply: amount => { given = amount; return amount; });

        Choose(runner, TempleDonate);
        Choose(runner, EventRunner.DonateGive);

        Digit(runner, '4');
        Digit(runner, '0');
        Press(runner, VirtualKey.Return);

        Assert.Equal(40, given);
        Assert.Null(runner.Giving);
        Assert.True(runner.DonateOpen);
        Assert.Equal(6, runner.Menu.Count);
    }

    [Fact]
    public void Giving_nothing_is_allowed_and_gives_nothing()
    {
        int given = -1;
        var runner = Started(apply: amount => { given = amount; return amount; });

        Choose(runner, TempleDonate);
        Choose(runner, EventRunner.DonateGive);
        Press(runner, VirtualKey.Return);

        Assert.Equal(0, given);
    }

    [Fact]
    public void Leaving_the_donate_menu_returns_to_the_temple()
    {
        var runner = Started();
        Choose(runner, TempleDonate);
        Choose(runner, EventRunner.DonateExit);

        Assert.False(runner.DonateOpen);
        Assert.Equal(7, runner.Menu.Count);
    }

    // ---- the trigger -----------------------------------------------------------------------------

    [Fact]
    public void Leaving_under_the_trigger_follows_the_events_own_chain()
    {
        // Both paths chain; what the trigger decides is *which*. The ordinary exit follows
        // ChainEventHappen, the donation one replaces the event with donationChain.
        var runner = Started(Temple(trigger: 100, chain: 42));

        var step = Choose(runner, TempleExit);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(55u, step.ChainTo);
    }

    [Fact]
    public void Leaving_over_the_trigger_fires_the_donation_chain()
    {
        int total = 0;
        var runner = Started(Temple(trigger: 100, chain: 42),
                             apply: amount => total += amount);

        Choose(runner, TempleDonate);
        Choose(runner, EventRunner.DonateGive);
        Digit(runner, '2');
        Digit(runner, '0');
        Digit(runner, '0');
        Press(runner, VirtualKey.Return);
        Choose(runner, EventRunner.DonateExit);

        var step = Choose(runner, TempleExit);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(42u, step.ChainTo);
    }

    [Fact]
    public void The_total_is_reset_as_the_chain_fires()
    {
        var runner = Started(Temple(trigger: 10, chain: 42), apply: amount => amount);

        Choose(runner, TempleDonate);
        Choose(runner, EventRunner.DonateGive);
        Digit(runner, '5');
        Digit(runner, '0');
        Press(runner, VirtualKey.Return);
        Choose(runner, EventRunner.DonateExit);
        Choose(runner, TempleExit);

        Assert.Equal(0, runner.TotalDonated);
    }
}
