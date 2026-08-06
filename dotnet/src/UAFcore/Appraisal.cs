using UAF.Serialization;

namespace UAFcore;

/// <summary>Which of the two unappraised kinds is being valued.</summary>
public enum Valuable
{
    Gem,
    Jewelry,
}

/// <summary>What the party decided about an appraised piece.</summary>
public enum Appraised
{
    /// <summary>Take the coins.</summary>
    Sell,

    /// <summary>Take a carried item worth the appraisal instead.</summary>
    Keep,
}

/// <summary>
/// Valuing an unappraised gem or piece of jewellery
/// (<c>APPRAISE_SELECT_DATA</c> and <c>APPRAISE_EVALUATE_DATA</c>, <c>RunEvent.cpp:26679</c>,
/// <c>:26793</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The piece leaves the purse before it is valued.</b> Choosing GEMS removes one immediately
/// and only then rolls what it was worth — so there is no way back to an unappraised gem, and
/// KEEP does not put it back.
/// </para>
/// <para>
/// <b>Both outcomes take it out of the purse; they differ in what replaces it.</b> SELL adds the
/// value in the design's default coin to the active character. KEEP creates a <i>carried item</i>
/// named after the design's gem or jewellery type, worth the appraisal — so a kept gem stops being
/// money and starts being inventory.
/// </para>
/// </remarks>
public static class Appraisal
{
    /// <summary>
    /// Rolls what one piece turns out to be worth (<c>GEM_CONFIG::GetAValue</c>,
    /// <c>Money.cpp:309</c>).
    /// </summary>
    /// <param name="roll">Rolls <c>count</c> dice of <c>sides</c> and totals them.</param>
    /// <remarks>
    /// <para>
    /// <b>The maximum is never rolled.</b> The range is <c>|max − min|</c> sides offset by
    /// <c>min − 1</c>, which spans <c>min</c> to <c>max − 1</c> — a design writing 10 to 100 gets
    /// 10 to 99. Transcribed rather than corrected: every price in a shipped design was balanced
    /// against it.
    /// </para>
    /// <para>
    /// <b>A range of nothing is the maximum</b>, which is how a design pins a fixed value by
    /// setting both ends the same.
    /// </para>
    /// </remarks>
    public static int Value(GemConfig config, Func<int, int, int> roll)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(roll);

        int sides = Math.Abs(config.MaxValue - config.MinValue);

        return sides <= 0
            ? config.MaxValue
            : roll(1, sides) + (config.MinValue - 1);
    }

    /// <summary>
    /// Whether the screen will value this kind at all.
    /// </summary>
    /// <remarks>
    /// <b>Two conditions, and the reference tests them as two statements.</b> The service has to
    /// offer it <i>and</i> the purse has to hold one — a shop that appraises gems still darkens
    /// the entry for a party carrying none.
    /// </remarks>
    public static bool CanAppraise(bool offered, int held) => offered && held > 0;
}
