using UAF.Serialization;

namespace UAF.Rules;

/// <summary>
/// Coins, gems and jewellery held by a character, a party or a treasure pile
/// (<c>MONEY_SACK</c>, <c>Shared/Money.cpp:1203</c>).
/// </summary>
/// <remarks>
/// <para>
/// Named <c>Purse</c> rather than <c>MoneySack</c> so it does not collide with
/// <see cref="UAF.Serialization.MoneySack"/>, which is the record read off disk. This is the live
/// thing rules operate on; that is a snapshot of it.
/// </para>
/// <para>
/// Every question about worth goes through <see cref="MoneyRules.BaseType"/>, the smallest
/// denomination — so <see cref="Total"/> is a copper count under the AD&amp;D defaults, not a
/// platinum one.
/// </para>
/// </remarks>
public sealed class Purse(MoneyRules rules)
{
    private readonly MoneyRules rules =
        rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>
    /// Coin counts, one per slot.
    /// </summary>
    /// <remarks>
    /// <b>Integers.</b> <c>MONEY_SACK::operator[]</c> returns an <c>int&amp;</c>, so there is no
    /// such thing as half a coin — every conversion result is truncated on the way in, and that
    /// truncation is where the overflow the conversions hand back has to go instead.
    /// </remarks>
    private readonly int[] coins = new int[MoneyRules.MaxCoinTypes];

    private readonly List<GemType> gems = [];
    private readonly List<GemType> jewelry = [];

    public MoneyRules Rules => rules;

    public IReadOnlyList<GemType> Gems => gems;

    public IReadOnlyList<GemType> Jewelry => jewelry;

    /// <summary>How many coins of one denomination.</summary>
    public int this[ItemClass type]
    {
        get => coins[MoneyRules.IndexOf(type)];
        set => coins[MoneyRules.IndexOf(type)] = value;
    }

    /// <summary>Builds a purse from a parsed record.</summary>
    /// <remarks>
    /// The record's coin list is in slot order, so it is copied straight across rather than mapped
    /// by denomination.
    /// </remarks>
    public static Purse FromRecord(MoneySack? sack, MoneyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var purse = new Purse(rules);
        if (sack is null)
        {
            return purse;
        }

        for (int i = 0; i < Math.Min(sack.Coins.Count, MoneyRules.MaxCoinTypes); i++)
        {
            purse.coins[i] = sack.Coins[i];
        }

        purse.gems.AddRange(sack.Gems);
        purse.jewelry.AddRange(sack.Jewelry);
        return purse;
    }

    public void Clear()
    {
        Array.Clear(coins);
        gems.Clear();
        jewelry.Clear();
    }

    public bool IsEmpty =>
        gems.Count == 0 && jewelry.Count == 0 && coins.All(c => c <= 0);

    /// <summary>
    /// Everything the coins are worth, in <see cref="MoneyRules.BaseType"/>
    /// (<c>Total</c>, <c>Money.cpp:1369</c>).
    /// </summary>
    /// <remarks>
    /// <b>Coins only — gems and jewellery are not counted.</b> They have to be appraised and sold,
    /// so a party holding nothing but gems has a total of zero and cannot buy anything. That is
    /// what the reference does and what the shop code depends on.
    /// </remarks>
    public double Total()
    {
        double total = 0;
        for (int i = 0; i < MoneyRules.MaxCoinTypes; i++)
        {
            if (coins[i] > 0)
            {
                total += rules.ConvertToBase(coins[i], MoneyRules.ClassOf(i));
            }
        }

        return total;
    }

    /// <summary>Value of the gems held (<c>TotalGemValue</c>, <c>Money.cpp:2178</c>).</summary>
    public int TotalGemValue() => gems.Sum(g => g.Value);

    /// <summary>Value of the jewellery held (<c>TotalJewelryValue</c>, <c>:2193</c>).</summary>
    public int TotalJewelryValue() => jewelry.Sum(j => j.Value);

    /// <summary>
    /// Whether the purse can cover an amount (<c>HaveEnough</c>, <c>Money.cpp:1462</c>).
    /// </summary>
    /// <remarks>
    /// Compares totals in the base denomination rather than checking the named coin, so a party
    /// with only platinum can still pay a price quoted in copper.
    /// </remarks>
    public bool HaveEnough(ItemClass type, int amount) =>
        rules.ConvertToBase(amount, type) <= Total();

    /// <summary>
    /// Adds coins of one denomination (<c>Add</c>, <c>Money.cpp:1472</c>).
    /// </summary>
    /// <remarks>
    /// <b>Adding to a denomination the design has not configured does nothing at all.</b> The
    /// amount is dropped rather than converted or rejected, so a treasure paying in a coin the
    /// design removed vanishes.
    /// </remarks>
    public void Add(ItemClass type, int amount)
    {
        if (!rules.IsActive(type))
        {
            return;
        }

        coins[MoneyRules.IndexOf(type)] += amount;
    }

    /// <summary>Adds another purse's contents, coins and valuables alike.</summary>
    public void Add(Purse other)
    {
        ArgumentNullException.ThrowIfNull(other);

        for (int i = 0; i < MoneyRules.MaxCoinTypes; i++)
        {
            coins[i] += other.coins[i];
        }

        gems.AddRange(other.gems);
        jewelry.AddRange(other.jewelry);
    }

    /// <summary>
    /// Moves everything out of <paramref name="source"/> into this purse
    /// (<c>Transfer</c>, <c>Money.cpp:1609</c>).
    /// </summary>
    /// <remarks>
    /// An add followed by clearing the source, so the money is moved rather than copied — which is
    /// what stops a treasure paying out twice if its event runs again.
    /// </remarks>
    public void Transfer(Purse source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Add(source);
        source.Clear();
    }

    public void AddGem(GemType gem) => gems.Add(gem);

    public void AddJewelry(GemType piece) => jewelry.Add(piece);

    /// <summary>
    /// Drops gems off the front of the list (<c>RemoveMultGems</c>, <c>Money.cpp:2208</c>).
    /// </summary>
    /// <remarks>
    /// <b>From the head, so the <i>oldest</i> gems go first — not the cheapest.</b> Every gem is a
    /// <see cref="GemType"/> with its own appraised value and this ignores all of them, so a
    /// two-gem price can take a 5,000gp stone and leave a 10gp one behind. The count is clamped to
    /// what is held rather than throwing, exactly as <c>min(count, NumGems())</c> does — and a
    /// negative count removes nothing, because the reference's loop never runs.
    /// </remarks>
    public void RemoveGems(int count) =>
        gems.RemoveRange(0, Math.Clamp(count, 0, gems.Count));

    /// <summary>
    /// Drops jewellery off the front of the list
    /// (<c>RemoveMultJewelry</c>, <c>Money.cpp:2223</c>).
    /// </summary>
    /// <remarks>The same head-first, value-blind rule as <see cref="RemoveGems"/>.</remarks>
    public void RemoveJewelry(int count) =>
        jewelry.RemoveRange(0, Math.Clamp(count, 0, jewelry.Count));

    /// <summary>
    /// Takes coins out, making change from other denominations when the named one is short
    /// (<c>Subtract</c>, <c>Money.cpp:1500</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three stages, and the order matters. The named denomination is drained first; the shortfall
    /// is then made up from the <b>other denominations in reverse slot order</b> — which under the
    /// defaults means starting at copper and working up to platinum, so the party spends its small
    /// change before breaking a platinum piece. Whatever a denomination cannot cover carries to the
    /// next.
    /// </para>
    /// <para>
    /// If that still does not cover it, everything is emptied and the remaining total is put back
    /// as a single amount in the base denomination — which is how the reference guarantees the
    /// arithmetic balances even when the change-making cannot.
    /// </para>
    /// <para>
    /// <b>Nothing happens at all unless the purse can cover the whole amount.</b> The
    /// <see cref="HaveEnough"/> guard comes first, so a partial payment is never taken.
    /// </para>
    /// </remarks>
    public void Subtract(ItemClass type, int amount)
    {
        if (!rules.IsActive(type) || !HaveEnough(type, amount))
        {
            return;
        }

        int index = MoneyRules.IndexOf(type);

        if (amount <= coins[index])
        {
            coins[index] = Math.Max(coins[index] - amount, 0);
            return;
        }

        int shortfall = amount - coins[index];
        coins[index] = 0;

        double leftover = 0.0;
        bool covered = false;
        for (int c = MoneyRules.MaxCoinTypes - 1; c >= 0 && !covered; c--)
        {
            if (coins[c] <= 0)
            {
                continue;
            }

            // Convert this denomination into the one being spent, keeping the remainder that would
            // not divide evenly. The result is taken as an int, as the reference does -- a partial
            // coin cannot pay for anything.
            int available = (int)rules.Convert(coins[c], MoneyRules.ClassOf(c), type,
                                               out double remainder);
            if (available <= 0)
            {
                continue;
            }

            if (available - shortfall >= 0)
            {
                covered = true;
                available -= shortfall;
                shortfall = 0;

                // Put back what would not divide, plus the unspent part converted back again. That
                // second conversion usually will NOT divide either -- change from a platinum piece
                // is not a whole number of platinum pieces -- so its own overflow is kept as the
                // leftover and redistributed below. Dropping it loses the change entirely.
                coins[c] = (int)Math.Floor(remainder + 0.5);
                coins[c] += (int)rules.Convert(available, type, MoneyRules.ClassOf(c),
                                               out double change);
                leftover = Math.Floor(change + 0.5);
            }
            else
            {
                coins[c] = (int)Math.Floor(remainder + 0.5);
                shortfall = Math.Abs(available - shortfall);
            }
        }

        if (!covered)
        {
            // Nothing could make the change, so the purse is flattened into a single base-coin
            // amount. The guard matters: a total of zero leaves the purse empty rather than
            // writing a zero the base slot would then report as present.
            double total = Total() - rules.ConvertToBase(shortfall, type);
            Array.Clear(coins);

            if (total > 0)
            {
                coins[MoneyRules.IndexOf(rules.BaseType)] = (int)total;
            }

            return;
        }

        if (leftover > 0.0)
        {
            GiveChange(leftover, type, index);
        }
    }

    /// <summary>
    /// Puts change back, in the largest denomination that will take it
    /// (<c>Money.cpp:1567</c>).
    /// </summary>
    /// <param name="amount">The change, expressed in <paramref name="type"/>.</param>
    /// <param name="type">The denomination the purchase was priced in.</param>
    /// <param name="spentIndex">Slot of <paramref name="type"/>, where any last remainder lands.</param>
    /// <remarks>
    /// <b>Each denomination is <i>assigned</i>, not added to.</b> The loop writes
    /// <c>Coins[c] = result</c>, so change landing on a slot that still holds coins overwrites
    /// them. It works out in practice because the change-making has already zeroed the slots it
    /// touched — but it is an assignment, and transcribing it as an addition would quietly hand the
    /// party extra money.
    /// </remarks>
    private void GiveChange(double amount, ItemClass type, int spentIndex)
    {
        bool placed = false;

        for (int c = 0; c < MoneyRules.MaxCoinTypes && !placed; c++)
        {
            int result = (int)rules.Convert(amount, type, MoneyRules.ClassOf(c),
                                            out double remainder);
            if (result > 0)
            {
                amount = remainder;
                coins[c] = result;
            }

            if (amount <= 0.0)
            {
                placed = true;
            }
        }

        if (!placed)
        {
            coins[spentIndex] = (int)amount;
        }
    }

    /// <summary>Takes another purse's coins out of this one.</summary>
    public void Subtract(Purse other)
    {
        ArgumentNullException.ThrowIfNull(other);

        for (int i = 0; i < MoneyRules.MaxCoinTypes; i++)
        {
            coins[i] = Math.Max(coins[i] - other.coins[i], 0);
        }
    }

    /// <summary>
    /// Rolls small coins up into larger ones (<c>AutoUpConvert</c>, <c>Money.cpp:1412</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Works from the least valuable denomination upward — ordering by rate, so the sequence is by
    /// <i>worth</i> rather than by slot, which matters because the slots are not in value order.
    /// Each step converts a denomination into the next more valuable one and keeps its own
    /// remainder <i>in the denomination it was left in</i>: 1050 copper becomes 1 platinum and
    /// 5 silver, not 1 platinum and 50 copper.
    /// </para>
    /// <para>
    /// <b>The scan stops at the first unconfigured slot rather than skipping it</b> — the
    /// reference's loop <c>break</c>s on a zero rate despite its own comment saying "only include
    /// the non-zero coin rates" (<c>Money.cpp:1425</c>). A design that leaves an early slot empty
    /// therefore gets no roll-up at all, and this is not hypothetical:
    /// <c>Ambassador's_Letter</c> configures only gold, silver and copper, leaving platinum at
    /// slot 0 empty.
    /// </para>
    /// </remarks>
    public void AutoUpConvert()
    {
        var order = Enumerable.Range(0, MoneyRules.MaxCoinTypes)
                              .Select(i => (Index: i, Rate: rules.RateOf(MoneyRules.ClassOf(i))))
                              .TakeWhile(c => c.Rate > 0.0)
                              .OrderBy(c => c.Rate)
                              .ToList();

        for (int i = order.Count - 1; i >= 1; i--)
        {
            int from = order[i].Index;
            int to = order[i - 1].Index;

            double converted = rules.Convert(coins[from], MoneyRules.ClassOf(from),
                                             MoneyRules.ClassOf(to), out double leftover);
            coins[to] += (int)converted;
            coins[from] = (int)leftover;
        }
    }
}
