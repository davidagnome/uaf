namespace UAF.Rules;

/// <summary>
/// An effect on a character, with the bookkeeping that decides how long it lasts.
/// </summary>
/// <param name="Effect">The arithmetic — see <see cref="SpellEffects"/>.</param>
/// <param name="StopTime">
/// The elapsed-minute reading at which it ends, from <see cref="SpellDuration.StopTimeFor"/>.
/// </param>
/// <param name="FromScript">Whether it came from a script, which shifts the expiry test by one.</param>
/// <param name="SourceSpell">
/// The spell that cast it (<c>SPELL_EFFECTS_DATA::SourceSpell_ID</c>). Empty when the effect is not
/// from a spell — an intrinsic ability, or an item.
/// </param>
/// <param name="Parent">
/// The active-spell entry this belongs to (<c>parent</c>), or <c>-1</c>. One cast produces one
/// entry however many targets it lands on, which is what lets a whole cast expire together.
/// </param>
public readonly record struct ActiveSpellEffect(SpellEffect Effect, double? StopTime,
                                                bool FromScript = false,
                                                string SourceSpell = "", int Parent = -1)
{
    /// <summary>The attribute this modifies.</summary>
    public string Attribute => Effect.Attribute;

    /// <summary>Whether this is an intrinsic character ability rather than a cast effect.</summary>
    /// <remarks>
    /// These survive a <see cref="SpellEffectFlags.RemoveAll"/> — they are part of what the
    /// character <i>is</i>, not something done to it.
    /// </remarks>
    public bool IsIntrinsic =>
        (Effect.Flags & SpellEffectFlags.CharacterSpecialAbility) != 0;
}

/// <summary>
/// The effects currently on a character, and the rules for what happens when a new one arrives
/// (<c>CHARACTER::AddSpellEffect</c>, <c>Char.cpp:11989</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the spell layer <see cref="SpellEffects"/> deliberately left out: not how an
/// effect changes a number, but <b>which effects are in the list at all</b>. Three rules decide
/// it — a negated effect never lands, a non-cumulative one refuses to stack, and a remove-all one
/// clears the attribute first.
/// </para>
/// <para>
/// Order is preserved, and it matters: <see cref="SpellEffects.ApplyAll"/> walks the list in order
/// and a percentage or absolute effect discards everything computed before it.
/// </para>
/// </remarks>
public sealed class SpellEffectList
{
    private readonly List<ActiveSpellEffect> effects = [];

    /// <summary>The effects, in application order.</summary>
    public IReadOnlyList<ActiveSpellEffect> Effects => effects;

    public int Count => effects.Count;

    /// <summary>
    /// Offers an effect to the list (<c>AddSpellEffect</c>, <c>Char.cpp:11989</c>).
    /// </summary>
    /// <returns>Whether it was added.</returns>
    /// <remarks>
    /// <para>
    /// The three rules, in the order the reference applies them:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///   <b><see cref="SpellEffectFlags.None"/> is refused outright.</b> That flag means a saving
    ///   throw negated the effect. The check was added because effects were landing <i>despite</i>
    ///   a successful save — there is a dated comment naming the person who reported it
    ///   (<c>:11994</c>).
    ///   </item>
    ///   <item>
    ///   <b>A non-cumulative effect refuses to stack.</b> If anything already modifies the same
    ///   attribute, the new effect is <i>dropped</i> — the incumbent wins, not the newcomer. Two
    ///   castings of the same buff do not add up, and the second is simply wasted.
    ///   </item>
    ///   <item>
    ///   <b><see cref="SpellEffectFlags.RemoveAll"/> clears the attribute and stops there</b>,
    ///   except for intrinsic character abilities, which cannot be removed because they are part
    ///   of the character rather than something done to it. It reports success without adding
    ///   anything — see below.
    ///   </item>
    /// </list>
    /// <para>
    /// <b>A remove-all effect is an instruction, not an effect.</b> The reference's branch ends in
    /// <c>return TRUE</c> (<c>Char.cpp:12054</c>) without ever reaching the add, so the flag means
    /// "strip this attribute" and nothing is left behind carrying the new change. An earlier
    /// version of this port cleared the attribute and <i>then</i> added the effect, which left a
    /// dispel behaving like a replacement — and its own change value quietly in force.
    /// </para>
    /// <para>
    /// Note the order still matters between rules 2 and 3: a remove-all effect that is <i>not</i>
    /// cumulative is refused at rule 2 and never clears anything at all.
    /// </para>
    /// </remarks>
    public bool Add(ActiveSpellEffect effect)
    {
        var flags = effect.Effect.Flags;

        // 1. A save negated it.
        if ((flags & SpellEffectFlags.None) != 0)
        {
            return false;
        }

        // 2. Not cumulative and something already holds this attribute: the incumbent wins.
        if ((flags & SpellEffectFlags.Cumulative) == 0
            && effects.Any(e => e.Attribute == effect.Attribute))
        {
            return false;
        }

        // 3. Clear the attribute, sparing intrinsic abilities, and stop -- the reference returns
        // here rather than falling through to the add.
        if ((flags & SpellEffectFlags.RemoveAll) != 0)
        {
            effects.RemoveAll(e => e.Attribute == effect.Attribute && !e.IsIntrinsic);
            return true;
        }

        effects.Add(effect);
        return true;
    }

    /// <summary>
    /// Drops every effect that has run out (<c>ProcessLingeringSpellEffects</c>' expiry half).
    /// </summary>
    /// <param name="elapsedMinutes">The clock now — one combat round is one minute.</param>
    /// <returns>The effects removed, in the order they were held.</returns>
    /// <remarks>
    /// Called at the head of a round. Because a round is a minute, a spell cast for three rounds
    /// survives exactly three of these.
    /// </remarks>
    public IReadOnlyList<ActiveSpellEffect> Expire(double elapsedMinutes)
    {
        var expired = effects
            .Where(e => SpellDuration.IsReadyToExpire(e.StopTime, elapsedMinutes, e.FromScript))
            .ToList();

        foreach (var e in expired)
        {
            effects.Remove(e);
        }

        return expired;
    }

    /// <summary>Applies every effect on an attribute to a value, in order.</summary>
    public double Apply(double value, string attribute) =>
        SpellEffects.ApplyAll(value, attribute, effects.Select(e => e.Effect));

    /// <summary>
    /// Applies every effect on an attribute to an integer value, clamped to the attribute's own
    /// legal range.
    /// </summary>
    /// <remarks>
    /// The bounds are required rather than defaulted, matching
    /// <see cref="SpellEffects.ApplyAll(int, string, IEnumerable{SpellEffect}, int, int)"/>: an
    /// effect can otherwise push an attribute outside the range it is allowed to hold, and what
    /// that range is depends on the attribute.
    /// </remarks>
    public int Apply(int value, string attribute, int min, int max) =>
        SpellEffects.ApplyAll(value, attribute, effects.Select(e => e.Effect), min, max);

    /// <summary>Removes everything, as the end of a fight does.</summary>
    public void Clear() => effects.Clear();

    /// <summary>
    /// Removes every effect matching <paramref name="match"/>, and answers how many went.
    /// </summary>
    /// <remarks>
    /// <b>Intrinsic effects are not exempt here.</b> <see cref="Add"/>'s
    /// <see cref="SpellEffectFlags.RemoveAll"/> path deliberately spares them — they are part of
    /// what the creature is rather than something cast on it — but the caller of this decides,
    /// because a script's <c>$CHAR_REMOVEALLSPELLS</c> selects on the source spell's level and has
    /// its own rule about what it may take.
    /// </remarks>
    public int RemoveWhere(Func<ActiveSpellEffect, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);

        return effects.RemoveAll(e => match(e));
    }
}
