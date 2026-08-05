namespace UAF.Rules;

/// <summary>
/// How long the party has left to rest, and how fast that time passes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The editing is not here.</b> <c>RestTimeForm</c> already owns the field cursor and the
/// carrying increment — it was built and tested rounds ago and simply had no screen behind it.
/// What this adds is the arithmetic a <i>running</i> rest needs: how much is left, and how fast
/// to spend it.
/// </para>
/// <para>
/// <b>Days have no ceiling.</b> Nothing bounds the field, and the tick ladder is built to make
/// very long rests pass quickly rather than to prevent them.
/// </para>
/// </remarks>
public readonly record struct RestDuration(int Days, int Hours, int Minutes)
{
    /// <summary>Minutes in a day, and in an hour.</summary>
    public const int MinutesPerDay = 1440;

    /// <inheritdoc cref="MinutesPerDay"/>
    public const int MinutesPerHour = 60;

    /// <summary>The whole duration as minutes.</summary>
    public int TotalMinutes => (Days * MinutesPerDay) + (Hours * MinutesPerHour) + Minutes;

    /// <summary>Whether any time is left to wait.</summary>
    public bool Elapsed => TotalMinutes <= 0;

    /// <summary>
    /// Subtracts elapsed minutes and re-splits what is left.
    /// </summary>
    /// <remarks>
    /// <b>The clamps afterwards are the reference's, minus-one and all.</b> It floors the days at
    /// zero, the hours into 0–23, and the minutes into <b>−1</b>–59 (<c>RunEvent.cpp:22893</c>).
    /// The negative floor cannot be reached from the arithmetic above it, which always produces a
    /// non-negative remainder — so it is a guard against a case that no longer exists rather than
    /// a sentinel, and nothing here depends on it.
    /// </remarks>
    public RestDuration Less(int minutes)
    {
        int left = TotalMinutes - minutes;

        if (left <= 0)
        {
            return new RestDuration(0, 0, 0);
        }

        int days = left / MinutesPerDay;
        left %= MinutesPerDay;

        return new RestDuration(days, left / MinutesPerHour, left % MinutesPerHour);
    }

    /// <summary>
    /// How many game minutes one cycle advances (<c>GetMinuteDelta</c>, <c>RunEvent.cpp:10485</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Time passes faster the more of it is left</b>, so a sixty-day rest does not take an hour
    /// of real time. The ladder is coarse — a fortnight a cycle at the top, a minute at the
    /// bottom.
    /// </para>
    /// <para>
    /// <b>Its top four rungs are commented out</b>, which is why anything above sixty days steps
    /// a fortnight at a time rather than a proportion of what remains. The remaining ladder is
    /// what runs.
    /// </para>
    /// <para>
    /// <b>Its final guard cannot fire.</b> <c>if (minuteTotal &lt; minuteDelta) minuteDelta = 1</c>
    /// — but every live rung returns a delta no larger than the threshold that selected it, so the
    /// remainder is never below it. It protects the four commented-out rungs at the top, which
    /// took a <i>proportion</i> of what was left rather than a fixed step. Kept because it is
    /// what the reference does and because uncommenting a rung would need it again.
    /// </para>
    /// </remarks>
    public int MinuteDelta()
    {
        int total = TotalMinutes;
        if (total <= 0)
        {
            return 0;
        }

        int delta = total switch
        {
            >= 60 * MinutesPerDay => 14 * MinutesPerDay,
            >= 30 * MinutesPerDay => 2 * MinutesPerDay,
            >= 2 * MinutesPerDay => MinutesPerDay,
            >= MinutesPerDay => 2 * MinutesPerHour,
            >= 12 * MinutesPerHour => MinutesPerHour,
            >= 6 * MinutesPerHour => 30,
            >= MinutesPerHour => 15,
            >= 30 => 5,
            _ => 1,
        };

        return total < delta ? 1 : delta;
    }
}
