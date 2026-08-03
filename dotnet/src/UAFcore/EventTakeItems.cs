using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Which classes of goods a take-items event confiscates (<c>takeItemsActionType</c>,
/// <c>GameEvent.h:332</c>).
/// </summary>
/// <remarks>
/// A bitmask, and <c>takeItems</c> is a <c>BYTE</c> — so an event may take any combination. The
/// runtime tests the four bits in the order inventory, gems, jewellery, <i>then</i> money
/// (<c>Party.cpp:2195</c>, <c>:2304</c>, <c>:2347</c>, <c>:2390</c>), which is neither bit order
/// nor the order the editor's checkboxes are read in.
/// </remarks>
[Flags]
public enum TakeItemsAction
{
    /// <summary>Named items, or everything, out of each character's pack.</summary>
    Inventory = 1,

    /// <summary>Coins. The amount is <c>platinum</c>, quoted in <c>moneyType</c>.</summary>
    Money = 2,

    /// <summary>Gems, by count.</summary>
    Gems = 4,

    /// <summary>Jewellery, by count.</summary>
    Jewelry = 8,
}

/// <summary>Whom a take-items event takes from (<c>takeItemsAffectsType</c>, <c>GameEvent.h:338</c>).</summary>
public enum TakeItemsAffects
{
    /// <summary>Every member, each charged the full amount in turn — not a share of it.</summary>
    Party = 0,

    /// <summary>One member drawn at random, the only branch that consumes a die roll.</summary>
    RandomCharacter = 1,

    /// <summary>Whoever is <see cref="UAFcore.Party.ActiveCharacter"/>.</summary>
    ActiveCharacter = 2,
}

/// <summary>
/// How much of one class of goods to take (<c>takeItemQtyType</c>, <c>GameEvent.h:335</c>).
/// </summary>
/// <remarks>
/// The event carries four of these, one per class. <b>Only two of the four values mean anything for
/// inventory</b> — see <see cref="EventTakeItems.Apply"/>.
/// </remarks>
public enum TakeQuantity
{
    /// <summary>The number the design authored, clamped to what the character has.</summary>
    Specified = 0,

    /// <summary>A roll of a die with that many sides, clamped the same way.</summary>
    Random = 1,

    /// <summary>That percentage of what the character has.</summary>
    Percent = 2,

    /// <summary>Everything, ignoring the authored number entirely.</summary>
    All = 3,
}

/// <summary>
/// One <c>UpdateMoneyInVault</c> call (<c>GlobalData.cpp:5637</c>) — coins of one denomination.
/// </summary>
/// <param name="Type">The denomination. Either the event's own <c>moneyType</c> or the base coin.</param>
/// <param name="Amount">
/// How many coins. Always positive here; the reference's negative case subtracts instead, and this
/// event never reaches it.
/// </param>
public readonly record struct CoinDeposit(ItemClass Type, int Amount);

/// <summary>What a take-items event removed from the party.</summary>
/// <param name="Items">
/// One entry per unit of inventory the reference counted as taken. <b>For
/// <see cref="TakeQuantity.Specified"/> these are the <i>event's</i> item entries at quantity 1,
/// not the character's own instances</b> — see <see cref="EventTakeItems.Apply"/>. For
/// <see cref="TakeQuantity.All"/> they are the character's instances, whole stacks intact.
/// </param>
/// <param name="Gems">The gems removed, oldest first, across every character reached.</param>
/// <param name="Jewelry">The jewellery removed, on the same rule.</param>
/// <param name="Money">
/// Coins charged, in <see cref="MoneyRules.BaseType"/> — the <i>smallest</i> denomination, so a
/// copper count under the AD&amp;D defaults. Summed over every character charged, and note that
/// some or all of it may have come out of the party's pooled purse rather than theirs.
/// </param>
/// <param name="MoneyForVault">
/// What a vault would receive for that money, and <b>empty unless <c>StoreItems</c> is set</b>,
/// because the reference only performs the conversion then. It can be worth less than
/// <paramref name="Money"/> — see <see cref="EventTakeItems.Apply"/>.
/// </param>
/// <remarks>
/// <b>There is no separate "stored" list for the first three.</b> When <c>StoreItems</c> is set the
/// vault receives exactly <paramref name="Items"/>, <paramref name="Gems"/> and
/// <paramref name="Jewelry"/>; when it is not, the same goods are destroyed. Only the money is
/// shaped differently on the way into a vault, which is why it alone has a second field.
/// </remarks>
public sealed record TakeItemsOutcome(
    IReadOnlyList<ItemInstance> Items,
    IReadOnlyList<GemType> Gems,
    IReadOnlyList<GemType> Jewelry,
    int Money,
    IReadOnlyList<CoinDeposit> MoneyForVault);

/// <summary>
/// Runs a <c>TAKE_PARTY_ITEMS_DATA</c> (<c>PARTY::TakePartyItems</c>, <c>Shared/Party.cpp:2177</c>,
/// reached from <c>TAKE_PARTY_ITEMS_DATA::OnKeypress</c>, <c>UAFWin/RunEvent.cpp:12013</c>).
/// </summary>
/// <remarks>
/// <para>
/// The event is one screen of text and a Return; everything observable happens in
/// <c>PARTY::TakePartyItems</c>, which is what this is. <c>OnKeypress</c> adds nothing but
/// presentation: it clears the picture, clears the text if <c>mustHitReturn</c>, and chains
/// (<c>:12023-12028</c>). There is no success or failure branch — a take-items event always follows
/// its ordinary chain, whether or not it found anything to take.
/// </para>
/// <para>
/// <b>The backup branch in <c>OnKeypress</c> is dead.</b> <c>if (ForcePartyBackup())
/// TaskMessage(TASKMSG_MovePartyBackward)</c> (<c>:12024</c>) calls the base implementation, which
/// is <c>{ return FALSE; }</c> (<c>GameEvent.h:985</c>) — a dozen other event types override it and
/// this one does not. So the party is never stepped back, and this port returns no such flag.
/// </para>
/// <para>
/// <b>Absent from the corpus, and this one really was counted.</b> A walk of every level file under
/// <c>reference/</c> — 23 levels across five designs, 6,236 events — finds <b>zero</b>
/// <c>TAKE_PARTY_ITEMS_DATA</c>, matching what <c>EventTypesAbsentFromCorpusTests</c> records. So
/// every line below is transcription rather than observation, which is the reason for reproducing
/// the reference's awkward parts exactly instead of tidying them.
/// </para>
/// <para>
/// <b>Two things this port cannot do, both because the state does not exist here.</b> There is no
/// vault: <c>globalData.vault[WhichVault]</c> has no counterpart in <see cref="WorldState"/>, so
/// the goods are reported through <see cref="TakeItemsOutcome"/> for a caller to deposit rather
/// than deposited. And there is no live encumbrance: the reference ends by recomputing
/// <c>determineEffectiveEncumbrance()</c> and <c>determineMaxMovement()</c> for everyone it touched
/// (<c>Party.cpp:2438-2450</c>), because coins and gear have weight; <see cref="Character"/> has
/// nowhere to write that, exactly as <see cref="EventWhoPays.Take"/> found.
/// </para>
/// <para>
/// <b>A third gap is this port's own.</b> <see cref="UAFcore.Party.Carried"/> — where a
/// <c>GIVE_TREASURE_DATA</c> pickup lands — has no counterpart in the reference, and this walks
/// each character's own pack just as <c>TakePartyItems</c> does. So an item picked up during play
/// cannot be confiscated. Widening the walk would be inventing a rule.
/// </para>
/// </remarks>
public static class EventTakeItems
{
    /// <summary>
    /// <c>NotReady</c> (<c>Items.h:122</c>) — the readied-location sentinel for an item in the pack.
    /// </summary>
    private static readonly uint NotReady = ReadiedLocation.Base38("NOTRDY");

    /// <summary>
    /// The denomination the coin amount is quoted in (<c>TAKE_PARTY_ITEMS_DATA::moneyType</c>,
    /// <c>GameEvent.h:3055</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zero does <i>not</i> mean platinum here, and that is the opposite of the sibling
    /// <c>WHO_PAYS</c> rule.</b> On a toll the field entered the format at 0.912 and the reader
    /// leaves it 0 below that, so <see cref="EventWhoPays.Currency"/> restores the constructor's
    /// <c>PlatinumType</c>. This event has <b>no version gate on <c>moneyType</c> at all</b>: both
    /// serializers read it unconditionally (<c>GameEvent.cpp:8318</c> for <c>CArchive</c>,
    /// <c>:8368</c> for <c>CAR</c>) and only <c>WhichVault</c>, two lines later, is gated — at
    /// 0.910. So the value on disk is always the value the design authored, and inventing a
    /// platinum default would silently rewrite it.
    /// </para>
    /// <para>
    /// <b>A stored 0 is <c>itemType</c>, and the reference dies on it.</b> <c>GetIndex</c> has no
    /// case for it and reaches <c>die(0xab526)</c> (<c>Money.cpp:424</c>), which
    /// <c>MoneyRules.IndexOf</c> spells as an <see cref="ArgumentOutOfRangeException"/>. It is not
    /// reachable from an editor-written design — the combo is filled only from denominations the
    /// design has configured and the selection is read back through <c>GetItemData</c>
    /// (<c>UAFWinEd/TakePartyItems.cpp:181-188</c>, <c>:127-128</c>) — and it is survivable even
    /// then, because <c>Convert</c> returns early for an amount of 0 and for a source equal to the
    /// destination (<c>Money.cpp:755-756</c>), both above the <c>GetIndex</c> calls. So a bad
    /// denomination only bites an event that actually has coins to convert.
    /// </para>
    /// </remarks>
    public static ItemClass Currency(TakePartyItemsEvent take)
    {
        ArgumentNullException.ThrowIfNull(take);

        return (ItemClass)take.MoneyType;
    }

    /// <summary>
    /// Which member the event falls on, or −1 for the whole party
    /// (<c>Party.cpp:2182-2193</c>).
    /// </summary>
    /// <param name="dice">
    /// A single roll of an <i>n</i>-sided die, 1..n — <c>RollDice(n, 1, 0)</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The roll happens inside the switch, so only one of the three modes consumes it.</b> That
    /// is the reverse of <c>HEAL_PARTY_DATA</c>, whose <c>rndDude</c> is drawn above its switch and
    /// therefore moves the generator on for every heal event (see <see cref="EventHeal.Apply"/>).
    /// Anything reproducing a recorded run has to get this right in both directions.
    /// </para>
    /// <para>
    /// <b>An empty party rolls nothing and lands on −1.</b> <c>RollDice</c> returns its bonus for a
    /// die of no sides (<c>Globals.cpp:4927</c>), so <c>RollDice(0, 1) - 1</c> is −1 — which is the
    /// whole-party sentinel, not a member. The empty loops that follow do nothing either way.
    /// </para>
    /// <para>
    /// <b>An affect value outside the enum is the whole party.</b> <c>dude</c> is initialised to −1
    /// and the switch has no default (<c>Party.cpp:2180</c>), so an unrecognised value leaves it
    /// there.
    /// </para>
    /// <para>
    /// <b><see cref="TakeItemsAffects.ActiveCharacter"/> is not range-checked.</b> The reference
    /// assigns <c>activeCharacter</c> straight through and indexes with it; this port drops a
    /// member index past the end of the roster instead, because there is no garbage to read.
    /// </para>
    /// </remarks>
    public static int Victim(TakePartyItemsEvent take, Party party, Func<int, int> dice)
    {
        ArgumentNullException.ThrowIfNull(take);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(dice);

        return (TakeItemsAffects)take.TakeAffects switch
        {
            TakeItemsAffects.RandomCharacter => party.Count > 0 ? dice(party.Count) - 1 : -1,
            TakeItemsAffects.ActiveCharacter => party.ActiveCharacter,
            _ => -1,
        };
    }

    /// <summary>
    /// How much of one class of goods to take (<c>PARTY::TakePartyItemQty</c>,
    /// <c>Party.cpp:2454</c>).
    /// </summary>
    /// <param name="type">Which of the four rules applies.</param>
    /// <param name="amount">The number the design authored — a count, a die size or a percentage.</param>
    /// <param name="available">How much the character has of it.</param>
    /// <param name="dice">The die roller, used by <see cref="TakeQuantity.Random"/> alone.</param>
    /// <remarks>
    /// <para>
    /// <b>Nothing at all is taken from a character who has none.</b> The <c>data == 0</c> guard is
    /// the first line, above the switch, so even <see cref="TakeQuantity.All"/> returns 0 rather
    /// than falling through the arithmetic. It tests <c>== 0</c> and not <c>&lt;= 0</c>, which
    /// matters only for a negative total the money system cannot produce.
    /// </para>
    /// <para>
    /// <b>Two parameters of the reference's signature are dead.</b> <c>dude</c> is never read, and
    /// <c>data</c> — the available amount — is taken by <c>int&amp;</c> and decremented by the qty
    /// on every branch, but every one of the six call sites passes a local that is never read
    /// again. So the decrement is invisible and this is a pure function.
    /// </para>
    /// <para>
    /// <b>A die of no sides rolls 0, not 1.</b> <c>RollDice</c> returns its bonus when
    /// <c>sides &lt;= 0</c> (<c>Globals.cpp:4927</c>), so a <see cref="TakeQuantity.Random"/> take
    /// of 0 takes nothing — where a roller that always returned at least 1 would take one.
    /// </para>
    /// <para>
    /// <b>The percentage truncates toward zero, and a negative amount really does come back
    /// negative.</b> Nothing clamps below: <c>min(qty, data)</c> only clamps above. Every caller
    /// then either guards on <c>&gt; 0</c> or hands the negative to <c>RemoveMultGems</c>, which
    /// takes <c>min(count, held)</c> and loops from 0 — so it removes nothing. The editor puts no
    /// validator on any of the four quantity boxes.
    /// </para>
    /// </remarks>
    public static int Quantity(TakeQuantity type, int amount, int available, Func<int, int> dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (available == 0)
        {
            return 0;
        }

        return type switch
        {
            TakeQuantity.Specified => Math.Min(amount, available),

            // RollDice(amount, 1): a non-positive die size yields the bonus, which is 0.
            TakeQuantity.Random => Math.Min(amount > 0 ? dice(amount) : 0, available),

            // (int)((double)data * ((double)amount / 100.0)) -- the order matters for the rounding.
            TakeQuantity.Percent => Math.Min((int)(available * (amount / 100.0)), available),

            TakeQuantity.All => available,

            // The switch is defaultless and qty was initialised to 0.
            _ => 0,
        };
    }

    /// <summary>
    /// Applies the event to the party (<c>PARTY::TakePartyItems</c>, <c>Party.cpp:2177</c>).
    /// </summary>
    /// <param name="dice">
    /// A single roll of an <i>n</i>-sided die, 1..n — <c>RollDice(n, 1, 0)</c>, and
    /// <c>Game.Dice</c> when the engine calls it, so a test can pin it.
    /// </param>
    /// <returns>What was removed, and what a vault would receive for the coins.</returns>
    /// <remarks>
    /// <para>
    /// <b><c>itemPcnt</c> is a count, not a percentage.</b> The field is named for a percentage and
    /// <see cref="TakePartyItemsEvent.ItemPercent"/> follows it, but the only thing that reads it is
    /// <c>while (count &lt; data.itemPcnt)</c> (<c>Party.cpp:2208</c>) — how many units of each
    /// named item to take from each character. The editor agrees: the box is <c>IDC_ITEMQTY</c>,
    /// bound to <c>m_ItemQty</c> (<c>UAFWinEd/TakePartyItems.cpp:129</c>).
    /// </para>
    /// <para>
    /// <b>Two of the four quantity rules do nothing for inventory.</b>
    /// <see cref="TakeQuantity.Random"/> and <see cref="TakeQuantity.Percent"/> fall into a
    /// commented "not used for items" and break (<c>Party.cpp:2259-2262</c>), and the editor drops
    /// them from that one combo (<c>UAFWinEd/TakePartyItems.cpp:159</c>) — so an event carrying
    /// either takes no items at all while still taking money and valuables.
    /// </para>
    /// <para>
    /// <b>The item a vault receives is the event's, not the character's.</b> The
    /// <see cref="TakeQuantity.Specified"/> path deposits <c>tempITEM</c> — the entry the <i>design
    /// authored</i> — with its quantity forced to 1 (<c>Party.cpp:2219</c>), so the deposited copy
    /// carries the design's charges, identification and cursed flag rather than the ones the
    /// character was carrying. The reference writes that 1 back into its own event record, since
    /// <c>tempITEM</c> is a reference into <c>data.items</c>; nothing ever reads the field again, so
    /// the mutation is inert and this port leaves the record immutable.
    /// <see cref="TakeQuantity.All"/> deposits the character's real instances instead, whole stacks
    /// and all.
    /// </para>
    /// <para>
    /// <b>Item names are matched case-sensitively.</b> <c>GetListKeyByItemName</c> compares two
    /// <c>ITEM_ID</c>s (<c>Items.cpp:5018</c>), which is <c>CString::operator==</c> — a plain
    /// <c>strcmp</c>. So this uses <see cref="StringComparison.Ordinal"/>, and differs deliberately
    /// from <see cref="UAFcore.Party.HasItem"/>, which folds case.
    /// </para>
    /// <para>
    /// <b>The whole party means each member charged in full, not a share.</b> Every loop runs the
    /// same take against every character in turn — six members and a 100 gold take is 600 gold, and
    /// the reference's own comment at <c>Party.cpp:2414</c> ("take equally from all party members")
    /// describes something it does not do.
    /// </para>
    /// <para>
    /// <b>The money can come out of the pooled purse, however it was measured.</b> The amount is
    /// decided from <c>characters[i].money.Total()</c> — that character's own coins
    /// (<c>Party.cpp:2397</c>) — and then charged through <c>payForItem</c>, which spends from
    /// <c>party.poolSack</c> whenever the party has pooled and the pool covers it
    /// (<c>Char.cpp:6762</c>). So a pauper in a rich party is assessed at nothing and pays nothing,
    /// while a rich character in a pooled party is assessed on their own purse and the common one
    /// is drained instead. The same <c>payForItem</c> <see cref="EventWhoPays.Take"/> transcribes,
    /// with its gem and jewellery arguments left at their defaults.
    /// </para>
    /// <para>
    /// <b>A percentage of money is a percentage of the base-converted amount, and that is a
    /// bug.</b> <c>qty</c> is <c>ConvertToBase(platinum, moneyType)</c> before any of the four
    /// rules see it (<c>Party.cpp:2393</c>), so under <see cref="TakeQuantity.Percent"/> "take 50%"
    /// authored in gold becomes 10,000% under the AD&amp;D rates and empties the purse. It behaves
    /// as authored only when <c>moneyType</c> <i>is</i> the base coin, where the conversion is the
    /// identity. <see cref="TakeQuantity.Random"/> is hit the same way — the die grows by the rate
    /// ratio — and <see cref="TakeQuantity.All"/> escapes it entirely, since it ignores the amount.
    /// </para>
    /// <para>
    /// <b>A coin type the design has not configured takes the money and stores nothing.</b>
    /// <c>Convert</c> returns 0 for an inactive denomination on either side (<c>Money.cpp:759</c>),
    /// so <c>qty</c> is 0 — which under <see cref="TakeQuantity.All"/> still charges the whole
    /// purse, and then converts back to a vault deposit of 0. The character is poorer and the vault
    /// is no richer.
    /// </para>
    /// <para>
    /// <b>The two vault paths are guarded differently.</b> <c>UpdateMoneyInVault</c> checks the
    /// index against <c>MAX_GLOBAL_VAULTS</c> and returns quietly if it is out of range
    /// (<c>GlobalData.cpp:5639</c>); the item, gem and jewellery paths index
    /// <c>globalData.vault[data.WhichVault]</c> directly with no check at all. <c>WhichVault</c> is
    /// a <c>BYTE</c> and the editor offers fifteen, so a hand-written record above 14 corrupts
    /// memory in the reference and simply reports goods here.
    /// </para>
    /// <para>
    /// <b>A readied cursed item cannot be taken, and the reference does not notice.</b>
    /// <c>DeleteItem</c> refuses when <c>UnReady</c> fails (<c>Items.cpp:4729</c>,
    /// <c>:1618-1637</c>), but <c>deleteItem</c> has already written the reduced quantity and
    /// returns TRUE regardless (<c>:3689</c>), and <c>delCharacterItem</c> passes that straight on.
    /// So a <see cref="TakeQuantity.Specified"/> take of a readied cursed item leaves it in the
    /// pack at quantity 0 — pinned there, since the clamp at <c>:3677</c> stops it going negative —
    /// and still hands the vault one copy per attempt. Under <see cref="TakeQuantity.All"/> it is
    /// simply skipped, because that path deposits only on a successful delete.
    /// </para>
    /// <para>
    /// <b>The gems and jewellery a vault receives are read through the wrong character, and it does
    /// not matter.</b> The whole-party arms walk <c>characters[j]</c> — the inner counter — where
    /// every other line uses <c>characters[i]</c> (<c>Party.cpp:2336-2338</c> and
    /// <c>:2379-2381</c>). It is inert: the <c>POSITION</c> came from <c>characters[i]</c>'s list
    /// and MFC's <c>CList::GetAt</c> and <c>GetNext</c> resolve a position as a node pointer without
    /// consulting the list object, so the gems actually read are character <i>i</i>'s. The removal a
    /// few lines later is correctly indexed either way. Transcribed as the correct character, with
    /// the typo recorded rather than reproduced, because reproducing it would invent behaviour the
    /// reference does not have.
    /// </para>
    /// <para>
    /// <b>Gems and jewellery are counted, never valued</b>, and come off the head of the list — so
    /// a one-gem take can carry off a 5,000gp stone and leave a 10gp one, exactly as
    /// <see cref="EventWhoPays"/> found. Unlike a toll, this reads the character's own purse only;
    /// there is no pooled equivalent for valuables anywhere in the reference.
    /// </para>
    /// <para>
    /// <b>The design's currency comes from the party's pooled purse.</b> The reference reads the
    /// one global <c>globalData.moneyData</c>; the nearest thing here is
    /// <see cref="UAFcore.Party.Pooled"/>'s <see cref="Purse.Rules"/>, which
    /// <see cref="Game"/> builds from the same <see cref="MoneyRules"/> as every character's.
    /// </para>
    /// <para>
    /// <b>A design old enough to store numeric item ids cannot be matched at all.</b> Below
    /// <c>DesignVersion.SpellNames</c> an editor-role <c>ITEM</c> carries
    /// <see cref="ItemInstance.LegacyItemId"/> and an empty <see cref="ItemInstance.ItemId"/>, in
    /// the event and in the pack alike, so every name compares equal to every other. Resolving the
    /// legacy id needs the item database, which does not reach this far; named-item takes on such a
    /// design are a known gap rather than a silent mismatch.
    /// </para>
    /// </remarks>
    public static TakeItemsOutcome Apply(TakePartyItemsEvent take, Party party, Func<int, int> dice)
    {
        ArgumentNullException.ThrowIfNull(take);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(dice);

        int dude = Victim(take, party, dice);

        // The reference writes each of the four blocks twice, once for `dude` and once as a loop
        // over the party; the two arms are identical apart from the inert `characters[j]` typo, so
        // they are transcribed once over whichever members the event reached.
        IReadOnlyList<Character> victims = dude < 0
            ? party.Members
            : dude < party.Count ? [party.Members[dude]] : [];

        var tally = new Tally();

        if ((take.TakeItems & (byte)TakeItemsAction.Inventory) != 0)
        {
            TakeInventory(take, victims, tally);
        }

        if ((take.TakeItems & (byte)TakeItemsAction.Gems) != 0)
        {
            foreach (var who in victims)
            {
                int qty = Quantity((TakeQuantity)take.GemsSelectFlags, take.Gems,
                                   who.Purse.Gems.Count, dice);

                if (qty > 0)
                {
                    tally.Gems.AddRange(who.Purse.Gems.Take(qty));
                }

                who.Purse.RemoveGems(qty);
            }
        }

        if ((take.TakeItems & (byte)TakeItemsAction.Jewelry) != 0)
        {
            foreach (var who in victims)
            {
                int qty = Quantity((TakeQuantity)take.JewelrySelectFlags, take.Jewelry,
                                   who.Purse.Jewelry.Count, dice);

                if (qty > 0)
                {
                    tally.Jewelry.AddRange(who.Purse.Jewelry.Take(qty));
                }

                who.Purse.RemoveJewelry(qty);
            }
        }

        if ((take.TakeItems & (byte)TakeItemsAction.Money) != 0)
        {
            TakeMoney(take, party, victims, dice, tally);
        }

        return new TakeItemsOutcome(tally.Items, tally.Gems, tally.Jewelry, tally.Money,
                                    tally.Coins);
    }

    /// <summary>The inventory block (<c>Party.cpp:2195-2302</c>).</summary>
    /// <remarks>
    /// The event's item list is the outer loop and the characters the inner one, so a two-item
    /// event sweeps the whole party for the first before starting on the second.
    /// </remarks>
    private static void TakeInventory(TakePartyItemsEvent take, IReadOnlyList<Character> victims,
                                      Tally tally)
    {
        switch ((TakeQuantity)take.ItemSelectFlags)
        {
            case TakeQuantity.Specified:
                foreach (var wanted in take.Items.Items)
                {
                    foreach (var who in victims)
                    {
                        TakeNamed(take, who, wanted, tally);
                    }
                }

                break;

            case TakeQuantity.Random:
            case TakeQuantity.Percent:
                // "not used for items" -- Party.cpp:2261, and the editor hides both.
                break;

            case TakeQuantity.All:
                foreach (var who in victims)
                {
                    TakeEverything(take, who, tally);
                }

                break;

            // The switch is defaultless: an unrecognised value takes no items either.
            default:
                break;
        }
    }

    /// <summary>
    /// Takes up to <c>itemPcnt</c> units of one named item from one character
    /// (<c>Party.cpp:2205-2227</c>).
    /// </summary>
    /// <remarks>
    /// <b>The counter advances on finding the item, not on removing it</b> (<c>:2213-2215</c>), and
    /// the search restarts from the head of the pack every pass — so a character holding the same
    /// item in two stacks has the first drained before the second is touched, and the loop ends the
    /// moment no stack matches.
    /// </remarks>
    private static void TakeNamed(TakePartyItemsEvent take, Character who, ItemInstance wanted,
                                  Tally tally)
    {
        int count = 0;

        while (count < take.ItemPercent)
        {
            int index = who.Items.FindIndex(
                i => string.Equals(i.ItemId, wanted.ItemId, StringComparison.Ordinal));

            if (index < 0)
            {
                break;
            }

            count++;

            if (DeleteUnits(who.Items, index, 1))
            {
                tally.Items.Add(wanted with { Quantity = 1 });
            }
        }
    }

    /// <summary>Empties one character's pack (<c>Party.cpp:2266-2298</c>).</summary>
    /// <remarks>
    /// The reference walks positions and deletes as it goes, having noticed that the obvious second
    /// <c>GetNext</c> would read a freed node — the commented-out line at <c>:2279</c> is the fix.
    /// An item that refuses to be deleted stays in the pack and the walk moves past it, which is
    /// what the index arithmetic here reproduces.
    /// </remarks>
    private static void TakeEverything(TakePartyItemsEvent take, Character who, Tally tally)
    {
        for (int i = 0; i < who.Items.Count;)
        {
            var item = who.Items[i];

            if (Remove(who.Items, i))
            {
                tally.Items.Add(item);
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>The money block (<c>Party.cpp:2390-2436</c>).</summary>
    private static void TakeMoney(TakePartyItemsEvent take, Party party,
                                  IReadOnlyList<Character> victims, Func<int, int> dice,
                                  Tally tally)
    {
        var rules = party.Pooled.Rules;
        var currency = Currency(take);

        // Party.cpp:2393 -- the amount is converted to the base coin before any of the four
        // quantity rules see it, which is what makes a percentage take meaningless.
        int qty = (int)rules.ConvertToBase(take.Platinum, currency);

        foreach (var who in victims)
        {
            int available = (int)who.Purse.Total();
            int taken = Quantity((TakeQuantity)take.PlatinumSelectFlags, qty, available, dice);

            if (taken <= 0)
            {
                continue;
            }

            if (take.StoreItems != 0)
            {
                int even = (int)rules.Convert(taken, rules.BaseType, currency, out double remain);

                // UpdateMoneyInVault returns early on a quantity of 0 (GlobalData.cpp:5642), and
                // the overflow reaches it as an int -- so both are tested after truncation.
                if (even != 0)
                {
                    tally.Coins.Add(new CoinDeposit(currency, even));
                }

                if ((int)remain != 0)
                {
                    tally.Coins.Add(new CoinDeposit(rules.BaseType, (int)remain));
                }
            }

            PayForItem(who, party, taken, rules.BaseType);
            tally.Money += taken;
        }
    }

    /// <summary>
    /// Charges one character (<c>CHARACTER::payForItem</c>, <c>Shared/Char.cpp:6758</c>), with the
    /// gem and jewellery costs left at 0 as this caller leaves them.
    /// </summary>
    /// <remarks>
    /// The pooled purse is spent first when the party has pooled and it covers the charge, and
    /// draining it clears the flag — so the next charge in the same loop may fall on a character
    /// instead. <see cref="EventWhoPays.Take"/> transcribes the same function from the other side.
    /// </remarks>
    private static void PayForItem(Character who, Party party, int cost, ItemClass type)
    {
        if (cost <= 0)
        {
            return;
        }

        if (party.MoneyPooled != 0 && party.Pooled.HaveEnough(type, cost))
        {
            party.Pooled.Subtract(type, cost);
            party.MoneyPooled = party.Pooled.IsEmpty ? 0 : 1;
        }
        else
        {
            who.Purse.Subtract(type, cost);
        }
    }

    /// <summary>
    /// Takes units off one item entry (<c>ITEM_LIST::deleteItem</c>, <c>Items.cpp:3667</c>).
    /// </summary>
    /// <returns>
    /// True, always, on this path: the reference's only failure is a key it cannot find, and the
    /// index was just looked up. <c>delCharacterItem</c> (<c>Char.cpp:6643</c>) adds nothing but the
    /// encumbrance recalculation this port has nowhere to put.
    /// </returns>
    /// <remarks>
    /// <b>A quantity larger than the entry holds is clamped, not refused</b> (<c>:3677</c>) — the
    /// reference logs "bogus qty" and carries on — so the entry cannot go negative, and an entry
    /// that refuses to be removed sits at 0 forever.
    /// </remarks>
    private static bool DeleteUnits(List<ItemInstance> items, int index, int quantity)
    {
        var item = items[index];

        if (quantity > item.Quantity)
        {
            quantity = item.Quantity;
        }

        item = item with { Quantity = item.Quantity - quantity };
        items[index] = item;

        if (item.Quantity < 1)
        {
            Remove(items, index);
        }

        return true;
    }

    /// <summary>
    /// Removes an item entry outright (<c>ITEM_LIST::DeleteItem</c>, <c>Items.cpp:4724</c>).
    /// </summary>
    /// <returns>False when the item is readied <i>and</i> cursed, which is the only refusal.</returns>
    /// <remarks>
    /// The delete is conditional on <c>UnReady</c> (<c>:1618</c>), which succeeds trivially for an
    /// item in the pack, unreadies one that is worn, and fails only when <c>CanUnReady</c>
    /// (<c>:1631</c>) finds it cursed. Readiness is the item's own <c>ReadyLocation</c> against the
    /// <c>NOTRDY</c> sentinel, not the twelve-slot <c>READY_ITEMS</c> block — <c>ITEM_LIST</c> has
    /// no such member, and nothing here writes one.
    /// </remarks>
    private static bool Remove(List<ItemInstance> items, int index)
    {
        var item = items[index];

        if (item.ReadyLocation != NotReady && item.Cursed != 0)
        {
            return false;
        }

        items.RemoveAt(index);
        return true;
    }

    /// <summary>What the four blocks have removed so far.</summary>
    private sealed class Tally
    {
        public List<ItemInstance> Items { get; } = [];

        public List<GemType> Gems { get; } = [];

        public List<GemType> Jewelry { get; } = [];

        public List<CoinDeposit> Coins { get; } = [];

        public int Money { get; set; }
    }
}
