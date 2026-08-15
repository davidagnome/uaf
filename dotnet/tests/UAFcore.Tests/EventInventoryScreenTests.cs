using UAF.Common;
using UAF.Media;
using UAF.Rules;
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

    private static ItemInstance Item(string id, uint? ready = null, byte cursed = 0) =>
        new(0, id, 0, ready ?? Inventory.NotReady, 1, 1, 0, cursed, 0);

    private static ItemList Carrying(params ItemInstance[] items) =>
        new(items, new ReadyItems([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]));

    /// <summary>
    /// A database where every item is a ring, so a whole pack of them can go on one at a time
    /// without the hand rules getting in the way of what these tests are about.
    /// </summary>
    private static ItemRecord? Database(string id) =>
        new(new ItemNames(0, "", "", "", "", "", ""), null, null,
            new ItemScalars("", 0, 0, 0, 0, 0, 0, 0),
            new ItemCombat(ReadiedLocation.Fingers, 0, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, 0, 0,
                         new SpecabBlock([], [], []), []));

    private static readonly uint Ring = ReadiedLocation.Fingers;

    /// <summary>A vault, whose menu has ITEMS at index 4 and EXIT at 5.</summary>
    private static VaultEvent Vault() => new(Base, 0, 0);

    private const int VaultItems = 4;

    /// <summary>How many rows these tests give the screen.</summary>
    /// <remarks>
    /// Stated rather than inherited: the page size is design configuration (<c>ITEMS_PER_PAGE</c>),
    /// so a test that assumes the default silently changes meaning when the default does. Eight
    /// keeps the arithmetic below small enough to read.
    /// </remarks>
    private const int Page = 8;

    private static EventRunner Started(ItemList? carried, Action<ItemList>? onChange = null)
    {
        ItemList? held = carried;
        var runner = new EventRunner
        {
            PageSize = Page,
            IsValidEvent = _ => true,
            ItemDatabase = Database,
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

    /// <summary>A stack of arrows, and a database that lets them be split and merged.</summary>
    private static ItemInstance Bundle(int key, int quantity) =>
        new(key, "Arrows", 0, Inventory.NotReady, quantity, 1, 0, 0, 0);

    private static ItemRecord? Bundles(string id) =>
        new(new ItemNames(0, "", "", "", "", "", ""), null, null,
            new ItemScalars("", 0, 0, 0, 0, 0, BundleQty: 20, 0),
            new ItemCombat(ReadiedLocation.Fingers, 0, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null,
                         CanBeHalvedJoined: 1, CanBeTradeDropSoldDep: 1,
                         new SpecabBlock([], [], []), []));

    private static EventRunner Splitting(ItemList carried)
    {
        ItemList? held = carried;
        var runner = new EventRunner
        {
            PageSize = Page,
            IsValidEvent = _ => true,
            ItemDatabase = Bundles,
            ActiveCharacterItems = () => held,
            ApplyItemChange = changed => held = changed,
        };
        runner.Begin(Vault(), Font(), Box, Anchors);
        return runner;
    }

    /// <summary>A vault screen with real vaults behind it.</summary>
    private static (EventRunner Runner, GlobalVaults Vaults) Depositing(ItemList carried)
    {
        ItemList? held = carried;
        var vaults = new GlobalVaults(MoneyRules.Default);

        var runner = new EventRunner
        {
            PageSize = Page,
            IsValidEvent = _ => true,
            ItemDatabase = Bundles,
            ActiveCharacterItems = () => held,
            ApplyItemChange = changed => held = changed,
            Vaults = () => vaults,
        };
        runner.Begin(Vault(), Font(), Box, Anchors);
        return (runner, vaults);
    }

    /// <summary>DEPOSIT moves the row out of the party and into the vault.</summary>
    [Fact]
    public void Deposit_moves_the_row_into_the_vault()
    {
        var (runner, vaults) = Depositing(Carrying(Bundle(1, 12), Bundle(2, 3)));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Deposit);

        Assert.Equal(InventoryBundles.DepositRefusal.None, runner.LastDepositRefusal);
        Assert.Single(runner.InventoryRows!);

        var deposited = Assert.Single(vaults.ItemsIn(0));
        Assert.Equal(12, deposited.Quantity);
        Assert.Null(runner.Unimplemented);
    }

    /// <summary>
    /// A worn item stays put, and the screen says why.
    /// </summary>
    /// <remarks>
    /// The one refusal the reference reports to the player; the rest simply redraw.
    /// </remarks>
    [Fact]
    public void Deposit_refuses_a_worn_item()
    {
        var worn = new ItemInstance(1, "Arrows", 0, ReadiedLocation.Fingers, 5, 1, 0, 0, 0);
        var (runner, vaults) = Depositing(Carrying(worn));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Deposit);

        Assert.Equal(InventoryBundles.DepositRefusal.IsReadied, runner.LastDepositRefusal);
        Assert.Single(runner.InventoryRows!);
        Assert.Empty(vaults.ItemsIn(0));
    }

    /// <summary>Without vaults the command does nothing rather than throwing.</summary>
    /// <remarks>
    /// The runner draws screens and does not own world state, so it can legitimately be built
    /// without any.
    /// </remarks>
    [Fact]
    public void Deposit_without_vaults_does_nothing()
    {
        var runner = Splitting(Carrying(Bundle(1, 5)));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Deposit);

        Assert.Single(runner.InventoryRows!);
        Assert.Null(runner.Unimplemented);
    }

    /// <summary>HALVE splits the selected row, and the screen shows both halves.</summary>
    [Fact]
    public void Halve_splits_the_selected_row()
    {
        var runner = Splitting(Carrying(Bundle(1, 10)));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Halve);

        Assert.Equal(2, runner.InventoryRows!.Count);
        Assert.Equal(5, runner.InventoryRows[0].Quantity);
        Assert.Equal(5, runner.InventoryRows[1].Quantity);

        // And nothing was reported as unimplemented.
        Assert.Null(runner.Unimplemented);
    }

    /// <summary>JOIN gathers the stacks back into the selected one.</summary>
    [Fact]
    public void Join_gathers_the_stacks_into_one()
    {
        var runner = Splitting(Carrying(Bundle(1, 4), Bundle(2, 3), Bundle(3, 2)));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Join);

        var only = Assert.Single(runner.InventoryRows!);
        Assert.Equal(9, only.Quantity);
        Assert.Null(runner.Unimplemented);
    }

    /// <summary>
    /// An item that cannot be split leaves the screen alone rather than reporting anything.
    /// </summary>
    /// <remarks>
    /// The reference simply redraws — there is no message for a refused HALVE, so a design's player
    /// sees nothing happen.
    /// </remarks>
    [Fact]
    public void A_refused_halve_changes_nothing()
    {
        // The default database has a bundle size of zero, so nothing can be split.
        var runner = Started(Carrying(Item("Long Sword")));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Halve);

        Assert.Single(runner.InventoryRows!);
        Assert.Null(runner.Unimplemented);
    }

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
        Assert.Equal(Ring, applied!.Items[0].ReadyLocation);
        Assert.Equal("FINGER", runner.InventoryRows![0].Ready);
        Assert.Equal(ReadyRefusal.None, runner.LastRefusal);
    }

    [Fact]
    public void A_refused_ready_changes_nothing_and_says_why()
    {
        // Two rings, one pair of hands' worth of fingers as far as the default rule is concerned.
        ItemList? applied = null;
        var runner = Started(Carrying(Item("Ring of Fire"), Item("Ring of Ice")),
                             changed => applied = changed);
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Ready);       // the first goes on
        applied = null;

        runner.Handle(InputEvent.KeyDown(VirtualKey.Down)); // select the second row
        Choose(runner, (int)InventoryCommand.Ready);

        Assert.Equal(ReadyRefusal.SlotTaken, runner.LastRefusal);
        Assert.Null(applied);
        Assert.Equal(string.Empty, runner.InventoryRows![1].Ready);
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
        var runner = Started(Carrying(Item("Cursed Ring", Ring, cursed: 1)));
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Ready);

        Assert.Equal(ReadyRefusal.Cursed, runner.LastRefusal);
        Assert.Equal("FINGER", runner.InventoryRows![0].Ready);
    }

    /// <summary>A full page and two over, so there is a short second page.</summary>
    private static ItemList TwoPages() =>
        Carrying([.. Enumerable.Range(0, Page + 2).Select(i => Item($"item{i}"))]);

    [Fact]
    public void The_list_pages_and_stops_at_both_ends()
    {
        var runner = Started(TwoPages());
        Choose(runner, VaultItems);

        Assert.Equal(0, runner.InventoryPage);
        Assert.Equal(Page, runner.InventoryPageRows.Count);

        Choose(runner, (int)InventoryCommand.Next);
        Assert.Equal(1, runner.InventoryPage);
        Assert.Equal(2, runner.InventoryPageRows.Count);

        // NEXT on the last page does nothing at all -- it does not wrap, and there is no
        // feedback either. That reads as a stuck key, and is what the reference does.
        Choose(runner, (int)InventoryCommand.Next);
        Assert.Equal(1, runner.InventoryPage);

        Choose(runner, (int)InventoryCommand.Prev);
        Assert.Equal(0, runner.InventoryPage);

        Choose(runner, (int)InventoryCommand.Prev);
        Assert.Equal(0, runner.InventoryPage);
    }

    [Fact]
    public void The_page_keys_turn_the_page_as_the_menu_entries_do()
    {
        var runner = Started(TwoPages());
        Choose(runner, VaultItems);

        runner.Handle(InputEvent.KeyDown(VirtualKey.PageDown));
        Assert.Equal(1, runner.InventoryPage);

        runner.Handle(InputEvent.KeyDown(VirtualKey.PageUp));
        Assert.Equal(0, runner.InventoryPage);
    }

    [Fact]
    public void Up_and_down_move_the_row_while_the_menu_keeps_the_horizontal_ones()
    {
        // The one screen where the arrow keys are split between two things at once.
        var runner = Started(TwoPages());
        Choose(runner, VaultItems);

        int menuBefore = runner.Menu.ActiveItem;

        runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
        runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
        Assert.Equal(2, runner.InventoryRowIndex);
        Assert.Equal(menuBefore, runner.Menu.ActiveItem);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Right));
        Assert.Equal(2, runner.InventoryRowIndex);
        Assert.NotEqual(menuBefore, runner.Menu.ActiveItem);
    }

    [Fact]
    public void The_row_cursor_wraps_within_the_page_rather_than_onto_the_next()
    {
        var runner = Started(TwoPages());
        Choose(runner, VaultItems);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Up));

        Assert.Equal(Page - 1, runner.InventoryRowIndex);
        Assert.Equal(0, runner.InventoryPage);
    }

    [Fact]
    public void The_row_cursor_clamps_onto_a_shorter_page()
    {
        // Eight rows on the first page, two on the second: a cursor left on row 7 has nowhere to
        // stand once the page turns.
        var runner = Started(TwoPages());
        Choose(runner, VaultItems);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Up));       // row 7, the last of the page
        Choose(runner, (int)InventoryCommand.Next);

        Assert.Equal(1, runner.InventoryRowIndex);
    }

    [Fact]
    public void The_row_the_cursor_is_on_is_the_one_that_is_readied()
    {
        ItemList? applied = null;
        var runner = Started(TwoPages(), changed => applied = changed);
        Choose(runner, VaultItems);

        runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
        runner.Handle(InputEvent.KeyDown(VirtualKey.Down));
        Choose(runner, (int)InventoryCommand.Ready);

        Assert.NotNull(applied);
        Assert.Equal(Ring, applied!.Items[2].ReadyLocation);
        Assert.Equal(Inventory.NotReady, applied.Items[0].ReadyLocation);
    }

    [Fact]
    public void Readying_acts_on_the_row_of_the_page_showing()
    {
        // The row index is not the item index once the list pages -- which is why a row carries
        // the item's own index rather than relying on its position.
        var many = TwoPages();
        ItemList? applied = null;
        var runner = Started(many, changed => applied = changed);
        Choose(runner, VaultItems);

        Choose(runner, (int)InventoryCommand.Next);
        Choose(runner, (int)InventoryCommand.Ready);

        // The ninth item -- the first on the second page -- and nothing on the first page.
        Assert.NotNull(applied);
        Assert.Equal(Ring, applied!.Items[Page].ReadyLocation);
        Assert.All(applied.Items.Take(Page),
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
