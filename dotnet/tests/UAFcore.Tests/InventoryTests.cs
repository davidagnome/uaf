using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the shared inventory screen's data — the rows, the slot words, and the ready rules.
/// </summary>
/// <remarks>
/// Worth building once: three of the seven town services push this screen, and so does combat.
/// Four of its fourteen commands run so far; the rest need machinery the port does not have.
/// </remarks>
public class InventoryTests
{
    // ---- a small item database -------------------------------------------------------------------

    /// <summary>An item database record carrying only what the ready rules read.</summary>
    private static ItemRecord Record(uint slot, int hands = 1) =>
        new(new ItemNames(0, "", "", "", "", "", ""),
            HitArt: null, MissileArt: null,
            new ItemScalars("", 0, 0, 0, 0, 0, 0, 0),
            new ItemCombat(slot, hands, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, 0, 0,
                         new SpecabBlock([], [], []), []));

    /// <summary>
    /// The default design: a sword and a shield in the hands, armour on the body, a bow needing
    /// two hands, and a ring.
    /// </summary>
    private static ItemRecord? Database(string id) => id switch
    {
        "sword" => Record(Weapon),
        "dagger" => Record(Weapon),
        "shield" => Record(Shield),
        "armor" => Record(Armor),
        "bow" => Record(Weapon, hands: 2),
        "ring" => Record(Fingers),
        "banner" => Record(Weapon, hands: 3),
        "idol" => Record(ReadiedLocation.Cannot, hands: 0),
        "amulet" => Record(Neck, hands: 0),
        _ => null,
    };

    private static readonly uint Weapon = ReadiedLocation.WeaponHand;
    private static readonly uint Shield = ReadiedLocation.ShieldHand;
    private static readonly uint Armor = ReadiedLocation.BodyArmor;
    private static readonly uint Fingers = ReadiedLocation.Fingers;
    private static readonly uint Neck = ReadiedLocation.Neck;

    private static ItemInstance Item(string id = "sword", uint? ready = null, int cursed = 0,
                                     int quantity = 1) =>
        new(Key: 0, ItemId: id, LegacyItemId: 0, ReadyLocation: ready ?? Inventory.NotReady,
            Quantity: quantity, Identified: 1, Charges: 0, Cursed: (byte)cursed, Paid: 0);

    private static ItemList Carrying(params ItemInstance[] items) =>
        new(items, new ReadyItems([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]));

    private static ItemList Toggle(ItemList items, int index, out ReadyRefusal refusal) =>
        Inventory.ToggleReady(items, index, Database, out refusal);

    // ---- the two conversion tables ----------------------------------------------------------------

    [Fact]
    public void An_item_in_the_pack_holds_a_word_rather_than_zero()
    {
        // NOTRDY, not 0 -- and this is the whole reason the distinction matters, since 0 is a
        // perfectly good weapon hand.
        Assert.Equal(ReadiedLocation.Base38("NOTRDY"), Inventory.NotReady);
        Assert.NotEqual(0u, Inventory.NotReady);
        Assert.Equal(ReadiedLocation.WeaponHand, ReadiedLocation.Synonym(0));
    }

    [Fact]
    public void A_carried_item_and_a_database_record_convert_by_different_tables()
    {
        // Nine of eleven ordinals agree. Ordinal 3 does not, and nothing else distinguishes the
        // two tables -- so a port that uses one for both swaps gauntlets for quivers in silence.
        for (uint i = 0; i < 11; i++)
        {
            if (i == 3)
            {
                continue;
            }
            Assert.Equal(ReadiedLocation.Convert(i), ReadiedLocation.Synonym(i));
        }

        Assert.Equal(ReadiedLocation.Hands, ReadiedLocation.Convert(3));
        Assert.Equal(ReadiedLocation.AmmoQuiver, ReadiedLocation.Synonym(3));
    }

    [Fact]
    public void The_carried_table_reaches_slots_the_database_table_does_not()
    {
        // Synonym runs to 16, and names five body parts plus CANNOT and PACK that no item record
        // can hold as an ordinal.
        Assert.Equal(ReadiedLocation.Cannot, ReadiedLocation.Synonym(11));
        Assert.Equal(ReadiedLocation.Pack, ReadiedLocation.Synonym(16));
        Assert.Equal(17u, ReadiedLocation.Synonym(17));   // past the table, unchanged
    }

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
        Assert.Equal("WEAPON", Inventory.ReadyWord(Weapon));
        Assert.Equal("QUIVER", Inventory.ReadyWord(ReadiedLocation.AmmoQuiver));
    }

    [Fact]
    public void Both_the_packed_and_the_legacy_ordinal_forms_decode()
    {
        // A savegame can hold either -- the ordinal from an old design, the packed word from a
        // modern one -- so the screen has to read both. The shipped saves hold both.
        Assert.Equal("ARMOR", Inventory.ReadyWord(2));                    // legacy ordinal
        Assert.Equal("ARMOR", Inventory.ReadyWord(Armor));                // packed
        Assert.Equal("WEAPON", Inventory.ReadyWord(0));                   // zero is a weapon hand
    }

    [Fact]
    public void A_packed_value_naming_no_slot_shows_nothing()
    {
        // Base38 is not invertible in general, so the decode is a match against the words that
        // exist rather than an unpack -- and anything else honestly names no slot.
        Assert.Equal(string.Empty, Inventory.ReadyWord(0xDEADBEEF));
    }

    // ---- the rows ------------------------------------------------------------------------------

    [Fact]
    public void Rows_carry_the_index_of_the_item_behind_them()
    {
        // The screen pages, so a row's position is not the item's -- the index is what a command
        // acts on.
        var rows = Inventory.Rows(Carrying(Item("sword"), Item("shield", ready: Shield),
                                           Item("armor")));

        Assert.Equal([0, 1, 2], rows.Select(r => r.Index));
        Assert.Equal("SHIELD", rows[1].Ready);
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
    public void An_item_goes_to_the_slot_its_own_record_names()
    {
        // Not a slot the caller chose: a helm cannot be worn on the hand, so there is nothing to
        // choose. This is why READY needs the item database at all.
        var after = Toggle(Carrying(Item("armor")), 0, out var refusal);

        Assert.Equal(ReadyRefusal.None, refusal);
        Assert.Equal(Armor, after.Items[0].ReadyLocation);
        Assert.Equal("ARMOR", Inventory.ReadyWord(after.Items[0].ReadyLocation));
    }

    [Fact]
    public void Different_items_go_to_different_slots()
    {
        var items = Carrying(Item("sword"), Item("shield"), Item("ring"));

        items = Toggle(items, 0, out _);
        items = Toggle(items, 1, out _);
        items = Toggle(items, 2, out _);

        Assert.Equal([Weapon, Shield, Fingers],
                     items.Items.Select(i => i.ReadyLocation));
    }

    [Fact]
    public void Unreadying_puts_it_back_in_the_pack()
    {
        var after = Toggle(Carrying(Item("sword", ready: Weapon)), 0, out var refusal);

        Assert.Equal(ReadyRefusal.None, refusal);
        Assert.Equal(Inventory.NotReady, after.Items[0].ReadyLocation);
        Assert.False(Inventory.IsReady(after.Items[0]));
    }

    [Fact]
    public void An_item_stored_as_a_bare_zero_is_worn_not_packed()
    {
        // The shipped saves hold zeroes. Toggling one takes it off; an engine that read zero as
        // "in the pack" would put it on instead, and the player would see nothing change.
        var after = Toggle(Carrying(Item("sword", ready: 0)), 0, out _);

        Assert.Equal(Inventory.NotReady, after.Items[0].ReadyLocation);
    }

    [Fact]
    public void A_cursed_item_cannot_be_taken_off()
    {
        // The same rule that stops one being dropped. Note it only blocks UNreadying -- putting a
        // cursed thing on is never refused, which is rather the point of a cursed thing.
        var after = Toggle(Carrying(Item("sword", ready: Weapon, cursed: 1)), 0, out var refusal);

        Assert.Equal(ReadyRefusal.Cursed, refusal);
        Assert.Equal(Weapon, after.Items[0].ReadyLocation);
    }

    [Fact]
    public void A_cursed_item_in_the_pack_can_still_be_readied()
    {
        var after = Toggle(Carrying(Item("sword", cursed: 1)), 0, out var refusal);

        Assert.Equal(ReadyRefusal.None, refusal);
        Assert.Equal(Weapon, after.Items[0].ReadyLocation);
    }

    [Fact]
    public void An_index_outside_the_list_changes_nothing()
    {
        var items = Carrying(Item());

        Assert.Same(items, Toggle(items, 5, out _));
        Assert.Same(items, Toggle(items, -1, out _));
    }

    [Fact]
    public void Toggling_leaves_the_other_items_alone()
    {
        var after = Toggle(Carrying(Item("sword"), Item("armor"), Item("ring")), 1, out _);

        Assert.Equal(Inventory.NotReady, after.Items[0].ReadyLocation);
        Assert.Equal(Armor, after.Items[1].ReadyLocation);
        Assert.Equal(Inventory.NotReady, after.Items[2].ReadyLocation);
    }

    // ---- the refusals --------------------------------------------------------------------------

    [Fact]
    public void A_slot_holds_one_thing()
    {
        var items = Toggle(Carrying(Item("sword"), Item("dagger")), 0, out _);

        var after = Toggle(items, 1, out var refusal);

        Assert.Equal(ReadyRefusal.SlotTaken, refusal);
        Assert.Equal(Inventory.NotReady, after.Items[1].ReadyLocation);
    }

    [Fact]
    public void A_two_hander_needs_both_hands_empty()
    {
        var items = Toggle(Carrying(Item("shield"), Item("bow")), 0, out _);

        Toggle(items, 1, out var refusal);

        Assert.Equal(ReadyRefusal.TakesTwoHands, refusal);
    }

    [Fact]
    public void A_two_hander_already_held_leaves_no_hand_free()
    {
        var items = Toggle(Carrying(Item("bow"), Item("shield")), 0, out _);

        Toggle(items, 1, out var refusal);

        Assert.Equal(ReadyRefusal.NoFreeHands, refusal);
    }

    [Fact]
    public void A_two_hander_can_still_be_put_down()
    {
        // The reference's early exit: an item already worn is never refused, so the hand rules
        // that stopped its neighbour going on do not trap it either.
        var items = Toggle(Carrying(Item("bow")), 0, out _);

        var after = Toggle(items, 0, out var refusal);

        Assert.Equal(ReadyRefusal.None, refusal);
        Assert.Equal(Inventory.NotReady, after.Items[0].ReadyLocation);
    }

    [Fact]
    public void Money_is_carried_never_worn()
    {
        Toggle(Carrying(Item("_$GEM$_")), 0, out var refusal);
        Assert.Equal(ReadyRefusal.Money, refusal);

        Toggle(Carrying(Item("_$JEWELRY$_")), 0, out refusal);
        Assert.Equal(ReadyRefusal.Money, refusal);
    }

    [Fact]
    public void An_item_the_design_no_longer_defines_cannot_be_touched()
    {
        // Not even to take off: the reference looks the record up before it decides which way the
        // toggle goes, and gives up there.
        Toggle(Carrying(Item("vanished", ready: Weapon)), 0, out var refusal);

        Assert.Equal(ReadyRefusal.Unknown, refusal);
    }

    [Fact]
    public void An_empty_stack_cannot_be_readied()
    {
        Toggle(Carrying(Item("sword", quantity: 0)), 0, out var refusal);

        Assert.Equal(ReadyRefusal.NoQuantity, refusal);
    }

    [Fact]
    public void More_than_two_hands_is_declined_rather_than_modelled()
    {
        Toggle(Carrying(Item("banner")), 0, out var refusal);

        Assert.Equal(ReadyRefusal.TooManyHands, refusal);
    }

    [Fact]
    public void An_item_whose_slot_is_CANNOT_is_refused()
    {
        // A deliberate divergence: the reference skips the whole slot check for such an item and
        // then readies it at a location named CANNOT.
        Toggle(Carrying(Item("idol")), 0, out var refusal);

        Assert.Equal(ReadyRefusal.CannotBeWorn, refusal);
    }

    [Fact]
    public void A_handless_item_ignores_what_the_hands_are_holding()
    {
        var items = Toggle(Carrying(Item("bow"), Item("amulet")), 0, out _);

        var after = Toggle(items, 1, out var refusal);

        Assert.Equal(ReadyRefusal.None, refusal);
        Assert.Equal(Neck, after.Items[1].ReadyLocation);
    }

    // ---- counting ------------------------------------------------------------------------------

    [Fact]
    public void The_slot_count_and_the_slot_occupant_disagree_by_design()
    {
        // The reference asks two different questions three lines apart: GetReadiedCount matches on
        // the database record's slot, GetReadiedItem on the carried item's own. They differ only
        // for an item placed somewhere its record does not name -- which the engine itself can do.
        var odd = Carrying(Item("sword", ready: Shield));

        Assert.Equal(1, Inventory.ReadiedCount(odd, Weapon, Database));    // by the record
        Assert.Null(Inventory.WornIn(odd, Weapon));                        // by the item
        Assert.NotNull(Inventory.WornIn(odd, Shield));
    }

    [Fact]
    public void An_item_with_no_record_is_skipped_rather_than_crashing()
    {
        // The reference dereferences the lookup without checking and falls over here.
        var items = Carrying(Item("vanished", ready: Weapon), Item("sword", ready: Weapon));

        Assert.Equal(1, Inventory.ReadiedCount(items, Weapon, Database));
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
