using UAF.Media;
using UAF.Rules;
using UAF.Serialization;

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
/// <b>Encumbrance and movement are filled in; armour class, THAC0 and damage are not.</b> The first
/// two are self-contained tables over a strength score (<see cref="Encumbrance"/>); the rest need
/// the parts of <c>GameRules.cpp</c> that fold in armour, dexterity, spell effects and racial
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
    /// A combat-block number as the sheet writes it — <c>%5i</c>, right-aligned in five characters.
    /// </summary>
    /// <remarks>
    /// The padding <b>is</b> the gap between label and value: the layout places these fields at an
    /// x offset of zero from their label's right edge, so an unpadded number renders as
    /// <c>ARMOR CLASS7</c>.
    /// </remarks>
    private static string Field(int value) => value.ToString().PadLeft(5);

    /// <summary>
    /// Builds the sheet.
    /// </summary>
    /// <param name="baseclasses">
    /// The design's baseclasses, used to word each experience line's level. Null still produces the
    /// experience totals, just without levels derived from thresholds.
    /// </param>
    public static CharacterSheet Build(
        Character character,
        IReadOnlyDictionary<string, BaseclassRecord>? baseclasses = null,
        Func<string, ItemRecord?>? items = null)
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
            Coins: Coins(character),
            ArmorClass: ArmorClassOf(character, items),
            Thac0: Thac0Of(character, baseclasses),
            Encumbrance: Field(record.Encumbrance),
            Movement: Field(Movement(record)));
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
        IReadOnlyDictionary<string, BaseclassRecord>? baseclasses)
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
    private static string[] Abilities(AbilityScores scores)
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

    /// <summary>
    /// <c>NotReady</c> (<c>Items.h:122</c>) — the readied-location sentinel for an item in the pack.
    /// </summary>
    private static readonly uint NotReady = ReadiedLocation.Base38("NOTRDY");

    /// <summary>
    /// The character's armour class: dexterity plus everything it has readied.
    /// </summary>
    /// <remarks>
    /// <b>Only readied items count</b>, and "readied" is a base-38 location that is not
    /// <c>NOTRDY</c> — not a boolean. Blank when the item database cannot be read, since a
    /// character's armour would silently vanish and 10 would look like a real answer.
    /// </remarks>
    private static string ArmorClassOf(Character character,
                                       Func<string, ItemRecord?>? items)
    {
        if (items is null)
        {
            return string.Empty;
        }

        var readied = new List<(int Base, int Bonus)>();
        foreach (var carried in character.Items)
        {
            if (carried.ReadyLocation == NotReady)
            {
                continue;
            }

            if (items(carried.ItemId) is { } record)
            {
                readied.Add((record.Combat.ProtectionBase, record.Combat.ProtectionBonus));
            }
        }

        return Field(ArmorClass.Effective(character.Record.Abilities.Dexterity, readied));
    }

    /// <summary>
    /// The character's attack number, from its baseclasses' own tables.
    /// </summary>
    /// <remarks>
    /// Blank when the design's baseclasses cannot be read, rather than the unskilled 20 — an
    /// unreadable database is not the same as a character who cannot fight, and a plausible wrong
    /// number is worse than an empty field.
    /// </remarks>
    private static string Thac0Of(
        Character character,
        IReadOnlyDictionary<string, BaseclassRecord>? baseclasses)
    {
        if (baseclasses is null)
        {
            return string.Empty;
        }

        var standings = new List<BaseclassStanding>();
        foreach (var progress in character.Baseclasses)
        {
            if (baseclasses.TryGetValue(progress.BaseclassId, out var baseclass))
            {
                standings.Add(new BaseclassStanding(progress.CurrentLevel, progress.PreviousLevel,
                                                    baseclass.Thac0));
            }
        }

        return standings.Count > 0 ? Field(Thac0.ForCharacter(standings)) : string.Empty;
    }

    /// <summary>
    /// The movement rate the character's load allows (<c>determineMaxMovement</c>).
    /// </summary>
    /// <remarks>
    /// <b>The reference divides by the <i>effective</i> encumbrance, which ignores magical items</b>
    /// (<c>determineEffectiveEncumbrance</c>); this uses the stored total, because item records are
    /// not resolved here. The two agree for a character carrying nothing magical, and this one
    /// reports a character as slower than they are otherwise — the safe direction, and recorded
    /// rather than hidden.
    /// </remarks>
    private static int Movement(CharacterRecord record) =>
        Encumbrance.MaxMovementFor(record.Encumbrance,
                                   record.Abilities.Strength, record.Abilities.StrengthMod);

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
