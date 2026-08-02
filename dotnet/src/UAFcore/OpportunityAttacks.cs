namespace UAFcore;

/// <summary>Why an opportunity attack is owed.</summary>
public enum OpportunityKind
{
    /// <summary>The mover left a square adjacent to this attacker.</summary>
    Free,

    /// <summary>The mover arrived beside a guarding attacker.</summary>
    Guard,
}

/// <summary>An attack a combatant has earned by somebody moving past it.</summary>
/// <param name="Attacker">Who gets the attack.</param>
/// <param name="Attacks">How many.</param>
/// <param name="Kind">Which rule granted it.</param>
public readonly record struct OpportunityAttack(int Attacker, int Attacks, OpportunityKind Kind);

/// <summary>
/// Attacks earned by an opponent moving into or out of reach
/// (<c>CheckOpponentFreeAttack</c>, <c>Combatant.cpp:10060</c>).
/// </summary>
/// <remarks>
/// <para>
/// Called from the middle of a step: having worked out who was adjacent before and who will be
/// adjacent after, it grants a <b>free attack</b> to anyone the mover retreated from and a
/// <b>guard attack</b> to anyone it walked up to. Both interrupt the mover's turn through the
/// queue, which already models exactly this — <see cref="TurnQueue.Push"/> with
/// <c>affectStats: false</c>.
/// </para>
/// <para>
/// <b>The rules live in the design's scripts, not in the engine.</b> The C++ asks
/// <c>FreeAttack-CanFreeAttack</c> and <c>Guarding-CanGuardAttack</c> and does nothing unless they
/// return an affirmative — note the polarity is the <i>opposite</i> of <c>IS_VALID_TARGET</c>,
/// where silence means yes. With no script at all, neither kind of attack ever happens. The
/// defaults below are transcribed from the scripts every reference design ships, and there is a
/// pseudo-code specification of the whole scheme sitting in a comment above the function
/// (<c>:9990</c>) which is worth reading before touching any of this.
/// </para>
/// </remarks>
public static class OpportunityAttacks
{
    /// <summary>
    /// Works out who is owed an attack.
    /// </summary>
    /// <param name="mover">The combatant taking the step.</param>
    /// <param name="canAttack">
    /// Whether an attacker could hit the mover. The reference passes a distance function that
    /// <b>always returns 1</b> (<c>FreeAttackDistance</c>, <c>:10047</c>), so the weapon's range
    /// test always sees an adjacent target — reach is decided by the adjacency scan below, not by
    /// the weapon.
    /// </param>
    /// <param name="hasRangedWeapon">
    /// Whether an attacker has a ranged weapon readied. Both shipped scripts refuse the attack in
    /// that case, which is the one rule they agree on.
    /// </param>
    /// <returns>
    /// The attacks owed, <b>guard first then free</b> — the order they must be pushed in. The
    /// reference's comment explains why: the queue is a stack, so pushing guard attacks first
    /// leaves the free attacks on top, and free attacks resolve first.
    /// </returns>
    public static IReadOnlyList<OpportunityAttack> Check(
        Combatant mover, int oldX, int oldY, int newX, int newY,
        IReadOnlyList<Combatant> all, CombatMap map,
        Func<Combatant, int, bool> canAttack,
        Func<Combatant, bool>? hasRangedWeapon = null)
    {
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(canAttack);

        var wasAdjacent = Adjacent(mover, oldX, oldY, all, map);
        var willBeAdjacent = Adjacent(mover, newX, newY, all, map);

        var owed = new List<OpportunityAttack>();

        // Guard attacks first: the mover walked up to somebody standing guard.
        foreach (int dude in willBeAdjacent.Except(wasAdjacent).Order())
        {
            var attacker = Find(all, dude);
            if (!Eligible(attacker, mover, canAttack) || hasRangedWeapon?.Invoke(attacker!) == true)
            {
                continue;
            }

            // Guarding-CanGuardAttack: the attacker must actually be guarding, and have attacks
            // left. Exactly one guard attack is granted.
            if (attacker!.State == CombatantState.Guarding && attacker.AvailableAttacks > 0)
            {
                owed.Add(new OpportunityAttack(dude, 1, OpportunityKind.Guard));
            }
        }

        // Free attacks second, so they end up on top of the stack and resolve first.
        foreach (int dude in wasAdjacent.Except(willBeAdjacent).Order())
        {
            var attacker = Find(all, dude);
            if (!Eligible(attacker, mover, canAttack) || hasRangedWeapon?.Invoke(attacker!) == true)
            {
                continue;
            }

            // FreeAttack-CanFreeAttack returns hook parameter 8, which is the attacker's total
            // number of attacks -- not what it has left.
            int count = attacker!.TotalAttacks;
            if (count > 0)
            {
                owed.Add(new OpportunityAttack(dude, count, OpportunityKind.Free));
            }
        }

        return owed;
    }

    /// <summary>
    /// Queues the attacks and rewinds the mover to where it started
    /// (the queueing blocks at <c>:10206</c> and <c>:10341</c>).
    /// </summary>
    /// <remarks>
    /// <b>The mover goes back to its old square while the attacks resolve</b>, with its intended
    /// destination parked on the queue entry (<c>SetXY</c>) so the step can finish afterwards.
    /// The reference guards that rewind with <c>qcomb->Top() == self</c> so it happens once
    /// however many attackers there are; here it is simply done once, before the pushes.
    /// </remarks>
    public static void Queue(IReadOnlyList<OpportunityAttack> owed, TurnQueue queue,
                             Combatant mover, CombatMap map,
                             int oldX, int oldY, int newX, int newY,
                             IReadOnlyList<Combatant> all)
    {
        ArgumentNullException.ThrowIfNull(owed);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(all);

        if (owed.Count == 0)
        {
            return;
        }

        // Park the destination and put the mover back where it came from.
        queue.SetDelayedPosition(newX, newY);
        map.Remove(mover.X, mover.Y, mover.Icon.Width, mover.Icon.Height);
        mover.X = oldX;
        mover.Y = oldY;
        map.Place(oldX, oldY, mover.Index, mover.Icon.Width, mover.Icon.Height);

        foreach (var attack in owed)
        {
            queue.Push(attack.Attacker, affectStats: false,
                       freeAttacks: attack.Kind == OpportunityKind.Free ? attack.Attacks : 0,
                       guardAttacks: attack.Kind == OpportunityKind.Guard ? attack.Attacks : 0);

            if (Find(all, attack.Attacker) is { } attacker)
            {
                attacker.TurnIsDone = false;
                attacker.Target = mover.Index;
            }
        }

        // The mover's turn is not over, but it will not resume in the moving state.
        mover.State = CombatantState.None;
    }

    /// <summary>
    /// Every enemy in the ring around a footprint placed at a position.
    /// </summary>
    /// <remarks>
    /// The scan runs <c>-1</c> to <c>width</c> and <c>-1</c> to <c>height</c> inclusive, so it
    /// covers the footprint <i>and</i> the ring around it — a 1×1 combatant checks a 3×3 block.
    /// Squares off the map are skipped, and the mover never counts as adjacent to itself.
    /// </remarks>
    private static HashSet<int> Adjacent(Combatant mover, int x, int y,
                                         IReadOnlyList<Combatant> all, CombatMap map)
    {
        var found = new HashSet<int>();

        for (int dx = -1; dx <= mover.Icon.Width; dx++)
        {
            for (int dy = -1; dy <= mover.Icon.Height; dy++)
            {
                if (!map.Contains(x + dx, y + dy))
                {
                    continue;
                }

                int dude = map.OccupantAt(x + dx, y + dy, 1, 1, ignoreCombatant: mover.Index);
                if (dude != CombatMap.NoDude
                    && Find(all, dude) is { } c && c.IsOnCombatMap())
                {
                    found.Add(dude);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The preconditions both branches share.
    /// </summary>
    /// <remarks>
    /// <b>A casting combatant never gets an opportunity attack</b> — interrupting a spell to swing
    /// would lose the spell. Otherwise: it must be on the other side, and able to attack the mover.
    /// </remarks>
    private static bool Eligible(Combatant? attacker, Combatant mover,
                                 Func<Combatant, int, bool> canAttack) =>
        attacker is not null
        && attacker.State != CombatantState.Casting
        && attacker.IsFriendly != mover.IsFriendly
        && Targeting.IsValidTarget(mover)
        && canAttack(attacker, mover.Index);

    private static Combatant? Find(IReadOnlyList<Combatant> all, int index)
    {
        foreach (var c in all)
        {
            if (c.Index == index)
            {
                return c;
            }
        }
        return null;
    }
}
