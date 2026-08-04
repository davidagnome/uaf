using UAF.Common;
using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>What rolling a new character produced.</summary>
public sealed record RolledStats(AbilityScores Abilities, int MaxHitPoints,
                                 int Age, int MaxAge, int Birthday);

/// <summary>
/// Rolls everything a new character needs, out of the design's own tables.
/// </summary>
/// <remarks>
/// <para>
/// The rules are each ported and tested on their own — <see cref="AbilityRoll"/>,
/// <see cref="NewCharacterHitPoints"/>, <see cref="NewCharacter.Roll"/>. This is the seam that
/// finally calls them with a real design behind it, and it is the last thing between the
/// generator's screens and a character that is mechanically anything.
/// </para>
/// <para>
/// <b>The order matters, and it is the reference's.</b> Abilities first, because the strength
/// bonus is rolled from the class only when the adjusted score comes out at exactly 18; then hit
/// points, which read the constitution the abilities just set.
/// </para>
/// </remarks>
public static class RolledCharacter
{
    /// <summary>The six abilities the generator names by hand, in record order.</summary>
    public static readonly string[] AbilityNames =
        ["Strength", "Intelligence", "Wisdom", "Dexterity", "Constitution", "Charisma"];

    /// <summary>
    /// What <c>START_AGE</c> is when a design does not set it (<c>GameRules.cpp:52</c>).
    /// </summary>
    /// <remarks>
    /// <b>The clamp to at least 1 only runs when the token is present.</b> A design writing
    /// <c>START_AGE 0</c> gets 1; a design writing nothing gets 17, because the initialiser is
    /// what stands.
    /// </remarks>
    public const int DefaultStartAge = 17;

    /// <summary>
    /// Rolls a character.
    /// </summary>
    /// <param name="roll">Rolls <c>count</c> dice of <c>sides</c> and totals them.</param>
    /// <param name="seed">
    /// The hit-point seed. Holding it fixed while ability scores change is the whole point of the
    /// private generator — see <see cref="LittleRandom"/>.
    /// </param>
    /// <remarks>
    /// <b>Below 0.870 the ability database is not consulted at all</b> and every score is 3d6,
    /// best of three. The version gate is live, so a design old enough gets the old dice whatever
    /// its <c>ability.dat</c> says.
    /// </remarks>
    public static RolledStats Roll(CharacterCreation made, LoadedDesign design,
                                   Func<int, int, int> roll, uint seed,
                                   int startAge = DefaultStartAge)
    {
        ArgumentNullException.ThrowIfNull(made);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(roll);

        bool modern = design.Globals.Version >= DesignVersion.V0870;

        int Score(string name)
        {
            if (!modern || design.Abilities?.GetValueOrDefault(name) is not { } ability)
            {
                return AbilityRoll.Legacy(roll);
            }

            return AbilityRoll.Modern(
                () => NewCharacter.Roll(ability.Roll, roll, Symbol, out _));
        }

        // Male, and Race_<name> for "is the character this race" -- the symbols a dice field
        // actually uses. Anything else is refused by name rather than guessed at.
        int? Symbol(string name)
        {
            if (name == DiceFormula.MaleSymbol)
            {
                return made.Gender == Gender.Male ? 1 : 0;
            }

            if (name.StartsWith(DiceFormula.RacePrefix, StringComparison.Ordinal))
            {
                return string.Equals(name[DiceFormula.RacePrefix.Length..], made.RaceId,
                                     StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;
            }

            return null;
        }

        int strength = Score(AbilityNames[0]);

        var abilities = new AbilityScores(
            Strength: strength,
            StrengthMod: StrengthBonus(made, design, strength, roll),
            Intelligence: Score(AbilityNames[1]),
            Wisdom: Score(AbilityNames[2]),
            Dexterity: Score(AbilityNames[3]),
            Constitution: Score(AbilityNames[4]),
            Charisma: Score(AbilityNames[5]));

        int hitPoints = HitPoints(made, design, seed);
        var (age, maxAge) = Ages(made, design, roll, startAge);

        return new RolledStats(abilities, hitPoints, age, maxAge,
                               NewCharacter.Birthday(roll));
    }

    /// <summary>
    /// The exceptional-strength percentile, rolled from the <b>class</b>'s dice.
    /// </summary>
    /// <remarks>
    /// Only at exactly 18, and only from whatever <c>strengthBonusDice</c> the class carries — the
    /// commented-out predecessor restricted it to fighters, rangers and paladins, so the
    /// restriction is data now and a class with no dice gets nothing.
    /// </remarks>
    private static int StrengthBonus(CharacterCreation made, LoadedDesign design, int strength,
                                     Func<int, int, int> roll)
    {
        if (!AbilityRoll.QualifiesForStrengthBonus(strength))
        {
            return 0;
        }

        var record = design.Classes?.GetValueOrDefault(made.ClassId ?? "");
        return NewCharacter.Roll(record?.StrengthBonusDice, roll,
                                 made.Gender == Gender.Male, out _) ?? 0;
    }

    private static int HitPoints(CharacterCreation made, LoadedDesign design, uint seed)
    {
        var record = design.Classes?.GetValueOrDefault(made.ClassId ?? "");
        if (record is null)
        {
            return 1;
        }

        var rows = new List<(int, int, Func<int, LevelHitDice>)>();

        foreach (string id in record.Baseclasses)
        {
            if (design.Baseclasses?.GetValueOrDefault(id) is not { } baseclass)
            {
                continue;
            }

            // The constitution bonus DetermineHitDiceBonus reads is not ported -- the class's
            // HIT_DICE_LEVEL_BONUS table indexed by an adjusted score. Zero until it is, which
            // makes a tough character short by the bonus and never long.
            rows.Add((1, 0, level => DiceAt(baseclass, level)));
        }

        return NewCharacterHitPoints.Roll(rows, seed);
    }

    private static LevelHitDice DiceAt(BaseclassRecord baseclass, int level)
    {
        if (baseclass.HitDice.Count == 0)
        {
            return new LevelHitDice(0, 0, 0);
        }

        var dice = baseclass.HitDice[Math.Clamp(level, 1, baseclass.HitDice.Count) - 1];
        return new LevelHitDice(dice.Sides, dice.Nbr, dice.Bonus);
    }

    /// <summary>
    /// The character's age and its maximum, from the race's dice.
    /// </summary>
    /// <remarks>
    /// <b>The floor and the ceiling never look at each other</b> — the roll is floored at the
    /// design's minimum starting age and then capped at the race's maximum, so a race whose
    /// maximum is below that minimum produces a character born at its own limit.
    /// </remarks>
    private static (int Age, int MaxAge) Ages(CharacterCreation made, LoadedDesign design,
                                              Func<int, int, int> roll, int startAge)
    {
        if (design.Races?.GetValueOrDefault(made.RaceId ?? "") is not { } race)
        {
            return (0, 0);
        }

        bool male = made.Gender == Gender.Male;
        int maxAge = NewCharacter.Roll(race.MaxAge, roll, male, out _) ?? 0;
        int rolled = NewCharacter.Roll(race.Age, roll, male, out _) ?? 0;

        int age = NewCharacter.StartAge(rolled, startAge);
        return (NewCharacter.CapAge(age, maxAge == 0 ? age : maxAge), maxAge);
    }
}
