namespace UAFcore;

/// <summary>
/// How a weapon reaches its target (<c>weaponClassType</c>, <c>Items.h:50</c>).
/// </summary>
/// <remarks>
/// The header carries a table of what each class can do, and it is worth reading before assuming
/// anything from the names — <see cref="HandThrow"/> attacks adjacent <i>and</i> at range and
/// consumes itself only beyond range 1, while <see cref="Throw"/> cannot attack adjacent at all.
/// </remarks>
public enum WeaponClass
{
    NotWeapon = 0,
    HandBlunt = 1,
    HandCutting = 2,
    HandThrow = 3,
    SlingNoAmmo = 4,
    Bow = 5,
    Crossbow = 6,
    Throw = 7,
    Ammo = 8,
    SpellCaster = 9,
    SpellLikeAbility = 10,
}

/// <summary>
/// A weapon as the attack tests see it. Enough to answer "can this reach?", not the whole item.
/// </summary>
/// <param name="Class">Which reach rule applies.</param>
/// <param name="Range">The item's own maximum range.</param>
/// <param name="Quantity">Stack count; a spent stack cannot attack.</param>
/// <param name="Charges">For a magical non-weapon, which attacks by being used up.</param>
/// <param name="IsMagical">Whether a <see cref="WeaponClass.NotWeapon"/> item can attack at all.</param>
/// <param name="AmmoClass">
/// The ammunition family. A bow and its arrows must agree, and an empty value means the item takes
/// no ammunition — a wand or potion, say.
/// </param>
/// <param name="CastsSelfTargetingSpell">
/// True when this is a spell item whose spell targets the caster, which pins its range to zero.
/// </param>
public readonly record struct ReadiedWeapon(
    WeaponClass Class, int Range, int Quantity = 1, int Charges = 0,
    bool IsMagical = false, string AmmoClass = "", bool CastsSelfTargetingSpell = false);

/// <summary>Readied ammunition, for the weapons that need it.</summary>
public readonly record struct ReadiedAmmo(WeaponClass Class, int Quantity, string AmmoClass);

/// <summary>
/// Whether a weapon can strike at a given distance
/// (<c>WpnCanAttackAtRange</c>, <c>Items.cpp:223</c>).
/// </summary>
public static class WeaponRange
{
    /// <summary>
    /// Whether <paramref name="weapon"/> can attack something <paramref name="range"/> squares
    /// away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ranged classes have a minimum as well as a maximum.</b> A bow, crossbow, sling or
    /// thrown weapon needs <c>range &gt;= 2</c> — they cannot be used on an adjacent enemy at all.
    /// The hand classes have no minimum, so a hand-thrown weapon covers both.
    /// </para>
    /// <para>
    /// A money item is refused outright in the reference before the switch is reached; that check
    /// belongs to the caller here, since nothing in this record identifies coins.
    /// </para>
    /// </remarks>
    public static bool CanAttackAt(ReadiedWeapon weapon, int range) => weapon.Class switch
    {
        WeaponClass.NotWeapon or WeaponClass.Ammo => false,

        // Ranged: never adjacent, and never past the item's own range.
        WeaponClass.SlingNoAmmo or WeaponClass.Bow or WeaponClass.Crossbow or WeaponClass.Throw =>
            range >= 2 && range <= weapon.Range,

        WeaponClass.HandBlunt or WeaponClass.HandCutting or WeaponClass.HandThrow =>
            range <= weapon.Range,

        // A spell item reaches as far as the item says, unless its spell targets the caster --
        // in which case the only legal range is zero (Items.cpp:264).
        WeaponClass.SpellCaster or WeaponClass.SpellLikeAbility =>
            weapon.CastsSelfTargetingSpell ? range == 0 : range <= weapon.Range,

        _ => false,
    };
}
