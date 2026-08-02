namespace UAF.Rules;

/// <summary>
/// How a spell's duration is measured (<c>spellDurationType</c>, <c>GameRules.h:333</c>).
/// </summary>
/// <remarks>
/// The numbering is serialized, so it is transcribed rather than tidied — note that
/// <see cref="ByDamageTaken"/> sits at 1, between <see cref="InRounds"/> and <see cref="InHours"/>,
/// which is not the order anyone would choose.
/// </remarks>
public enum SpellDurationRate
{
    InRounds = 0,
    ByDamageTaken = 1,
    InHours = 2,
    InDays = 3,
    Permanent = 4,
    ByNumberOfAttacks = 5,
}

/// <summary>
/// When a spell effect stops (<c>Char.cpp:16397</c> and <c>Spell.cpp:967</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything is measured in elapsed game minutes, and one combat round is one minute.</b>
/// <c>StartNewRound</c> calls <c>party.incrementClock(1)</c> and that parameter is minutes
/// (<c>Party.cpp:1422</c>), so a three-round spell and a three-minute spell are the same thing.
/// That is the bridge between the round clock and the duration layer, and it is why the two could
/// not be built independently.
/// </para>
/// </remarks>
public static class SpellDuration
{
    /// <summary>Minutes in an hour, as the conversion uses it.</summary>
    public const int MinutesPerHour = 60;

    /// <summary>Minutes in a day.</summary>
    public const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// The elapsed-minute reading at which an effect stops
    /// (the <c>Duration_Rate</c> switch at <c>Char.cpp:16397</c>).
    /// </summary>
    /// <param name="rate">How <paramref name="duration"/> is to be read.</param>
    /// <param name="duration">The rolled duration, in whatever unit the rate names.</param>
    /// <param name="elapsedMinutes">The clock now.</param>
    /// <returns>
    /// The stop time, or <see langword="null"/> when the reference computes none — see the
    /// remarks, because null does not mean "never".
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Every timed rate has a floor of one minute</b>, applied after the unit conversion, so a
    /// spell rolled at "half an hour" and one rolled at "one round" both last a minute at least.
    /// The comments on two of the three even disagree about what the floor is called — "1 minute
    /// min" on rounds, "1 round min" on hours and days — which is the same value under two names.
    /// </para>
    /// <para>
    /// <b><see cref="SpellDurationRate.ByDamageTaken"/> and
    /// <see cref="SpellDurationRate.ByNumberOfAttacks"/> store the count raw</b>, not a time, and
    /// are then unreachable: <c>IsReadyToExpire</c> reaches its error path for both
    /// (<c>Spell.cpp:991</c>). They are declared, authored and never actually supported.
    /// </para>
    /// <para>
    /// <b><see cref="SpellDurationRate.Permanent"/> has no case at all</b> — the switch falls to
    /// <c>default: die()</c> and leaves the stop time at whatever it already was, which after
    /// construction is zero. Zero then means <i>expire immediately</i> (see
    /// <see cref="IsReadyToExpire"/>), so a permanent effect in the reference lasts no time at all.
    /// This returns null for it rather than reproducing a value the reference never computes;
    /// <see cref="PermanentExpiresImmediately"/> records what the reference actually does.
    /// </para>
    /// </remarks>
    public static double? StopTimeFor(SpellDurationRate rate, double duration,
                                      double elapsedMinutes) => rate switch
    {
        SpellDurationRate.InRounds => Math.Max(1, duration) + elapsedMinutes,
        SpellDurationRate.InHours => Math.Max(1, duration * MinutesPerHour) + elapsedMinutes,
        SpellDurationRate.InDays => Math.Max(1, duration * MinutesPerDay) + elapsedMinutes,

        // Stored raw, and never consulted -- see the remarks.
        SpellDurationRate.ByDamageTaken or SpellDurationRate.ByNumberOfAttacks => duration,

        _ => null,
    };

    /// <summary>
    /// What the reference does with <see cref="SpellDurationRate.Permanent"/>: nothing, leaving a
    /// stop time of zero, which expires on the first check.
    /// </summary>
    /// <remarks>
    /// Recorded as a named constant rather than buried in a comment because it is the kind of
    /// thing a future reader will otherwise assume is a porting mistake.
    /// </remarks>
    public const bool PermanentExpiresImmediately = true;

    /// <summary>
    /// Whether an effect has run out (<c>IsReadyToExpire</c>, <c>Spell.cpp:967</c>).
    /// </summary>
    /// <param name="stopTime">From <see cref="StopTimeFor"/>.</param>
    /// <param name="elapsedMinutes">The clock now.</param>
    /// <param name="fromScript">
    /// Whether the effect came from a script rather than a spell in the database. It changes the
    /// comparison — see the remarks.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A stop time of zero expires immediately.</b> It is the first test in the function, ahead
    /// of everything else, so "no duration" and "already over" are the same state.
    /// </para>
    /// <para>
    /// <b>The two paths disagree by one.</b> A script effect expires when
    /// <c>elapsed &gt;= stopTime</c>; a spell effect when <c>elapsed &gt; stopTime</c>
    /// (<c>:983</c> against <c>:1000</c>). So the same duration lasts a minute longer as a spell
    /// than as a script. Transcribed, because both are reachable and neither is obviously the
    /// intended one.
    /// </para>
    /// </remarks>
    public static bool IsReadyToExpire(double? stopTime, double elapsedMinutes,
                                       bool fromScript = false)
    {
        if (stopTime is not { } stop || stop == 0)
        {
            return true;
        }

        return fromScript ? elapsedMinutes >= stop : elapsedMinutes > stop;
    }
}
