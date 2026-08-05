using UAF.Rules;

namespace UAFcore;

/// <summary>What one turn of the clock did, for the caller to report or redraw.</summary>
/// <param name="Healed">Characters who gained a hit point from a full day's rest.</param>
/// <param name="Memorized">
/// What the last minute of memorising finished, if anything — <b>not</b> everything the rest
/// memorised. See <see cref="PartyTime.Advance"/>.
/// </param>
/// <param name="Redraw">Whether anything visible changed.</param>
public sealed record TimePassed(IReadOnlyList<Character> Healed, string? Memorized, bool Redraw);

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
/// <b>The waking does not happen here — it happens when the rest screen opens.</b> This function
/// has a block that would do it (<c>:4175</c>), sitting inside <c>if (lastUpdateTime != -1)</c>
/// and gated on <c>if (resting &amp;&amp; (lastUpdateTime == -1))</c> — two conditions that
/// contradict, so that copy never runs. <c>PARTY::BeginResting</c> (<c>:4018</c>) does the job
/// instead, called from <c>REST_MENU_DATA::OnInitialEvent</c> (<c>RunEvent.cpp:22812</c>) — see
/// <see cref="BeginResting"/>. The auto-heal below still skips the unconscious, but by the time a
/// day has passed they have already been woken.
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
    /// <param name="canCast">
    /// Whether a character may memorise at all. <c>CanCastSpells</c> — and not
    /// <c>CanMemorizeSpells(1)</c>, whose "resting" circumstance the engine defines in a header
    /// comment and never asks.
    /// </param>
    /// <param name="nameOf">A spell's name, for the announcement.</param>
    public static TimePassed Advance(Party party, RestClock clock, int elapsedMinutes,
                                     bool resting, bool newDay,
                                     Func<Character, bool>? canCast = null,
                                     Func<string, string>? nameOf = null)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(clock);

        if (elapsedMinutes <= 0)
        {
            return new TimePassed([], null, false);
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

        string? announced = null;

        if (resting)
        {
            announced = Memorize(party, elapsedMinutes, canCast, nameOf, ref redraw);
        }

        if (healing > 0)
        {
            foreach (var member in party.Members)
            {
                // Alive and *not unconscious*: someone still out cold heals nothing. They are
                // woken when the rest screen opens rather than here -- see BeginResting.
                if (IsAlive(member) && member.Status != CharacterStatus.Unconscious)
                {
                    member.HitPoints += healing;
                    healed.Add(member);
                    redraw = true;
                }
            }
        }

        return new TimePassed(healed, announced, redraw);
    }

    /// <summary>
    /// Ticks every caster's book, a minute at a time
    /// (<c>ProcessTimeSensitiveData</c>'s resting branch, <c>Party.cpp:4118</c>).
    /// </summary>
    /// <returns>The last announcement standing, or null.</returns>
    /// <remarks>
    /// <para>
    /// <b>A minute at a time, unlike the auto-heal.</b> The reference loops <c>inc</c> times over
    /// the whole party — so memorisation really does get every minute of a coarse step, where the
    /// day's hit point is granted at most once per cycle.
    /// </para>
    /// <para>
    /// <b>Only the last announcement survives.</b> A minute that finishes a copy sets the paused
    /// text; a minute that finishes nothing <i>clears</i> it — and the clearing is inside the
    /// per-character loop, so one caster finishing nothing wipes what another just set. What a
    /// player sees is whatever the very last character on the very last minute did.
    /// </para>
    /// </remarks>
    private static string? Memorize(Party party, int minutes, Func<Character, bool>? canCast,
                                    Func<string, string>? nameOf, ref bool redraw)
    {
        string? announced = null;

        for (int minute = 0; minute < minutes; minute++)
        {
            foreach (var member in party.Members)
            {
                if (canCast?.Invoke(member) == false || member.Book.Entries.Count == 0)
                {
                    continue;
                }

                if (!member.Book.AddMemorizeTime(1))
                {
                    announced = null;
                    continue;
                }

                var finished = member.Book.Entries.FirstOrDefault(e => e.JustMemorized);
                if (finished is not null)
                {
                    finished.JustMemorized = false;
                    announced = $"{member.Name} memorizes {nameOf?.Invoke(finished.SpellId)
                                                           ?? finished.SpellId}";
                    redraw = true;
                }
            }
        }

        return announced;
    }

    /// <summary>
    /// Wakes the party at the start of a rest (<c>PARTY::BeginResting</c>, <c>Party.cpp:4018</c>).
    /// </summary>
    /// <returns>Whoever was brought round.</returns>
    /// <remarks>
    /// <para>
    /// <b>An unconscious character wakes at one hit point.</b> Not healed — woken, so that the
    /// day's auto-heal, which skips the unconscious, can reach them at all.
    /// </para>
    /// <para>
    /// <b>A <c>BeginResting</c> script can veto it</b> by answering anything but zero. With no
    /// scripting layer the answer is the default, which is to wake them.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Character> BeginResting(Party party,
                                                        Func<Character, bool>? vetoed = null)
    {
        ArgumentNullException.ThrowIfNull(party);

        var woken = new List<Character>();

        foreach (var member in party.Members)
        {
            if (vetoed?.Invoke(member) == true || member.Status != CharacterStatus.Unconscious)
            {
                continue;
            }

            member.HitPoints = 1;
            member.Status = CharacterStatus.Okay;
            woken.Add(member);
        }

        return woken;
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
