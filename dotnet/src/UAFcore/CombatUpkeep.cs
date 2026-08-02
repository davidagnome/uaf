namespace UAFcore;

/// <summary>
/// The passes that run over every combatant at the head of a round
/// (<c>CheckDyingCombatants</c> and <c>CheckMorale</c>, <c>Combatants.cpp:4697</c>, <c>:4501</c>),
/// plus bandaging, which is what takes a combatant out of the dying loop.
/// </summary>
/// <remarks>
/// <b>Only one of the two start-of-round passes does anything.</b> See <see cref="CheckMorale"/>.
/// </remarks>
public static class CombatUpkeep
{
    /// <summary>
    /// Bleeds every dying combatant for a point
    /// (<c>CheckDyingCombatants</c>, <c>Combatants.cpp:4697</c>).
    /// </summary>
    /// <param name="deadAtZero">The design's <c>deadAtZeroHP</c> flag.</param>
    /// <returns>The combatants that died as a result.</returns>
    /// <remarks>
    /// <para>
    /// This is what gives the −1..−9 dying band its meaning: without it a combatant knocked below
    /// zero stays there forever. Nine rounds of this and a dying combatant reaches −10 and is dead.
    /// </para>
    /// <para>
    /// <b>Bandaging is the only escape</b> and it is permanent — <c>isBandaged</c> is set once and
    /// never cleared for the rest of the fight, so a bandaged combatant is out of the loop rather
    /// than merely skipped this round.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Combatant> CheckDyingCombatants(IReadOnlyList<Combatant> combatants,
                                                                bool deadAtZero = false)
    {
        ArgumentNullException.ThrowIfNull(combatants);

        var died = new List<Combatant>();
        foreach (var c in combatants)
        {
            if (c.Status != CharacterStatus.Dying || c.IsBandaged)
            {
                continue;
            }

            c.HitPoints = Attack.ApplyDamage(c, c.HitPoints, damage: 1, c.MaxHitPoints,
                                             deadAtZero);
            if (c.Status == CharacterStatus.Dead)
            {
                died.Add(c);
            }
        }

        return died;
    }

    /// <summary>
    /// The morale pass — which does nothing, deliberately
    /// (<c>COMBATANT::CheckMorale</c>, <c>Combatant.cpp:3736</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Morale was switched off on purpose and the reason is in the source.</b> The function
    /// still computes a modifier from allies fled and slain and from being outnumbered three to
    /// one — and then discards it, because the <c>SetMorale(GetMorale() - mod)</c> that would have
    /// applied it is commented out. The decision itself is hard-coded:
    /// </para>
    /// <code>
    /// //int cur_morale = GetAdjMorale();
    /// Flee = FALSE; //(RollDice(100, 1, 0) &gt; cur_morale);
    /// </code>
    /// <para>
    /// Immediately above sits a quoted email from the designer, dated 2018-11-02: "I would like the
    /// Morale value to not autochange." So this is a deliberate removal rather than an
    /// unfinished feature, and everything downstream of it is unreachable — the two sites that set
    /// <c>fleeBecauseImpossible</c> are both inside <c>if (Flee)</c>, and the block that would put
    /// a combatant into the running state is already excluded by an early return.
    /// </para>
    /// <para>
    /// <b>Morale is still loaded and stored</b> — a monster's value comes off its record — but
    /// nothing reads it for a decision. <c>GetAdjMorale</c> appears only in commented-out code.
    /// </para>
    /// <para>
    /// Nothing is ported, because there is nothing to port. This method exists so the round has a
    /// named place to stop, and so the next person does not spend an afternoon transcribing dead
    /// arithmetic. The live routes into fleeing are elsewhere: walking off the map, and an AI
    /// script setting the flag.
    /// </para>
    /// </remarks>
    public static void CheckMorale(IReadOnlyList<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(combatants);
        // Intentionally empty -- see the remarks.
    }

    /// <summary>
    /// Bandages the most badly hurt dying combatant (<c>COMBAT_DATA::Bandage</c>,
    /// <c>Combatants.cpp:1271</c>).
    /// </summary>
    /// <returns>Who was bandaged, or null when nobody was dying.</returns>
    /// <remarks>
    /// <para>
    /// <b>Exactly one combatant is bandaged per action</b>, whoever has the fewest hit points, and
    /// the effect is to <b>set them to zero and unconscious</b> — bandaging does not heal, it
    /// stabilises. Combined with <see cref="CheckDyingCombatants"/> that is the whole of the dying
    /// mechanic: bleed a point a round until somebody stops it.
    /// </para>
    /// <para>
    /// Ties go to the <i>later</i> combatant: the reference compares with <c>&lt;=</c>, so the last
    /// one found at the lowest hit points wins.
    /// </para>
    /// <para>
    /// <b>The reference seeds its comparison with combatant 0 whether or not it is dying</b>
    /// (<c>mosthurt = 0</c>, then <c>combatants[i].HP &lt;= combatants[mosthurt].HP</c>). It
    /// works because a dying combatant is below zero and a healthy one is not, so the first dying
    /// candidate always displaces the seed — but a design where combatant 0 is itself the most hurt
    /// makes the seed the answer, which is the same result by a different route. This picks the
    /// minimum over dying combatants only, which agrees on every reachable case.
    /// </para>
    /// </remarks>
    public static Combatant? Bandage(IReadOnlyList<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(combatants);

        Combatant? worst = null;
        foreach (var c in combatants)
        {
            if (c.Status == CharacterStatus.Dying
                && (worst is null || c.HitPoints <= worst.HitPoints))
            {
                worst = c;
            }
        }

        if (worst is null)
        {
            return null;
        }

        worst.IsBandaged = true;
        worst.HitPoints = 0;
        worst.Status = CharacterStatus.Unconscious;
        return worst;
    }
}
