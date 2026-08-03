using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers training a character: who the hall will teach, what a session costs, and what a level
/// is worth.
/// </summary>
/// <remarks>
/// The rules take a <see cref="TrainingRules"/> rather than a loaded design precisely so these
/// can exist — a rule that needs a game on disk to run is a rule nobody checks.
/// </remarks>
public class TrainingTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    // ---- fixtures ------------------------------------------------------------------------------

    /// <summary>Levels 1..5 at 0, 2000, 4000, 8000, 16000 experience.</summary>
    private static readonly uint[] Thresholds = [0, 2000, 4000, 8000, 16000];

    /// <summary>A baseclass whose every level rolls 1d8+1.</summary>
    private static BaseclassRecord Baseclass(string name, int sides = 8, int nbr = 1,
                                             int bonus = 1) =>
        new(name, 0, name, [], [], Thresholds, 0, [], "", [], [],
            new SpecabBlock([], [], []),
            [.. Enumerable.Repeat(new HitDice(sides, nbr, bonus), Thresholds.Length)],
            [], [], [], [], [], []);

    private static readonly Dictionary<string, BaseclassRecord> Known = new()
    {
        ["fighter"] = Baseclass("fighter"),
        ["thief"] = Baseclass("thief", sides: 6, bonus: 0),
    };

    private static TrainingRules Rules(int levelCap = Levelling.NoLevelCap) =>
        new(id => Known.GetValueOrDefault(id), _ => levelCap);

    private static Character Member(int experience = 4000, int level = 1, int previousLevel = 0,
                                    int gold = 1000, string[]? baseclasses = null,
                                    int maxHitPoints = 10)
    {
        var stats = (baseclasses ?? ["fighter"])
            // Experience is the LAST field, with PreDrainLevel fourth -- getting those the wrong
            // way round leaves every character on zero experience and every test saying "not ready".
            .Select(b => new BaseclassStats(b, level, previousLevel, 0, experience))
            .ToList();

        var record = new CharacterRecord(
            0, 0, 0, "human", 0, "fighter", 0, 0, 0, "", 0, "Aramil", "",
            0, 0, 0, 0, 0, maxHitPoints, maxHitPoints, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, stats, [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

        var who = new Character(record, MoneyRules.Default);
        who.Purse.Add(MoneyRules.Default.BaseType, gold);
        return who;
    }

    private static TrainingHallEvent Hall(int cost = 100, params string[] teaches) =>
        new(new GameEventBase(
                new EventControl(0, 0, 0, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0),
                NoPic, NoPic, (int)EventType.TrainingHallEvent, 1, 0, 0, 0, 0, "", "", "", []),
            ForceExit: 0,
            Trainable: [.. (teaches.Length == 0 ? ["fighter"] : teaches)
                          .Select(b => new TrainableBaseclass(b, 1, 20, ""))],
            Cost: cost);

    /// <summary>A roller that always shows the top face, so a total is a fact rather than a range.</summary>
    private static int Max(int count, int sides) => count * sides;

    // ---- who the hall will teach ---------------------------------------------------------------

    [Fact]
    public void A_character_with_the_experience_can_train()
    {
        Assert.Equal(TrainingRefusal.None, Training.Refusal(Hall(), Member(), Rules()));
    }

    [Fact]
    public void A_character_short_of_the_next_level_is_not_ready()
    {
        Assert.Equal(TrainingRefusal.NotReady,
                     Training.Refusal(Hall(), Member(experience: 1999), Rules()));
    }

    [Fact]
    public void A_hall_that_does_not_teach_the_class_turns_them_away()
    {
        // The player's only clue is a dark menu entry -- the reference shows no reason at all,
        // and shows the same nothing for this as for having too little experience.
        Assert.Equal(TrainingRefusal.NothingHereToTrain,
                     Training.Refusal(Hall(teaches: "thief"), Member(), Rules()));
    }

    [Fact]
    public void The_fee_has_to_be_payable()
    {
        Assert.Equal(TrainingRefusal.CannotAfford,
                     Training.Refusal(Hall(cost: 5000), Member(gold: 100), Rules()));
    }

    [Fact]
    public void A_level_cap_stops_training_even_with_the_experience()
    {
        // Cap of 1: the character has the experience for level 2 and is refused anyway.
        Assert.Equal(TrainingRefusal.NotReady,
                     Training.Refusal(Hall(), Member(), Rules(levelCap: 1)));
    }

    [Fact]
    public void A_baseclass_the_design_does_not_define_is_skipped()
    {
        Assert.Equal(TrainingRefusal.NotReady,
                     Training.Refusal(Hall(teaches: "bard"),
                                      Member(baseclasses: ["bard"]), Rules()));
    }

    [Fact]
    public void The_halls_advertised_level_range_is_not_consulted()
    {
        // A hall listing levels 1 to 3 trains a level 9 character just the same:
        // LocateTrainableBaseclass matches on the id alone and nothing looks at the bounds.
        var hall = new TrainingHallEvent(Hall().Base, 0,
                                         [new TrainableBaseclass("fighter", 1, 3, "")], 100);

        Assert.Equal(TrainingRefusal.None,
                     Training.Refusal(hall, Member(experience: 16000, level: 4), Rules()));
    }

    // ---- what a session does -------------------------------------------------------------------

    [Fact]
    public void Training_raises_the_level_and_takes_the_fee()
    {
        var who = Member(gold: 1000);

        var outcome = Training.Train(Hall(cost: 100), who, Rules(), Max);

        Assert.True(outcome.Trained);
        Assert.Equal(2, who.Baseclasses[0].CurrentLevel);
        Assert.Equal(900, who.Purse[MoneyRules.Default.BaseType]);
        Assert.Equal(100, outcome.Paid);
    }

    [Fact]
    public void One_session_grants_one_level_however_much_experience_is_banked()
    {
        // 16000 is level 5's threshold, and the character arrives at 2. The entitlement is not
        // lost -- it takes three more visits, and three more fees.
        var who = Member(experience: 16000);

        Training.Train(Hall(), who, Rules(), Max);

        Assert.Equal(2, who.Baseclasses[0].CurrentLevel);
        Assert.Equal(TrainingRefusal.None, Training.Refusal(Hall(), who, Rules()));
    }

    [Fact]
    public void A_level_is_worth_a_roll_of_the_baseclasss_own_dice()
    {
        // 1d8+1 at the top face: nine hit points, added to the ten already there.
        var who = Member(maxHitPoints: 10);

        var outcome = Training.Train(Hall(), who, Rules(), Max);

        Assert.Equal(9, outcome.Gains[0].HitPoints);
        Assert.Equal(19, who.MaxHitPoints);
    }

    [Fact]
    public void Training_heals_to_the_new_maximum()
    {
        var who = Member(maxHitPoints: 10);
        who.HitPoints = 3;

        Training.Train(Hall(), who, Rules(), Max);

        Assert.Equal(who.MaxHitPoints, who.HitPoints);
    }

    [Fact]
    public void Each_baseclass_rolls_its_own_dice()
    {
        // A fighter/thief trains both at once, at 1d8+1 and 1d6 -- nine and six, not two of one.
        var who = Member(baseclasses: ["fighter", "thief"]);

        var outcome = Training.Train(Hall(100, "fighter", "thief"), who, Rules(), Max);

        Assert.Equal([9, 6], outcome.Gains.Select(g => g.HitPoints));
        Assert.Equal(25, who.MaxHitPoints);
    }

    [Fact]
    public void Only_the_baseclasses_this_hall_teaches_advance()
    {
        var who = Member(baseclasses: ["fighter", "thief"]);

        Training.Train(Hall(100, "thief"), who, Rules(), Max);

        Assert.Equal(1, who.Baseclasses[0].CurrentLevel);   // fighter, untouched
        Assert.Equal(2, who.Baseclasses[1].CurrentLevel);   // thief
    }

    [Fact]
    public void The_announcement_names_the_character_the_level_and_the_baseclass()
    {
        var outcome = Training.Train(Hall(), Member(), Rules(), Max);

        Assert.Equal(["Aramil IS NOW A 2 LEVEL fighter"], outcome.Announcements);
    }

    [Fact]
    public void A_refused_session_costs_nothing_and_changes_nothing()
    {
        var who = Member(experience: 0, gold: 1000);

        var outcome = Training.Train(Hall(), who, Rules(), Max);

        Assert.False(outcome.Trained);
        Assert.Equal(TrainingRefusal.NotReady, outcome.Refusal);
        Assert.Equal(1000, who.Purse[MoneyRules.Default.BaseType]);
        Assert.Equal(1, who.Baseclasses[0].CurrentLevel);
        Assert.Equal(10, who.MaxHitPoints);
    }

    // ---- the dice ------------------------------------------------------------------------------

    [Fact]
    public void Two_levels_at_once_roll_twice()
    {
        // Not reachable through a hall, which grants one level a visit -- but the hit-point rule
        // is the general one, and rolling once for a two-level jump would be a different game.
        Assert.Equal(18, Training.HitPointsFor(Known["fighter"], 1, 3, Max));
    }

    [Fact]
    public void A_level_past_the_table_uses_the_last_row()
    {
        // GetBaseclassHitDice clamps into the table at both ends rather than reading off it.
        Assert.Equal(9, Training.HitPointsFor(Known["fighter"], 40, 41, Max));
    }

    [Fact]
    public void A_baseclass_with_no_hit_dice_grants_none()
    {
        var empty = Baseclass("ghost") with { HitDice = [] };

        Assert.Equal(0, Training.HitPointsFor(empty, 1, 2, Max));
    }
}
