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
    /// Why a DEPOSIT was refused.
    /// </summary>
    /// <remarks>
    /// <b>The reference reports only one of these to the player</b> — <c>ItemIsReadied</c> gets a
    /// message and the rest simply redraw. Named separately here because "nothing happened" is the
    /// hardest kind of bug to see.
    /// </remarks>
    public enum DepositRefusal
    {
        None,

        /// <summary>The row does not exist.</summary>
        NoSuchItem,

        /// <summary>
        /// Money, which the reference sends down a separate path.
        /// </summary>
        /// <remarks>
        /// Coins, gems and jewellery are deposited by <i>quantity</i> through a prompt
        /// (<c>GET_MONEY_QTY_DATA</c>), not as a carried item — so this is not a refusal so much as
        /// a different screen, and the port has not built it.
        /// </remarks>
        IsMoney,

        /// <summary>The item's record or class forbids it.</summary>
        CannotBeDeposited,

        /// <summary>It is being worn, and the reference says so.</summary>
        IsReadied,

        /// <summary>There is no such vault.</summary>
        NoSuchVault,
    }

    /// <summary>
    /// Whether an item may be deposited, traded, dropped or sold
    /// (<c>itemCanBeDeposited</c>, <c>Items.cpp:433</c>).
    /// </summary>
    /// <remarks>
    /// <b>One flag covers all four.</b> The record field is literally
    /// <c>CanBeTradeDropSoldDep</c>, and <c>itemCanBeDeposited</c>, <c>itemCanBeSold</c> and their
    /// siblings all just return it — so a design cannot allow selling but forbid dropping. The
    /// three-class exclusion is the same one splitting uses.
    /// </remarks>
    public static bool CanLeaveTheParty(ItemInstance item, Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(database);

        return database(item.ItemId) is { } record && record.Tail.CanBeTradeDropSoldDep != 0;
    }

    /// <summary>
    /// Moves an item from a character into a vault (<c>RunEvent.cpp:8067</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole stack goes, not one of it.</b> The reference deletes the row with its full
    /// quantity, so a character depositing forty arrows deposits forty — HALVE is how you keep
    /// some.
    /// </para>
    /// <para>
    /// <b>The record flag is checked here where the reference checks it when it builds the
    /// menu.</b> Its DEPOSIT entry is greyed out for an item whose record forbids it
    /// (<c>RunEvent.cpp:8528</c>) and the command handler then re-tests only the item's
    /// <i>class</i>. The port's menu does not grey entries yet, so the same gate is applied at the
    /// point of use — the same outcome, one step later.
    /// </para>
    /// <para>
    /// <b>The class gate is not applied at all, and does not need to be.</b> It excludes special
    /// items, keys and quest items, which live on a <i>different list</i> behind the same screen —
    /// the reason two menu entries are both called EXAMINE. Nothing on the ordinary-items list can
    /// be one of them.
    /// </para>
    /// </remarks>
    public static DepositRefusal Deposit(List<ItemInstance> items, int index,
                                         GlobalVaults vaults, int vault,
                                         Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(vaults);
        ArgumentNullException.ThrowIfNull(database);

        if (index < 0 || index >= items.Count)
        {
            return DepositRefusal.NoSuchItem;
        }

        if (!GlobalVaults.IsValid(vault))
        {
            return DepositRefusal.NoSuchVault;
        }

        var item = items[index];

        // Money first: it is a different screen rather than a refusal, and saying so is more use
        // than the flag check below, which money would also fail for want of a database record.
        if (Inventory.IsMoney(item.ItemId))
        {
            return DepositRefusal.IsMoney;
        }

        if (!CanLeaveTheParty(item, database))
        {
            return DepositRefusal.CannotBeDeposited;
        }

        // The one refusal the reference tells the player about.
        if (Inventory.IsReady(item))
        {
            return DepositRefusal.IsReadied;
        }

        vaults.Deposit(vault, item);
        items.RemoveAt(index);

        return DepositRefusal.None;
    }

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
