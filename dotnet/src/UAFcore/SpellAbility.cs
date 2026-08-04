using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// What a character can do in one school of magic (<c>SCHOOL_ABILITY</c>).
/// </summary>
/// <param name="Base">Spells known per spell level, from the baseclass tables.</param>
/// <param name="Bonus">Extra spells per spell level, from a high bonus ability.</param>
/// <param name="ContributingLevel">
/// The highest baseclass level that supplied the largest base count at each spell level.
/// </param>
public sealed class SchoolAbility(string schoolId, int spellLevels)
{
    public string SchoolId { get; } = schoolId;

    /// <summary>The highest spell level this character may know in this school.</summary>
    public int MaxSpellLevel { get; set; }

    /// <summary>How many spells may be known at all.</summary>
    public int MaxSpells { get; set; }

    public int[] Base { get; } = new int[spellLevels];

    public int[] Bonus { get; } = new int[spellLevels];

    public int[] ContributingLevel { get; } = new int[spellLevels];
}

/// <summary>
/// Deriving a character's casting ability from its baseclasses
/// (<c>UpdateSpellAbilityForBaseclass</c>, <c>Char.cpp:8652</c>).
/// </summary>
/// <remarks>
/// This is what <c>CanKnowSpell</c> consults, and therefore what decides which spells a character
/// is offered at creation — see <see cref="SpellAvailability"/>.
/// </remarks>
public static class SpellAbility
{
    /// <summary><c>MAX_SPELL_LEVEL</c>.</summary>
    public const int MaxSpellLevel = 9;

    /// <summary>
    /// Folds one baseclass's casting tables into the character's school abilities.
    /// </summary>
    /// <param name="abilities">
    /// The schools accumulated so far, by school id. Added to in place, because a character with
    /// two baseclasses casting from one school combines them here.
    /// </param>
    /// <param name="level">The character's effective level in this baseclass.</param>
    /// <param name="abilityScore">Resolves an ability id to this character's adjusted score.</param>
    /// <remarks>
    /// <para>
    /// <b>The maximum spell level is <i>assigned</i>, not maximised.</b> The line above it in the
    /// reference is the commented-out <c>if (maxSpellLevel &gt; …) …maxAbilitySpellLevel =
    /// maxSpellLevel;</c>, replaced in 2017 by a bare assignment from the by-prime table. So for a
    /// character whose two baseclasses cast from the same school, <b>the last one folded in
    /// wins</b> rather than the better of the two — and the order is the order of
    /// <c>BASECLASS_STATS</c>.
    /// </para>
    /// <para>
    /// <b>The old code's <c>maxSpellLevel</c> is still computed and now goes nowhere.</b> The scan
    /// for the highest non-zero entry in the level's spell-limit row survives the replacement and
    /// its result is never read.
    /// </para>
    /// <para>
    /// <b>The base count updates on <c>&gt;=</c>, not <c>&gt;</c>.</b> A baseclass matching the
    /// count already recorded still takes the slot, so a tie hands the "contributing level" to
    /// whichever baseclass is folded in later — and that level is what tells a level-up how many
    /// new spells to grant.
    /// </para>
    /// <para>
    /// <b>Bonus spells are triples of (threshold, bonus, level)</b> and are <i>accumulated</i>, not
    /// replaced — every qualifying triple adds. A bonus naming a level above the school's maximum
    /// is skipped rather than clamped.
    /// </para>
    /// </remarks>
    public static void Fold(Dictionary<string, SchoolAbility> abilities, BaseclassRecord baseclass,
                            int level, Func<string, int> abilityScore)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(baseclass);
        ArgumentNullException.ThrowIfNull(abilityScore);

        foreach (var casting in baseclass.Casting)
        {
            if (!abilities.TryGetValue(casting.SchoolId, out var ability))
            {
                abilities[casting.SchoolId] = ability =
                    new SchoolAbility(casting.SchoolId, MaxSpellLevel);
            }

            int prime = abilityScore(casting.PrimeAbility);
            int bonusScore = abilityScore(baseclass.SpellBonusAbility);

            int maxSpells = ByPrime(casting.MaxSpellsByPrime, prime);
            if (maxSpells > ability.MaxSpells)
            {
                ability.MaxSpells = maxSpells;
            }

            // Assigned, not maximised -- see the remarks.
            ability.MaxSpellLevel = ByPrime(casting.MaxSpellLevelByPrime, prime);

            var limits = LimitsForLevel(casting.SpellsPerLevel, level);
            for (int spellLevel = 0; spellLevel < MaxSpellLevel; spellLevel++)
            {
                if (limits[spellLevel] >= ability.Base[spellLevel])
                {
                    ability.Base[spellLevel] = limits[spellLevel];

                    if (level > ability.ContributingLevel[spellLevel])
                    {
                        ability.ContributingLevel[spellLevel] = level;
                    }
                }
            }

            AddBonusSpells(ability, baseclass.BonusSpells, bonusScore);
        }
    }

    /// <summary>The row of spell limits for one baseclass level.</summary>
    /// <remarks>
    /// <b>The blob is level-major and one-based.</b> Row <c>level-1</c> holds
    /// <see cref="MaxSpellLevel"/> bytes; a level below 1 or past the table would index outside
    /// it, so both are clamped rather than trusted.
    /// </remarks>
    private static ReadOnlySpan<byte> LimitsForLevel(byte[] spellsPerLevel, int level)
    {
        int rows = spellsPerLevel.Length / MaxSpellLevel;
        if (rows == 0)
        {
            return new byte[MaxSpellLevel];
        }

        int row = Math.Clamp(level, 1, rows) - 1;
        return spellsPerLevel.AsSpan(row * MaxSpellLevel, MaxSpellLevel);
    }

    /// <summary>A by-prime-score table lookup, clamped into the table.</summary>
    private static int ByPrime(byte[] table, int score) =>
        table.Length == 0 ? 0 : table[Math.Clamp(score, 1, table.Length) - 1];

    /// <summary>
    /// Adds the bonus spells a high ability score grants (<c>Char.cpp:8723</c>).
    /// </summary>
    private static void AddBonusSpells(SchoolAbility ability, byte[] bonusSpells, int score)
    {
        for (int i = 0; i + 2 < bonusSpells.Length; i += 3)
        {
            if (score < bonusSpells[i])
            {
                continue;
            }

            int level = bonusSpells[i + 2];
            if (level < 1 || level > ability.MaxSpellLevel)
            {
                continue;
            }

            ability.Bonus[level - 1] += bonusSpells[i + 1];
        }
    }

    /// <summary>
    /// Whether a character may know a spell (<c>CHARACTER::CanKnowSpell</c>, <c>Char.h:1376</c>).
    /// </summary>
    /// <remarks>
    /// <b>A school the character has no ability in is a refusal, not a zero.</b> The lookup
    /// returning -1 returns FALSE before the level is even compared.
    /// </remarks>
    public static bool CanKnow(IReadOnlyDictionary<string, SchoolAbility> abilities,
                               string schoolId, int spellLevel)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        return abilities.TryGetValue(schoolId, out var ability)
               && spellLevel <= ability.MaxSpellLevel;
    }
}
