using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Splitting and merging bundles — the inventory's HALVE and JOIN.
/// </summary>
public class InventoryBundleTests
{
    /// <summary>An item record with the two fields these rules read.</summary>
    private static ItemRecord Record(int bundleQty, int canHalveJoin, int canLeave = 1) =>
        new(new ItemNames(0, "", "", "", "", "", ""),
            HitArt: null, MissileArt: null,
            new ItemScalars("", 0, 0, 0, 0, 0, bundleQty, 0),
            new ItemCombat(ReadiedLocation.WeaponHand, 1, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, canHalveJoin, canLeave,
                         new SpecabBlock([], [], []), []));

    private static ItemInstance Item(string id, int key, int quantity, uint? where = null) =>
        new(key, id, 0, where ?? ReadiedLocation.NotReady, quantity, 1, 0, 0, 0);

    private static Func<string, ItemRecord?> Database(
        int bundleQty = 10, int canHalveJoin = 1) =>
        _ => Record(bundleQty, canHalveJoin);

    /// <summary>
    /// Halving leaves the larger share behind.
    /// </summary>
    /// <remarks>
    /// The new entry takes <c>qty / 2</c> rounded down and the original keeps the rest, so five
    /// becomes three and two rather than two and three.
    /// </remarks>
    [Theory]
    [InlineData(10, 5, 5)]
    [InlineData(5, 3, 2)]
    [InlineData(2, 1, 1)]
    [InlineData(7, 4, 3)]
    public void Halving_leaves_the_larger_share(int start, int kept, int split)
    {
        var items = new List<ItemInstance> { Item("arrow", 1, start) };

        Assert.True(InventoryBundles.Halve(items, 0, Database()));

        Assert.Equal(2, items.Count);
        Assert.Equal(kept, items[0].Quantity);
        Assert.Equal(split, items[1].Quantity);
    }

    /// <summary>A single item cannot be split.</summary>
    [Fact]
    public void One_cannot_be_halved()
    {
        var items = new List<ItemInstance> { Item("arrow", 1, 1) };

        Assert.False(InventoryBundles.Halve(items, 0, Database()));
        Assert.Single(items);
    }

    /// <summary>
    /// The half that leaves is never readied, whatever the original was.
    /// </summary>
    /// <remarks>
    /// A character wearing one of a pair does not end up wearing both halves.
    /// </remarks>
    [Fact]
    public void The_split_half_is_not_readied()
    {
        uint worn = ReadiedLocation.Base38("WEAPON");
        var items = new List<ItemInstance> { Item("arrow", 1, 6, worn) };

        Assert.True(InventoryBundles.Halve(items, 0, Database()));

        Assert.Equal(worn, items[0].ReadyLocation);
        Assert.Equal(ReadiedLocation.NotReady, items[1].ReadyLocation);
    }

    /// <summary>
    /// The two halves are not merged straight back together.
    /// </summary>
    /// <remarks>
    /// The reference passes <c>FALSE</c> to <c>AddItem</c> for exactly this reason — its own
    /// comment reads "don't auto join them back together!". A list that merged on add would make
    /// HALVE a no-op.
    /// </remarks>
    [Fact]
    public void Halving_twice_gives_three_entries()
    {
        var items = new List<ItemInstance> { Item("arrow", 1, 8) };

        Assert.True(InventoryBundles.Halve(items, 0, Database()));
        Assert.True(InventoryBundles.Halve(items, 0, Database()));

        Assert.Equal(3, items.Count);
        Assert.Equal(8, items.Sum(i => i.Quantity));
    }

    /// <summary>
    /// Joining gathers every other stack, not just one.
    /// </summary>
    /// <remarks>
    /// <b>So HALVE and JOIN are not inverses.</b> Halving twice and joining once leaves one entry,
    /// not two — the reference walks the whole list and deletes everything it added up.
    /// </remarks>
    [Fact]
    public void Joining_gathers_every_other_stack()
    {
        var items = new List<ItemInstance>
        {
            Item("arrow", 1, 4),
            Item("arrow", 2, 3),
            Item("sword", 3, 1),
            Item("arrow", 4, 2),
        };

        Assert.True(InventoryBundles.Join(items, 0, Database()));

        Assert.Equal(2, items.Count);
        Assert.Equal(9, items[0].Quantity);

        // The other item is untouched.
        Assert.Contains(items, i => i.ItemId == "sword" && i.Quantity == 1);
    }

    /// <summary>The selected entry survives even when the stacks before it are removed.</summary>
    [Fact]
    public void The_selected_entry_survives_a_join_from_the_middle()
    {
        var items = new List<ItemInstance>
        {
            Item("arrow", 1, 4),
            Item("arrow", 2, 3),
            Item("arrow", 3, 2),
        };

        // Join into the LAST one, so entries before it are deleted.
        Assert.True(InventoryBundles.Join(items, 2, Database()));

        var only = Assert.Single(items);
        Assert.Equal(9, only.Quantity);
        Assert.Equal(3, only.Key);
    }

    /// <summary>Nothing to join is refused rather than reported as a change.</summary>
    [Fact]
    public void Joining_a_lone_stack_changes_nothing()
    {
        var items = new List<ItemInstance> { Item("arrow", 1, 4) };

        Assert.False(InventoryBundles.Join(items, 0, Database()));
        Assert.Single(items);
    }

    /// <summary>
    /// Matching is by item id and key alone, so the other entries' flags are discarded.
    /// </summary>
    /// <remarks>
    /// Charges, identification and where each was worn are all ignored — a joined stack keeps
    /// whichever the <i>selected</i> entry had.
    /// </remarks>
    [Fact]
    public void The_selected_entrys_flags_win()
    {
        var items = new List<ItemInstance>
        {
            new(1, "wand", 0, ReadiedLocation.NotReady, 1, Identified: 0, Charges: 2, 0, 0),
            new(2, "wand", 0, ReadiedLocation.NotReady, 1, Identified: 1, Charges: 9, 0, 0),
        };

        Assert.True(InventoryBundles.Join(items, 0, Database()));

        var only = Assert.Single(items);
        Assert.Equal(2, only.Quantity);
        Assert.Equal(0, only.Identified);
        Assert.Equal(2, only.Charges);
    }

    /// <summary>
    /// A bundle size of one makes an item unsplittable, whatever else its record says.
    /// </summary>
    /// <remarks>
    /// This is what stops a sword being divided: there is nothing to divide.
    /// </remarks>
    [Fact]
    public void A_bundle_of_one_cannot_be_split_or_merged()
    {
        var items = new List<ItemInstance> { Item("sword", 1, 4), Item("sword", 2, 2) };

        Assert.False(InventoryBundles.Halve(items, 0, Database(bundleQty: 1)));
        Assert.False(InventoryBundles.Join(items, 0, Database(bundleQty: 1)));

        Assert.Equal(2, items.Count);
    }

    /// <summary>And so does the record's own flag.</summary>
    [Fact]
    public void The_records_own_flag_refuses_both()
    {
        var items = new List<ItemInstance> { Item("thing", 1, 4), Item("thing", 2, 2) };

        Assert.False(InventoryBundles.Halve(items, 0, Database(canHalveJoin: 0)));
        Assert.False(InventoryBundles.Join(items, 0, Database(canHalveJoin: 0)));
    }

    /// <summary>Money is refused by both, before anything else is looked at.</summary>
    [Fact]
    public void Money_is_refused()
    {
        var items = new List<ItemInstance> { Item("_$GEM$_", 1, 10) };

        Assert.False(InventoryBundles.CanSplitOrMerge(items[0], Database()));
        Assert.False(InventoryBundles.Halve(items, 0, Database()));
    }

    /// <summary>An item the design does not carry is refused rather than assumed splittable.</summary>
    [Fact]
    public void An_unknown_item_is_refused()
    {
        var items = new List<ItemInstance> { Item("ghost", 1, 4) };

        Assert.False(InventoryBundles.Halve(items, 0, _ => null));
    }

    /// <summary>
    /// Three classes are excluded by class, however their records read.
    /// </summary>
    /// <remarks>
    /// A second gate the screen applies before the item's own rule — it is what stops a quest token
    /// being divided in half.
    /// </remarks>
    [Theory]
    [InlineData(ItemClass.Item, true)]
    [InlineData(ItemClass.Gem, true)]
    [InlineData(ItemClass.SpecialItem, false)]
    [InlineData(ItemClass.SpecialKey, false)]
    [InlineData(ItemClass.Quest, false)]
    public void Three_classes_are_excluded_outright(ItemClass kind, bool allowed) =>
        Assert.Equal(allowed, InventoryBundles.ClassCanSplitOrMerge(kind));

    /// <summary>An item moves out of the party and into the vault, whole.</summary>
    /// <remarks>
    /// <b>The whole stack goes, not one of it.</b> A character depositing forty arrows deposits
    /// forty — HALVE is how you keep some.
    /// </remarks>
    [Fact]
    public void Depositing_moves_the_whole_stack()
    {
        var items = new List<ItemInstance> { Item("arrow", 1, 40), Item("sword", 2, 1) };
        var vaults = new GlobalVaults(MoneyRules.Default);

        Assert.Equal(InventoryBundles.DepositRefusal.None,
                     InventoryBundles.Deposit(items, 0, vaults, 0, Database()));

        Assert.Single(items);
        Assert.Equal("sword", items[0].ItemId);

        var deposited = Assert.Single(vaults.ItemsIn(0));
        Assert.Equal("arrow", deposited.ItemId);
        Assert.Equal(40, deposited.Quantity);
    }

    /// <summary>
    /// A worn item is refused, and that is the one refusal the reference reports.
    /// </summary>
    [Fact]
    public void A_worn_item_cannot_be_deposited()
    {
        var items = new List<ItemInstance>
        {
            Item("arrow", 1, 4, ReadiedLocation.Base38("WEAPON")),
        };
        var vaults = new GlobalVaults(MoneyRules.Default);

        Assert.Equal(InventoryBundles.DepositRefusal.IsReadied,
                     InventoryBundles.Deposit(items, 0, vaults, 0, Database()));

        Assert.Single(items);
        Assert.Empty(vaults.ItemsIn(0));
    }

    /// <summary>
    /// Money is not refused so much as sent elsewhere.
    /// </summary>
    /// <remarks>
    /// Coins, gems and jewellery are deposited by <i>quantity</i> through a prompt, not as a
    /// carried item — a different screen the port has not built, which is why it has its own code
    /// rather than sharing "cannot be deposited".
    /// </remarks>
    [Fact]
    public void Money_goes_down_a_different_path()
    {
        var items = new List<ItemInstance> { Item("_$GEM$_", 1, 5) };
        var vaults = new GlobalVaults(MoneyRules.Default);

        Assert.Equal(InventoryBundles.DepositRefusal.IsMoney,
                     InventoryBundles.Deposit(items, 0, vaults, 0, Database()));
    }

    /// <summary>
    /// One record flag governs depositing, trading, dropping and selling alike.
    /// </summary>
    /// <remarks>
    /// The field is literally <c>CanBeTradeDropSoldDep</c> and every one of those four checks just
    /// returns it — so a design cannot allow selling but forbid dropping.
    /// </remarks>
    [Fact]
    public void One_flag_governs_all_four_ways_out()
    {
        var items = new List<ItemInstance> { Item("arrow", 1, 4) };
        var vaults = new GlobalVaults(MoneyRules.Default);

        // The bundle flag is set, so this is not the halve/join rule refusing it.
        Assert.True(InventoryBundles.CanSplitOrMerge(items[0], Database()));

        Assert.False(InventoryBundles.CanLeaveTheParty(items[0], Locked()));
        Assert.Equal(InventoryBundles.DepositRefusal.CannotBeDeposited,
                     InventoryBundles.Deposit(items, 0, vaults, 0, Locked()));
    }

    /// <summary>A database whose items may be split but never leave the party.</summary>
    private static Func<string, ItemRecord?> Locked() =>
        _ => new(new ItemNames(0, "", "", "", "", "", ""),
                 HitArt: null, MissileArt: null,
                 new ItemScalars("", 0, 0, 0, 0, 0, 10, 0),
                 new ItemCombat(ReadiedLocation.WeaponHand, 1, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
                 new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, 1,
                              CanBeTradeDropSoldDep: 0, new SpecabBlock([], [], []), []));

    /// <summary>A vault that does not exist is refused rather than created.</summary>
    [Fact]
    public void A_vault_that_does_not_exist_is_refused()
    {
        var items = new List<ItemInstance> { Item("arrow", 1, 4) };
        var vaults = new GlobalVaults(MoneyRules.Default);

        Assert.Equal(InventoryBundles.DepositRefusal.NoSuchVault,
                     InventoryBundles.Deposit(items, 0, vaults, GlobalVaults.Count, Database()));
        Assert.Equal(InventoryBundles.DepositRefusal.NoSuchVault,
                     InventoryBundles.Deposit(items, 0, vaults, -1, Database()));

        Assert.Single(items);
    }

    /// <summary>An item moves from one character to another, whole.</summary>
    [Fact]
    public void Trading_hands_the_row_over()
    {
        var giver = new List<ItemInstance> { Item("arrow", 1, 20), Item("sword", 2, 1) };
        var taker = new List<ItemInstance>();

        Assert.Equal(InventoryBundles.TradeRefusal.None,
                     InventoryBundles.Trade(giver, 0, taker, toSelf: false, Database()));

        Assert.Single(taker);
        Assert.Equal(20, taker[0].Quantity);
        Assert.Equal("sword", Assert.Single(giver).ItemId);
    }

    /// <summary>
    /// Trading to yourself moves the row to the end, which is the point of it.
    /// </summary>
    /// <remarks>
    /// <b>A feature, not an accident.</b> The reference's own comment calls it "a form of inventory
    /// re-arrangement by the player" — and that path skips the weight check, since a character can
    /// always carry what it is already carrying.
    /// </remarks>
    [Fact]
    public void Trading_to_yourself_moves_the_row_to_the_end()
    {
        var own = new List<ItemInstance> { Item("arrow", 1, 5), Item("sword", 2, 1) };

        Assert.Equal(InventoryBundles.TradeRefusal.None,
                     InventoryBundles.Trade(own, 0, own, toSelf: true, Database(),
                                            takerCarrying: 9999, maxCarried: 0,
                                            weigh: _ => 9999));

        Assert.Equal(["sword", "arrow"], own.Select(i => i.ItemId));
    }

    /// <summary>
    /// A taker who cannot carry it means the giver keeps it.
    /// </summary>
    /// <remarks>
    /// <b>The taker is given it before the giver loses it</b>, so a failed add cannot destroy the
    /// item — the reference deletes only when the add succeeded.
    /// </remarks>
    [Fact]
    public void A_taker_who_cannot_carry_it_leaves_it_where_it_was()
    {
        var giver = new List<ItemInstance> { Item("arrow", 1, 20) };
        var taker = new List<ItemInstance>();

        Assert.Equal(InventoryBundles.TradeRefusal.TooHeavy,
                     InventoryBundles.Trade(giver, 0, taker, toSelf: false, Database(),
                                            takerCarrying: 10, maxCarried: 10,
                                            weigh: _ => 5));

        Assert.Single(giver);
        Assert.Empty(taker);
    }

    /// <summary>A worn item stays put, and so does one the record will not let go.</summary>
    [Fact]
    public void A_worn_or_bound_item_cannot_be_traded()
    {
        var worn = new List<ItemInstance>
        {
            Item("arrow", 1, 4, ReadiedLocation.Base38("WEAPON")),
        };

        Assert.Equal(InventoryBundles.TradeRefusal.IsReadied,
                     InventoryBundles.Trade(worn, 0, [], toSelf: false, Database()));

        var bound = new List<ItemInstance> { Item("arrow", 1, 4) };

        Assert.Equal(InventoryBundles.TradeRefusal.CannotBeTraded,
                     InventoryBundles.Trade(bound, 0, [], toSelf: false, Locked()));
    }

    /// <summary>
    /// Money goes through a quantity prompt, not the item path.
    /// </summary>
    [Fact]
    public void Money_is_not_traded_as_an_item()
    {
        var giver = new List<ItemInstance> { Item("_$GEM$_", 1, 5) };

        Assert.Equal(InventoryBundles.TradeRefusal.IsMoney,
                     InventoryBundles.Trade(giver, 0, [], toSelf: false, Database()));
    }

    /// <summary>Everything the row carried travels with it.</summary>
    /// <remarks>
    /// The purchase price in particular, which is what stops a trade laundering an item into being
    /// worth its list price at a shop.
    /// </remarks>
    [Fact]
    public void The_rows_own_values_travel_with_it()
    {
        var bought = new ItemInstance(1, "wand", 0, ReadiedLocation.NotReady,
                                      1, Identified: 1, Charges: 7, 0, Paid: 42);
        var giver = new List<ItemInstance> { bought };
        var taker = new List<ItemInstance>();

        Assert.Equal(InventoryBundles.TradeRefusal.None,
                     InventoryBundles.Trade(giver, 0, taker, toSelf: false, Database()));

        var moved = Assert.Single(taker);
        Assert.Equal(42, moved.Paid);
        Assert.Equal(7, moved.Charges);
        Assert.Equal(1, moved.Identified);
    }

    /// <summary>An index off the end is refused rather than throwing.</summary>
    [Fact]
    public void An_index_off_the_end_is_refused()
    {
        var items = new List<ItemInstance> { Item("arrow", 1, 4) };

        Assert.False(InventoryBundles.Halve(items, 9, Database()));
        Assert.False(InventoryBundles.Halve(items, -1, Database()));
        Assert.False(InventoryBundles.Join(items, 9, Database()));
    }
}
