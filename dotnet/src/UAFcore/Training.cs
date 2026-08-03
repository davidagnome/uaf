using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>Why a training hall turned a character away.</summary>
public enum TrainingRefusal
{
    /// <summary>Trained.</summary>
    None = 0,

    /// <summary>Not enough experience for the next level in any baseclass.</summary>
    NotReady,

    /// <summary>This hall trains none of the character's baseclasses.</summary>
    NothingHereToTrain,

    /// <summary>The character cannot pay the fee.</summary>
    CannotAfford,
}

/// <summary>
/// What the training rules need from the design — the baseclass table, and the cap on one
/// character's levels in a given baseclass.
/// </summary>
/// <remarks>
/// Two functions rather than the whole <c>LoadedDesign</c>, which needs a design on disk to
/// exist at all. Training is a rule, and a rule that cannot be exercised without loading a game
/// is a rule nobody checks.
/// </remarks>
public sealed record TrainingRules(Func<string, BaseclassRecord?> Baseclass,
                                   Func<BaseclassRecord, int> LevelCap);

/// <summary>One baseclass that gained a level, and where it landed.</summary>
public sealed record LevelGain(string BaseclassId, int FromLevel, int ToLevel, int HitPoints);

/// <summary>What a visit to the training hall did.</summary>
/// <param name="Announcements">
/// One line per baseclass that moved, in the reference's own wording. The screen shows them one
/// at a time, waiting for a keypress between.
/// </param>
public sealed record TrainingOutcome(TrainingRefusal Refusal, IReadOnlyList<LevelGain> Gains,
                                     IReadOnlyList<string> Announcements, int Paid)
{
    public bool Trained => Refusal is TrainingRefusal.None;

    public static TrainingOutcome Refused(TrainingRefusal why) => new(why, [], [], 0);
}

/// <summary>
/// Training a character (<c>CHARACTER::TrainCharacter</c>, <c>Char.cpp:4210</c>, and the payment
/// and announcements around it in <c>MAIN_MENU_DATA</c> case 4, <c>RunEvent.cpp:2044</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A session grants at most one level per baseclass</b> — <c>TrainCharacter</c> passes a
/// <c>maxLevelGain</c> of exactly 1, so a character sitting on four levels' worth of experience
/// must visit four times and pay four times. The entitlement itself is not lost, only deferred.
/// </para>
/// <para>
/// <b>Hit points are rolled, not tabled.</b> The gain is a fresh roll of the baseclass's own
/// per-level dice, so training the same character twice from the same save gives different
/// results — which is why the roller is a parameter here rather than a static call.
/// </para>
/// </remarks>
public static class Training
{
    /// <summary>How many levels one session grants (<c>TrainCharacter</c>'s literal 1).</summary>
    public const int MaxLevelGain = 1;

    /// <summary>
    /// Whether this hall will train this character right now — the rule behind whether the party
    /// menu's TRAIN entry lights up at all (<c>RunEvent.cpp:2569</c>).
    /// </summary>
    /// <remarks>
    /// Three things must hold together: the character is ready to train, they can pool the fee,
    /// and at least one baseclass they are ready to advance is on this hall's list. The reference
    /// checks readiness per baseclass and the fee against <c>poolCharacterGold</c>, so a character
    /// ready in a baseclass the hall does not teach leaves the entry dark.
    /// </remarks>
    public static bool CanTrain(TrainingHallEvent hall, Character who, TrainingRules rules) =>
        Refusal(hall, who, rules) is TrainingRefusal.None;

    /// <summary>The first reason this hall would turn the character away, if any.</summary>
    /// <remarks>
    /// <b>"Not ready" and "wrong hall" are told apart by asking twice</b> — once ignoring the
    /// hall's list, once against it. The reference shows neither reason; it just leaves the entry
    /// dark, which is the same answer for both and is why a player in the wrong hall has nothing
    /// to go on.
    /// </remarks>
    public static TrainingRefusal Refusal(TrainingHallEvent hall, Character who,
                                          TrainingRules rules)
    {
        ArgumentNullException.ThrowIfNull(hall);
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(rules);

        if (!Ready(who, rules).Any())
        {
            return TrainingRefusal.NotReady;
        }

        if (Eligible(hall, who, rules).Count == 0)
        {
            return TrainingRefusal.NothingHereToTrain;
        }

        // enoughMoney against the design's default denomination, which is what payForItem then
        // charges -- and HaveEnough makes change across the other coins, so a character with no
        // gold but plenty of silver can still pay a gold fee.
        return who.Purse.HaveEnough(Fee(who), hall.Cost)
            ? TrainingRefusal.None
            : TrainingRefusal.CannotAfford;
    }

    /// <summary>The denomination a training fee is charged in (<c>GetDefaultType</c>).</summary>
    private static ItemClass Fee(Character who) => who.Purse.Rules.BaseType;

    /// <summary>
    /// The baseclasses this hall will advance for this character: ready to train, and on the
    /// hall's list (<c>LocateTrainableBaseclass</c>, <c>RunEvent.cpp:12112</c>).
    /// </summary>
    /// <remarks>
    /// <b>The hall's per-entry <c>MinLevel</c> and <c>MaxLevel</c> are not consulted.</b>
    /// <c>LocateTrainableBaseclass</c> matches on the baseclass id alone and the caller looks no
    /// further, so a hall advertising "levels 1 to 3" trains a level 9 character just the same.
    /// The fields are read and kept; nothing in the engine reads them back.
    /// </remarks>
    public static List<BaseclassProgress> Eligible(TrainingHallEvent hall, Character who,
                                                   TrainingRules rules)
    {
        ArgumentNullException.ThrowIfNull(hall);

        return [.. Ready(who, rules)
                   .Where(p => hall.Trainable.Any(t => t.BaseclassId == p.BaseclassId))];
    }

    /// <summary>
    /// The character's baseclasses that have earned a level they have not taken, whatever any
    /// hall teaches (<c>CHARACTER::IsReadyToTrain</c>, asked per baseclass).
    /// </summary>
    /// <remarks>
    /// A baseclass the design does not define is skipped — the reference logs "unknown baseclass"
    /// and moves on rather than treating it as untrainable, which is the same outcome by a
    /// noisier route.
    /// </remarks>
    public static IEnumerable<BaseclassProgress> Ready(Character who, TrainingRules rules)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var progress in who.Baseclasses)
        {
            if (rules.Baseclass(progress.BaseclassId) is not { } baseclass)
            {
                continue;
            }

            if (Levelling.IsReadyToTrain(baseclass.ExperienceLevels, (uint)progress.Experience,
                                         progress.CurrentLevel, progress.PreviousLevel,
                                         rules.LevelCap(baseclass)))
            {
                yield return progress;
            }
        }
    }

    /// <summary>
    /// Trains the character, taking the fee and raising what it can.
    /// </summary>
    /// <param name="roll">
    /// Rolls <c>count</c> dice of <c>sides</c> and returns the total. Injected because training is
    /// random and a test that cannot fix the dice cannot assert a hit-point total.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The fee is taken before anything is checked to have worked</b>, exactly as the reference
    /// does — <c>payForItem</c> runs, then <c>TrainCharacter</c>. Since the entry is only reachable
    /// when <see cref="CanTrain"/> holds, the two cannot disagree in practice; the order is kept
    /// so that they cannot drift apart if one day it can.
    /// </para>
    /// <para>
    /// <b>Not ported:</b> the thief-skill and spell-ability recalculations that follow
    /// (<c>SetThiefSkills</c>, <c>UpdateSpellAbility</c>), and the initial magic-user spell pick —
    /// which the reference itself disables with a hard <c>PickSpells = FALSE</c> two lines after
    /// computing it.
    /// </para>
    /// </remarks>
    public static TrainingOutcome Train(TrainingHallEvent hall, Character who, TrainingRules rules,
                                        Func<int, int, int> roll)
    {
        ArgumentNullException.ThrowIfNull(hall);
        ArgumentNullException.ThrowIfNull(who);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(roll);

        var refusal = Refusal(hall, who, rules);
        if (refusal is not TrainingRefusal.None)
        {
            return TrainingOutcome.Refused(refusal);
        }

        var eligible = Eligible(hall, who, rules);
        who.Purse.Subtract(Fee(who), hall.Cost);

        var gains = new List<LevelGain>();
        var lines = new List<string>();

        foreach (var progress in eligible)
        {
            if (rules.Baseclass(progress.BaseclassId) is not { } baseclass)
            {
                continue;
            }

            int from = progress.CurrentLevel;
            int to = Levelling.Train(baseclass.ExperienceLevels, (uint)progress.Experience,
                                     from, progress.PreviousLevel, MaxLevelGain,
                                     rules.LevelCap(baseclass));

            if (to <= from)
            {
                continue;
            }

            progress.CurrentLevel = to;

            int hitPoints = HitPointsFor(baseclass, from, to, roll);
            who.MaxHitPoints += hitPoints;

            gains.Add(new LevelGain(progress.BaseclassId, from, to, hitPoints));
            lines.Add($"{who.Name} IS NOW A {to} LEVEL {progress.BaseclassId}");
        }

        // Training heals: the reference sets hitPoints to the new maximum outright.
        who.HitPoints = who.MaxHitPoints;

        return new TrainingOutcome(TrainingRefusal.None, gains, lines, hall.Cost);
    }

    /// <summary>
    /// The hit points a baseclass gains crossing from one level to another
    /// (<c>DetermineCharMaxHitPoints</c>'s live path, <c>Char.cpp:5100</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One roll per level crossed, from the baseclass's own per-level table</b> — not one roll
    /// for the jump. A character gaining two levels at once rolls twice, at each level's own dice,
    /// which matters because the table need not be uniform.
    /// </para>
    /// <para>
    /// <b>Not ported: the constitution bonus.</b> <c>DetermineHitDiceBonus</c> reads the class
    /// record's <c>HIT_DICE_LEVEL_BONUS</c> table, indexes it by the named ability's adjusted
    /// score, and adds that to every roll. The table is read, the adjusted ability scores are not
    /// yet — so this rolls the dice and adds the table's own constant, and a high-constitution
    /// fighter is currently short by the bonus.
    /// </para>
    /// </remarks>
    public static int HitPointsFor(BaseclassRecord baseclass, int fromLevel, int toLevel,
                                   Func<int, int, int> roll)
    {
        ArgumentNullException.ThrowIfNull(baseclass);
        ArgumentNullException.ThrowIfNull(roll);

        int total = 0;
        for (int level = fromLevel + 1; level <= toLevel; level++)
        {
            var dice = DiceForLevel(baseclass, level);
            if (dice is null)
            {
                continue;
            }

            total += roll(dice.Nbr, dice.Sides) + dice.Bonus;
        }
        return total;
    }

    /// <summary>
    /// The dice for one level (<c>GetBaseclassHitDice</c>, <c>GameRules.cpp:1167</c>), clamped
    /// into the table at both ends as the reference clamps it.
    /// </summary>
    private static HitDice? DiceForLevel(BaseclassRecord baseclass, int level)
    {
        if (baseclass.HitDice.Count == 0)
        {
            return null;
        }

        int index = Math.Clamp(level, 1, baseclass.HitDice.Count) - 1;
        return baseclass.HitDice[index];
    }
}
