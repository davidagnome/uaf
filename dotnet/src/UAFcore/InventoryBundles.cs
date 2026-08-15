using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Splitting and merging bundles of one item (<c>halveItem</c>, <c>joinItems</c>,
/// <c>Shared/Items.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>HALVE and JOIN are inverses only in spirit.</b> Halving splits one entry into two; joining
/// gathers <i>every</i> other entry of the same item into the one selected, not just the one that
/// was split off. So halving twice and joining once leaves one entry, not two.
/// </para>
/// <para>
/// Both refuse the same things: money, an item the design does not carry, an item whose bundle
/// size is one, and one whose record forbids it.
/// </para>
/// </remarks>
public static class InventoryBundles
{
    /// <summary>
    /// Whether an item may be split or merged at all.
    /// </summary>
    /// <remarks>
    /// <b>The two rules are identical in the reference</b> — <c>itemCanBeHalved</c> and
    /// <c>itemCanBeJoined</c> have the same four tests (<c>Items.cpp:462</c>, <c>:481</c>) — so
    /// they are one function here. A <c>Bundle_Qty</c> of one is what makes a sword unsplittable:
    /// there is nothing to divide.
    /// </remarks>
    public static bool CanSplitOrMerge(ItemInstance item, Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(database);

        return !Inventory.IsMoney(item.ItemId)
               && database(item.ItemId) is { } record
               && record.Scalars.BundleQty > 1
               && record.Tail.CanBeHalvedJoined != 0;
    }

    /// <summary>
    /// Item classes that can never be split or merged (<c>itemCanBeHalved(itemClassType)</c>,
    /// <c>Items.cpp:566</c>).
    /// </summary>
    /// <remarks>
    /// <b>A second, separate gate the screen applies before the item's own rule.</b> Special items,
    /// special keys and quest items are excluded by <i>class</i> however their record reads — which
    /// is what stops a quest token being divided in half.
    /// </remarks>
    public static bool ClassCanSplitOrMerge(ItemClass kind) =>
        kind is not (ItemClass.SpecialItem or ItemClass.SpecialKey or ItemClass.Quest);

    /// <summary>
    /// Splits a bundle in two (<c>ITEM_LIST::halveItem</c>).
    /// </summary>
    /// <returns>Whether anything was split.</returns>
    /// <remarks>
    /// <para>
    /// <b>The half that stays keeps the larger share.</b> The new entry takes <c>qty / 2</c>,
    /// rounded down, and the original keeps the rest — so splitting five leaves three and two, and
    /// splitting one does nothing at all.
    /// </para>
    /// <para>
    /// <b>The new entry is not readied</b>, whatever the original was: a character wearing one of
    /// a pair does not end up wearing both halves. And the reference passes <c>FALSE</c> to
    /// <c>AddItem</c> explicitly so the two are not merged straight back together — its own comment
    /// says "don't auto join them back!".
    /// </para>
    /// </remarks>
    public static bool Halve(List<ItemInstance> items, int index,
                             Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (index < 0 || index >= items.Count)
        {
            return false;
        }

        var item = items[index];

        if (!CanSplitOrMerge(item, database) || item.Quantity <= 1)
        {
            return false;
        }

        int split = item.Quantity / 2;

        items[index] = item with { Quantity = item.Quantity - split };
        items.Add(item with { Quantity = split, ReadyLocation = ReadiedLocation.NotReady });

        return true;
    }

    /// <summary>
    /// Gathers every other bundle of the same item into this one (<c>ITEM_LIST::joinItems</c>).
    /// </summary>
    /// <returns>Whether anything was merged.</returns>
    /// <remarks>
    /// <para>
    /// <b>All of them, not the nearest one.</b> The reference walks the whole list adding up every
    /// entry with the same item id and a different key, then deletes them all — so a character
    /// carrying four separate stacks ends with one.
    /// </para>
    /// <para>
    /// <b>Matching is by item id and key alone.</b> Charges, whether an entry is identified, and
    /// what each was worn in are all ignored — so joining an identified stack with an unidentified
    /// one keeps whichever flags the <i>selected</i> entry had and silently discards the rest.
    /// </para>
    /// </remarks>
    public static bool Join(List<ItemInstance> items, int index,
                            Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (index < 0 || index >= items.Count)
        {
            return false;
        }

        var item = items[index];

        if (!CanSplitOrMerge(item, database))
        {
            return false;
        }

        int gathered = 0;

        // Backwards, so removing does not disturb the indices still to be looked at -- and the
        // selected entry's own index survives, which a forward walk would have to track.
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (i == index
                || items[i].ItemId != item.ItemId
                || items[i].Key == item.Key)
            {
                continue;
            }

            gathered += items[i].Quantity;
            items.RemoveAt(i);

            if (i < index)
            {
                index--;
            }
        }

        if (gathered == 0)
        {
            return false;
        }

        items[index] = item with { Quantity = item.Quantity + gathered };
        return true;
    }
}
