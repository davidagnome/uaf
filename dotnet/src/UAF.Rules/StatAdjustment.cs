namespace UAF.Rules;

/// <summary>What one press of up or down on the stats screen did.</summary>
/// <param name="Changed">
/// Whether the screen should redraw. <b>Not the same as "the score moved"</b> — see
/// <see cref="StatAdjustment.Increase"/>.
/// </param>
/// <param name="Score">The score afterwards, after the clamps.</param>
/// <param name="Available">The points left to spend.</param>
public readonly record struct StatChange(bool Changed, int Score, int Available);

/// <summary>
/// Raising and lowering an ability score by hand on the stats screen
/// (<c>STF_IncrStat</c> and <c>STF_DecrStat</c>, <c>CharStatsForm.cpp:1771</c>, <c>:1858</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is point-buy from nothing.</b> The available points start at zero every time the screen
/// opens and no other code adds to them, so the only way to raise a score is to lower another
/// first. A player cannot make a character better this way, only differently shaped.
/// </para>
/// <para>
/// <b>The two directions are not symmetric.</b> The increase charges the change it actually
/// achieved; the decrease credits exactly one point whatever happened. The guarding
/// <c>if (orig != final)</c> around the credit is commented out in the reference.
/// </para>
/// </remarks>
public static class StatAdjustment
{
    /// <summary>The score at which a class's exceptional-strength percentile applies.</summary>
    public const int ExceptionalStrength = 18;

    /// <summary>
    /// Raises a score by one, if there is a point to spend and the class allows it.
    /// </summary>
    /// <param name="normalise">
    /// <c>UpdateStats</c>'s clamps — the race's limits and then the class's
    /// (<c>Char.cpp:4451</c>). Applied after the score is set, and its answer is what the
    /// character keeps.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The guard and the clamp do not consult the same limits, and that is the whole reason
    /// the cost is computed rather than fixed.</b> The guard reads only the <i>class</i> maximum;
    /// <c>UpdateStats</c> then clamps against the <i>race</i>'s as well, and the race check runs
    /// first. So where a race is stricter than the class, the press is allowed, the score is put
    /// back where it was, and <c>*avail += orig - final</c> charges nothing. The player sees the
    /// screen redraw and the number not move.
    /// </para>
    /// <para>
    /// <b>A score of exactly zero available points refuses before anything else</b>, so the
    /// clamps are never even consulted on the first press.
    /// </para>
    /// </remarks>
    public static StatChange Increase(int score, int available, AbilityLimits classLimits,
                                      Func<int, int>? normalise = null)
    {
        if (available == 0)
        {
            return new StatChange(false, score, available);
        }

        if (score >= classLimits.Max)
        {
            return new StatChange(false, score, available);
        }

        int final = (normalise ?? (x => x))(score + 1);

        // Charges what it achieved, which is nothing when a tighter limit put the score back.
        return new StatChange(true, final, available + score - final);
    }

    /// <summary>
    /// Lowers a score by one, if the class allows it.
    /// </summary>
    /// <remarks>
    /// <b>The point is credited unconditionally.</b> Where a clamp puts the score straight back
    /// up — a race whose minimum is above the class's — the player gains a point for nothing and
    /// can spend it elsewhere. The reference's own <c>if (orig != final)</c> guard against this is
    /// commented out.
    /// </remarks>
    public static StatChange Decrease(int score, int available, AbilityLimits classLimits,
                                      Func<int, int>? normalise = null)
    {
        if (score <= classLimits.Min)
        {
            return new StatChange(false, score, available);
        }

        int final = (normalise ?? (x => x))(score - 1);

        return new StatChange(true, final, available + 1);
    }

    /// <summary>
    /// The exceptional-strength percentile after a change to strength.
    /// </summary>
    /// <param name="before">The score before the change.</param>
    /// <param name="after">The score after it, and after the clamps.</param>
    /// <param name="cached">
    /// The percentile already rolled this visit, or 0 for none. The reference holds it in a static
    /// that is cleared when the screen opens, so walking a score down off 18 and back up returns
    /// the same percentile rather than re-rolling for a better one.
    /// </param>
    /// <param name="roll">Rolls the class's <c>strengthBonusDice</c>.</param>
    /// <returns>
    /// The percentile to store and the value to cache. <b>A null percentile means leave the stored
    /// one alone</b> — neither branch touches the modifier except at 18, so a score moving between
    /// 12 and 13 keeps whatever it had.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The two branches sit in different functions and only overlap at 18.</b> The decrease
    /// zeroes the modifier when the score <i>was</i> 18, before moving it; the increase sets it
    /// when the score <i>is</i> 18, after moving it.
    /// </para>
    /// <para>
    /// <b>A roll of zero or less is stored as zero and re-rolled next time</b>, because the cache
    /// is tested by being zero. A class with no strength dice therefore re-rolls nothing on every
    /// press, which costs nothing and produces nothing.
    /// </para>
    /// </remarks>
    public static (int? Percentile, int Cached) StrengthPercentile(
        int before, int after, int cached, Func<int?> roll)
    {
        ArgumentNullException.ThrowIfNull(roll);

        if (after == ExceptionalStrength)
        {
            if (cached == 0)
            {
                cached = Math.Max(roll() ?? 0, 0);
            }

            return (cached, cached);
        }

        return before == ExceptionalStrength ? (0, cached) : (null, cached);
    }
}
