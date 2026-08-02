using UAF.Rules;

namespace UAFcore;

/// <summary>What one attack did.</summary>
/// <param name="Refusal">Why it did not happen, or <see cref="AttackRefusal.None"/>.</param>
/// <param name="Roll">The d20 rolled, or 0 when the attack was refused.</param>
/// <param name="TargetNumber">What the roll had to reach.</param>
/// <param name="Hit">Whether it landed.</param>
/// <param name="Damage">Damage dealt; zero on a miss.</param>
/// <param name="NonLethal">
/// Whether the weapon's damage is flagged non-lethal. Carried through for the caller;
/// <see cref="Attack.ApplyDamage"/> has no branch for it, because the reference's does not either.
/// </param>
/// <param name="AmmoSpent">Whether a round of ammunition was used.</param>
/// <param name="WeaponSpent">Whether the weapon itself was thrown or expended.</param>
public sealed record AttackResult(AttackRefusal Refusal, int Roll, int TargetNumber, bool Hit,
                                  int Damage, bool NonLethal, bool AmmoSpent, bool WeaponSpent)
{
    public bool Happened => Refusal == AttackRefusal.None;

    public static AttackResult Refused(AttackRefusal reason) =>
        new(reason, 0, 0, false, 0, false, false, false);
}

/// <summary>
/// Resolving a single attack, from validity through to damage
/// (<c>StartAttack</c> and the attack half of <c>Combatant.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic already lived in <c>UAF.Rules</c> — <see cref="ToHit"/>, <c>Thac0</c>,
/// <c>ArmorClass</c>. This is the wiring: check the attack is allowed, roll, apply damage, and
/// spend whatever the weapon consumes.
/// </para>
/// <para>
/// <b>Dice come from the caller.</b> The reference rolls through the engine's shared generator, so
/// a test that wants a known outcome has to inject one; passing the roller in keeps this
/// deterministic and keeps <c>UAFcore</c> free of a global.
/// </para>
/// </remarks>
public static class Attack
{
    /// <summary>
    /// Resolves one attack.
    /// </summary>
    /// <param name="attacker">Who is swinging.</param>
    /// <param name="target">Who is being hit.</param>
    /// <param name="map">The grid, for the line-of-sight test.</param>
    /// <param name="dice">
    /// A roller: given a number of sides, returns 1..sides. The d20 is drawn first, then the
    /// damage dice in order.
    /// </param>
    /// <param name="damage">The dice this attack rolls — see <see cref="DamageDice"/>.</param>
    /// <param name="weapon">The readied weapon, or null for a natural attack.</param>
    /// <param name="ammo">Readied ammunition, for bows and crossbows.</param>
    /// <param name="strengthBonus">
    /// The wielder's strength damage bonus. <b>Zero for a missile</b> — see the remarks.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A missile carries no strength bonus.</b> That is the caller's responsibility and it is
    /// worth stating because getting it wrong was a real defect in this port once already: a
    /// crossbow collecting a strength bonus it should never get.
    /// </para>
    /// <para>
    /// Validity is re-checked here rather than assumed. The reference tests it again inside
    /// <c>StartAttack</c> (<c>:4197</c>) because a target can die or flee between the decision and
    /// the swing.
    /// </para>
    /// </remarks>
    public static AttackResult Resolve(Combatant attacker, Combatant? target, CombatMap map,
                                       Func<int, int> dice, DamageRoll damage,
                                       ReadiedWeapon? weapon = null, ReadiedAmmo? ammo = null,
                                       int attackerThac0 = 20, int targetArmorClass = 10,
                                       int environmentalBonus = 0, int weaponToHitBonus = 0,
                                       int strengthBonus = 0, int currentRound = 0)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(dice);

        var refusal = Targeting.CanAttack(attacker, target, map, weapon, ammo,
                                          currentRound: currentRound);
        if (refusal != AttackRefusal.None)
        {
            return AttackResult.Refused(refusal);
        }

        int roll = dice(20);
        int targetNumber = ToHit.TargetNumber(attackerThac0, targetArmorClass,
                                              environmentalBonus, weaponToHitBonus);
        bool hit = ToHit.Hits(roll, targetNumber);

        int dealt = 0;
        if (hit)
        {
            int rolled = 0;
            for (int i = 0; i < damage.Count; i++)
            {
                rolled += dice(damage.Sides);
            }

            dealt = ToHit.Damage(rolled, damage.Bonus, strengthBonus);
        }

        // Ammunition is spent whether or not the blow landed -- an arrow that misses is still
        // gone. The reference decrements on the swing, not on the hit.
        int distance = CombatMap.Distance(attacker.X, attacker.Y, target!.X, target.Y);
        var cls = weapon?.Class ?? WeaponClass.NotWeapon;
        bool ammoSpent = weapon is not null && WeaponRange.ConsumesAmmoAt(cls, distance);
        bool weaponSpent = weapon is not null && ammoSpent
                           && WeaponRange.ConsumesSelfAsAmmo(cls);

        // The attack is now spent, whatever it achieved.
        attacker.AvailableAttacks -= 1;
        attacker.LastAttackRound = currentRound;
        attacker.LastAttacked = target.Index;
        target.LastAttacker = attacker.Index;

        return new AttackResult(AttackRefusal.None, roll, targetNumber, hit, dealt,
                                damage.NonLethal, ammoSpent, weaponSpent);
    }

    /// <summary>
    /// The floor hit points cannot go below (<c>Char.cpp:8258</c>).
    /// </summary>
    public const int MinimumHitPoints = -10;

    /// <summary>
    /// Applies damage and updates the status
    /// (<c>CHARACTER::giveCharacterDamage</c>, <c>Char.cpp:8245</c>).
    /// </summary>
    /// <param name="hitPoints">Current hit points, before the blow.</param>
    /// <param name="maxHitPoints">The cap, which also bounds healing.</param>
    /// <param name="deadAtZero">
    /// The design's <c>deadAtZeroHP</c> flag (<c>GlobalData.h:866</c>), which collapses the
    /// unconscious and dying bands into plain death.
    /// </param>
    /// <returns>The new hit points.</returns>
    /// <remarks>
    /// <para>
    /// <b>The same reference function as <see cref="EventDamage.GiveCharacterDamage"/>, over the
    /// other of the two types in this port that carry an effect list.</b> That one takes a
    /// <see cref="Character"/>, which is what the event path holds; this takes a
    /// <see cref="Combatant"/>. They are deliberately two ports of one function and they must not
    /// disagree — every remark below applies to both.
    /// </para>
    /// <para>
    /// <b>Damage only lands on a combatant in one of five statuses</b> — okay, running,
    /// unconscious, animated or dying. Anything else, including already dead, fled or petrified,
    /// takes nothing at all and keeps its hit points unchanged. A caller that assumes damage always
    /// applies will kill things twice.
    /// </para>
    /// <para>
    /// <b>The gate reads the <i>adjusted</i> status.</b> <c>charStatusType stype =
    /// GetAdjStatus()</c> (<c>Char.cpp:8248</c>) runs the combatant's <c>$CHAR_STATUS</c> spell
    /// effects over the stored value — see <see cref="AdjustedStatus"/> — so a spell that moves a
    /// combatant's apparent status decides whether damage lands, and the write afterwards goes to
    /// the stored field regardless.
    /// </para>
    /// <para>
    /// <b>Hit points floor at −10 and are then capped at the maximum, in that order.</b> The floor
    /// is applied *before* the status test, so "dead" and "at the floor" are the same state and a
    /// massive blow is indistinguishable from a marginal one. The ceiling means passing negative
    /// damage heals but cannot overheal. Floor-then-ceiling is the reference's order and not
    /// <see cref="Math.Clamp(int,int,int)"/>: a <paramref name="maxHitPoints"/> below the floor
    /// wins rather than raises, where <c>Math.Clamp</c> would throw.
    /// </para>
    /// <para>
    /// <b>The bands are read off the <i>adjusted</i> hit points.</b> <c>HP =
    /// GetAdjHitPoints()</c> (<c>Char.cpp:8264</c>) — so a combatant carrying a
    /// <c>$CHAR_HITPOINTS</c> effect can be driven to zero stored hit points and stay conscious, or
    /// be knocked out while stored hit points are positive. The stored value is what took the
    /// damage; the adjusted value is what decides the consequence.
    /// </para>
    /// <para>
    /// <b>Zero is unconscious, not dying.</b> The bands are <c>&lt;= −10</c> dead, <c>&lt; 0</c>
    /// dying, <c>== 0</c> unconscious (<c>:8271</c>) — so the dying band is −1 to −9 and exactly
    /// zero is the stable one. Folding zero into dying is an easy slip and would make every
    /// knocked-out character bleed out.
    /// </para>
    /// <para>
    /// <b>Death clears the combatant's spell effects</b> — see <see cref="Kill"/>. Only death does;
    /// going unconscious or dying leaves them in place.
    /// </para>
    /// <para>
    /// <b>Non-lethal damage is not handled here.</b> The reference's flag rides on the damage and
    /// is consumed elsewhere; this function has no branch for it, so neither does the port.
    /// </para>
    /// </remarks>
    public static int ApplyDamage(Combatant combatant, int hitPoints, int damage,
                                  int maxHitPoints = int.MaxValue, bool deadAtZero = false)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        if (AdjustedStatus(combatant) is not (CharacterStatus.Okay or CharacterStatus.Running
                                              or CharacterStatus.Unconscious
                                              or CharacterStatus.Animated or CharacterStatus.Dying))
        {
            return hitPoints;
        }

        int remaining = Bound(hitPoints - damage, maxHitPoints);

        // GetAdjHitPoints over what the stored value is about to become. The reference writes the
        // hit points and then re-reads them through the accessor; this hands them back to the
        // caller instead, so the effects go over `remaining`.
        int adjusted = Bound((int)combatant.Effects.Apply(remaining, "$CHAR_HITPOINTS"),
                             maxHitPoints);

        if (deadAtZero)
        {
            if (adjusted <= 0)
            {
                Kill(combatant);
            }

            return remaining;
        }

        if (adjusted <= MinimumHitPoints)
        {
            Kill(combatant);
        }
        else if (adjusted < 0)
        {
            combatant.Status = CharacterStatus.Dying;
        }
        else if (adjusted == 0)
        {
            combatant.Status = CharacterStatus.Unconscious;
        }

        return remaining;

        // Sequential, not Math.Clamp: the reference floors then ceilings, so a maximum below the
        // floor wins rather than raises -- and does not throw when it does.
        static int Bound(int value, int max) => Math.Min(Math.Max(value, MinimumHitPoints), max);
    }

    /// <summary>
    /// This combatant's status with spell effects applied
    /// (<c>CHARACTER::GetAdjStatus</c>, <c>Char.cpp:13936</c>).
    /// </summary>
    /// <remarks>
    /// <b>It reverts rather than clamps.</b> Unlike every neighbouring <c>GetAdj*</c> accessor,
    /// which pins the adjusted value inside a legal range, this one throws the adjustment away
    /// entirely and returns the stored status when the result falls outside the enum — so an effect
    /// of +100 on <c>$CHAR_STATUS</c> changes nothing at all, where +1 changes everything. The same
    /// rule as <see cref="EventDamage.AdjustedStatus"/>, over a <see cref="Combatant"/> rather than
    /// a <see cref="Character"/>.
    /// </remarks>
    public static CharacterStatus AdjustedStatus(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        int value = (int)combatant.Effects.Apply((int)combatant.Status, "$CHAR_STATUS");

        return value < 0 || value >= EventDamage.CharacterStatusTypes
            ? combatant.Status
            : (CharacterStatus)value;
    }

    /// <summary>
    /// <c>SetStatus(Dead)</c> (<c>Char.h:907</c>), side effect and all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The setter is <c>{ status=val; if (status==Dead) m_spellEffects.RemoveAll(); }</c> — an
    /// inline one-liner with a side effect, easy to read past. Only <c>Dead</c> clears the effects;
    /// <see cref="CharacterStatus.Unconscious"/> and <see cref="CharacterStatus.Dying"/> leave
    /// everything in place, which is what lets a bandaged combatant keep its buffs.
    /// </para>
    /// <para>
    /// <see cref="Combatant.TurnIsDone"/> is this port's own and not the reference's: a dead
    /// combatant has to leave the round, and that latch is what
    /// <see cref="CombatRound.Advance"/> walks.
    /// </para>
    /// </remarks>
    private static void Kill(Combatant combatant)
    {
        combatant.Status = CharacterStatus.Dead;
        combatant.Effects.Clear();
        combatant.TurnIsDone = true;
    }
}
