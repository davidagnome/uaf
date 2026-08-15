using UAF.Serialization;
using UAFcore;

namespace UAFedit.Databases;

/// <summary>One entry of a combo box over a small integer field.</summary>
/// <remarks>
/// A record, so a <c>ComboBox</c> can match its <c>SelectedItem</c> by value rather than by
/// reference. Non-generic because a generic type parameter cannot be spelled in the compiled
/// binding of a <c>DataTemplate</c>'s <c>x:DataType</c>.
/// </remarks>
public sealed record IntChoice(int Value, string Label);

/// <summary>One entry of a combo box over a string-keyed field — a class id, an undead type.</summary>
public sealed record TextChoice(string Value, string Label);

/// <summary>One entry of the readied-location combo, whose stored value is a base-38 word.</summary>
public sealed record SlotChoice(uint Value, string Label);

/// <summary>
/// The fixed choice lists both database forms draw on.
/// </summary>
/// <remarks>
/// Every list here is closed in the reference but open on disk: nothing stops a design holding a
/// weapon type of 47. <see cref="WithCurrent"/> is what keeps such a value visible and intact
/// instead of silently snapping to whatever the combo happens to select first.
/// </remarks>
public static class Choices
{
    /// <summary>
    /// <c>weaponClassType</c> (<c>Items.h:50</c>), as <see cref="WeaponClass"/> already names it.
    /// </summary>
    public static IReadOnlyList<IntChoice> WeaponTypes { get; } =
    [
        new((int)WeaponClass.NotWeapon, "Not a weapon"),
        new((int)WeaponClass.HandBlunt, "Hand — blunt"),
        new((int)WeaponClass.HandCutting, "Hand — cutting"),
        new((int)WeaponClass.HandThrow, "Hand or throw"),
        new((int)WeaponClass.SlingNoAmmo, "Sling (no ammo)"),
        new((int)WeaponClass.Bow, "Bow"),
        new((int)WeaponClass.Crossbow, "Crossbow"),
        new((int)WeaponClass.Throw, "Throw only"),
        new((int)WeaponClass.Ammo, "Ammunition"),
        new((int)WeaponClass.SpellCaster, "Spell caster"),
        new((int)WeaponClass.SpellLikeAbility, "Spell-like ability"),
    ];

    /// <summary>What <c>IDC_HANDSNEEDED</c> offered.</summary>
    public static IReadOnlyList<IntChoice> HandCounts { get; } =
        [new(0, "0"), new(1, "1"), new(2, "2")];

    /// <summary><c>creatureSizeType</c> (<c>Monster.h:33</c>).</summary>
    public static IReadOnlyList<IntChoice> CreatureSizes { get; } =
        [new(0, "Small"), new(1, "Medium"), new(2, "Large")];

    /// <summary>
    /// <c>itemRechargeRate</c> (<c>Items.h:44</c>) — <c>irrNever</c> then <c>irrDaily</c>.
    /// </summary>
    /// <remarks>
    /// The dialog captioned <c>irrNever</c> "Total", meaning the charge count is all the item will
    /// ever have. Read as a recharge rate, "Total" says the opposite of what the value means.
    /// </remarks>
    public static IReadOnlyList<IntChoice> RechargeRates { get; } =
        [new(0, "Never — a fixed total"), new(1, "Daily")];

    /// <summary>
    /// The turning categories, as <c>UndeadTypeText</c> lists them.
    /// </summary>
    /// <remarks>
    /// <b>Empty is the "not undead" value, not index 0's <c>"Not Undead"</c>.</b> A design at or
    /// below 0.998115 stores an index and the reader maps only <c>0 &lt; i &lt; 14</c>, so the
    /// zero row is unreachable and every "is this undead?" test in the engine is
    /// "is the string non-empty" (<see cref="MonsterRecordReader.UndeadTypeName"/>). Offering
    /// <c>"Not Undead"</c> as a selectable value would make a monster undead.
    /// </remarks>
    public static IReadOnlyList<TextChoice> UndeadTypes { get; } =
    [
        new(string.Empty, "(not undead)"),
        .. MonsterRecordReader.UndeadTypeNames.Skip(1).Select(n => new TextChoice(n, n)),
    ];

    /// <summary>
    /// Every slot the engine names, keyed by the packed <c>DWORD</c> the field holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from <see cref="ReadiedLocation"/>'s own constants rather than from a second copy of
    /// the word list — the packing is not invertible, so a duplicated table would be a table that
    /// could drift from the encoder without anything failing.
    /// </para>
    /// <para>
    /// <b><c>CANNOT</c> and <c>NOTRDY</c> are both in the list and they are not the same thing.</b>
    /// <c>CANNOT</c> is a property of the item — a gem can never be worn; <c>NOTRDY</c> is what a
    /// <i>carried</i> item holds while it sits in the pack. A database record set to <c>NOTRDY</c>
    /// is an item that is readied nowhere rather than one that cannot be readied.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SlotChoice> Slots { get; } =
    [
        Slot(ReadiedLocation.WeaponHand), Slot(ReadiedLocation.ShieldHand),
        Slot(ReadiedLocation.BodyArmor), Slot(ReadiedLocation.Hands),
        Slot(ReadiedLocation.Head), Slot(ReadiedLocation.Waist),
        Slot(ReadiedLocation.BodyRobe), Slot(ReadiedLocation.Back),
        Slot(ReadiedLocation.Feet), Slot(ReadiedLocation.Fingers),
        Slot(ReadiedLocation.AmmoQuiver), Slot(ReadiedLocation.Arms),
        Slot(ReadiedLocation.Legs), Slot(ReadiedLocation.Face),
        Slot(ReadiedLocation.Neck), Slot(ReadiedLocation.Pack),
        Slot(ReadiedLocation.Cannot), Slot(ReadiedLocation.NotReady),
        Slot(ReadiedLocation.Undefined),
    ];

    /// <summary>The three item usage bits (<c>Items.h:755-757</c>).</summary>
    public const int UsageUsable = 0x00000001;

    /// <summary>Set on a spell scroll, which is what makes it scribable into a spell book.</summary>
    public const int UsageScribable = 0x00000002;

    /// <summary>
    /// Marks an item the <i>identify</i> machinery must leave alone.
    /// </summary>
    /// <remarks>
    /// Note the polarity: the stored bit says "not magical", so an ordinary item has it clear and
    /// reads as magical to anything that only checks for zero.
    /// </remarks>
    public const int UsageNotMagical = 0x00000004;

    /// <summary>
    /// The list with <paramref name="current"/> added when the design holds a value nothing names.
    /// </summary>
    /// <remarks>
    /// A combo bound to <c>SelectedItem</c> shows nothing for an off-list value, and the first
    /// touch of the control would then write a value the design never had. Adding the entry keeps
    /// the record editable without losing what it says.
    /// </remarks>
    public static IReadOnlyList<IntChoice> WithCurrent(IReadOnlyList<IntChoice> standard,
                                                       int current)
    {
        ArgumentNullException.ThrowIfNull(standard);

        return standard.Any(c => c.Value == current)
            ? standard
            : [.. standard, new IntChoice(current, $"({current})")];
    }

    /// <inheritdoc cref="WithCurrent(IReadOnlyList{IntChoice}, int)"/>
    public static IReadOnlyList<SlotChoice> WithCurrent(IReadOnlyList<SlotChoice> standard,
                                                        uint current)
    {
        ArgumentNullException.ThrowIfNull(standard);

        return standard.Any(c => c.Value == current)
            ? standard
            : [.. standard, new SlotChoice(current, $"({current})")];
    }

    /// <inheritdoc cref="WithCurrent(IReadOnlyList{IntChoice}, int)"/>
    public static IReadOnlyList<TextChoice> WithCurrent(IReadOnlyList<TextChoice> standard,
                                                        string current)
    {
        ArgumentNullException.ThrowIfNull(standard);
        ArgumentNullException.ThrowIfNull(current);

        return standard.Any(c => string.Equals(c.Value, current, StringComparison.Ordinal))
            ? standard
            : [.. standard, new TextChoice(current, $"{current} (not in this design)")];
    }

    /// <summary>
    /// A legacy ordinal converted to the slot it names, so old designs land on a real choice.
    /// </summary>
    /// <remarks>
    /// <b>The database's table, not the carried item's.</b> They disagree on ordinal 3 —
    /// <c>HANDS</c> here, <c>QUIVER</c> for a carried <c>ITEM</c> — so using
    /// <c>ReadiedLocation.Synonym</c> would turn every old design's gauntlets into quivers on the
    /// first save.
    /// </remarks>
    public static uint NormaliseSlot(uint stored) => ReadiedLocation.Convert(stored);

    private static SlotChoice Slot(uint packed) =>
        new(packed, ReadiedLocation.WordFor(packed) is { Length: > 0 } word ? word : $"({packed})");
}
