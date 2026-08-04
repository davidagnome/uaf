using UAF.Serialization;

namespace UAFcore;

/// <summary>One spell a character could learn, and how likely they are to manage it.</summary>
public sealed record AvailableSpell(SpellRecord Spell, int Probability);

/// <summary>
/// Which spells a character may be offered (<c>CreateSpellAvailabilityList</c>,
/// <c>Spell.cpp:8635</c>).
/// </summary>
/// <remarks>
/// <para>
/// Four filters in order, and the fourth is a script hook that defaults to certainty — so a
/// design that writes no hook behaves identically here. The same shape as
/// <c>IS_BASECLASS_ALLOWED</c> in the class filter: the scripting phase will add refusals this
/// port currently grants.
/// </para>
/// <para>
/// <b>The character's own test is injected rather than reached for.</b>
/// <c>CHARACTER::CanKnowSpell</c> asks the character's <c>spellAbility</c> for a per-school
/// maximum level, which <c>UpdateSpellAbility</c> derives — a chain this port has not built. A
/// caller supplies the answer, exactly as <see cref="Training"/> takes two functions rather than
/// a whole design, so the rest of the rule can be exercised now.
/// </para>
/// </remarks>
public static class SpellAvailability
{
    /// <summary>What the <c>KNOWABLE_SPELLS</c> hook means when nothing answers it.</summary>
    /// <remarks>
    /// The hook is run on the <b>class first and the spell second</b>, and an empty reply from
    /// both means 100 — certainty. So the absence of scripting makes every offered spell learnable
    /// on the free allowance or a guaranteed roll, which is more generous than a scripted design
    /// and never less.
    /// </remarks>
    public const int CertainProbability = 100;

    /// <summary>
    /// Builds the list, and reports the highest spell level on it.
    /// </summary>
    /// <param name="canKnow">
    /// <c>CanKnowSpell</c>: whether the character's ability in a school reaches a spell level.
    /// </param>
    /// <param name="probability">
    /// The <c>KNOWABLE_SPELLS</c> hook's answer, or null for "no opinion" — which is
    /// <see cref="CertainProbability"/>. Not ported; a caller with no script runner passes null.
    /// </param>
    /// <returns>The spells on offer and the highest level among them.</returns>
    /// <remarks>
    /// <para>
    /// <b>A probability of zero removes the spell entirely</b> rather than offering it as
    /// impossible — <c>if (probability != 0)</c> guards the add, so a hook can hide a spell as
    /// well as make it unlikely.
    /// </para>
    /// <para>
    /// <b>The reference reads an unparsed reply as an indeterminate number.</b> <c>probability</c>
    /// is an uninitialised local and the reply goes through <c>sscanf(result, "%d", …)</c>, which
    /// leaves it untouched when the text is not a number — so a hook answering "yes" gives
    /// whatever was on the stack. This port treats an unparsable answer as certainty, which is
    /// the only defined behaviour available.
    /// </para>
    /// </remarks>
    public static (List<AvailableSpell> Spells, int MaxLevel) For(
        IEnumerable<SpellRecord> spells,
        IReadOnlyList<string> classBaseclasses,
        Func<string, int, bool> canKnow,
        Func<SpellRecord, int?>? probability = null)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(classBaseclasses);
        ArgumentNullException.ThrowIfNull(canKnow);

        var offered = new List<AvailableSpell>();
        int maxLevel = 0;

        foreach (var spell in spells)
        {
            // 1. The spell must be scribable at all.
            if (spell.AllowScribe == 0)
            {
                continue;
            }

            // 2. The character's ability in the spell's school must reach its level.
            if (!canKnow(spell.SchoolId, spell.Level))
            {
                continue;
            }

            // 3. The class must share a baseclass with the spell's allowed list. A spell that
            //    allows none is therefore offered to nobody.
            if (!spell.AllowedBaseclasses.Any(
                    b => classBaseclasses.Contains(b, StringComparer.Ordinal)))
            {
                continue;
            }

            // 4. The hook, which nothing answers yet.
            int chance = probability?.Invoke(spell) ?? CertainProbability;
            if (chance == 0)
            {
                continue;
            }

            offered.Add(new AvailableSpell(spell, chance));
            maxLevel = Math.Max(maxLevel, spell.Level);
        }

        return (offered, maxLevel);
    }

    /// <summary>
    /// Groups an availability list by spell level, ready for
    /// <see cref="UAF.Rules.SpellAcquisition"/>.
    /// </summary>
    /// <remarks>
    /// <b>Index 0 is the totals row and holds no spells</b> — the acquisition rules read it for
    /// the global floor and ceiling and start their loop at 1, so the list has to be built with
    /// that slot reserved rather than packed from the first level.
    /// </remarks>
    public static List<List<AvailableSpell>> ByLevel(IReadOnlyList<AvailableSpell> spells,
                                                     int maxLevel)
    {
        ArgumentNullException.ThrowIfNull(spells);

        var levels = new List<List<AvailableSpell>>(maxLevel + 1);
        for (int i = 0; i <= Math.Max(maxLevel, 0); i++)
        {
            levels.Add([]);
        }

        foreach (var spell in spells)
        {
            if (spell.Spell.Level >= 0 && spell.Spell.Level < levels.Count)
            {
                levels[spell.Spell.Level].Add(spell);
            }
        }

        return levels;
    }
}
