using UAF.Rules;

namespace UAFcore;

/// <summary>What one turn of the clock did, for the caller to report or redraw.</summary>
/// <param name="Healed">Characters who gained a hit point from a full day's rest.</param>
/// <param name="Redraw">Whether anything visible changed.</param>
public sealed record TimePassed(IReadOnlyList<Character> Healed, bool Redraw);

/// <summary>
/// What the passage of game time does to a party
/// (<c>PARTY::ProcessTimeSensitiveData</c>, <c>Party.cpp:4052</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This runs on every cycle, not only while resting</b> — spell effects expire and days turn
/// over whether the party is asleep or walking. Resting only changes what else happens.
/// </para>
/// <para>
/// <b>What is here and what is not.</b> Ported: the rest tally, the auto-heal, the new-day resets,
/// and expiring spell effects. Not ported: spell <i>memorisation</i>, which needs
/// <c>IncAllMemorizedTime</c> and the per-character spell list; drink points; and the
/// background-music day/night switch. The poison block is commented out in the original, so there
/// is nothing there to port.
/// </para>
/// <para>
/// <b>Resting does not wake an unconscious character, and that is not an omission here.</b> The
/// reference has such a block (<c>:4175</c>) sitting inside <c>if (lastUpdateTime != -1)</c> and
/// gated on <c>if (resting &amp;&amp; (lastUpdateTime == -1))</c> — two conditions that
/// contradict, so it never runs. <c>PARTY::BeginResting</c> (<c>:4018</c>) does the same job at
/// the right moment and is <i>never called</i> from anywhere in the source. Since the auto-heal
/// also skips the unconscious, a character who goes down stays down however long the party sleeps.
/// </para>
/// </remarks>
public static class PartyTime
{
    /// <summary>
    /// Advances everything that depends on the clock.
    /// </summary>
    /// <param name="elapsedMinutes">Game minutes since the last cycle.</param>
    /// <param name="resting">Whether the party is resting rather than adventuring.</param>
    /// <param name="newDay">Whether the day counter turned over during those minutes.</param>
    public static TimePassed Advance(Party party, RestClock clock, int elapsedMinutes,
                                     bool resting, bool newDay)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(clock);

        if (elapsedMinutes <= 0)
        {
            return new TimePassed([], false);
        }

        int healing = clock.Advance(elapsedMinutes, resting);

        var healed = new List<Character>();
        bool redraw = false;

        foreach (var member in party.Members)
        {
            // Expiry runs for everyone every cycle, which is why a blessing wears off while the
            // party walks and not only when it sleeps.
            if (member.Effects.Expire(elapsedMinutes).Count > 0)
            {
                redraw = true;
            }

            if (newDay)
            {
                // ResetItemCharges(TRUE) and HasLayedOnHandsToday = FALSE. Neither is modelled on
                // the live character yet -- the charges live on the record's items and lay-on-
                // hands has no rule -- so this is where they will go rather than what they do.
                redraw = true;
            }

        }

        if (healing > 0)
        {
            foreach (var member in party.Members)
            {
                // Alive and *not unconscious*: someone still out cold heals nothing, and
                // nothing in the engine wakes them -- see this class's remarks.
                if (IsAlive(member) && member.Status != CharacterStatus.Unconscious)
                {
                    member.HitPoints += healing;
                    healed.Add(member);
                    redraw = true;
                }
            }
        }

        return new TimePassed(healed, redraw);
    }

    /// <summary>
    /// <c>CHARACTER::IsAlive</c> (<c>Char.h:680</c>).
    /// </summary>
    /// <remarks>
    /// <b>Four statuses count as alive, and one of them is <c>Dying</c>.</b> Petrified and Gone do
    /// not — so a petrified character rests all night and gains nothing.
    /// </remarks>
    public static bool IsAlive(Character who)
    {
        ArgumentNullException.ThrowIfNull(who);

        return who.Status is CharacterStatus.Okay or CharacterStatus.Unconscious
                          or CharacterStatus.Running or CharacterStatus.Dying;
    }
}
