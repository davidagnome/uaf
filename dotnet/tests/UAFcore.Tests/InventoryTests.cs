using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the shared inventory screen's data — the rows, the slot words and the ready toggle.
/// </summary>
/// <remarks>
/// Worth building once: three of the seven town services push this screen, and so does combat.
/// Four of its fourteen commands run so far; the rest need machinery the port does not have.
/// </remarks>
public class InventoryTests
{
    private static ItemInstance Item(string id = "Long Sword", uint ready = 0, int cursed = 0,
                                     int quantity = 1) =>
        new(Key: 0, ItemId: id, LegacyItemId: 0, ReadyLocation: ready, Quantity: quantity,
            Identified: 1, Charges: 0, Cursed: (byte)cursed, Paid: 0);

    private static ItemList Carrying(params ItemInstance[] items) =>
        new(items, new ReadyItems([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]));

    private static readonly uint WeaponHand = ReadiedLocation.Convert(0);

    // ---- the slot word -------------------------------------------------------------------------

    [Fact]
    public void An_item_in_the_pack_shows_no_slot()
    {
        Assert.Equal(string.Empty, Inventory.ReadyWord(Inventory.NotReady));
    }

    [Fact]
    public void A_readied_item_shows_the_slot_it_is_worn_in()
    {
        // The screen shows the word, not a tick, because there are eleven places to wear a thing.
        Assert.Equal("WEAPON", Inventory.ReadyWord(WeaponHand));
        Assert.Equal("QUIVER", Inventory.ReadyWord(ReadiedLocation.Convert(10)));
    }

    [Fact]
    public void Both_the_packed_and_the_legacy_ordinal_forms_decode()
    {
        // A savegame can hold either -- the ordinal from an old design, the packed word from a
        // modern one -- so the screen has to read both.
        Assert.Equal("ARMOR", Inventory.ReadyWord(2));                       // legacy ordinal
        Assert.Equal("ARMOR", Inventory.ReadyWord(ReadiedLocation.Convert(2)));  // packed
    }

    [Fact]
    public void A_packed_value_naming_no_slot_shows_nothing()
    {
        // Base38 is not invertible in general, so the decode is a match against the eleven words
        // that exist rather than an unpack -- and anything else honestly names no slot.
        Assert.Equal(string.Empty, Inventory.ReadyWord(0xDEADBEEF));
    }

    // ---- the rows ------------------------------------------------------------------------------

    [Fact]
    public void Rows_carry_the_index_of_the_item_behind_them()
    {
        // The screen pages, so a row's position is not the item's -- the index is what a command
        // acts on.
        var rows = Inventory.Rows(Carrying(Item("A"), Item("B", ready: WeaponHand), Item("C")));

        Assert.Equal([0, 1, 2], rows.Select(r => r.Index));
        Assert.Equal("WEAPON", rows[1].Ready);
        Assert.Equal(string.Empty, rows[0].Ready);
    }

    [Fact]
    public void Rows_resolve_names_and_prices_when_the_caller_supplies_them()
    {
        var rows = Inventory.Rows(
            Carrying(Item("sword-id")),
            itemName: id => id == "sword-id" ? "Long Sword" : null,
            itemCost: _ => 15);

        Assert.Equal("Long Sword", rows[0].Name);
        Assert.Equal(15, rows[0].Cost);
    }

    [Fact]
    public void An_unresolvable_name_falls_back_to_the_id()
    {
        // Better than a blank line: an item the design no longer defines is still visible.
        var rows = Inventory.Rows(Carrying(Item("missing-id")), itemName: _ => null);

        Assert.Equal("missing-id", rows[0].Name);
    }

    [Fact]
    public void A_vault_or_camp_shows_no_price()
    {
        Assert.Equal(0, Inventory.Rows(Carrying(Item()))[0].Cost);
    }

    // ---- readying ------------------------------------------------------------------------------

    [Fact]
    public void Readying_an_item_puts_it_in_the_named_slot()
    {
        var after = Inventory.ToggleReady(Carrying(Item()), 0, WeaponHand);

        Assert.Equal(WeaponHand, after.Items[0].ReadyLocation);
        Assert.Equal("WEAPON", Inventory.ReadyWord(after.Items[0].ReadyLocation));
    }

    [Fact]
    public void Unreadying_puts_it_back_in_the_pack()
    {
        var after = Inventory.ToggleReady(Carrying(Item(ready: WeaponHand)), 0, WeaponHand);

        Assert.Equal(Inventory.NotReady, after.Items[0].ReadyLocation);
    }

    [Fact]
    public void A_cursed_item_cannot_be_taken_off()
    {
        // The same rule that stops one being dropped. Note it only blocks UNreadying -- putting a
        // cursed thing on is never refused, which is rather the point of a cursed thing.
        var cursed = Carrying(Item(ready: WeaponHand, cursed: 1));
        var after = Inventory.ToggleReady(cursed, 0, WeaponHand);

        Assert.Equal(WeaponHand, after.Items[0].ReadyLocation);
    }

    [Fact]
    public void A_cursed_item_in_the_pack_can_still_be_readied()
    {
        var after = Inventory.ToggleReady(Carrying(Item(cursed: 1)), 0, WeaponHand);

        Assert.Equal(WeaponHand, after.Items[0].ReadyLocation);
    }

    [Fact]
    public void An_index_outside_the_list_changes_nothing()
    {
        var items = Carrying(Item());

        Assert.Same(items, Inventory.ToggleReady(items, 5, WeaponHand));
        Assert.Same(items, Inventory.ToggleReady(items, -1, WeaponHand));
    }

    [Fact]
    public void Toggling_leaves_the_other_items_alone()
    {
        var after = Inventory.ToggleReady(Carrying(Item("A"), Item("B"), Item("C")), 1, WeaponHand);

        Assert.Equal(Inventory.NotReady, after.Items[0].ReadyLocation);
        Assert.Equal(WeaponHand, after.Items[1].ReadyLocation);
        Assert.Equal(Inventory.NotReady, after.Items[2].ReadyLocation);
    }

    // ---- the menu ------------------------------------------------------------------------------

    [Fact]
    public void The_menu_is_the_references_own_fourteen_entries()
    {
        Assert.Equal(
            ["READY", "USE", "TRADE", "DROP", "DEPOSIT", "HALVE", "JOIN", "SELL", "ID ITEM",
             "EXAMINE", "EXAMINE", "NEXT", "PREV", "EXIT"],
            Inventory.Menu.Select(e => e.Label));

        // Two entries share the word EXAMINE -- one for ordinary items and one for special items
        // and keys, which are different lists behind the same screen.
        Assert.Equal((int)InventoryCommand.Examine, 9);
        Assert.Equal((int)InventoryCommand.ExamineSpecial, 10);
    }

    [Fact]
    public void Four_of_the_fourteen_commands_run()
    {
        Assert.True(Inventory.Runs(InventoryCommand.Ready));
        Assert.True(Inventory.Runs(InventoryCommand.Next));
        Assert.True(Inventory.Runs(InventoryCommand.Prev));
        Assert.True(Inventory.Runs(InventoryCommand.Exit));

        Assert.False(Inventory.Runs(InventoryCommand.Trade));
        Assert.False(Inventory.Runs(InventoryCommand.Sell));
        Assert.False(Inventory.Runs(InventoryCommand.Identify));
    }
}
