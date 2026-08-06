using UAF.Rules;

namespace UAFcore;

/// <summary>
/// Writing a permanent spell effect straight onto an attribute
/// (<c>CHARACTER::AddSpellEffect</c>'s second branch, <c>Char.cpp:12107</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A spell whose <c>Duration_Rate</c> is <c>Permanent</c> never reaches the effect list at
/// all.</b> <c>AddSpellEffect</c> branches on <c>isPerm</c> and the permanent arm reads the
/// attribute, applies the change and writes it back — <c>SetDataXXX</c> — storing nothing, because
/// there is nothing to expire. The non-permanent arm is the one that keeps a
/// <see cref="SpellEffectList"/> entry.
/// </para>
/// <para>
/// <b>This is what makes healing work.</b> A cure spell is permanent, so it moves the character's
/// real hit points rather than layering an adjustment over them — which is why
/// <see cref="FixSpells.WantsFixing"/>, reading raw hit points, eventually stops saying yes and
/// FIX terminates.
/// </para>
/// <para>
/// <b>Virtual traits are the exception.</b> An attribute with no character field behind it has
/// nowhere to be written, so the reference stores it in the effect list even when the spell is
/// permanent (<c>:12283</c>). Here that is any attribute <see cref="Write"/> does not recognise,
/// and <see cref="Applies"/> is what the caller asks first.
/// </para>
/// </remarks>
public static class PermanentEffects
{
    /// <summary>Current hit points (<c>CHAR_HITPOINTS</c>).</summary>
    public const string HitPoints = "$CHAR_HITPOINTS";

    /// <summary>Hit points at full health (<c>CHAR_MAXHITPOINTS</c>).</summary>
    public const string MaxHitPoints = "$CHAR_MAXHITPOINTS";

    /// <summary>Base armour class (<c>CHAR_AC</c>).</summary>
    public const string ArmorClass = "$CHAR_AC";

    /// <summary>To-hit number (<c>CHAR_THAC0</c>).</summary>
    public const string Thac0 = "$CHAR_THAC0";

    /// <summary>Percentage magic resistance (<c>CHAR_MAGICRESIST</c>).</summary>
    public const string MagicResistance = "$CHAR_MAGICRESIST";

    /// <summary>Morale (<c>CHAR_MORALE</c>).</summary>
    public const string Morale = "$CHAR_MORALE";

    /// <summary>
    /// Whether a permanent effect on this attribute can be written to a character at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>False means "treat it as a virtual trait" — store it instead.</b> That is the reference's
    /// own fallback and not a gap being papered over, though the set of real attributes here is
    /// narrower than the reference's: it covers what <see cref="Character"/> actually models, and
    /// an attribute this port has no field for behaves exactly as a virtual trait does.
    /// </para>
    /// <para>
    /// <b>Armour class, THAC0 and magic resistance are named above but not in this set</b>, because
    /// <see cref="Character"/> reads all three straight off its immutable record. A permanent
    /// effect on one of them falls through to the effect list, which is observably the same for
    /// every reader — all three are read through their adjusted form — but a <i>saved</i> game
    /// would not carry the change where the reference's would.
    /// </para>
    /// </remarks>
    public static bool Applies(string attribute) =>
        attribute is HitPoints or MaxHitPoints or Morale;

    /// <summary>
    /// Applies a permanent change and writes it back.
    /// </summary>
    /// <returns>Whether anything was written.</returns>
    /// <remarks>
    /// <para>
    /// <b>The change goes through the same <c>ApplyChange</c> the adjustment pass uses</b>
    /// (<see cref="SpellEffects.Apply(double, SpellEffect)"/>), so a percentage effect is a
    /// percentage of the current value and an absolute one replaces it — the difference between
    /// the two branches is where the answer is put, not how it is computed.
    /// </para>
    /// <para>
    /// <b>Hit points are not clamped here.</b> The reference writes through <c>SetHitPoints</c>,
    /// which is where any bounding would live; a cure that overshoots leaves the character above
    /// their maximum until something else pulls them back, and
    /// <see cref="Character.AdjustedHitPoints"/> is what caps the number anyone reads.
    /// </para>
    /// </remarks>
    public static bool Write(Character target, string attribute, SpellEffect effect)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(attribute);

        if (!Applies(attribute))
        {
            return false;
        }

        int value = (int)SpellEffects.Apply(Read(target, attribute), effect);

        switch (attribute)
        {
            case HitPoints: target.HitPoints = value; return true;
            case MaxHitPoints: target.MaxHitPoints = value; return true;
            default: target.Morale = value; return true;
        }
    }

    private static double Read(Character target, string attribute) => attribute switch
    {
        HitPoints => target.HitPoints,
        MaxHitPoints => target.MaxHitPoints,
        _ => target.Morale,
    };
}
