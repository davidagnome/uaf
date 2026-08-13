namespace UAF.Import.Frua;

/// <summary>
/// The four trait bitfields a <c>MONSTER_DATA</c> carries (<c>Monster.h:60</c>–<c>126</c>).
/// </summary>
/// <remarks>
/// <b>These are the engine's values, not FRUA's.</b> FRUA packs its creature traits into two
/// bytes whose bits mean something different in each; the import spreads those fourteen bits
/// across these four unrelated fields. Naming them here is what makes
/// <see cref="FruaCharacterConverter"/> readable rather than a wall of magic numbers.
/// </remarks>
public static class FruaMonsterTraits
{
    /// <summary>What kind of creature this is, for spells that name a form.</summary>
    [Flags]
    public enum Form : uint
    {
        None = 0,
        Mammal = 1,
        Animal = 2,
        Snake = 4,
        Giant = 8,

        /// <summary>Counts as large in combat regardless of its icon's footprint.</summary>
        Large = 16,
    }

    /// <summary>Racial bonuses and penalties that apply when fighting this creature.</summary>
    [Flags]
    public enum Penalty : uint
    {
        None = 0,
        DwarfArmorClass = 1,
        GnomeArmorClass = 2,
        DwarfThac0 = 4,
        GnomeThac0 = 8,
        RangerDamage = 16,
    }

    /// <summary>What this creature cannot be harmed by.</summary>
    [Flags]
    public enum Immunity : uint
    {
        None = 0,
        Poison = 1,
        Death = 2,
        Confusion = 4,
        Vorpal = 8,
    }

    /// <summary>The two options that fit nowhere else.</summary>
    [Flags]
    public enum MiscOptions : uint
    {
        None = 0,
        CanBeHeldCharmed = 1,
        AffectedByDispelEvil = 2,
    }

    /// <summary>
    /// The engine's creature sizes (<c>Monster.h:33</c>).
    /// </summary>
    public enum Size
    {
        Small,
        Medium,
        Large,
    }
}
