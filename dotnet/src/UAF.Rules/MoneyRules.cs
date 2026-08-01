using UAF.Serialization;

namespace UAF.Rules;

/// <summary>
/// A design's currency: which denominations exist, what they are worth, and how to convert between
/// them (<c>MONEY_DATA_TYPE</c>, <c>Shared/Money.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// A design configures its own coins, so nothing here may assume gold and silver. Ten slots exist;
/// the five AD&amp;D denominations occupy the first five and <c>Coin6</c>–<c>Coin10</c> are spare.
/// </para>
/// <para>
/// <b>"Base" means two different things in this class and they are different coins.</b>
/// <see cref="Coin.IsBase"/> is a per-denomination flag the editor sets, and the defaults set it on
/// <i>platinum</i>. <see cref="BaseType"/> — what <c>GetBaseType</c> returns and what
/// <see cref="ConvertToBase"/> converts to — is the coin with the <b>highest rate</b>, which for
/// those same defaults is <i>copper</i> (<c>ComputeHighestRate</c>, <c>Money.cpp:574</c>). So
/// totals and price comparisons are all in the smallest denomination, and reading the flag instead
/// would value a purse a thousand times low.
/// </para>
/// </remarks>
public sealed class MoneyRules
{
    /// <summary><c>MAX_COIN_TYPES</c> — ten denominations, five of them spare by default.</summary>
    public const int MaxCoinTypes = 10;

    private readonly Coin[] coins;

    public MoneyRules(IReadOnlyList<Coin> denominations)
    {
        ArgumentNullException.ThrowIfNull(denominations);

        coins = new Coin[MaxCoinTypes];
        Array.Fill(coins, Coin.Inactive);

        for (int i = 0; i < Math.Min(denominations.Count, MaxCoinTypes); i++)
        {
            coins[i] = denominations[i] ?? Coin.Inactive;
        }

        (BaseType, HighestRate) = ComputeHighestRate();
    }

    /// <summary>Builds the rules from a design's parsed <c>MONEY_DATA_TYPE</c>.</summary>
    public static MoneyRules FromDesign(MoneyData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new MoneyRules(
            [.. data.Coins.Select(c => new Coin(c.Rate, c.IsBase != 0, c.Name))]);
    }

    /// <summary>The AD&amp;D defaults (<c>SetUADefaults</c>, <c>Money.cpp:514</c>).</summary>
    /// <remarks>
    /// In slot order, which is <b>not</b> descending value: electrum sits at index 1 and gold at 2,
    /// while gold (rate 5) is worth more than electrum (rate 10).
    /// </remarks>
    public static MoneyRules Default { get; } = new(
    [
        new Coin(1.0, IsBase: true, "Platinum"),
        new Coin(10.0, false, "Electrum"),
        new Coin(5.0, false, "Gold"),
        new Coin(100.0, false, "Silver"),
        new Coin(1000.0, false, "Copper"),
    ]);

    /// <summary>
    /// The denomination everything is measured in — the one with the highest rate, which is the
    /// <i>least</i> valuable coin. See the class remarks.
    /// </summary>
    public ItemClass BaseType { get; }

    /// <summary><see cref="BaseType"/>'s rate.</summary>
    public int HighestRate { get; }

    public Coin this[ItemClass type] => coins[IndexOf(type)];

    /// <summary>A denomination the design has configured. Rate 0 means the slot is unused.</summary>
    public bool IsActive(ItemClass type) => coins[IndexOf(type)].Rate > 0.0;

    public double RateOf(ItemClass type) => coins[IndexOf(type)].Rate;

    /// <summary>Every configured denomination, in slot order.</summary>
    public IEnumerable<ItemClass> ActiveTypes =>
        Enumerable.Range(0, MaxCoinTypes).Where(i => coins[i].Rate > 0.0).Select(ClassOf);

    private (ItemClass Type, int Rate) ComputeHighestRate()
    {
        // Strictly greater, so the FIRST slot at the highest rate wins a tie. With no coins at all
        // the original leaves HRType as BogusItemType rather than defaulting to a real coin.
        int rate = 0;
        var type = ItemClass.BogusItem;

        for (int i = 0; i < MaxCoinTypes; i++)
        {
            if (coins[i].Rate > rate)
            {
                rate = (int)coins[i].Rate;
                type = ClassOf(i);
            }
        }

        return (type, rate);
    }

    /// <summary>
    /// Slot index for a denomination (<c>GetIndex</c>, <c>Money.cpp:409</c>).
    /// </summary>
    /// <remarks>
    /// <b>Two ranges, not one offset.</b> The five AD&amp;D types map by subtracting one
    /// (platinum 1→0 … copper 5→4) and the five spares by subtracting seven (Coin6 12→5 …
    /// Coin10 16→9), because <c>BogusItemType</c> at 11 sits between them. A single arithmetic
    /// conversion is the obvious guess and is wrong for the spares.
    /// </remarks>
    public static int IndexOf(ItemClass type) => type switch
    {
        ItemClass.Platinum => 0,
        ItemClass.Electrum => 1,
        ItemClass.Gold => 2,
        ItemClass.Silver => 3,
        ItemClass.Copper => 4,
        ItemClass.Coin6 => 5,
        ItemClass.Coin7 => 6,
        ItemClass.Coin8 => 7,
        ItemClass.Coin9 => 8,
        ItemClass.Coin10 => 9,

        // die(0xab526) -- the reference aborts rather than returning a sentinel, because reaching
        // here means a non-coin item class was passed to the money system.
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "die(0xab526): not a coin denomination."),
    };

    /// <summary>The denomination in a slot (<c>GetItemClass</c>, <c>Money.cpp:434</c>).</summary>
    public static ItemClass ClassOf(int index) => index switch
    {
        0 => ItemClass.Platinum,
        1 => ItemClass.Electrum,
        2 => ItemClass.Gold,
        3 => ItemClass.Silver,
        4 => ItemClass.Copper,
        5 => ItemClass.Coin6,
        6 => ItemClass.Coin7,
        7 => ItemClass.Coin8,
        8 => ItemClass.Coin9,
        9 => ItemClass.Coin10,
        _ => throw new ArgumentOutOfRangeException(
            nameof(index), index, "die(0xab527): not a coin slot."),
    };

    /// <summary>
    /// Converts an amount between denominations (<c>Convert</c>, <c>Money.cpp:752</c>).
    /// </summary>
    /// <param name="overflow">
    /// Coins that would not divide evenly, expressed in the <i>source</i> denomination. Converting
    /// 105 copper to silver yields 10 silver with 5 copper of overflow.
    /// </param>
    /// <returns>The whole number of destination coins.</returns>
    /// <remarks>
    /// <para>
    /// The result is truncated to whole coins and the remainder comes back through
    /// <paramref name="overflow"/>, so a caller that ignores it <b>loses money</b>. Every call site
    /// in the reference either passes it or is converting a value it knows divides evenly.
    /// </para>
    /// <para>
    /// The overflow is rounded with <c>floor(x + 0.5)</c> — half away from zero for positives —
    /// rather than with C's <c>round</c> or a cast. The original's own commented-out <c>ceil</c>
    /// sits beside it, so this was deliberate.
    /// </para>
    /// <para>
    /// An inactive denomination on either side yields 0 rather than throwing, which silently
    /// destroys the amount. Reproduced: a design that removes a coin type in mid-campaign really
    /// does behave this way.
    /// </para>
    /// </remarks>
    public double Convert(double amount, ItemClass source, ItemClass destination,
                          out double overflow)
    {
        overflow = 0.0;

        if (amount == 0)
        {
            return 0.0;
        }

        if (source == destination)
        {
            return amount;
        }

        double sourceRate = RateOf(source);
        double destinationRate = RateOf(destination);

        // Three separate guards, in the original's order: an inactive coin on either side destroys
        // the amount, but two coins at the same rate exchange one for one.
        if (sourceRate == 0.0 || destinationRate == 0.0)
        {
            return 0.0;
        }

        if (destinationRate == sourceRate)
        {
            return amount;
        }

        // A lower destination rate is a more valuable coin, so fewer of them.
        double divisor = destinationRate < sourceRate
            ? Math.Max(1.0, sourceRate / destinationRate)
            : Math.Max(1.0, destinationRate / sourceRate);

        double total = destinationRate < sourceRate ? amount / divisor : amount * divisor;

        double fraction = total - Math.Truncate(total);
        total = Math.Truncate(total);

        overflow = Math.Floor((fraction * divisor) + 0.5);
        return total;
    }

    /// <inheritdoc cref="Convert(double, ItemClass, ItemClass, out double)"/>
    public double Convert(double amount, ItemClass source, ItemClass destination) =>
        Convert(amount, source, destination, out _);

    /// <summary>Converts to <see cref="BaseType"/> — the smallest denomination.</summary>
    public double ConvertToBase(double amount, ItemClass source, out double overflow) =>
        Convert(amount, source, BaseType, out overflow);

    /// <inheritdoc cref="ConvertToBase(double, ItemClass, out double)"/>
    public double ConvertToBase(double amount, ItemClass source) =>
        Convert(amount, source, BaseType, out _);
}
