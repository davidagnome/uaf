namespace UAFcore;

/// <summary>
/// Why an attack is not possible. <see cref="None"/> means it is.
/// </summary>
/// <remarks>
/// The reference returns a bare <c>BOOL</c> from <c>canAttack</c>. Naming the reasons costs
/// nothing and makes the tests state which rule they are exercising, rather than asserting
/// <c>false</c> fifteen times over.
/// </remarks>
public enum AttackRefusal
{
    None = 0,
    NoAttacksLeft,
    NoTarget,
    PartialAttackTooSoon,
    CannotAttackSelf,
    SameSideAutoCombatant,
    SameSidePlayerCharacter,
    OutOfWeaponRange,
    WeaponStackEmpty,
    NotAWeapon,
    NoCharges,
    NoAmmoReadied,
    WrongAmmoClass,
    AmmoStackEmpty,
    NoLineOfSight,
    ScriptRefusedTarget,
    TargetInvisible,
}

/// <summary>
/// Who a combatant may attack (<c>GetCurrTarget</c>, <c>IsValidTarget</c> and <c>canAttack</c> —
/// <c>Combatant.cpp:4172</c>, <c>Combatants.cpp:1329</c>, <c>Combatant.cpp:8950</c>).
/// </summary>
public static class Targeting
{
    /// <summary>
    /// Whether a script has vetoed this target (<c>IsValidTarget</c>, <c>Combatants.cpp:1329</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Entirely a script hook, and it can only refuse.</b> The reference runs
    /// <c>IS_VALID_TARGET</c> on the target and treats a leading <c>'N'</c> as a veto; an empty
    /// result — which is what a design with no such script produces — leaves the answer at valid.
    /// So with GPDL unported every target is valid, and that is the faithful default rather than a
    /// stand-in.
    /// </para>
    /// <para>
    /// The reference caches the answer per attacker in <c>targetValidity</c> and returns the cache
    /// whenever it is non-negative, so the script runs once per target per turn.
    /// </para>
    /// </remarks>
    public static bool IsValidTarget(Combatant? target) => target is not null;

    /// <summary>
    /// The combatant's current target, dropping it if it has left the map
    /// (<c>GetCurrTarget</c>, <c>Combatant.cpp:4172</c>).
    /// </summary>
    /// <param name="updateTarget">
    /// When false the stale target is <i>returned anyway</i> rather than cleared — the reference
    /// distinguishes asking from acting, and animation code asks without wanting the side effect.
    /// </param>
    /// <param name="onTargetLost">
    /// Invoked to pick a replacement when the target has gone and <paramref name="updateTarget"/>
    /// is set (<c>RemoveCurrTarget</c>). Returning <see cref="CombatMap.NoDude"/> gives up.
    /// </param>
    public static int CurrentTarget(Combatant attacker, IReadOnlyList<Combatant> combatants,
                                    bool updateTarget = true,
                                    bool unconsciousOk = false, bool petrifiedOk = false,
                                    Func<Combatant, int>? onTargetLost = null)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(combatants);

        if (attacker.Target == CombatMap.NoDude)
        {
            return CombatMap.NoDude;
        }

        var target = Find(combatants, attacker.Target);
        if (target is null)
        {
            return CombatMap.NoDude;
        }

        if (!target.IsOnCombatMap(unconsciousOk, petrifiedOk))
        {
            if (!updateTarget)
            {
                return attacker.Target;
            }

            attacker.Target = onTargetLost?.Invoke(attacker) ?? CombatMap.NoDude;
            if (attacker.Target == CombatMap.NoDude)
            {
                return CombatMap.NoDude;
            }

            target = Find(combatants, attacker.Target);
        }

        return IsValidTarget(target) ? attacker.Target : CombatMap.NoDude;
    }

    /// <summary>
    /// Whether an attack is possible right now (<c>canAttack</c>, <c>Combatant.cpp:8950</c>).
    /// </summary>
    /// <param name="weapon">
    /// The readied weapon, or null for natural attacks — claws, jaws, fists. <b>Natural attacks
    /// are melee only</b>: the reference refuses any distance above 1 outright, with no range
    /// table involved.
    /// </param>
    /// <param name="ammo">Readied ammunition, for bows and crossbows.</param>
    /// <param name="currentRound">Used only by the partial-attack rule.</param>
    /// <remarks>
    /// The order of the tests is preserved, because the first refusal is the one reported and
    /// several of them overlap — a friendly player character is refused for being on your side
    /// before anything looks at whether you could reach them.
    /// </remarks>
    public static AttackRefusal CanAttack(Combatant attacker, Combatant? target, CombatMap map,
                                          ReadiedWeapon? weapon = null, ReadiedAmmo? ammo = null,
                                          int additionalAttacks = 0, bool canAttackSelf = false,
                                          int currentRound = 0)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(map);

        if (attacker.AvailableAttacks + additionalAttacks <= 0)
        {
            return AttackRefusal.NoAttacksLeft;
        }

        if (target is null)
        {
            return AttackRefusal.NoTarget;
        }

        // A fractional attack cannot be spent in consecutive rounds: half an attack banked from
        // last round is not enough to swing again this one.
        if (attacker.AvailableAttacks + additionalAttacks < 1.0
            && currentRound - attacker.LastAttackRound <= 1)
        {
            return AttackRefusal.PartialAttackTooSoon;
        }

        if (target.Index == attacker.Index && !canAttackSelf)
        {
            return AttackRefusal.CannotAttackSelf;
        }

        if (target.IsFriendly == attacker.IsFriendly)
        {
            // The computer never turns on its own side, and a player cannot strike a party
            // character. A non-pregenerated NPC is the one same-side target that is allowed --
            // in the reference that is where it would change sides, though the line is commented
            // out (Combatant.cpp:8996).
            if (attacker.IsAuto && !canAttackSelf)
            {
                return AttackRefusal.SameSideAutoCombatant;
            }

            if (target.Kind == CombatantKind.Character && !canAttackSelf)
            {
                return AttackRefusal.SameSidePlayerCharacter;
            }
        }

        int distance = CombatMap.Distance(attacker.X, attacker.Y, target.X, target.Y);

        if (weapon is { } w)
        {
            if (w.Quantity <= 0)
            {
                return AttackRefusal.WeaponStackEmpty;
            }

            if (!WeaponRange.CanAttackAt(w, distance))
            {
                return AttackRefusal.OutOfWeaponRange;
            }

            switch (w.Class)
            {
                case WeaponClass.NotWeapon:
                    // Only a magical one can attack, and only while it has charges. Unreachable
                    // through CanAttackAt above, which already refuses NotWeapon -- kept because
                    // the reference tests it and a future range rule might let it through.
                    if (!w.IsMagical)
                    {
                        return AttackRefusal.NotAWeapon;
                    }
                    if (w.Charges <= 0)
                    {
                        return AttackRefusal.NoCharges;
                    }
                    break;

                case WeaponClass.Bow or WeaponClass.Crossbow:
                    if (ammo is not { } a)
                    {
                        return AttackRefusal.NoAmmoReadied;
                    }
                    if (a.Class != WeaponClass.Ammo)
                    {
                        return AttackRefusal.WrongAmmoClass;
                    }
                    // An empty ammo class means the item takes no ammunition at all.
                    if (string.IsNullOrEmpty(a.AmmoClass) || a.AmmoClass != w.AmmoClass)
                    {
                        return AttackRefusal.WrongAmmoClass;
                    }
                    if (a.Quantity <= 0)
                    {
                        return AttackRefusal.AmmoStackEmpty;
                    }
                    break;

                case WeaponClass.Ammo:
                    return AttackRefusal.NotAWeapon;

                default:
                    // The hand, sling, throw and spell classes are fully handled by the range
                    // rule; there is nothing further to test.
                    break;
            }
        }
        else if (distance > 1)
        {
            return AttackRefusal.OutOfWeaponRange;
        }

        if (!LineOfSight.Between(map, attacker.CenterX, attacker.CenterY, target.X, target.Y))
        {
            return AttackRefusal.NoLineOfSight;
        }

        if (!IsValidTarget(target))
        {
            return AttackRefusal.ScriptRefusedTarget;
        }

        // Invisibility only protects against attacks from a distance -- an adjacent attacker can
        // find you regardless.
        if (distance > 1 && !attacker.DetectsInvisible)
        {
            if (target.IsInvisible
                || (attacker.IsUndead && target.IsInvisibleToUndead)
                || (attacker.IsAnimal && target.IsInvisibleToAnimals))
            {
                return AttackRefusal.TargetInvisible;
            }
        }

        return AttackRefusal.None;
    }

    private static Combatant? Find(IReadOnlyList<Combatant> combatants, int index)
    {
        foreach (var c in combatants)
        {
            if (c.Index == index)
            {
                return c;
            }
        }
        return null;
    }
}
