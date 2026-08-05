namespace UAF.Rules;

/// <summary>
/// How long the party has rested without a break, and when that earns a hit point
/// (<c>PARTY::ProcessTimeSensitiveData</c>, <c>Party.cpp:4052</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only unbroken rest counts.</b> Any tick where the party is not resting sets the tally back
/// to zero outright — not down, to zero — so twenty-three hours of rest interrupted for a minute
/// is worth nothing at all.
/// </para>
/// <para>
/// <b>Over a day, the remainder carries.</b> The tally is reduced by a day rather than cleared, so
/// a two-day rest is two hit points and not one — as long as no cycle in between happened while
/// awake.
/// </para>
/// </remarks>
public sealed class RestClock
{
    /// <summary>A full day of rest, which is what one hit point costs.</summary>
    public const int MinutesPerDay = 1440;

    /// <summary>Minutes of unbroken rest so far.</summary>
    public int MinutesRested { get; private set; }

    /// <summary>Total minutes the clock has seen, resting or not.</summary>
    public int MinuteTotal { get; private set; }

    /// <summary>
    /// Takes the minutes since the last cycle.
    /// </summary>
    /// <returns>How many hit points a day of rest has earned — 0 or 1.</returns>
    /// <remarks>
    /// <b>At most one hit point per cycle, however long the cycle was.</b> The reference tests
    /// <c>if (minutesRested >= 1440)</c> once and subtracts once — not a loop — so a rest whose
    /// step covers a fortnight still heals a single point that cycle and carries the rest of the
    /// tally forward. With the delta ladder shortening as a rest runs down, that is roughly a
    /// point per cycle rather than per day.
    /// </remarks>
    public int Advance(int elapsedMinutes, bool resting)
    {
        if (elapsedMinutes <= 0)
        {
            return 0;
        }

        MinuteTotal += elapsedMinutes;
        MinutesRested = resting ? MinutesRested + elapsedMinutes : 0;

        if (MinutesRested < MinutesPerDay)
        {
            return 0;
        }

        MinutesRested -= MinutesPerDay;
        return 1;
    }

    /// <summary>
    /// Resets the bookkeeping, as loading a saved game does.
    /// </summary>
    /// <remarks>
    /// The reference passes a non-positive time to mean "start again" (<c>:4059</c>), which is how
    /// a loaded game avoids inheriting the clock of the one it replaced.
    /// </remarks>
    public void Reset()
    {
        MinutesRested = 0;
        MinuteTotal = 0;
    }
}
