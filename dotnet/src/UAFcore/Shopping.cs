using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Why a purchase did not happen (the <c>miscErrorType</c> values <c>CHARACTER::buyItem</c> can
/// set, <c>Externs.h:794</c>).
/// </summary>
public enum BuyRefusal
{
    /// <summary>Bought.</summary>
    None = 0,

    /// <summary>
    /// The buyer is not <see cref="CharacterStatus.Okay"/>. The reference names the condition —
    /// dead, unconscious, petrified and six more — but every one of them means the same thing here.
    /// </summary>
    NotWell,

    /// <summary>An id the design no longer defines. The reference sets no error at all for this.</summary>
    UnknownItem,

    /// <summary>The price is beyond the buyer's purse and the pool.</summary>
    NotEnoughMoney,

    /// <summary>One of it would already be too heavy.</summary>
    TooMuchWeight,

    /// <summary>
    /// The inventory would not take it. <b>Also what a bundle too heavy to carry reports</b> — see
    /// <see cref="Shopping.Buy"/>.
    /// </summary>
    MaxItemsReached,
}

/// <summary>
/// Buying from a shop (<c>BUY_SHOP_ITEMS_DATA</c>, <c>RunEvent.cpp:11085</c>, and
/// <c>CHARACTER::buyItem</c>, <c>Char.cpp:6670</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The shop's list is the event's own, and opening BUY edits it.</b>
/// <c>OnInitialEvent</c> walks <c>itemsAvail</c> setting <c>identified = TRUE</c> on every entry
/// before showing it — "shops disclose full name". The flag is written into the event, not into a
/// copy, so an unidentified item placed in a shop by the designer is identified from the first
/// time a player opens the door and stays that way for the rest of the session.
/// </para>
/// <para>
/// <b>Only the active character buys, and only if they are well.</b> The shop menu darkens BUY for
/// anyone not <see cref="CharacterStatus.Okay"/> (<c>SHOP::OnUpdateUI</c>), and
/// <c>buyItem</c> tests it again.
/// </para>
/// </remarks>
public static class Shopping
{
    /// <summary>The buy screen's menu (<c>BuyMenu</c>, <c>GameMenu.cpp:758</c>).</summary>
    public static readonly (string Label, int Shortcut)[] Menu =
        [("BUY", 0), ("NEXT", 0), ("PREV", 0), ("EXIT", 1)];

    /// <summary>What one item costs at this shop.</summary>
    public static int Price(ItemRecord record, CostFactor factor)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Prices.Apply(factor, record.Scalars.Cost);
    }

    /// <summary>
    /// What a quantity of one item weighs (<c>getItemEncumbrance</c>, <c>Items.cpp:602</c>).
    /// </summary>
    /// <param name="record">
    /// The item's database record, or null for money and for an id the design has lost.
    /// </param>
    /// <param name="coinsPerUnit">
    /// <c>moneyData.GetWeight()</c> — how many coins make one unit of encumbrance. Zero means coins
    /// are weightless.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>An item's stated encumbrance is for the whole bundle, not for one of them.</b> A quiver
    /// of 20 arrows weighing 2 divides to 0.1 each, and the quantity multiplies back up — so the
    /// database field means something different for a bundled item than for a single one, and a
    /// design that sets a bundle's weight per-arrow makes them twenty times too heavy.
    /// </para>
    /// <para>
    /// <b>The division is floating-point and the result truncates.</b> One arrow out of that quiver
    /// weighs 0.1, which becomes 0 — so a character can carry any number of part-bundles for free
    /// as long as each row stays under a whole unit.
    /// </para>
    /// <para>
    /// <b>Money never weighs nothing when it weighs anything.</b> The coin branch floors at 1, so a
    /// single copper piece is a whole unit of encumbrance where a hundred of them (at 100 per unit)
    /// are also one.
    /// </para>
    /// <para>
    /// <b>An unknown id weighs nothing</b>, where <see cref="Buy"/> refuses to buy it at all — the
    /// two functions disagree about a lost id and it does not matter, because the weight is only
    /// ever asked about something already carried.
    /// </para>
    /// </remarks>
    public static int ItemWeight(string itemId, ItemRecord? record, int quantity, int coinsPerUnit)
    {
        ArgumentNullException.ThrowIfNull(itemId);

        if (quantity <= 0)
        {
            return 0;
        }

        if (Inventory.IsMoney(itemId))
        {
            if (coinsPerUnit <= 0)
            {
                return quantity;
            }

            int carried = quantity / coinsPerUnit;
            return carried <= 0 ? 1 : carried;
        }

        if (record is null)
        {
            return 0;
        }

        double bundle = Math.Max(record.Scalars.BundleQty, 1);
        double weight = record.Scalars.Encumbrance / bundle * quantity;

        return Math.Max((int)weight, 0);
    }

    /// <summary>
    /// What a purse weighs (<c>MONEY_SACK::GetTotalWeight</c>, <c>Money.cpp:2362</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Coins, gems and jewellery are counted as one pile.</b> A gem weighs exactly as much as a
    /// copper piece, and its appraised value has nothing to do with it.
    /// </para>
    /// <para>
    /// <b>An empty purse weighs one unit.</b> The floor is applied to the division without ever
    /// asking whether there was anything to divide, so 0/100 becomes 1 — every character in a
    /// design that gives coins a weight carries a unit of nothing. Transcribed.
    /// </para>
    /// </remarks>
    public static int PurseWeight(Purse purse, int coinsPerUnit)
    {
        ArgumentNullException.ThrowIfNull(purse);

        if (coinsPerUnit <= 0)
        {
            return 0;
        }

        int pieces = purse.Gems.Count + purse.Jewelry.Count;
        for (int i = 0; i < MoneyRules.MaxCoinTypes; i++)
        {
            pieces += purse[MoneyRules.ClassOf(i)];
        }

        return Math.Max(1, pieces / coinsPerUnit);
    }

    /// <summary>
    /// What a character is carrying (<c>CHARACTER::determineEffectiveEncumbrance</c>,
    /// <c>Char.cpp:6098</c>).
    /// </summary>
    /// <remarks>
    /// <b>"Effective" and plain encumbrance are the same function.</b> The effective one was meant
    /// to skip magical items — <c>if (!isMagical(...))</c> is still there, commented out — so the
    /// two differ by nothing at all and the engine calls whichever. Monsters weigh nothing, which
    /// is the only real branch left in it.
    /// </remarks>
    public static int Carried(Character who, int coinsPerUnit, Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(database);

        int total = PurseWeight(who.Purse, coinsPerUnit);

        foreach (var item in who.Items)
        {
            total += ItemWeight(item.ItemId, database(item.ItemId), item.Quantity, coinsPerUnit);
        }

        return total;
    }

    /// <summary>
    /// The most a character can carry (<c>GetAdjMaxEncumbrance</c>, <c>Char.cpp:13811</c>).
    /// </summary>
    /// <remarks>
    /// Floored at 1 after the spell effects, so an effect cannot leave someone unable to carry
    /// anything at all.
    /// </remarks>
    public static int MaxCarried(Character who)
    {
        ArgumentNullException.ThrowIfNull(who);
        return Math.Max(1, (int)who.Effects.Apply(who.Record.MaxEncumbrance, "$CHAR_MAXENC"));
    }

    /// <summary>
    /// Buys one bundle for the active character (<c>CHARACTER::buyItem</c>, <c>Char.cpp:6670</c>).
    /// </summary>
    /// <param name="itemId">The row the cursor is on.</param>
    /// <param name="record">Its database record, or null for an id the design has lost.</param>
    /// <param name="factor">The shop's <c>costFactor</c>.</param>
    /// <param name="coinsPerUnit">The design's <c>moneyData.GetWeight()</c>.</param>
    /// <param name="database">Resolves the ids already carried, for the weight already carried.</param>
    /// <remarks>
    /// <para>
    /// <b>The first weight test asks about one of them, not about the bundle.</b>
    /// <c>getItemEncumbrance(itemID, 1)</c> — so a bundle of 20 arrows is weighed as a single arrow
    /// at this gate, which for a bundled item is usually a fraction that truncates to nothing.
    /// </para>
    /// <para>
    /// <b>And then <c>addCharacterItem</c> weighs it again, properly, and reports the wrong
    /// error.</b> The second test uses the bundle quantity and sets <c>TooMuchWeight</c> — but it
    /// returns FALSE, and <c>buyItem</c>'s caller-side <c>else</c> overwrites that with
    /// <c>MaxItemsReached</c>. So a purchase refused because the party cannot carry it says the
    /// character is holding too many things. Reproduced: it is the only way a shop reports weight
    /// on a bundle, and the message a player actually sees.
    /// </para>
    /// <para>
    /// <b>Payment happens last, and only if the item went in.</b> A refusal at any point leaves the
    /// purse untouched — there is no partial purchase.
    /// </para>
    /// <para>
    /// <b>Nothing stacks.</b> <c>addItem</c> calls <c>AddItem(newItem, FALSE)</c> — auto-join off —
    /// so buying the same dagger ten times leaves ten rows on the inventory screen, each with its
    /// own key and its own paid price. JOIN on the inventory menu is what merges them, by hand.
    /// </para>
    /// <para>
    /// <b>The price paid is remembered on the item.</b> <c>paid</c> is what the shop charged after
    /// its cost factor, not the database price, and it is what a buyback is later computed from.
    /// </para>
    /// <para>
    /// <b>Bought goods are identified.</b> The new row carries <c>identified = TRUE</c> whatever
    /// the shop's own entry said.
    /// </para>
    /// </remarks>
    public static BuyRefusal Buy(Character buyer, Party party, string itemId, ItemRecord? record,
                                 CostFactor factor, int coinsPerUnit,
                                 Func<string, ItemRecord?> database)
    {
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(database);

        if (buyer.Status != CharacterStatus.Okay)
        {
            return BuyRefusal.NotWell;
        }

        if (record is null)
        {
            return BuyRefusal.UnknownItem;
        }

        int cost = Price(record, factor);
        var currency = buyer.Purse.Rules.BaseType;

        if (!CanAfford(buyer, party, currency, cost))
        {
            return BuyRefusal.NotEnoughMoney;
        }

        int carried = Carried(buyer, coinsPerUnit, database);
        int maximum = MaxCarried(buyer);

        // One of them, not the bundle.
        if (ItemWeight(itemId, record, 1, coinsPerUnit) + carried > maximum)
        {
            return BuyRefusal.TooMuchWeight;
        }

        int bundle = Math.Max(record.Scalars.BundleQty, 1);

        // addCharacterItem's own test, with the real quantity -- and its TooMuchWeight is thrown
        // away by the caller, which reports MaxItemsReached instead.
        if (ItemWeight(itemId, record, bundle, coinsPerUnit) + carried > maximum)
        {
            return BuyRefusal.MaxItemsReached;
        }

        if (!Add(buyer, itemId, record, bundle, cost))
        {
            return BuyRefusal.MaxItemsReached;
        }

        Pay(buyer, party, currency, cost);
        return BuyRefusal.None;
    }

    /// <summary>
    /// Whether the price can be met (<c>CHARACTER::enoughMoney</c> with no gem or jewellery cost,
    /// <c>Char.cpp:6726</c>).
    /// </summary>
    /// <remarks>
    /// <b>The pool is tried first and the character second</b>, so a party that has pooled its money
    /// buys out of the common purse until it runs dry and then quietly starts spending the active
    /// character's own. See <see cref="EventWhoPays.CanPay"/>, which is the same two-sided test.
    /// </remarks>
    private static bool CanAfford(Character buyer, Party party, ItemClass currency, int cost)
    {
        if (cost <= 0)
        {
            return true;
        }

        return (party.MoneyPooled != 0 && party.Pooled.HaveEnough(currency, cost))
            || buyer.Purse.HaveEnough(currency, cost);
    }

    /// <summary><c>CHARACTER::payForItem</c> (<c>Char.cpp:6758</c>), coins only.</summary>
    private static void Pay(Character buyer, Party party, ItemClass currency, int cost)
    {
        if (cost <= 0)
        {
            return;
        }

        if (party.MoneyPooled != 0 && party.Pooled.HaveEnough(currency, cost))
        {
            party.Pooled.Subtract(currency, cost);
            party.MoneyPooled = party.Pooled.IsEmpty ? 0 : 1;
        }
        else
        {
            buyer.Purse.Subtract(currency, cost);
        }
    }

    /// <summary>
    /// Puts the bundle in the pack (<c>ITEM_LIST::addItem</c>, <c>Items.cpp:3612</c>).
    /// </summary>
    /// <remarks>
    /// <b>A quantity above one on an unbundled item is silently reduced to one</b> — the reference
    /// logs it and carries on. Not reachable from <see cref="Buy"/>, which derives the quantity
    /// from <c>Bundle_Qty</c> itself and so can never disagree with it; kept because this is
    /// <c>addItem</c>'s own guard and the other callers of it do pass a quantity of their own.
    /// </remarks>
    private static bool Add(Character buyer, string itemId, ItemRecord record, int quantity,
                            int paid)
    {
        if (quantity < 1)
        {
            return false;
        }

        if (record.Scalars.BundleQty <= 1 && quantity > 1)
        {
            quantity = 1;
        }

        if (buyer.Items.Count >= MaxItems)
        {
            return false;
        }

        buyer.Items.Add(new ItemInstance(
            Key: NextKey(buyer.Items),
            ItemId: itemId,
            LegacyItemId: 0,
            ReadyLocation: Inventory.NotReady,
            Quantity: quantity,
            Identified: 1,
            Charges: record.Scalars.NumCharges,
            Cursed: (byte)record.Scalars.Cursed,
            Paid: paid));

        return true;
    }

    /// <summary>How many rows an inventory can hold (<c>MAX_ITEMS</c>, <c>Items.h:38</c>).</summary>
    /// <remarks>
    /// <b>Sixteen million.</b> <see cref="BuyRefusal.MaxItemsReached"/> is therefore unreachable by
    /// its own name — every time a player sees that message it came from the weight test above it.
    /// </remarks>
    public const int MaxItems = 0x00FFFFFF;

    /// <summary><c>ITEM_LIST::GetNextKey</c> (<c>Items.cpp:4554</c>).</summary>
    /// <remarks>
    /// <b>Keys start at 1, and 0 is never issued</b> — which is what lets <c>AddItem</c> return 0
    /// for "the list was full" without that colliding with a real key. The reference also wraps
    /// round at <see cref="int.MaxValue"/> by hunting for a gap left by deletions; a run that
    /// issues two billion keys is not reachable here and the wrap is not ported.
    /// </remarks>
    private static int NextKey(List<ItemInstance> items) =>
        items.Count == 0 ? 1 : items[^1].Key + 1;
}
