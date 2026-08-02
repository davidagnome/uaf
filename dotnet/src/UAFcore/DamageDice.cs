namespace UAFcore;

/// <summary>A damage roll: <c>Count</c>d<c>Sides</c> plus <c>Bonus</c>.</summary>
/// <param name="NonLethal">
/// Whether the damage knocks out rather than kills. Carried on the item, not the attack.
/// </param>
public readonly record struct DamageRoll(int Count, int Sides, int Bonus, bool NonLethal = false)
{
    /// <summary>The average result, for tests and for the AI's weapon choice.</summary>
    public double Average => (Count * (Sides + 1) / 2.0) + Bonus;

    public override string ToString() =>
        Bonus == 0 ? $"{Count}d{Sides}" : $"{Count}d{Sides}{Bonus:+#;-#}";
}

/// <summary>
/// A weapon's damage profile. Weapons roll differently against large targets.
/// </summary>
/// <param name="AgainstSmall">Dice against a small or man-sized target.</param>
/// <param name="AgainstLarge">Dice against a large one.</param>
/// <param name="AttackBonus">
/// The item's to-hit bonus. <b>It is added to damage as well</b> — see
/// <see cref="DamageDice.ForWeapon"/>.
/// </param>
public readonly record struct WeaponDamage(DamageRoll AgainstSmall, DamageRoll AgainstLarge,
                                           int AttackBonus = 0);

/// <summary>
/// A combatant's unarmed damage, which is <b>not</b> symmetric between the two target sizes.
/// </summary>
/// <remarks>See <see cref="DamageDice.ForUnarmed"/> for the asymmetry, which is in the original.</remarks>
public readonly record struct UnarmedDamage(int CountSmall, int SidesSmall, int BonusSmall,
                                            int CountLarge, int SidesLarge);

/// <summary>
/// Which dice an attack rolls (<c>GetDamageDice</c>, <c>Combatant.cpp:8379</c>).
/// </summary>
/// <remarks>
/// Three sources, in the order the reference tries them: a readied weapon, a monster's own attack
/// table, or the combatant's unarmed dice.
/// </remarks>
public static class DamageDice
{
    /// <summary>
    /// The dice for a readied weapon.
    /// </summary>
    /// <param name="damageBonus">
    /// The wielder's own adjusted damage bonus (<c>GetAdjDmgBonus</c>) — strength and spell
    /// effects.
    /// </param>
    /// <remarks>
    /// <b>The weapon's to-hit bonus is added to its damage too.</b> The reference adds
    /// <c>Attack_Bonus</c> into the damage bonus alongside the size-specific one and the wielder's
    /// (<c>:8407</c>), so a +1 sword both lands more often and hits harder. That is one value
    /// doing two jobs, and it means <see cref="UAF.Rules.ToHit.TargetNumber"/>'s weapon bonus and
    /// this one come from the same field.
    /// </remarks>
    public static DamageRoll ForWeapon(WeaponDamage weapon, bool targetIsLarge,
                                       int damageBonus = 0)
    {
        var dice = targetIsLarge ? weapon.AgainstLarge : weapon.AgainstSmall;
        return dice with { Bonus = dice.Bonus + damageBonus + weapon.AttackBonus };
    }

    /// <summary>
    /// The dice for a combatant fighting with fists, claws or jaws.
    /// </summary>
    /// <remarks>
    /// <b>The large branch drops the unarmed bonus.</b> Against a small target the reference uses
    /// <c>unarmedBonus + GetAdjDmgBonus()</c>; against a large one it uses <c>GetAdjDmgBonus()</c>
    /// alone (<c>:8469</c>), silently discarding the combatant's own unarmed bonus. There is no
    /// comment and no obvious reason — transcribed because a fighter's unarmed damage against
    /// large targets is what it is, and "fixing" it would change every monster brawl.
    /// </remarks>
    public static DamageRoll ForUnarmed(UnarmedDamage unarmed, bool targetIsLarge,
                                        int damageBonus = 0) =>
        targetIsLarge
            ? new DamageRoll(unarmed.CountLarge, unarmed.SidesLarge, damageBonus)
            : new DamageRoll(unarmed.CountSmall, unarmed.SidesSmall,
                             unarmed.BonusSmall + damageBonus);

    /// <summary>
    /// The dice for one of a monster's own attacks
    /// (the <c>MONSTER_TYPE</c> branch, <c>:8419</c>).
    /// </summary>
    /// <param name="attacks">The monster's attack table, in order.</param>
    /// <param name="totalAttacks">How many attacks it gets per round.</param>
    /// <param name="availableAttacks">How many it has left, which is how the index is derived.</param>
    /// <param name="unarmed">The fallback when the monster defines no attacks at all.</param>
    /// <param name="damageBonus">The wielder's adjusted damage bonus.</param>
    /// <remarks>
    /// <para>
    /// <b>Which attack is being made is inferred, not tracked.</b> The index is
    /// <c>totalAttacks − availAttacks</c>, so a monster with three attacks rolls its first, second
    /// and third profile as its allowance drains. Both bounds are clamped to zero on the way — the
    /// reference clamps twice, once before and once after consulting the table, because
    /// <c>availAttacks</c> is a <c>double</c> and can leave the index out of range.
    /// </para>
    /// <para>
    /// A monster with an empty attack table falls back to the <b>large</b> unarmed dice regardless
    /// of the target's size (<c>:8437</c>).
    /// </para>
    /// <para>
    /// <b>The attack table's own bonus is used alone.</b> Unlike every other branch, this one does
    /// <i>not</i> add <c>GetAdjDmgBonus()</c> (<c>:8462</c>) — so a monster striking with a defined
    /// attack gets no strength or spell adjustment, while the same monster falling back to unarmed
    /// dice does. <paramref name="damageBonus"/> therefore applies only to the fallback.
    /// </para>
    /// </remarks>
    public static DamageRoll ForMonster(IReadOnlyList<DamageRoll> attacks, double totalAttacks,
                                        double availableAttacks, UnarmedDamage unarmed,
                                        int damageBonus = 0)
    {
        ArgumentNullException.ThrowIfNull(attacks);

        if (attacks.Count == 0)
        {
            // Note: the large dice, whatever the target is -- and this branch does take the bonus.
            return new DamageRoll(unarmed.CountLarge, unarmed.SidesLarge, damageBonus);
        }

        int index = (int)(totalAttacks - availableAttacks);
        if (index < 0 || index >= attacks.Count)
        {
            index = 0;
        }

        // No damageBonus here -- see the remarks.
        return attacks[index];
    }
}
