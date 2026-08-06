namespace UAF.Rules;

/// <summary>
/// How a service scales a price (<c>costFactorType</c>, <c>Externs.h:842</c>).
/// </summary>
/// <remarks>
/// <b>The order is the wire order</b> — designs store the ordinal, so the twenty entries have to
/// keep their positions: free, nine divisions from a hundredth up, normal, then eight
/// multiplications.
/// </remarks>
public enum CostFactor
{
    Free = 0,
    Divide100, Divide50, Divide20, Divide10, Divide5, Divide4, Divide3, Divide2, Divide1_5,
    Normal,
    Multiply1_5, Multiply2, Multiply3, Multiply4, Multiply5, Multiply10, Multiply20, Multiply50,
    Multiply100,
}

/// <summary>
/// Applies a service's cost factor to a price (<c>ApplyCostFactor</c>, <c>Globals.cpp:971</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Free is answered before the arithmetic and is the only way to pay nothing.</b> Every other
/// factor floors at one, so a one-coin spell at a hundredth still costs a coin — a temple that
/// means to give something away has to say <see cref="CostFactor.Free"/> rather than dividing
/// enough.
/// </para>
/// <para>
/// <b>It truncates rather than rounding.</b> The scaled price is computed as a double and cast to
/// an integer, so three divided by two is one.
/// </para>
/// <para>
/// Shared by the temple's spells, a shop's items (<c>Items.cpp:2191</c>) and
/// <c>Char.cpp:6695</c> — one scale for every price in the game.
/// </para>
/// </remarks>
public static class Prices
{
    /// <summary>The number of factors a design can store (<c>NUM_COST_FACTOR_TYPES</c>).</summary>
    public const int FactorCount = 20;

    /// <summary>What each factor multiplies a price by. Free is handled before this is read.</summary>
    private static readonly double[] Scale =
    [
        0,                                                  // Free -- never consulted
        1.0 / 100, 1.0 / 50, 1.0 / 20, 1.0 / 10, 1.0 / 5,
        1.0 / 4, 1.0 / 3, 1.0 / 2, 1.0 / 1.5,
        1.0,                                                // Normal
        1.5, 2, 3, 4, 5, 10, 20, 50, 100,
    ];

    /// <summary>Scales a price.</summary>
    public static int Apply(CostFactor factor, int price)
    {
        if (factor == CostFactor.Free)
        {
            return 0;
        }

        // An ordinal a design stored that this build does not know leaves the price alone, which
        // is what the reference's switch does by not matching a case.
        double scaled = (int)factor >= 0 && (int)factor < Scale.Length
            ? price * Scale[(int)factor]
            : price;

        return Math.Max(1, (int)scaled);
    }

    /// <summary>Reads a design's stored ordinal.</summary>
    /// <remarks>
    /// Out of range is <see cref="CostFactor.Normal"/> rather than <see cref="CostFactor.Free"/>:
    /// a value the engine cannot place should not silently make a service free.
    /// </remarks>
    public static CostFactor FactorOf(int stored) =>
        stored >= 0 && stored < FactorCount ? (CostFactor)stored : CostFactor.Normal;
}
