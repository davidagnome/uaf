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
    public const uint NotReady = 0;

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
        if (readyLocation == NotReady)
        {
            return string.Empty;
        }

        return ReadiedLocation.WordFor(readyLocation);
    }

    /// <summary>
    /// Toggles an item between readied and packed (<c>toggleReadyItem</c>).
    /// </summary>
    /// <returns>The item list with the change, or the original when it was refused.</returns>
    /// <remarks>
    /// <b>A cursed item that is readied cannot be unreadied</b> — the same rule that stops one
    /// being dropped (<c>CanUnReady</c>, <c>Items.cpp:1631</c>). Readying is never refused; only
    /// taking a cursed thing off is.
    /// </remarks>
    public static ItemList ToggleReady(ItemList items, int index, uint slot)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (index < 0 || index >= items.Items.Count)
        {
            return items;
        }

        var item = items.Items[index];
        bool readied = item.ReadyLocation != NotReady;

        if (readied && item.Cursed != 0)
        {
            return items;
        }

        var changed = new List<ItemInstance>(items.Items)
        {
            [index] = item with { ReadyLocation = readied ? NotReady : slot },
        };

        return new ItemList(changed, items.Ready);
    }

    /// <summary>Whether a command is one this port runs rather than merely names.</summary>
    public static bool Runs(InventoryCommand command) => command
        is InventoryCommand.Ready or InventoryCommand.Next or InventoryCommand.Prev
        or InventoryCommand.Exit;
}
