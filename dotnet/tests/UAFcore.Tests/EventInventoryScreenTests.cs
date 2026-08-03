using UAF.Common;
using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the inventory as a screen over a town service — opening it, paging it, readying through
/// it, and getting back to what pushed it.
/// </summary>
/// <remarks>
/// <b>The inventory replaces the service's menu rather than drawing over it</b>, unlike the
/// character sheet. The reference gets the return for free by pushing an event and popping it;
/// this runner presents one event at a time and has to put the parent back by hand, which is the
/// thing most worth testing here.
/// </remarks>
public class EventInventoryScreenTests
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
        (int)EventType.Vault, 1, 0, 0, ChainEventHappen: 77, ChainEventNotHappen: 0,
        "The vault.", "", "", []);

    private static ItemInstance Item(string id, uint ready = 0, byte cursed = 0) =>
        new(0, id, 0, ready, 1, 1, 0, cursed, 0);

    private static ItemList Carrying(params ItemInstance[] items) =>
        new(items, new ReadyItems([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]));

    /// <summary>A vault, whose menu has ITEMS at index 4 and EXIT at 5.</summary>
    private static VaultEvent Vault() => new(Base, 0, 0);

    private const int VaultItems = 4;

    private static EventRunner Started(ItemList? carried, Action<ItemList>? onChange = null)
    {
        ItemList? held = carried;
        var runner = new EventRunner
        {
            IsValidEvent = _ => true,
            ActiveCharacterItems = () => held,
            ApplyItemChange = changed =>
            {
                held = changed;
                onChange?.Invoke(changed);
            },
        };
        runner.Begin(Vault(), Font(), Box, Anchors);
        return runner;
    }

    /// <summary>
    /// Walks the menu to <paramref name="item"/> and commits it.
    /// </summary>
    /// <remarks>
    /// The step count is computed from where the selection already is, not from zero — this screen
    /// is the first that takes several commands in a row without rebuilding its menu, and pressing
    /// Right a fixed number of times from an arbitrary start lands somewhere else entirely.
    /// </remarks>
    private static void Choose(EventRunner runner, int item)
    {
        int count = runner.Menu.Count;
        int steps = count == 0 ? 0 : ((item - runner.Menu.ActiveItem) % count + count) % count;

        for (int i = 0; i < steps; i++)
        {
            runner.Handle(InputEvent.KeyDown(VirtualKey.Right));
        }
        runner.Handle(InputEvent.KeyDown(VirtualKey.Return));
    }

    private static string[] Labels(EventRunner runner) =>
        [.. runner.Menu.Items.Select(i => BitmapFont.Decode(i.Text))];

    [Fact]
    public void Items_opens_the_inventory_over_the_service()
    {
        var runner = Started(Carrying(Item("Long Sword")));
        Choose(runner, VaultItems);

        Assert.True(runner.InventoryOpen);
        Assert.Equal("READY", Labels(runner)[0]);
        Assert.Equal("Long Sword", runner.InventoryRows![0].Name);
    }

    [Fact]
    public void Exiting_the_inventory_puts_the_services_menu_back()
    {
        // The reference pops an event; this runner has to rebuild what was underneath.
        var runner = Started(Carrying(Item("Long Sword")));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Exit);

        Assert.False(runner.InventoryOpen);
        Assert.Equal(["VIEW", "TAKE", "POOL", "SHARE", "ITEMS", "EXIT"], Labels(runner));
        Assert.True(runner.IsActive);
    }

    [Fact]
    public void Escape_closes_the_inventory_rather_than_the_service()
    {
        var runner = Started(Carrying(Item("Long Sword")));
        Choose(runner, VaultItems);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Escape));

        Assert.False(runner.InventoryOpen);
        Assert.True(runner.IsActive);
    }

    [Fact]
    public void Ready_puts_an_item_on_and_the_row_shows_where()
    {
        ItemList? applied = null;
        var runner = Started(Carrying(Item("Long Sword")), changed => applied = changed);
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Ready);

        Assert.NotNull(applied);
        Assert.Equal(EventRunner.DefaultReadySlot, applied!.Items[0].ReadyLocation);
        Assert.Equal("WEAPON", runner.InventoryRows![0].Ready);
    }

    [Fact]
    public void Ready_takes_it_off_again()
    {
        var runner = Started(Carrying(Item("Long Sword")));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Ready);
        Choose(runner, (int)InventoryCommand.Ready);

        Assert.Equal(string.Empty, runner.InventoryRows![0].Ready);
    }

    [Fact]
    public void A_cursed_item_cannot_be_taken_off_through_the_screen()
    {
        var runner = Started(Carrying(Item("Cursed Blade", EventRunner.DefaultReadySlot, cursed: 1)));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Ready);

        Assert.Equal("WEAPON", runner.InventoryRows![0].Ready);
    }

    [Fact]
    public void The_list_pages_and_wraps()
    {
        // A page holds TreasurePageSize rows; ten items therefore need two.
        var many = Carrying([.. Enumerable.Range(0, 10).Select(i => Item($"item{i}"))]);
        var runner = Started(many);
        Choose(runner, VaultItems);

        Assert.Equal(0, runner.InventoryPage);
        Assert.Equal(EventRunner.TreasurePageSize, runner.InventoryPageRows.Count);

        Choose(runner, (int)InventoryCommand.Next);
        Assert.Equal(1, runner.InventoryPage);
        Assert.Equal(2, runner.InventoryPageRows.Count);

        // NEXT off the end wraps rather than sticking, as the reference's paging does.
        Choose(runner, (int)InventoryCommand.Next);
        Assert.Equal(0, runner.InventoryPage);

        Choose(runner, (int)InventoryCommand.Prev);
        Assert.Equal(1, runner.InventoryPage);
    }

    [Fact]
    public void Readying_acts_on_the_row_of_the_page_showing()
    {
        // The row index is not the item index once the list pages -- which is why a row carries
        // the item's own index rather than relying on its position.
        var many = Carrying([.. Enumerable.Range(0, 10).Select(i => Item($"item{i}"))]);
        ItemList? applied = null;
        var runner = Started(many, changed => applied = changed);
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Next);
        Choose(runner, (int)InventoryCommand.Ready);

        // The ninth item -- the first on the second page -- and nothing on the first page.
        Assert.NotNull(applied);
        Assert.Equal(EventRunner.DefaultReadySlot,
                     applied!.Items[EventRunner.TreasurePageSize].ReadyLocation);
        Assert.All(applied.Items.Take(EventRunner.TreasurePageSize),
                   i => Assert.Equal(Inventory.NotReady, i.ReadyLocation));
    }

    [Fact]
    public void The_commands_this_port_has_not_built_are_named()
    {
        var runner = Started(Carrying(Item("Long Sword")));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Trade);

        Assert.True(runner.InventoryOpen);
        Assert.Contains("TRADE", runner.Unimplemented);
    }

    [Fact]
    public void A_caller_with_no_party_leaves_items_named()
    {
        var runner = Started(carried: null);
        Choose(runner, VaultItems);

        Assert.False(runner.InventoryOpen);
        Assert.Contains("ITEMS", runner.Unimplemented);
    }

    [Fact]
    public void The_inventory_does_not_leak_into_the_next_event()
    {
        var runner = Started(Carrying(Item("Long Sword")));
        Choose(runner, VaultItems);
        Assert.True(runner.InventoryOpen);

        runner.Begin(Vault(), Font(), Box, Anchors);

        Assert.False(runner.InventoryOpen);
        Assert.Equal(0, runner.InventoryPage);
    }
}
