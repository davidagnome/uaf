using UAF.Media;
using UAF.Rules;

namespace UAFcore;

/// <summary>
/// Turns a <see cref="Character"/> into the strings <see cref="CharStatsForm"/> draws.
/// </summary>
/// <remarks>
/// <para>
/// The wording lives here rather than in the form, so <c>UAF.Media</c> needs no character type. The
/// tables are transcribed from <c>CharStatsForm.cpp:54-86</c>.
/// </para>
/// <para>
/// <b>The derived combat fields are deliberately left empty.</b> Armour class, THAC0, damage,
/// encumbrance and movement come from <c>GameRules.cpp</c>, which is not ported —
/// <c>CHARACTER::GetAdjAC</c> alone folds in armour, dexterity, spell effects and racial
/// adjustments. The record carries a stored <c>ArmorClass</c>, but it is the value as saved rather
/// than the adjusted one the sheet shows, and printing it would look right while being wrong for
/// any character wearing anything.
/// </para>
/// </remarks>
public static class CharacterSheetBuilder
{
    /// <summary><c>CharStatusTypeText</c>.</summary>
    private static readonly string[] StatusNames =
    [
        "OKAY", "UNCONSCIOUS", "DEAD", "FLED", "PETRIFIED",
        "GONE", "ANIMATED", "TEMP GONE", "RUNNING", "DYING",
    ];

    /// <summary><c>CharAlignmentTypeText</c>.</summary>
    private static readonly string[] AlignmentNames =
    [
        "LAWFUL GOOD", "NEUTRAL GOOD", "CHAOTIC GOOD",
        "LAWFUL NEUTRAL", "TRUE NEUTRAL", "CHAOTIC NEUTRAL",
        "LAWFUL EVIL", "NEUTRAL EVIL", "CHAOTIC EVIL",
    ];

    private static string Name(string[] table, int index) =>
        index >= 0 && index < table.Length ? table[index] : string.Empty;

    /// <summary>
    /// Builds the sheet.
    /// </summary>
    /// <param name="baseclasses">
    /// The design's baseclasses, used to word each experience line's level. Null still produces the
    /// experience totals, just without levels derived from thresholds.
    /// </param>
    public static CharacterSheet Build(
        Character character,
        IReadOnlyDictionary<string, UAF.Serialization.BaseclassRecord>? baseclasses = null)
    {
        ArgumentNullException.ThrowIfNull(character);

        var record = character.Record;

        var experience = new List<string>();
        foreach (var progress in character.Baseclasses)
        {
            // "FIGHTER 25460" -- the reference words the level separately, on the LEVEL line.
            experience.Add($"{progress.BaseclassId.ToUpperInvariant()} {progress.Experience}");
        }

        return new CharacterSheet(
            Name: character.Name,
            Gender: character.Gender == UAFcore.Gender.Female ? "FEMALE" : "MALE",
            Age: $"{record.Age} YEARS",
            Status: Name(StatusNames, record.Status),
            Alignment: Name(AlignmentNames, record.Alignment),
            Race: character.Race.ToUpperInvariant(),
            Class: character.ClassId.ToUpperInvariant(),
            Level: $"LEVEL {HighestLevel(character, baseclasses)}",
            Hits: character.HitPoints.ToString(),
            MaxHits: $"/{character.MaxHitPoints}",
            ExperienceLines: experience,
            Abilities: Abilities(record.Abilities),
            Coins: Coins(character));
    }

    /// <summary>
    /// The level the sheet's LEVEL line shows: the highest across the character's baseclasses.
    /// </summary>
    /// <remarks>
    /// Derived from the thresholds when the design's baseclasses are available, and taken from the
    /// character's own stored level otherwise — the same fallback <c>LoadedDesign.IsReadyToTrain</c>
    /// uses, and for the same reason: a design whose <c>baseclass.dat</c> this port refuses still
    /// has to draw a sheet.
    /// </remarks>
    private static int HighestLevel(
        Character character,
        IReadOnlyDictionary<string, UAF.Serialization.BaseclassRecord>? baseclasses)
    {
        int highest = 0;
        foreach (var progress in character.Baseclasses)
        {
            int level = progress.CurrentLevel;

            if (baseclasses is not null
                && baseclasses.TryGetValue(progress.BaseclassId, out var baseclass))
            {
                level = Math.Max(
                    level,
                    Levelling.GetLevel(baseclass.ExperienceLevels, (uint)progress.Experience));
            }

            highest = Math.Max(highest, level);
        }

        return Math.Max(highest, 1);
    }

    /// <summary>
    /// The six scores, in the order the sheet lists them.
    /// </summary>
    /// <remarks>
    /// <b>Strength carries a percentile.</b> An 18 with a non-zero modifier reads <c>18/75</c>, and
    /// <c>18/00</c> at 100 — the top of the exceptional-strength table, written as two zeroes
    /// rather than as a hundred. Every other score is a plain number.
    /// </remarks>
    private static string[] Abilities(UAF.Serialization.AbilityScores scores)
    {
        string strength = scores.StrengthMod > 0
            ? $"{scores.Strength}/{(scores.StrengthMod >= 100 ? "00" : $"{scores.StrengthMod:00}")}"
            : scores.Strength.ToString();

        return
        [
            strength,
            scores.Intelligence.ToString(),
            scores.Wisdom.ToString(),
            scores.Dexterity.ToString(),
            scores.Constitution.ToString(),
            scores.Charisma.ToString(),
        ];
    }

    /// <summary>The character's purse, with the design's own coin names.</summary>
    private static ItemsFormCoin[] Coins(Character character)
    {
        var rules = character.Purse.Rules;
        var coins = new ItemsFormCoin[MoneyRules.MaxCoinTypes];

        for (int slot = 0; slot < coins.Length; slot++)
        {
            var type = MoneyRules.ClassOf(slot);

            // An unconfigured denomination is left off entirely, the same way the treasure list
            // does it -- designs use as few as three.
            coins[slot] = rules.IsActive(type)
                ? new ItemsFormCoin(rules[type].Name.ToUpperInvariant(),
                                    character.Purse[type].ToString())
                : new ItemsFormCoin(string.Empty, null);
        }

        return coins;
    }
}
