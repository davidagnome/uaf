namespace UAF.Rules;

/// <summary>
/// The flags on a spell effect (<c>SPELL_EFFECTS_DATA</c>, <c>class.h:2348</c>).
/// </summary>
/// <remarks>
/// Three groups in one word: <b>how</b> the change applies (<see cref="Percent"/>,
/// <see cref="Absolute"/>, <see cref="None"/>), <b>who</b> it came from (the source flags), and
/// <b>bookkeeping</b> for the engine's own duration handling.
/// </remarks>
[Flags]
public enum SpellEffectFlags
{
    /// <summary>No flag: the change is a delta added to the original.</summary>
    Delta = 0,

    /// <summary>Set the value to a percentage of the original.</summary>
    Percent = 0x00000002,

    Target = 0x00000004,
    Targeter = 0x00000008,
    Cumulative = 0x00000010,

    /// <summary>Set the value to the amount outright.</summary>
    Absolute = 0x00000020,

    ItemSpecialAbility = 0x00000040,
    SpellSpecialAbility = 0x00000080,
    CharacterSpecialAbility = 0x00000100,
    Spell = 0x00000200,

    /// <summary>Remove every effect of this type, except inherent character abilities.</summary>
    RemoveAll = 0x00000400,

    /// <summary>Affect the target once rather than once per round.</summary>
    OnceOnly = 0x00000800,

    /// <summary>Set the first time the effect is applied.</summary>
    Applied = 0x00001000,

    /// <summary>The effect does nothing — a saving throw negated it.</summary>
    None = 0x00002000,

    Script = 0x00004000,
    TimedSpecialAbility = 0x00008000,

    /// <summary>Any of the four source flags.</summary>
    AllSources = ItemSpecialAbility | SpellSpecialAbility | CharacterSpecialAbility | Spell,

    /// <summary>The three special-ability sources.</summary>
    SpecialAbilities = ItemSpecialAbility | SpellSpecialAbility | CharacterSpecialAbility,
}

/// <summary>
/// One effect currently modifying an attribute.
/// </summary>
/// <param name="Attribute">
/// The attribute it changes, as the engine's keyword — <c>$CHAR_AC</c>, <c>$CHAR_THAC0</c> and so
/// on.
/// </param>
/// <param name="Change">
/// The rolled amount. The reference rolls its dice once and caches the result, dying if the value
/// is read before that happens; this takes the rolled number, so the dice live with the caller.
/// </param>
public readonly record struct SpellEffect(string Attribute, double Change,
                                          SpellEffectFlags Flags = SpellEffectFlags.Delta);

/// <summary>
/// Applying spell effects to an attribute (<c>SPELL_EFFECTS_DATA::ApplyChange</c>,
/// <c>Spell.cpp:497</c>, and <c>CHARACTER::ApplySpellEffectAdjustments</c>, <c>Char.cpp:13062</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the layer every <c>GetAdj*</c> accessor routes through — armour class, THAC0, hit
/// points, ability scores. It is the last piece of shared machinery between the ported rules and
/// combat.
/// </para>
/// <para>
/// <b>Two of the three modes replace rather than adjust, so order decides the answer.</b> A
/// percentage or absolute effect discards everything computed before it, which means the list's
/// order is part of the result and an absolute effect anywhere in it wipes out the ones above.
/// The reference walks the character's effect list in its stored order and this does the same.
/// </para>
/// <para>
/// <b>Durations, sources and stacking are not modelled here.</b> The reference tracks each effect's
/// parent spell, expiry time and once-only bookkeeping; this is only the arithmetic, which is what
/// the character sheet and the combat numbers need. What is missing is the part that decides which
/// effects are in the list at all.
/// </para>
/// </remarks>
public static class SpellEffects
{
    /// <summary>
    /// Applies one effect to a value.
    /// </summary>
    /// <remarks>
    /// <b>It returns the new value, not a delta</b>, despite the caller's comment saying otherwise
    /// (<c>Char.cpp:13073</c>: "return accumulated delta"). Reading that comment rather than the
    /// function would make every effect compound.
    /// </remarks>
    public static double Apply(double value, SpellEffect effect)
    {
        // A negated effect leaves the value exactly as it was -- checked before the others, so a
        // negated percentage change is still a no-op rather than a multiply by zero.
        if (effect.Flags.HasFlag(SpellEffectFlags.None))
        {
            return value;
        }

        if (effect.Flags.HasFlag(SpellEffectFlags.Percent))
        {
            return effect.Change * 0.01 * value;
        }

        if (effect.Flags.HasFlag(SpellEffectFlags.Absolute))
        {
            return effect.Change;
        }

        return value + effect.Change;
    }

    /// <summary>
    /// Applies every effect on an attribute, in order.
    /// </summary>
    /// <param name="attribute">The engine keyword, matched exactly.</param>
    public static double ApplyAll(double value, string attribute,
                                  IEnumerable<SpellEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        ArgumentNullException.ThrowIfNull(effects);

        foreach (var effect in effects)
        {
            if (string.Equals(effect.Attribute, attribute, StringComparison.Ordinal))
            {
                value = Apply(value, effect);
            }
        }

        return value;
    }

    /// <summary>
    /// Applies every effect and clamps the result, as the <c>GetAdj*</c> accessors do.
    /// </summary>
    /// <remarks>
    /// The clamp is the accessor's, not the effect layer's — <c>GetAdjAC</c> holds the result
    /// inside <c>MIN_AC</c> and <c>MAX_AC</c> after applying effects, so an absolute effect cannot
    /// push an attribute outside its own legal range.
    /// </remarks>
    public static int ApplyAll(int value, string attribute, IEnumerable<SpellEffect> effects,
                               int min, int max) =>
        (int)Math.Clamp(ApplyAll((double)value, attribute, effects), min, max);
}
