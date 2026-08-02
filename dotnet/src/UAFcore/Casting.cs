namespace UAFcore;

/// <summary>
/// Beginning and abandoning a spell (<c>StartInitialSpellCasting</c>, <c>CastSpell</c> and
/// <c>StopCasting</c>, <c>Combatant.cpp:459</c>, <c>:615</c>, <c>:580</c>).
/// </summary>
/// <remarks>
/// Casting splits in two. <b>Beginning</b> spends the memorised copy, puts the caster into
/// <see cref="CombatantState.Casting"/> and hands the spell to the clock. <b>Resolving</b> happens
/// later — when the clock says so, the caster is put back on the turn queue and picks its targets
/// then. Between those two moments the caster is interruptible, which is the whole point of the
/// split.
/// </remarks>
public static class Casting
{
    /// <summary>
    /// Whether this combatant may cast at all (<c>CanCast</c>, <c>Combatant.cpp:424</c>).
    /// </summary>
    /// <param name="noMagic">The zone or encounter forbids magic (<c>GetConfigNoMagic</c>).</param>
    /// <param name="monstersMayNotCast">
    /// The design's "monsters cannot cast" configuration, which applies only to non-party
    /// combatants.
    /// </param>
    public static bool CanCast(Combatant caster, bool noMagic = false,
                               bool monstersMayNotCast = false)
    {
        ArgumentNullException.ThrowIfNull(caster);

        if (noMagic)
        {
            return false;
        }

        if (!caster.IsFriendly && monstersMayNotCast)
        {
            return false;
        }

        if (caster.IsDone())
        {
            return false;
        }

        return caster.Book.Castable.Any();
    }

    /// <summary>
    /// Begins a spell (<c>StartInitialSpellCasting</c>).
    /// </summary>
    /// <param name="castingTime">The spell's <c>Casting_Time</c>.</param>
    /// <param name="type">The spell's <c>Casting_Time_Type</c>.</param>
    /// <returns>
    /// Whether the spell was begun. False when the caster does not know it, which is the
    /// reference's one failure path — and it ends the caster's turn, because a caster who reached
    /// the menu and then could not cast has nothing else planned.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The memorised copy is spent when the spell is begun, not when it lands.</b> A caster
    /// interrupted three rounds into a spell does not get it back — which is what makes
    /// interruption worth doing to an enemy.
    /// </para>
    /// <para>
    /// <b>Nothing checks that a copy was available.</b> <c>CastSpell</c> calls <c>DecMemorized</c>
    /// for its side effect and ignores the result; the gate is the CAST menu, which only lists
    /// spells with a copy ready (see <see cref="SpellList.Castable"/>). Reached any other way — a
    /// script, or an AI that picks its own spell — a caster casts what it does not have.
    /// </para>
    /// </remarks>
    public static bool Begin(Combatant caster, string spellId, int castingTime,
                             SpellCastingTime type, PendingSpellList pending, int round,
                             TurnQueue queue)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(queue);

        caster.SpellBeingCast = spellId;
        caster.State = CombatantState.Casting;

        if (caster.Book.Find(spellId) is null)
        {
            caster.SpellBeingCast = null;
            caster.EndTurn(queue);
            return false;
        }

        caster.Book.DecrementMemorized(spellId);
        caster.PendingSpellKey = pending.Begin(caster.Index, spellId, castingTime, type,
                                               caster.Initiative, round);
        return true;
    }

    /// <summary>
    /// Begins a spell from an item (<c>StartInitialItemSpellCasting</c>, <c>Combatant.cpp:502</c>).
    /// </summary>
    /// <remarks>
    /// <b>Three differences from <see cref="Begin"/>, all small and all deliberate.</b> The
    /// caster's targets are cleared <i>only when it is not on automatic</i>, so an AI-driven item
    /// use keeps whatever it had preselected; there is no spell book to fetch from and no
    /// memorised copy to spend, the item's charges being the resource; and the schedule overflows
    /// a round later (see <see cref="PendingSpellList.BeginFromItem"/>).
    /// </remarks>
    public static void BeginFromItem(Combatant caster, string spellId, int castingTime,
                                     SpellCastingTime type, PendingSpellList pending, int round)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(pending);

        caster.SpellBeingCast = spellId;
        caster.ItemSpellBeingCast = spellId;
        caster.State = CombatantState.Casting;
        caster.PendingSpellKey = pending.BeginFromItem(caster.Index, spellId, castingTime, type,
                                                       caster.Initiative, round);
    }

    /// <summary>
    /// Abandons a spell in progress (<c>StopCasting</c>, <c>Combatant.cpp:580</c>).
    /// </summary>
    /// <param name="endTurn">
    /// Whether to end the caster's turn as well. The reference passes true only where the caster is
    /// dead or the spell's own script asked to stop; damage passes false, so an interrupted caster
    /// keeps whatever is left of its turn.
    /// </param>
    /// <param name="canFinishCasting">
    /// Set when the damage came from the caster's own spell (<c>m_canFinishCasting</c>,
    /// <c>Char.cpp:12258</c> — literally <c>pAttacker == this</c>). Such a caster is not
    /// interrupted: this returns having done nothing at all.
    /// </param>
    /// <remarks>
    /// <b>The state is cleared before the turn ends, not after.</b> The reference writes
    /// <c>State(ICS_None)</c> and then calls <c>EndTurn(State())</c> — passing the state it just
    /// overwrote, so an interrupted caster ends its turn in <c>None</c> rather than in
    /// <c>Casting</c>. Reproduced, since anything else would leave a combatant marked as casting
    /// with no spell.
    /// </remarks>
    public static void Stop(Combatant caster, PendingSpellList pending, TurnQueue queue,
                            bool endTurn = false, bool canFinishCasting = false)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(queue);

        if (canFinishCasting)
        {
            return;
        }

        caster.SpellBeingCast = null;
        caster.ItemSpellBeingCast = null;

        if (caster.State == CombatantState.Casting)
        {
            caster.State = CombatantState.None;
        }

        if (caster.PendingSpellKey >= 0)
        {
            pending.Remove(caster.PendingSpellKey);
            caster.PendingSpellKey = -1;
        }

        if (endTurn)
        {
            caster.EndTurn(queue, caster.State);
        }
    }

    /// <summary>
    /// The interruption rule: any damage voids a spell being cast
    /// (<c>SetHitPoints</c>, <c>Combatant.cpp:8781</c>).
    /// </summary>
    /// <param name="damage">Damage just taken. Zero or less leaves casting alone.</param>
    /// <param name="fromSelf">
    /// Whether the caster inflicted it on itself. A caster caught in its own fireball finishes
    /// casting it.
    /// </param>
    /// <remarks>
    /// Two branches, and the order matters. A caster brought to zero or below is stopped
    /// <i>and</i> has its turn ended — being dead outranks finishing the spell, and the
    /// self-damage exemption does not apply. Only a caster still standing gets the exemption.
    /// </remarks>
    public static void OnDamaged(Combatant caster, int damage, PendingSpellList pending,
                                 TurnQueue queue, bool fromSelf = false)
    {
        ArgumentNullException.ThrowIfNull(caster);

        if (caster.HitPoints <= 0)
        {
            Stop(caster, pending, queue, endTurn: true);
        }
        else if (damage > 0)
        {
            Stop(caster, pending, queue, endTurn: false, canFinishCasting: fromSelf);
        }
    }
}
