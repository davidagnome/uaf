using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// What a spell's five dice parameters mean, which depends on how it targets
/// (<c>SPELL_DATA::TargetQuantity</c>, <c>TargetRange</c>, <c>TargetWidth</c> and
/// <c>TargetHeight</c>, <c>Spell.cpp:4787</c>–<c>:4905</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One field means different things to different spells.</b> <c>P1</c> is the target
/// <i>count</i> for a spell that picks units and the area <i>width</i> for one that covers ground;
/// <c>P2</c> is the height, except for a circle where it is the width again. Four accessors read
/// six fields through four switch tables, and the only way to know which is which is the targeting
/// mode.
/// </para>
/// <para>
/// <b>The field names are fossils and will mislead.</b> They are called <c>P1</c>…<c>P6</c>
/// precisely because they were renamed away from what they used to mean — the header still carries
/// <c>//Was NumTargets</c> on <c>P1</c> and <c>//Was TargetRange</c> on <c>P2</c>. Neither comment
/// is true now: the range comes from <c>P3</c> and <c>P2</c> is a height. The port's own reader
/// repeats those stale comments where it names the fields it reads.
/// </para>
/// <para>
/// <b>The four accessors also return two constants that are not fields at all.</b>
/// <see cref="Infinity"/> stands for "no limit" and <see cref="TouchRange"/> for the one square a
/// touch spell reaches — both are static <c>DICEPLUS</c> objects the accessors hand back by
/// reference, so a mode with no meaningful value still gets a rollable one.
/// </para>
/// </remarks>
public static class SpellParameters
{
    /// <summary>The reference's <c>DICEPLUS infinity("999999")</c> (<c>Spell.cpp:4783</c>).</summary>
    public const int Infinity = 999999;

    /// <summary>The reference's <c>DICEPLUS RollOne("0d0+1")</c> (<c>Spell.cpp:4846</c>).</summary>
    public const int TouchRange = 1;

    /// <summary>
    /// How many targets the spell takes (<c>TargetQuantity</c>).
    /// </summary>
    /// <remarks>
    /// <b>Only the four unit-picking modes have a count.</b> Self and whole-party take everyone
    /// they are going to take, and the area shapes take whatever they cover, so all of those
    /// answer <see cref="Infinity"/> — which is what makes a target-count cap meaningless for
    /// them rather than zero.
    /// </remarks>
    public static int Quantity(SpellRecord spell, Func<DicePlus, int> roll)
    {
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(roll);

        return (SpellTargeting)spell.Targeting switch
        {
            SpellTargeting.SelectedByCount or SpellTargeting.TouchedTargets
                or SpellTargeting.AreaCircle or SpellTargeting.SelectByHitDice
                => Roll(spell, P1, roll),

            _ => Infinity,
        };
    }

    /// <summary>
    /// How far the spell reaches (<c>TargetRange</c>).
    /// </summary>
    /// <remarks>
    /// <b>The range is <c>P3</c>, not <c>P2</c></b> — see the class remarks. A touch spell answers
    /// one square from a constant rather than from the design's data, so a designer cannot give
    /// touch a reach.
    /// </remarks>
    public static int Range(SpellRecord spell, Func<DicePlus, int> roll)
    {
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(roll);

        return (SpellTargeting)spell.Targeting switch
        {
            SpellTargeting.Self or SpellTargeting.WholeParty => Infinity,
            SpellTargeting.TouchedTargets => TouchRange,
            _ => Roll(spell, P3, roll),
        };
    }

    /// <summary>
    /// How wide the area is (<c>TargetWidth</c>).
    /// </summary>
    /// <remarks>
    /// <b>A circle reads <c>P2</c> where every other shape reads <c>P1</c></b>, so a circle's
    /// width and height are the same field — which is what makes it a circle rather than an
    /// ellipse, and why <c>P1</c> is free to be its target count.
    /// </remarks>
    public static int Width(SpellRecord spell, Func<DicePlus, int> roll)
    {
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(roll);

        return (SpellTargeting)spell.Targeting switch
        {
            SpellTargeting.AreaCircle => Roll(spell, P2, roll),

            SpellTargeting.AreaSquare or SpellTargeting.AreaCone
                or SpellTargeting.AreaLinePickStart or SpellTargeting.AreaLinePickEnd
                => Roll(spell, P1, roll),

            _ => Infinity,
        };
    }

    /// <summary>How tall the area is (<c>TargetHeight</c>).</summary>
    public static int Height(SpellRecord spell, Func<DicePlus, int> roll)
    {
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(roll);

        return (SpellTargeting)spell.Targeting switch
        {
            SpellTargeting.AreaSquare or SpellTargeting.AreaCone
                or SpellTargeting.AreaLinePickStart or SpellTargeting.AreaLinePickEnd
                or SpellTargeting.AreaCircle
                => Roll(spell, P2, roll),

            _ => Infinity,
        };
    }

    /// <summary>
    /// Where each field sits in <see cref="SpellRecord.Parameters"/>.
    /// </summary>
    /// <remarks>
    /// Index 0 is the spell's own duration; the five that follow are <c>P1</c>…<c>P5</c>. The last
    /// three are only written by designs at version 0.999432 or later, so an older design has a
    /// shorter list and a missing field reads as zero rather than throwing.
    /// </remarks>
    private const int P1 = 1;

    /// <inheritdoc cref="P1"/>
    private const int P2 = 2;

    /// <inheritdoc cref="P1"/>
    private const int P3 = 3;

    private static int Roll(SpellRecord spell, int index, Func<DicePlus, int> roll) =>
        index < spell.Parameters.Count ? roll(spell.Parameters[index]) : 0;
}
