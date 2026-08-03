using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the small-town hub — the first of the town services to run.
/// </summary>
/// <remarks>
/// The cheapest of the seven: a horizontal menu of six destinations and an exit, and nothing about
/// the party changes. What it establishes for the rest is the shape — a fixed menu whose entries
/// are chains, an Escape that selects rather than cancels, and a destination that names no event
/// leaving the player where they were.
/// </remarks>
public class EventSmallTownTests
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

    private static readonly GameEventBase Base = new(
        new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
        new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        new PicRecord(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        (int)EventType.SmallTown, 1, 0, 0,
        ChainEventHappen: 77, ChainEventNotHappen: 0,
        "The market square.", "", "", []);

    private static SmallTownEvent Town(
        uint temple = 10, uint training = 11, uint shop = 12,
        uint inn = 13, uint tavern = 14, uint vault = 15) =>
        new(Base, Unused: 0, TempleChain: temple, TrainingHallChain: training, ShopChain: shop,
            InnChain: inn, TavernChain: tavern, VaultChain: vault);

    private static EventRunner Started(SmallTownEvent town, Func<uint, bool>? isValid = null)
    {
        var runner = new EventRunner { IsValidEvent = isValid ?? (_ => true) };
        runner.Begin(town, Font(), Box, Anchors);
        return runner;
    }

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

    [Fact]
    public void The_screen_offers_the_six_destinations_and_an_exit()
    {
        var runner = Started(Town());

        Assert.Equal(
            ["TEMPLE", "TRAINING HALL", "SHOP", "INN", "PUB", "VAULT", "EXIT"],
            runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text)));

        // Horizontal, which no other event this runner presents is.
        Assert.Equal(MenuOrientation.Horizontal, runner.Menu.Orientation);
        Assert.Contains("market square", BitmapFont.Decode(runner.Text.Lines[0].Text));
    }

    [Theory]
    [InlineData(0, 10u)]
    [InlineData(1, 11u)]
    [InlineData(2, 12u)]
    [InlineData(3, 13u)]
    [InlineData(4, 14u)]
    [InlineData(5, 15u)]
    public void Each_destination_chains_to_its_own_target(int item, uint expected)
    {
        // PUB is the menu's name for the tavern chain -- the fifth entry, not a sixth field.
        var step = Choose(Started(Town()), item);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(expected, step.ChainTo);
    }

    [Fact]
    public void Exit_runs_the_towns_own_chain()
    {
        var step = Choose(Started(Town()), EventRunner.SmallTownExit);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(77u, step.ChainTo);
    }

    [Fact]
    public void Escape_selects_exit_rather_than_cancelling()
    {
        // menu.MapKeyCodeToMenuItem(KC_ESCAPE, 7): a player who backs out of a town still runs its
        // chain, which is not what cancelling would do.
        var runner = Started(Town());
        var step = Press(runner, VirtualKey.Escape);

        Assert.Equal(EventStepKind.Chain, step.Kind);
        Assert.Equal(77u, step.ChainTo);
    }

    [Fact]
    public void A_destination_that_names_no_event_leaves_the_player_on_the_screen()
    {
        // The reference pushes a DO_NOTHING_EVENT, which returns to the town -- so choosing SHOP in
        // a town with no shop is a no-op, NOT a fallback to the town's own chain.
        var runner = Started(Town(), isValid: id => id != 12);
        var step = Choose(runner, 2);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.True(runner.IsActive);
    }

    [Fact]
    public void A_destination_of_zero_is_the_same_no_op()
    {
        var runner = Started(Town(shop: 0));
        var step = Choose(runner, 2);

        Assert.Equal(EventStepKind.Running, step.Kind);
        Assert.True(runner.IsActive);
    }

    [Fact]
    public void The_escape_mapping_does_not_leak_into_the_next_event()
    {
        // It is per-event state, and every other type's Escape must go on doing nothing.
        var runner = Started(Town());
        runner.Begin(new TextEvent(Base, 0, 0, 0, 0, ""), Font(), Box, Anchors);

        Assert.Equal(EventStepKind.Running, Press(runner, VirtualKey.Escape).Kind);
        Assert.True(runner.IsActive);
    }
}
