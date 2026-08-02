using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Building the AI's view of what a combatant can attack with
/// (<c>ListWeapons</c> and <c>ListAttacks</c>, <c>Combatant.cpp:1142</c>, <c>:1308</c>).
/// </summary>
/// <remarks>
/// Two separate lists in the reference: carried <i>weapons</i>, which are items readied in the
/// weapon hand, and natural <i>attacks</i>, which a monster's record supplies. They feed different
/// branches of the enumeration — see <see cref="AiActions"/> — and, as it turns out, they estimate
/// damage differently.
/// </remarks>
public static class AiWeapons
{
    /// <summary>
    /// The scale every damage estimate is kept in: tenths, so the comparisons stay integral.
    /// </summary>
    /// <remarks>
    /// The reference writes the factor as a literal <c>5</c> against a doubled expression rather
    /// than <c>10</c> against a halved one, which is the same thing done to avoid a division.
    /// </remarks>
    public const int DamageScale = 10;

    /// <summary>
    /// A carried weapon's damage estimate (<c>ListWeapons</c>, <c>Combatant.cpp:1169</c>).
    /// </summary>
    /// <remarks>
    /// <c>5 × (sides + 1) × count</c>, which is ten times the true average of <c>count d sides</c>,
    /// plus <c>10 × bonus</c> held in its own field. Correct.
    /// </remarks>
    public static int WeaponDamage(int count, int sides, int bonus) =>
        DiceEstimate(count, sides) + (DamageScale * bonus);

    /// <summary>
    /// A natural attack's damage estimate (<c>ListAttacks</c>, <c>Combatant.cpp:1346</c>).
    /// </summary>
    /// <remarks>
    /// <b>The two dice operands are transposed relative to <see cref="WeaponDamage"/>, and this
    /// reproduces it.</b> The reference writes
    /// <c>5 × ((1 + nbr) × sides + 2 × bonus)</c> where the weapon path writes
    /// <c>5 × ((1 + sides) × nbr)</c> — the <c>1 +</c> lands on the die <i>count</i> instead of the
    /// die <i>size</i>. The bonus term is right (the outer 5 makes <c>2 × bonus</c> into
    /// <c>10 × bonus</c>); only the dice are swapped.
    /// <para>
    /// The effect is systematic: a claw doing <c>1d8</c> is estimated at 80 where its true average
    /// scaled is 45, while <c>3d2</c> is estimated at 40 against a true 45. **The AI overrates
    /// few-but-large dice and underrates many-but-small ones**, which is exactly the shape of a
    /// dragon's bite against a swarm's nibbles. Nothing else reads this number, so it only ever
    /// changes which natural attack the AI prefers.
    /// </para>
    /// </remarks>
    public static int AttackDamage(int count, int sides, int bonus) =>
        5 * (((1 + count) * sides) + (2 * bonus));

    /// <summary>
    /// What a monster can attack with: its readied items, then its natural attacks.
    /// </summary>
    /// <param name="itemInfo">The item database, for a carried weapon's kind and reach.</param>
    /// <remarks>
    /// <para>
    /// <b>Only items readied in the weapon hand are weapons</b> (<c>Location_Readied ==
    /// WeaponHand</c>), and the reference additionally asks <c>CanReady</c> — class restrictions,
    /// curses, hands free — which this port does not model yet, so every weapon-hand item counts.
    /// </para>
    /// <para>
    /// A monster's natural attacks arrive as <see cref="WeaponClass.NotWeapon"/> entries with a
    /// reach of one, because <see cref="AiActions"/> counts them separately: what it takes from
    /// here is how many there are and how hard they hit.
    /// </para>
    /// </remarks>
    public static List<AiWeapon> For(Combatant combatant, MonsterRecord? monster,
                                     Func<string, ItemRecord?>? itemInfo = null)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        var weapons = new List<AiWeapon>();

        foreach (var carried in combatant.Items)
        {
            if (itemInfo?.Invoke(carried.ItemId) is not { } item
                || item.Combat.LocationReadied != WeaponHandSlot)
            {
                continue;
            }

            var kind = (WeaponClass)item.Tail.WeaponType;

            // The dice and the bonus are kept apart, because a weapon firing ammunition
            // contributes only its bonus -- see AiActions.WeaponActions.
            weapons.Add(new AiWeapon(
                kind,
                item.Tail.RangeMax,
                DiceEstimate(item.Combat.NbrDiceSm, item.Combat.DmgDiceSm),
                HasSpell: !string.IsNullOrEmpty(item.Names.SpellId),
                AmmoType: item.Scalars.AmmoType,
                DamageBonus: DamageScale * item.Combat.DmgBonusSm));
        }

        return weapons;
    }

    /// <summary>
    /// What a combatant can shoot (<c>ListAmmo</c>, <c>Combatant.cpp:1213</c>).
    /// </summary>
    /// <remarks>
    /// Ammunition is any carried item whose weapon type is <see cref="WeaponClass.Ammo"/>,
    /// wherever it sits — unlike weapons, there is no readied-slot test. The quantity comes from
    /// the carried stack rather than the database.
    /// </remarks>
    public static List<AiAmmo> AmmoFor(Combatant combatant,
                                       Func<string, ItemRecord?>? itemInfo = null)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        var ammo = new List<AiAmmo>();

        foreach (var carried in combatant.Items)
        {
            if (itemInfo?.Invoke(carried.ItemId) is not { } item
                || (WeaponClass)item.Tail.WeaponType != WeaponClass.Ammo)
            {
                continue;
            }

            ammo.Add(new AiAmmo(item.Scalars.AmmoType, carried.Quantity,
                                DiceEstimate(item.Combat.NbrDiceSm, item.Combat.DmgDiceSm),
                                DamageScale * item.Combat.DmgBonusSm));
        }

        return ammo;
    }

    /// <summary>The dice half of a damage estimate, without its bonus.</summary>
    private static int DiceEstimate(int count, int sides) => 5 * ((1 + sides) * count);

    /// <summary>
    /// How many natural attacks a monster has, and what the best of them is worth.
    /// </summary>
    /// <returns>The count, and the damage estimate of the hardest-hitting one.</returns>
    /// <remarks>
    /// A monster with no record has none. The reference also refuses the whole list when the
    /// combatant has no attacks left this round (<c>availAttacks == 0</c>), which the caller
    /// checks.
    /// </remarks>
    public static (int Count, int BestDamage) NaturalAttacks(MonsterRecord? monster)
    {
        if (monster?.Attacks is not { Count: > 0 } attacks)
        {
            return (0, 0);
        }

        int best = attacks.Max(a => AttackDamage(a.Nbr, a.Sides, a.Bonus));
        return (attacks.Count, best);
    }

    /// <summary>The equipment slot a weapon must occupy (<c>WeaponHand</c>, slot zero).</summary>
    private const uint WeaponHandSlot = 0;
}
