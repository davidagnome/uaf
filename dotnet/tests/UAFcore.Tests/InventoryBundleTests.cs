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
    private static ItemRecord Record(int bundleQty, int canHalveJoin) =>
        new(new ItemNames(0, "", "", "", "", "", ""),
            HitArt: null, MissileArt: null,
            new ItemScalars("", 0, 0, 0, 0, 0, bundleQty, 0),
            new ItemCombat(ReadiedLocation.WeaponHand, 1, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, canHalveJoin, 0,
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
