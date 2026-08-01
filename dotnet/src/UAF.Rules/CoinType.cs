namespace UAF.Rules;

/// <summary>
/// What kind of thing an item slot holds (<c>itemClassType</c>, <c>Shared/Items.h:169</c>).
/// </summary>
/// <remarks>
/// <para>
/// Ordinal values, and they are not contiguous: <see cref="BogusItem"/> at 11 sits between
/// <see cref="Quest"/> at 10 and <see cref="Coin6"/> at 12, because the five extra coin slots were
/// appended after it. Only the coin members are used by the money system; the rest are here so the
/// numbering is visible rather than implied.
/// </para>
/// </remarks>
public enum ItemClass
{
    Item = 0,
    Platinum = 1,
    Electrum = 2,
    Gold = 3,
    Silver = 4,
    Copper = 5,
    Gem = 6,
    Jewelry = 7,
    SpecialItem = 8,
    SpecialKey = 9,
    Quest = 10,
    BogusItem = 11,
    Coin6 = 12,
    Coin7 = 13,
    Coin8 = 14,
    Coin9 = 15,
    Coin10 = 16,
    EquipmentSet = 17,
}

/// <summary>One coin denomination.</summary>
/// <param name="Rate">
/// How many of this coin equal one of the <i>most valuable</i> coin — so a <b>higher</b> rate means
/// a <b>less</b> valuable coin. The AD&amp;D defaults are platinum 1, gold 5, electrum 10,
/// silver 100, copper 1000 (<c>SetUADefaults</c>, <c>Money.cpp:514</c>).
/// </param>
/// <param name="IsBase">
/// The design's own "base" flag, set on platinum by default. <b>This is not what
/// <see cref="MoneyRules.BaseType"/> means</b> — see the remarks there.
/// </param>
/// <param name="Name">The denomination's display name.</param>
public sealed record Coin(double Rate, bool IsBase, string Name)
{
    /// <summary>A slot no design has configured. Rate 0 means inactive.</summary>
    public static readonly Coin Inactive = new(0.0, false, string.Empty);
}
