namespace UAF.Rules;

/// <summary>
/// Armour class (<c>CHARACTER::SetCharBaseAC</c>, <c>Char.cpp:4679</c>, and
/// <c>GetEffectiveAC</c>, <c>:13220</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lower is better.</b> 10 is unarmoured; armour and shields carry <i>negative</i> protection
/// values that pull it down, and the floor is a long way below zero.
/// </para>
/// <para>
/// <b>Equipment is no longer folded into the stored value, and the header says why.</b>
/// <c>m_AC</c> was renamed in 2010 with the note that "we used to change AC as a PC readied armor
/// and such, but it was not changed for enemies who wore armor. This made things very confusing."
/// So the stored field is now dexterity alone and equipment is applied at every read — which is why
/// <c>SetCharAC</c> still carries the old <c>AC += GetProtectModForRdyItems()</c> line commented
/// out. Reading the stored value and calling it the character's armour class reproduces exactly the
/// bug the rename was meant to end.
/// </para>
/// <para>
/// <b>Two different "adjusted" values exist and neither includes both adjustments.</b>
/// <c>GetEffectiveAC</c> is base plus readied items; <c>GetAdjAC</c> is base plus spell effects.
/// Nothing combines them. This ports the former, since the spell-effect layer does not exist yet.
/// </para>
/// </remarks>
public static class ArmorClass
{
    /// <summary>The worst armour class, and the unarmoured starting point (<c>MAX_AC</c>).</summary>
    public const int Worst = 10;

    /// <summary>The best armour class attainable (<c>MIN_AC</c>).</summary>
    public const int Best = -500;

    /// <summary>The dexterity above which a character starts dodging.</summary>
    public const int DexterityThreshold = 14;

    /// <summary>
    /// The armour class dexterity alone gives (<c>SetCharBaseAC</c>).
    /// </summary>
    /// <remarks>
    /// One point per point of dexterity above 14, and nothing at all below it — <b>there is no
    /// penalty for being clumsy</b>, unlike the tabletop rules this otherwise follows.
    /// </remarks>
    public static int Base(int dexterity)
    {
        // The reference reads the score into a BYTE first, so a value past 255 wraps.
        byte dex = (byte)dexterity;

        int ac = dex > DexterityThreshold ? Worst - (dex - DexterityThreshold) : Worst;
        return Clamp(ac);
    }

    /// <summary>
    /// The protection a set of readied items contributes
    /// (<c>ITEM_LIST::GetProtectModForRdyItems</c>).
    /// </summary>
    /// <param name="readied">
    /// Base and bonus protection for each item the character has readied. Items in the pack
    /// contribute nothing, however good they are.
    /// </param>
    /// <remarks>
    /// A flat sum of every readied item's base plus bonus, with <b>no slot rules at all</b> — the
    /// reference does not check that two suits of armour cannot both be worn, so a design that lets
    /// a character ready two stacks their protection.
    /// </remarks>
    public static int Protection(IEnumerable<(int Base, int Bonus)> readied)
    {
        ArgumentNullException.ThrowIfNull(readied);

        int total = 0;
        foreach (var (baseValue, bonus) in readied)
        {
            total += baseValue + bonus;
        }
        return total;
    }

    /// <summary>
    /// A character's armour class: dexterity plus everything readied (<c>GetEffectiveAC</c>).
    /// </summary>
    public static int Effective(int dexterity, IEnumerable<(int Base, int Bonus)> readied) =>
        Clamp(Base(dexterity) + Protection(readied));

    /// <summary>Holds a value inside <see cref="Best"/> and <see cref="Worst"/>.</summary>
    public static int Clamp(int armorClass) => Math.Clamp(armorClass, Best, Worst);
}
