using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the four remaining town-service shells — shop, temple, tavern and vault.
/// </summary>
/// <remarks>
/// With these the shell layer is complete: all seven town services present their menus and run
/// their exits. What remains behind them is the dozen inner screens, which is where the cost is —
/// see §camp and the training hall in the plan.
/// </remarks>
public class EventTownShellTests
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

    private static GameEventBase Base(EventType type, string text = "Welcome.",
                                      string text2 = "What will you have?") => new(
        new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
        new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        (int)type, 1, 0, 0, ChainEventHappen: 77, ChainEventNotHappen: 0,
        text, text2, "", []);

    private static TavernEvent Tavern(uint fight = 0, int forceExit = 0) =>
        new(Base(EventType.TavernEvent), forceExit, 0, 0, 1, 1, fight, 0, 0, 0, 0, [], []);

    private static ShopEvent Shop(int forceExit = 0) =>
        new(Base(EventType.ShopEvent), forceExit, 0, 0, 0, 0, 0, 0, 0,
            new ItemList([], new ReadyItems([])));

    private static VaultEvent Vault(int forceBackup = 0) =>
        new(Base(EventType.Vault), forceBackup, 0);

    private static TempleEvent Temple(int forceExit = 0) =>
        new(Base(EventType.TempleEvent), forceExit, 0, 0, 0, 0, 0,
            new SpellBook(0, []), 0);

    private static EventRunner Started(IGameEvent gameEvent, Func<uint, bool>? isValid = null)
    {
        var runner = new EventRunner { IsValidEvent = isValid ?? (_ => true) };
        runner.Begin(gameEvent, Font(), Box, Anchors);
        return runner;
    }

    private static EventStep Choose(EventRunner runner, int item)
    {
        for (int i = 0; i < item; i++)
        {
            runner.Handle(InputEvent.KeyDown(VirtualKey.Right));
        }
        return runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
    }

    private static string[] Labels(EventRunner runner) =>
        [.. runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text))];

    private static CharacterSheet TestSheet() => new(
        Name: "Sherlas of Hemlock", Gender: "MALE", Age: "17 YEARS", Status: "OKAY",
        Alignment: "TRUE NEUTRAL", Race: "HUMAN", Class: "RANGER", Level: "LEVEL 3",
        Hits: "18", MaxHits: "/22",
        ExperienceLines: ["RANGER 8000"],
        Abilities: ["18/75", "12", "9", "14", "16", "11"],
        Coins: []);

    // ---- the menus -----------------------------------------------------------------------------

    [Fact]
    public void Each_service_offers_its_own_menu()
    {
        Assert.Equal(["FIGHT", "DRINK", "LISTEN", "EXIT"], Labels(Started(Tavern())));

        Assert.Equal(["BUY", "ITEMS", "VIEW", "TAKE", "POOL", "SHARE", "APPRAISE", "EXIT"],
                     Labels(Started(Shop())));

        Assert.Equal(["VIEW", "TAKE", "POOL", "SHARE", "ITEMS", "EXIT"], Labels(Started(Vault())));
    }

    [Fact]
    public void The_temple_opens_on_a_welcome_before_its_menu()
    {
        // The only town service with two screens of its own, and the two use DIFFERENT text
        // fields: the welcome shows Text, the menu shows Text2.
        var runner = Started(Temple());

        Assert.Equal(["PRESS ENTER TO CONTINUE"], Labels(runner));
        Assert.Contains("Welcome", BitmapFont.Decode(runner.Text.Lines[0].Text));

        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(["HEAL", "DONATE", "VIEW", "TAKE", "POOL", "SHARE", "EXIT"], Labels(runner));
        Assert.Contains("What will you have", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    // ---- exit ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(3)]   // tavern
    public void Exit_runs_the_services_chain(int exitItem)
    {
        var step = Choose(Started(Tavern()), exitItem);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(77u, step.ChainTo);
    }

    [Fact]
    public void Every_service_backs_the_party_up_when_it_asks_to()
    {
        var tavern = Started(Tavern(forceExit: 1));
        Choose(tavern, 3);
        Assert.True(tavern.BackupRequested);

        var shop = Started(Shop(forceExit: 1));
        Choose(shop, 7);
        Assert.True(shop.BackupRequested);

        // The vault spells the same field ForceBackup rather than ForceExit -- one virtual, three
        // spellings across four event types.
        var vault = Started(Vault(forceBackup: 1));
        Choose(vault, 5);
        Assert.True(vault.BackupRequested);
    }

    [Fact]
    public void Escape_selects_exit_in_every_service()
    {
        foreach (var service in (IGameEvent[])[Tavern(), Shop(), Vault()])
        {
            var runner = Started(service);
            var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Escape));

            Assert.Equal(EventStepKind.Chain, step.Kind);
            Assert.Equal(77u, step.ChainTo);
        }
    }

    // ---- the tavern's brawl --------------------------------------------------------------------

    [Fact]
    public void Fight_chains_to_the_brawl()
    {
        var step = Choose(Started(Tavern(fight: 55)), 0);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(55u, step.ChainTo);
    }

    [Fact]
    public void A_tavern_with_no_brawl_says_so()
    {
        // The only place a town service tells the player why nothing happened rather than just
        // staying put.
        var runner = Started(Tavern(fight: 0));
        var step = Choose(runner, 0);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Contains("runs away", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    [Fact]
    public void A_brawl_naming_a_missing_event_says_the_same()
    {
        var runner = Started(Tavern(fight: 55), isValid: _ => false);
        Choose(runner, 0);

        Assert.Contains("runs away", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    // ---- what is not built yet -----------------------------------------------------------------

    [Theory]
    [InlineData(0, "BUY")]
    [InlineData(1, "ITEMS")]
    [InlineData(3, "TAKE")]
    [InlineData(4, "POOL")]
    [InlineData(5, "SHARE")]
    [InlineData(6, "APPRAISE")]
    public void A_shops_inner_screens_are_named(int item, string label)
    {
        var runner = Started(Shop());
        var step = Choose(runner, item);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.Contains(label, runner.Unimplemented);
    }

    [Fact]
    public void A_taverns_drink_and_listen_are_named()
    {
        foreach ((int item, string label) in (( int, string )[])[(1, "DRINK"), (2, "LISTEN")])
        {
            var runner = Started(Tavern());
            Choose(runner, item);

            Assert.Contains(label, runner.Unimplemented);
        }
    }

    [Fact]
    public void View_shows_the_character_sheet_where_a_service_offers_it()
    {
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            ActiveCharacterSheet = TestSheet,
        };
        runner.Begin(Vault(), Font(), Box, Anchors);

        var step = Choose(runner, 0);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.True(runner.CoversRoster);
    }
}
