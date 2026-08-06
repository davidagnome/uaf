using UAF.Common;
using UAF.Media;
using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Drives the shop's BUY screen through the runner.</summary>
public class EventBuyTests
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

    private static ItemRecord Record(int cost) =>
        new(new ItemNames(0, "", "", "", "", "", ""),
            HitArt: null, MissileArt: null,
            new ItemScalars("", 0, cost, 0, 0, 0, 0, 0),
            new ItemCombat(ReadiedLocation.WeaponHand, 1, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, 0, 0,
                         new SpecabBlock([], [], []), []));

    private static ItemInstance Stocked(string id) =>
        new(0, id, 0, Inventory.NotReady, 1, Identified: 0, 0, 0, 0);

    private static ShopEvent Shop(int costFactor = (int)CostFactor.Normal, string[]? stock = null,
                                  int apprGems = 1, int apprJewels = 1) =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                NoPic, NoPic, (int)EventType.ShopEvent, 1, 0, 0,
                ChainEventHappen: 55, ChainEventNotHappen: 0,
                "You are in a shop.", "", "", []),
            ForceExit: 0, CostFactor: costFactor, CostToIdentify: 0,
            BuybackPercentage: 50, CanIdentify: 0,
            CanAppraiseGems: apprGems, CanAppraiseJewels: apprJewels,
            BuyItemsSoldOnly: 0,
            new ItemList([.. (stock ?? []).Select(Stocked)], new ReadyItems([])));

    /// <summary>Prices every id at ten times the digit in its name, so rows differ.</summary>
    private static ItemRecord? Priced(string id) =>
        id.StartsWith("item", StringComparison.Ordinal)
        && int.TryParse(id.AsSpan(4), out int n)
            ? Record(n * 10)
            : null;

    private static string[] Stock(int count) =>
        [.. Enumerable.Range(1, count).Select(n => $"item{n}")];

    private static EventRunner Started(ShopEvent shop, int pageSize = 4, int purse = 1000,
                                       List<(string Id, CostFactor Factor)>? bought = null)
    {
        int coins = purse;

        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            PageSize = pageSize,
            ItemDatabase = Priced,
            ItemNames = id => id.ToUpperInvariant(),
            CanAfford = cost => cost <= coins,
            BuyItem = (id, factor) =>
            {
                int cost = Priced(id) is { } record ? Shopping.Price(record, factor) : 0;
                if (cost > coins)
                {
                    return BuyRefusal.NotEnoughMoney;
                }

                coins -= cost;
                bought?.Add((id, factor));
                return BuyRefusal.None;
            },
        };

        runner.Begin(shop, Font(), Box, Anchors);
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

    private static string[] Labels(EventRunner runner) =>
        [.. runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text))];

    private static EventRunner Opened(ShopEvent shop, int pageSize = 4, int purse = 1000,
                                      List<(string Id, CostFactor Factor)>? bought = null)
    {
        var runner = Started(shop, pageSize, purse, bought);
        Choose(runner, 0);                              // BUY
        return runner;
    }

    // ---- the shelf ---------------------------------------------------------------------------

    [Fact]
    public void Buy_opens_the_shelf()
    {
        var runner = Opened(Shop(stock: Stock(3)));

        Assert.True(runner.BuyOpen);
        Assert.Equal(["BUY", "NEXT", "PREV", "EXIT"], Labels(runner));
        Assert.Equal(["ITEM1", "ITEM2", "ITEM3"], runner.ShopRows!.Select(r => r.Name));
    }

    [Fact]
    public void The_shelf_is_priced_through_the_shops_factor()
    {
        var runner = Opened(Shop((int)CostFactor.Multiply2, Stock(3)));

        Assert.Equal([20, 40, 60], runner.ShopRows!.Select(r => r.Cost));
    }

    [Fact]
    public void An_id_the_design_lost_prices_at_nothing()
    {
        var runner = Opened(Shop(stock: ["ghost"]));

        Assert.Equal([0], runner.ShopRows!.Select(r => r.Cost));
    }

    [Fact]
    public void The_shelf_pages()
    {
        var runner = Opened(Shop(stock: Stock(6)), pageSize: 4);

        Assert.Equal(["ITEM1", "ITEM2", "ITEM3", "ITEM4"],
                     runner.ShopPageRows.Select(r => r.Name));

        Choose(runner, 1);                              // NEXT
        Assert.Equal(["ITEM5", "ITEM6"], runner.ShopPageRows.Select(r => r.Name));

        Choose(runner, 2);                              // PREV
        Assert.Equal(["ITEM1", "ITEM2", "ITEM3", "ITEM4"],
                     runner.ShopPageRows.Select(r => r.Name));
    }

    [Fact]
    public void A_list_that_fits_on_one_page_darkens_both_page_keys()
    {
        var runner = Opened(Shop(stock: Stock(4)), pageSize: 4);

        Assert.False(runner.Menu.Items[1].Enabled);
        Assert.False(runner.Menu.Items[2].Enabled);
    }

    [Fact]
    public void The_page_keys_light_at_the_ends_they_can_reach()
    {
        var runner = Opened(Shop(stock: Stock(6)), pageSize: 4);

        Assert.True(runner.Menu.Items[1].Enabled);      // NEXT
        Assert.False(runner.Menu.Items[2].Enabled);     // PREV, at the start

        Choose(runner, 1);

        Assert.False(runner.Menu.Items[1].Enabled);     // NEXT, at the end
        Assert.True(runner.Menu.Items[2].Enabled);
    }

    [Fact]
    public void Paging_onto_a_short_page_pulls_the_cursor_back()
    {
        var runner = Opened(Shop(stock: Stock(6)), pageSize: 4);

        Press(runner, VirtualKey.Down);
        Press(runner, VirtualKey.Down);
        Press(runner, VirtualKey.Down);
        Assert.Equal(3, runner.ShopRowIndex);

        Choose(runner, 1);                              // NEXT -- only two rows left

        Assert.Equal(1, runner.ShopRowIndex);
    }

    [Fact]
    public void The_item_cursor_wraps_within_the_page()
    {
        var runner = Opened(Shop(stock: Stock(6)), pageSize: 4);

        Press(runner, VirtualKey.Up);
        Assert.Equal(3, runner.ShopRowIndex);

        Press(runner, VirtualKey.Down);
        Assert.Equal(0, runner.ShopRowIndex);
    }

    // ---- what BUY does -----------------------------------------------------------------------

    [Fact]
    public void Buy_takes_the_row_the_cursor_is_on()
    {
        var bought = new List<(string, CostFactor)>();
        var runner = Opened(Shop(stock: Stock(3)), bought: bought);

        Press(runner, VirtualKey.Down);
        Press(runner, VirtualKey.Down);
        Choose(runner, 0);

        Assert.Equal([("item3", CostFactor.Normal)], bought);
    }

    [Fact]
    public void Buy_takes_the_row_on_the_page_it_is_showing()
    {
        var bought = new List<(string, CostFactor)>();
        var runner = Opened(Shop(stock: Stock(6)), pageSize: 4, bought: bought);

        Choose(runner, 1);                              // NEXT
        Press(runner, VirtualKey.Down);                 // the second row of the short page
        Choose(runner, 0);

        Assert.Equal([("item6", CostFactor.Normal)], bought);
    }

    [Fact]
    public void The_shops_factor_reaches_the_purchase()
    {
        var bought = new List<(string, CostFactor)>();
        var runner = Opened(Shop((int)CostFactor.Divide2, Stock(1)), bought: bought);

        Choose(runner, 0);

        Assert.Equal([("item1", CostFactor.Divide2)], bought);
    }

    [Fact]
    public void The_shelf_does_not_shrink_as_things_are_bought()
    {
        // A shop's stock is a list of what it offers, not a count of what it has.
        var bought = new List<(string, CostFactor)>();
        var runner = Opened(Shop(stock: Stock(1)), bought: bought);

        Choose(runner, 0);
        Choose(runner, 0);
        Choose(runner, 0);

        Assert.Equal(3, bought.Count);
        Assert.Single(runner.ShopRows!);
    }

    [Fact]
    public void Buy_darkens_on_the_price_of_the_row_under_the_cursor()
    {
        // A purse of 25 covers item1 and item2 but not item3.
        var runner = Opened(Shop(stock: Stock(3)), purse: 25);

        Assert.True(runner.Menu.Items[0].Enabled);

        Press(runner, VirtualKey.Down);
        Assert.True(runner.Menu.Items[0].Enabled);

        Press(runner, VirtualKey.Down);
        Assert.False(runner.Menu.Items[0].Enabled);
    }

    [Fact]
    public void Buy_darkens_once_the_money_runs_out()
    {
        var runner = Opened(Shop(stock: Stock(1)), purse: 15);

        Assert.True(runner.Menu.Items[0].Enabled);

        Choose(runner, 0);                              // 10 spent, 5 left

        Assert.False(runner.Menu.Items[0].Enabled);
    }

    [Fact]
    public void An_empty_shop_darkens_buy()
    {
        var runner = Opened(Shop(stock: []));

        Assert.False(runner.Menu.Items[0].Enabled);
        Assert.Empty(runner.ShopRows!);
    }

    [Fact]
    public void A_refusal_is_reported_and_nothing_else_happens()
    {
        var runner = Opened(Shop(stock: Stock(3)), purse: 5);

        Press(runner, VirtualKey.Down);
        Press(runner, VirtualKey.Down);
        Press(runner, VirtualKey.Return);               // BUY, dark but pressed

        Assert.Equal(BuyRefusal.NotEnoughMoney, runner.LastPurchase);
    }

    // ---- leaving -----------------------------------------------------------------------------

    [Fact]
    public void Exit_returns_to_the_shop_menu()
    {
        var runner = Opened(Shop(stock: Stock(3)));

        Press(runner, VirtualKey.Escape);

        Assert.False(runner.BuyOpen);
        Assert.Equal(["BUY", "ITEMS", "VIEW", "TAKE", "POOL", "SHARE", "APPRAISE", "EXIT"],
                     Labels(runner));
    }

    [Fact]
    public void The_shop_reaches_the_appraisal_too()
    {
        var runner = Started(Shop(stock: Stock(1)));
        runner.AppraiseKind = kind => (kind == Valuable.Gem ? "GEMS" : "JEWELRY", 1);

        Choose(runner, 6);                              // APPRAISE

        Assert.True(runner.AppraiseOpen);
        Assert.Equal(["GEMS", "JEWELRY", "EXIT"], Labels(runner));

        Press(runner, VirtualKey.Escape);

        Assert.False(runner.AppraiseOpen);
        Assert.Equal(8, runner.Menu.Count);              // back on the shop's own menu
    }

    [Fact]
    public void A_shop_that_appraises_one_kind_darkens_the_other()
    {
        // The shop is the only service that passes canApprGems/canApprJewels; a temple pushes the
        // screen without them and so appraises both.
        var runner = Started(Shop(stock: Stock(1), apprJewels: 0));
        runner.AppraiseKind = kind => (kind == Valuable.Gem ? "GEMS" : "JEWELRY", 3);

        Choose(runner, 6);

        Assert.True(runner.Menu.Items[0].Enabled);
        Assert.False(runner.Menu.Items[1].Enabled);
    }

    [Fact]
    public void A_shop_that_appraises_nothing_darkens_the_entry_itself()
    {
        var runner = Started(Shop(stock: Stock(1), apprGems: 0, apprJewels: 0));

        Assert.False(runner.Menu.Items[6].Enabled);
    }

    [Fact]
    public void Buy_darkens_for_a_character_who_is_not_well()
    {
        var runner = Started(Shop(stock: Stock(1)));
        runner.ActiveCharacterOkay = () => false;
        runner.Begin(Shop(stock: Stock(1)), Font(), Box, Anchors);

        Assert.False(runner.Menu.Items[0].Enabled);
    }

    [Fact]
    public void Exiting_the_shop_still_chains()
    {
        var runner = Started(Shop(stock: Stock(1)));

        for (int i = 0; i < runner.Menu.Count && runner.Menu.ActiveItem != 7; i++)
        {
            Press(runner, VirtualKey.Right);
        }

        var step = runner.Handle(InputEvent.KeyDown(VirtualKey.Return));

        Assert.Equal(EventStep.To(55), step);
    }
}
