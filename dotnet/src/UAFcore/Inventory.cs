using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The inventory screen's fourteen commands (<c>ItemsMenu</c>, <c>GameMenu.cpp:1047</c>).
/// </summary>
/// <remarks>
/// <b>Two entries are both called <c>EXAMINE</c></b> — one for ordinary items and one for special
/// items and keys, which are different lists behind the same screen. Nothing in the label
/// distinguishes them, and the reference's own comment beside the table is what says which is
/// which.
/// </remarks>
public enum InventoryCommand
{
    Ready = 0,
    Use = 1,
    Trade = 2,
    Drop = 3,
    Deposit = 4,
    Halve = 5,
    Join = 6,
    Sell = 7,
    Identify = 8,

    /// <summary>Ordinary items.</summary>
    Examine = 9,

    /// <summary>Special items and keys — a different list behind the same word.</summary>
    ExamineSpecial = 10,

    Next = 11,
    Prev = 12,
    Exit = 13,
}

/// <summary>
/// Why the engine refused to ready an item (<c>miscErrorType</c>, <c>CanReadyItem</c>).
/// </summary>
/// <remarks>
/// The reference collapses most of these onto <c>UnknownError</c> and shows nothing; they are
/// separated here because the difference between "that is a gem" and "your hands are full" is the
/// whole content of the message, and a screen that says neither is indistinguishable from a
/// screen that is broken.
/// </remarks>
public enum ReadyRefusal
{
    /// <summary>The item was readied, or taken off.</summary>
    None = 0,

    /// <summary>Gems and jewellery are carried, never worn.</summary>
    Money,

    /// <summary>No such row, or an item the design no longer defines.</summary>
    Unknown,

    /// <summary>The stack is empty.</summary>
    NoQuantity,

    /// <summary>More than two hands. The reference declines to model it, and so does this.</summary>
    TooManyHands,

    /// <summary>The item's own slot is <c>CANNOT</c>.</summary>
    CannotBeWorn,

    /// <summary>A cursed item, once on, stays on.</summary>
    Cursed,

    /// <summary>Something is already worn in that slot.</summary>
    SlotTaken,

    /// <summary>A two-hander needs both hands, and one of them is holding something.</summary>
    TakesTwoHands,

    /// <summary>A two-hander is already held, so neither hand is free.</summary>
    NoFreeHands,
}

/// <summary>
/// One line of a character's inventory, as the screen shows it.
/// </summary>
/// <param name="Ready">
/// The six-character slot word, or blank when the item is in the pack. The screen shows the word
/// rather than a tick, because an item can be readied in one of eleven places.
/// </param>
public sealed record InventoryRow(string Ready, int Quantity, int Cost, string Name, int Index);

/// <summary>
/// A character's inventory, as <c>ITEMS_MENU_DATA</c> presents it
/// (<c>RunEvent.cpp:7843</c>) — the screen the vault, the shop and the camp all push.
/// </summary>
/// <remarks>
/// <para>
/// Building this once is what makes it worth building at all: three of the seven town services
/// reach it, and so does combat. The screen itself is a paged list and a fourteen-entry menu; the
/// commands behind it range from a one-line toggle to a whole trade negotiation.
/// </para>
/// <para>
/// <b>What runs so far: READY, NEXT, PREV and EXIT.</b> The other ten each need machinery this
/// port does not have — the trade partner picker, the shop's price list, the scribe rules — and
/// are named rather than silently doing nothing.
/// </para>
/// </remarks>
public static class Inventory
{
    /// <summary>The menu, in table order (<c>GameMenu.cpp:1047</c>).</summary>
    public static readonly (string Label, int Shortcut)[] Menu =
        [("READY", 0), ("USE", 0), ("TRADE", 0), ("DROP", 0), ("DEPOSIT", 3), ("HALVE", 0),
         ("JOIN", 0), ("SELL", 0), ("ID ITEM", 0), ("EXAMINE", 0), ("EXAMINE", 0),
         ("NEXT", 0), ("PREV", 0), ("EXIT", 1)];

    /// <summary>What <c>ReadyLocation</c> holds for an item in the pack (<c>NOTRDY</c>).</summary>
    /// <remarks>
    /// <b>Not zero.</b> Zero is the weapon hand — see <see cref="ReadiedLocation.Synonym"/> — and
    /// the shipped savegames really do carry zeroes, so an engine that reads zero as "in the pack"
    /// quietly strips the party's weapons on load.
    /// </remarks>
    public static uint NotReady => ReadiedLocation.NotReady;

    /// <summary>The two item ids that mean money (<c>ITEM_ID::IsMoney</c>, <c>Externs.h:1388</c>).</summary>
    private static readonly string[] MoneyIds = ["_$GEM$_", "_$JEWELRY$_"];

    /// <summary>Whether an id names money rather than a thing that can be worn.</summary>
    public static bool IsMoney(string itemId) => Array.IndexOf(MoneyIds, itemId) >= 0;

    /// <summary>
    /// Where a carried item is actually worn — its stored location put through the carried-item
    /// conversion, which is <b>not</b> the one the database record uses.
    /// </summary>
    public static uint SlotOf(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ReadiedLocation.Synonym(item.ReadyLocation);
    }

    /// <summary>Whether a carried item is being worn.</summary>
    public static bool IsReady(ItemInstance item) => SlotOf(item) != NotReady;

    /// <summary>
    /// The slot an item's <b>database record</b> says it is worn in, converted with the database's
    /// own table (<c>ITEM_DATA::Location_Readied</c>).
    /// </summary>
    public static uint SlotFor(ItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ReadiedLocation.Convert(record.Combat.LocationReadied);
    }

    /// <summary>
    /// Builds the rows for one character's carried items.
    /// </summary>
    /// <param name="itemName">Resolves an <c>ITEM_ID</c> to its display name.</param>
    /// <param name="itemCost">
    /// The item's price, for the shop's COST column. Null leaves it zero, which is what a vault or
    /// a camp shows.
    /// </param>
    public static List<InventoryRow> Rows(ItemList items, Func<string, string?>? itemName = null,
                                          Func<string, int>? itemCost = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var rows = new List<InventoryRow>(items.Items.Count);
        for (int i = 0; i < items.Items.Count; i++)
        {
            var carried = items.Items[i];
            rows.Add(new InventoryRow(
                ReadyWord(carried.ReadyLocation),
                carried.Quantity,
                itemCost?.Invoke(carried.ItemId) ?? 0,
                itemName?.Invoke(carried.ItemId) ?? carried.ItemId,
                i));
        }
        return rows;
    }

    /// <summary>
    /// The six-character slot word an item is readied in, or blank for one in the pack.
    /// </summary>
    /// <remarks>
    /// <b>The field is base-38 packed, not an ordinal</b> — except in a design old enough to have
    /// written the ordinal, which is why <see cref="ReadiedLocation.IsLegacyOrdinal"/> exists. Both
    /// forms are decoded here, because a saved game can hold either.
    /// </remarks>
    public static string ReadyWord(uint readyLocation)
    {
        uint slot = ReadiedLocation.Synonym(readyLocation);
        if (slot == NotReady)
        {
            return string.Empty;
        }

        return ReadiedLocation.WordFor(slot);
    }

    /// <summary>
    /// How many worn items claim <paramref name="slot"/> (<c>GetReadiedCount</c>,
    /// <c>Items.cpp:4856</c>).
    /// </summary>
    /// <remarks>
    /// <b>Matched on the database record's slot, not the carried item's.</b> An item worn
    /// somewhere other than where its record says still counts against its record's slot. That is
    /// the reference's own asymmetry — <see cref="WornIn"/>, three lines away in the same
    /// function, matches the other way — and it is kept because the two are used for different
    /// questions and diverge only for an item the engine placed by hand.
    /// </remarks>
    public static int ReadiedCount(ItemList items, uint slot, Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(database);

        int count = 0;
        foreach (var item in items.Items)
        {
            // The reference dereferences the record without checking; a design that dropped an
            // item its savegame still carries crashes it. Skipping is the same answer, alive.
            if (IsReady(item) && database(item.ItemId) is { } record && SlotFor(record) == slot)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// The item actually worn in <paramref name="slot"/>, or null (<c>GetReadiedItem</c>,
    /// <c>Items.cpp:4821</c>) — matched on where the item says it is.
    /// </summary>
    public static ItemInstance? WornIn(ItemList items, uint slot)
    {
        ArgumentNullException.ThrowIfNull(items);

        foreach (var item in items.Items)
        {
            if (SlotOf(item) == slot)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether an item can be put on (<c>ITEM_LIST::CanReadyItem</c>, <c>Items.cpp:1460</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An item already worn returns <see cref="ReadyRefusal.None"/></b> before any other test —
    /// the reference's early exit, which is what lets a two-hander be taken off again despite the
    /// hand rules that stopped its neighbour going on.
    /// </para>
    /// <para>
    /// <b>Not ported: the class check.</b> The reference also refuses an item no sub-class of the
    /// character may use (<c>IsUsableByClass</c>). That needs the baseclass tables, which the
    /// port has not reached; until then any class may wear anything.
    /// </para>
    /// </remarks>
    public static ReadyRefusal CanReady(ItemList items, int index, Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(database);

        if (index < 0 || index >= items.Items.Count)
        {
            return ReadyRefusal.Unknown;
        }

        var item = items.Items[index];

        if (IsMoney(item.ItemId))
        {
            return ReadyRefusal.Money;
        }

        if (IsReady(item))
        {
            return ReadyRefusal.None;
        }

        if (item.Quantity <= 0)
        {
            return ReadyRefusal.NoQuantity;
        }

        if (database(item.ItemId) is not { } record)
        {
            return ReadyRefusal.Unknown;
        }

        if (record.Combat.HandsToUse > 2)
        {
            return ReadyRefusal.TooManyHands;
        }

        uint slot = SlotFor(record);
        if (slot == ReadiedLocation.Cannot)
        {
            // itemUsesRdySlot is false, and the reference then skips every remaining test and
            // readies it anyway -- at a slot named CANNOT. Refused here instead; see the plan.
            return ReadyRefusal.CannotBeWorn;
        }

        bool inHand = slot == ReadiedLocation.WeaponHand || slot == ReadiedLocation.ShieldHand;

        if (record.Combat.HandsToUse == 2 && inHand)
        {
            if (WornIn(items, ReadiedLocation.WeaponHand) is not null
                || WornIn(items, ReadiedLocation.ShieldHand) is not null)
            {
                return ReadyRefusal.TakesTwoHands;
            }
        }
        else if (record.Combat.HandsToUse > 0)
        {
            foreach (uint hand in (uint[])[ReadiedLocation.WeaponHand, ReadiedLocation.ShieldHand])
            {
                if (WornIn(items, hand) is { } held
                    && database(held.ItemId) is { } heldRecord
                    && heldRecord.Combat.HandsToUse > 1)
                {
                    return ReadyRefusal.NoFreeHands;
                }
            }
        }

        // With no CAN_READY script to say otherwise, the slot must be empty.
        return ReadiedCount(items, slot, database) == 0
            ? ReadyRefusal.None
            : ReadyRefusal.SlotTaken;
    }

    /// <summary>
    /// Toggles an item between worn and packed (<c>CHARACTER::toggleReadyItem</c>,
    /// <c>Char.cpp:6806</c>).
    /// </summary>
    /// <returns>The item list with the change, or the original when it was refused.</returns>
    /// <remarks>
    /// <para>
    /// <b>The slot comes from the item's database record, not the caller.</b> An item is worn
    /// where its record says and nowhere else — there is no choosing a hand.
    /// </para>
    /// <para>
    /// <b>A cursed item that is worn cannot be taken off</b> (<c>CanUnReady</c>,
    /// <c>Items.cpp:1631</c>). Putting a cursed thing on is never refused, which is rather the
    /// point of a cursed thing.
    /// </para>
    /// <para>
    /// Not ported: the twelve <c>ReadyWeaponScript</c>-family hooks the reference fires around
    /// this, which is where an item's special abilities are switched on and off.
    /// </para>
    /// </remarks>
    public static ItemList ToggleReady(ItemList items, int index, Func<string, ItemRecord?> database,
                                       out ReadyRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(database);

        refusal = ReadyRefusal.Unknown;
        if (index < 0 || index >= items.Items.Count)
        {
            return items;
        }

        var item = items.Items[index];

        // Money is turned away before anything else: the reference does it at the menu
        // (itemCanBeReadied by item class, RunEvent.cpp:7897) rather than in the toggle, so a gem
        // never reaches the database lookup below -- which would refuse it for the wrong reason.
        if (IsMoney(item.ItemId))
        {
            refusal = ReadyRefusal.Money;
            return items;
        }

        // The reference looks the record up first and gives up if it is gone, whichever way the
        // toggle is about to go -- so an item whose design vanished cannot even be taken off.
        if (database(item.ItemId) is not { } record)
        {
            return items;
        }

        uint slot;
        if (IsReady(item))
        {
            if (item.Cursed != 0)
            {
                refusal = ReadyRefusal.Cursed;
                return items;
            }
            slot = NotReady;
        }
        else
        {
            refusal = CanReady(items, index, database);
            if (refusal != ReadyRefusal.None)
            {
                return items;
            }
            slot = SlotFor(record);
        }

        refusal = ReadyRefusal.None;
        var changed = new List<ItemInstance>(items.Items)
        {
            [index] = item with { ReadyLocation = slot },
        };

        return new ItemList(changed, items.Ready);
    }

    /// <summary>Whether a command is one this port runs rather than merely names.</summary>
    public static bool Runs(InventoryCommand command) => command
        is InventoryCommand.Ready or InventoryCommand.Next or InventoryCommand.Prev
        or InventoryCommand.Exit;
}
