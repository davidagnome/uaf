using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// One party member attempting an ability check (<c>WHO_TRIES_EVENT_DATA</c>).
/// </summary>
/// <remarks>
/// No shipped design contains one of these, so every case here is transcribed from
/// <c>RunEvent.cpp</c> rather than observed. The cases that matter most are the ones proving how
/// little of the check survives the editor: a modern design cannot set <c>compareToDie</c>,
/// <c>compareDie</c> or any thief skill, which leaves the strength percentile as the only way to
/// fail a check and makes <c>NbrTries</c> unreachable.
/// </remarks>
public class EventWhoTriesTests
{
    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static readonly TransferData Nowhere = new(0, 0, 0, 0, 0, 0);

    /// <summary>A destination distinguishable from <see cref="Nowhere"/> in an assertion.</summary>
    private static readonly TransferData Somewhere = new(1, 0, 3, 11, 12, 2);

    /// <summary>The six ability flags in wire order, with the named ones ticked.</summary>
    private static int[] Ticked(params Ability[] abilities)
    {
        var flags = new int[6];
        foreach (var ability in abilities)
        {
            flags[(int)ability] = 1;
        }

        return flags;
    }

    private static WhoTriesEvent Trial(
        int alwaysSucceeds = 0, int alwaysFails = 0,
        int[]? abilities = null, byte strengthBonus = 0,
        int compareToDie = 0, int compareDie = 0, int nbrTries = 1,
        TrialAction successAction = TrialAction.NoAction,
        TrialAction failAction = TrialAction.NoAction,
        uint successChain = 0, uint failChain = 0,
        TransferData? successTransfer = null, TransferData? failTransfer = null,
        int[]? thiefSkills = null) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.WhoTries, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            alwaysSucceeds, alwaysFails, abilities ?? new int[6], thiefSkills ?? new int[8],
            strengthBonus, compareToDie, compareDie, nbrTries,
            successChain, (int)successAction, (int)failAction, failChain,
            successTransfer ?? Nowhere, failTransfer ?? Nowhere);

    private static Character Hero(int strength = 10, int strengthMod = 0, int intelligence = 10,
                                  int wisdom = 10, int dexterity = 10, int constitution = 10,
                                  int charisma = 10)
    {
        var record = new CharacterRecord(
            CharacterVersion: 0, PreSpellNamesKey: 0, Type: 0, Race: "human", Gender: 0, ClassId: "fighter",
            Alignment: 0, AllowInCombat: 0, Status: 0, UndeadType: "", CreatureSize: 0,
            Name: "hero", CharacterId: "hero-1",
            Thac0: 18, Morale: 50, Encumbrance: 0, MaxEncumbrance: 0, ArmorClass: 5,
            HitPoints: 10, MaxHitPoints: 10, NumberOfHitDice: 1,
            Age: 0, MaxAge: 0, Birthday: 0, MaxCureDisease: 0,
            UnarmedDieSmall: 0, UnarmedNumberDieSmall: 0, UnarmedBonus: 0,
            UnarmedDieLarge: 0, UnarmedNumberDieLarge: 0,
            MaxMovement: 12, ReadyToTrain: 0, CanTradeItems: 0,
            Abilities: new AbilityScores(strength, strengthMod, intelligence, wisdom,
                                         dexterity, constitution, charisma),
            OpenDoors: 0, OpenMagicDoors: 0, BendBarsLiftGates: 0,
            HitBonus: 0, DamageBonus: 0, MagicResistance: 0,
            BaseclassStats: [new BaseclassStats("fighter", 1, 1, 1, 0)],
            SkillAdjustments: [], SpellAdjustments: [],
            IsPreGenerated: 0, CanBeSaved: 0, HasLayedOnHandsToday: 0,
            Money: null, NumberOfAttacks: 1,
            Icon: null, IconIndex: 0, OriginalIndex: 0, UniquePartyId: 0,
            DisableTalkIfDead: 0, TalkEvent: 0, TalkLabel: "",
            ExamineEvent: 0, ExamineLabel: "",
            SpellBook: new UAF.Serialization.SpellBook(0, []),
            DetectingInvisible: 0, DetectingTraps: 0,
            SpellEffects: [], Blockages: [], SmallPic: null!,
            Items: new ItemList([], new ReadyItems([])),
            SpecialAbilities: new SpecabBlock([], [], []), Attributes: []);

        return new Character(record, MoneyRules.Default);
    }

    private static ActiveSpellEffect Effect(string attribute, double change) =>
        new(new UAF.Rules.SpellEffect(attribute, change), StopTime: null);

    /// <summary>A die that always rolls its maximum, so a roll is pinned rather than random.</summary>
    private static readonly Func<int, int> Always = sides => sides;

    /// <summary>A die that always rolls 1.</summary>
    private static readonly Func<int, int> Lowest = _ => 1;

    private static WhoTriesAttempt Attempt(WhoTriesEvent trial, Character? who = null,
                                           Func<int, int>? dice = null, int tries = 0) =>
        EventWhoTries.Attempt(trial, who ?? Hero(), dice ?? Always, tries);

    private static WhoTriesOutcome Resolve(WhoTriesEvent trial, bool succeeded,
                                           Func<uint, bool>? valid = null) =>
        EventWhoTries.Resolve(trial, succeeded, valid ?? (_ => true));

    // ---- the two blanket flags -------------------------------------------------------------------

    [Fact]
    public void Always_succeeds_passes_without_looking_at_the_character()
    {
        // A character who would fail every check still passes: the else branch is never entered.
        var outcome = Attempt(Trial(alwaysSucceeds: 1, abilities: Ticked(Ability.Strength),
                                    strengthBonus: 100),
                              Hero(strength: 3, strengthMod: 0));

        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public void Always_fails_beats_always_succeeds_when_both_are_set()
    {
        // alwaysFails is tested first and alwaysSucceeds only lives in its else. Nothing stops an
        // editor writing both.
        var outcome = Attempt(Trial(alwaysSucceeds: 1, alwaysFails: 1));

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public void Always_fails_jumps_the_try_count_to_the_last_one()
    {
        // currTry = NbrTries, then the shared increment -- which is what stops an always-failing
        // event with a die comparison from demanding NbrTries presses to say so.
        var outcome = Attempt(Trial(alwaysFails: 1, nbrTries: 4, compareToDie: 1, compareDie: 20));

        Assert.Equal(5, outcome.Tries);
        Assert.False(outcome.Retry);
    }

    [Fact]
    public void An_event_with_nothing_configured_is_a_free_pass()
    {
        // `failed` starts false and only a check can set it, so a blank WhoTries always succeeds.
        var outcome = Attempt(Trial());

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, outcome.Tries);
    }

    // ---- the target number -----------------------------------------------------------------------

    [Fact]
    public void Without_a_die_the_compare_value_is_the_target_outright()
    {
        Assert.Equal(15, EventWhoTries.TargetNumber(Trial(compareDie: 15), Always));
    }

    [Fact]
    public void A_negative_compare_value_floors_at_zero()
    {
        Assert.Equal(0, EventWhoTries.TargetNumber(Trial(compareDie: -4), Always));
    }

    [Fact]
    public void With_a_die_the_target_is_rolled()
    {
        Assert.Equal(20, EventWhoTries.TargetNumber(Trial(compareToDie: 1, compareDie: 20), Always));
        Assert.Equal(1, EventWhoTries.TargetNumber(Trial(compareToDie: 1, compareDie: 20), Lowest));
    }

    [Fact]
    public void A_die_with_no_sides_rolls_nothing()
    {
        // RollDice returns the bonus -- here zero -- before rolling when sides are not positive.
        Assert.Equal(0, EventWhoTries.TargetNumber(Trial(compareToDie: 1, compareDie: 0), Always));
    }

    // ---- the ability checks ----------------------------------------------------------------------

    [Theory]
    [InlineData(Ability.Intelligence)]
    [InlineData(Ability.Wisdom)]
    [InlineData(Ability.Dexterity)]
    [InlineData(Ability.Constitution)]
    [InlineData(Ability.Charisma)]
    public void A_ticked_ability_below_the_target_fails(Ability ability)
    {
        // Reachable only through the old CArchive serializer: a modern design cannot set compareDie.
        var trial = Trial(abilities: Ticked(ability), compareDie: 12);
        var weakling = Hero(intelligence: 8, wisdom: 8, dexterity: 8, constitution: 8, charisma: 8);

        Assert.False(Attempt(trial, weakling).Succeeded);
        Assert.True(Attempt(trial, Hero(intelligence: 12, wisdom: 12, dexterity: 12,
                                        constitution: 12, charisma: 12)).Succeeded);
    }

    [Fact]
    public void Every_ticked_ability_has_to_pass()
    {
        // The five after strength are independent ifs that can only set the flag, so they are ANDed.
        var trial = Trial(abilities: Ticked(Ability.Wisdom, Ability.Charisma), compareDie: 12);

        Assert.False(Attempt(trial, Hero(wisdom: 18, charisma: 3)).Succeeded);
        Assert.True(Attempt(trial, Hero(wisdom: 18, charisma: 12)).Succeeded);
    }

    [Fact]
    public void An_unticked_ability_is_not_looked_at()
    {
        var trial = Trial(abilities: Ticked(Ability.Wisdom), compareDie: 12);

        Assert.True(Attempt(trial, Hero(wisdom: 12, charisma: 3)).Succeeded);
    }

    [Fact]
    public void The_comparison_is_a_floor_not_a_strict_one()
    {
        // score < target fails, so a score exactly on the target passes.
        var trial = Trial(abilities: Ticked(Ability.Dexterity), compareDie: 12);

        Assert.True(Attempt(trial, Hero(dexterity: 12)).Succeeded);
        Assert.False(Attempt(trial, Hero(dexterity: 11)).Succeeded);
    }

    [Fact]
    public void Spell_effects_move_the_score_the_check_sees()
    {
        var trial = Trial(abilities: Ticked(Ability.Dexterity), compareDie: 12);
        var hero = Hero(dexterity: 10);
        hero.Effects.Add(Effect("$CHAR_DEX", 3));

        Assert.Equal(13, EventWhoTries.Adjusted(hero, Ability.Dexterity));
        Assert.True(Attempt(trial, hero).Succeeded);
    }

    // ---- strength, which is the only one with two ways to fail -----------------------------------

    [Fact]
    public void Strength_checks_the_percentile_as_well_as_the_score()
    {
        // The one clause of the whole check a modern design can still reach.
        var trial = Trial(abilities: Ticked(Ability.Strength), strengthBonus: 91);

        Assert.False(Attempt(trial, Hero(strength: 18, strengthMod: 50)).Succeeded);
        Assert.True(Attempt(trial, Hero(strength: 18, strengthMod: 91)).Succeeded);
    }

    [Fact]
    public void A_strength_bonus_of_zero_is_no_requirement_at_all()
    {
        // Every character's percentile is at least zero, so an unset strBonus never fails.
        var trial = Trial(abilities: Ticked(Ability.Strength));

        Assert.True(Attempt(trial, Hero(strength: 3, strengthMod: 0)).Succeeded);
    }

    [Fact]
    public void A_strength_effect_can_carry_the_percentile_over_the_bar()
    {
        var trial = Trial(abilities: Ticked(Ability.Strength), strengthBonus: 91);
        var hero = Hero(strength: 18, strengthMod: 50);
        hero.Effects.Add(Effect("$CHAR_STRMOD", 45));

        Assert.Equal(95, EventWhoTries.AdjustedStrengthMod(hero));
        Assert.True(Attempt(trial, hero).Succeeded);
    }

    [Fact]
    public void Failing_the_strength_score_skips_the_percentile()
    {
        // The percentile clause is an else if. Both reads are pure so nothing observable turns on
        // it -- this pins the nesting rather than an effect of it.
        var trial = Trial(abilities: Ticked(Ability.Strength), compareDie: 12, strengthBonus: 91);

        Assert.False(Attempt(trial, Hero(strength: 9, strengthMod: 100)).Succeeded);
    }

    // ---- what a modern design can actually express -----------------------------------------------

    [Fact]
    public void With_the_editors_flattened_fields_the_score_comparisons_cannot_fail()
    {
        // compareToDie is written FALSE and compareDie 0, so the target is 0 and `score < 0` is
        // false for every undrained character however feeble.
        var trial = Trial(abilities: Ticked(Ability.Strength, Ability.Intelligence, Ability.Wisdom,
                                            Ability.Dexterity, Ability.Constitution,
                                            Ability.Charisma));

        Assert.True(Attempt(trial, Hero(strength: 1, intelligence: 1, wisdom: 1, dexterity: 1,
                                        constitution: 1, charisma: 1)).Succeeded);
    }

    [Fact]
    public void A_drained_score_is_the_one_way_past_that()
    {
        // GetAdjStr is unclamped -- it is GetLimitedStr that bounds the result, and the check does
        // not call it. So an effect really can push a score below the zero target.
        var trial = Trial(abilities: Ticked(Ability.Constitution));
        var hero = Hero(constitution: 4);
        hero.Effects.Add(Effect("$CHAR_CON", -9));

        Assert.Equal(-5, EventWhoTries.Adjusted(hero, Ability.Constitution));
        Assert.False(Attempt(trial, hero).Succeeded);
    }

    [Fact]
    public void The_thief_skill_flags_are_ignored_however_they_are_set()
    {
        // Dead twice over: the runtime block is commented out, and the editor writes a literal
        // FALSE for all eight. A design old enough to carry them set still gets nothing.
        var trial = Trial(thiefSkills: [1, 1, 1, 1, 1, 1, 1, 1], compareDie: 99);

        Assert.True(Attempt(trial).Succeeded);
    }

    // ---- retries -------------------------------------------------------------------------------

    [Fact]
    public void A_failure_is_final_when_there_is_no_die_to_re_roll()
    {
        // (currTry >= NbrTries) || (!compareToDie). The second disjunct is always true in a modern
        // design, so NbrTries is unreachable there however large it is.
        var trial = Trial(abilities: Ticked(Ability.Wisdom), compareDie: 12, nbrTries: 5);

        var outcome = Attempt(trial, Hero(wisdom: 3));

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Retry);
        Assert.Equal(1, outcome.Tries);
    }

    [Fact]
    public void A_die_comparison_with_tries_left_asks_again()
    {
        var trial = Trial(abilities: Ticked(Ability.Wisdom), compareToDie: 1, compareDie: 20,
                          nbrTries: 3);

        var first = Attempt(trial, Hero(wisdom: 3), Always);
        Assert.False(first.Succeeded);
        Assert.True(first.Retry);
        Assert.Equal(1, first.Tries);

        var second = Attempt(trial, Hero(wisdom: 3), Always, first.Tries);
        Assert.True(second.Retry);

        var third = Attempt(trial, Hero(wisdom: 3), Always, second.Tries);
        Assert.Equal(3, third.Tries);
        Assert.False(third.Retry);          // currTry >= NbrTries
    }

    [Fact]
    public void A_success_is_never_a_retry()
    {
        var trial = Trial(compareToDie: 1, compareDie: 20, nbrTries: 9);

        Assert.False(Attempt(trial).Retry);
    }

    [Fact]
    public void A_fresh_roll_each_try_is_what_the_retries_are_for()
    {
        // The same character, the same event: only the die changes the answer.
        var trial = Trial(abilities: Ticked(Ability.Wisdom), compareToDie: 1, compareDie: 20,
                          nbrTries: 3);
        var hero = Hero(wisdom: 10);

        Assert.False(Attempt(trial, hero, Always).Succeeded);   // target 20
        Assert.True(Attempt(trial, hero, Lowest).Succeeded);    // target 1
    }

    // ---- branching -------------------------------------------------------------------------------

    [Fact]
    public void No_action_follows_the_ordinary_chain()
    {
        var outcome = Resolve(Trial(successAction: TrialAction.NoAction), succeeded: true);

        Assert.True(outcome.Chains);
        Assert.Null(outcome.GoTo);
        Assert.Null(outcome.Destination);
    }

    [Fact]
    public void Each_branch_reads_its_own_action_and_chain()
    {
        var trial = Trial(successAction: TrialAction.ChainEvent, successChain: 50,
                          failAction: TrialAction.ChainEvent, failChain: 60);

        Assert.Equal(50u, Resolve(trial, succeeded: true).GoTo);
        Assert.Equal(60u, Resolve(trial, succeeded: false).GoTo);
    }

    [Fact]
    public void An_unreachable_chain_falls_back_on_the_ordinary_one()
    {
        // The asymmetry with QUEST_EVENT_DATA, which pushes a do-nothing event and ends the run
        // here. WhoTries goes through ChainOrQuit, which calls ChainHappened instead.
        var outcome = Resolve(Trial(successAction: TrialAction.ChainEvent, successChain: 404),
                              succeeded: true, valid: _ => false);

        Assert.True(outcome.Chains);
        Assert.Null(outcome.GoTo);
    }

    [Fact]
    public void A_chain_of_zero_does_the_same()
    {
        var outcome = Resolve(Trial(failAction: TrialAction.ChainEvent, failChain: 0),
                              succeeded: false);

        Assert.True(outcome.Chains);
        Assert.Null(outcome.GoTo);
    }

    [Fact]
    public void A_teleport_carries_its_branchs_destination_and_does_not_chain()
    {
        // HandleTransfer ends in PopEvent, so nothing follows the transfer.
        var trial = Trial(successAction: TrialAction.Teleport, successTransfer: Somewhere,
                          failAction: TrialAction.Teleport, failTransfer: Nowhere);

        var outcome = Resolve(trial, succeeded: true);

        Assert.Equal(Somewhere, outcome.Destination);
        Assert.False(outcome.Chains);
        Assert.Null(outcome.GoTo);

        Assert.Equal(Nowhere, Resolve(trial, succeeded: false).Destination);
    }

    [Fact]
    public void Backing_up_a_step_chains_as_well()
    {
        // TASKMSG_MovePartyBackward and then ChainHappened -- NoAction with a step attached.
        var outcome = Resolve(Trial(failAction: TrialAction.BackupOneStep), succeeded: false);

        Assert.Equal(TrialAction.BackupOneStep, outcome.Action);
        Assert.True(outcome.Chains);
        Assert.Null(outcome.GoTo);
    }

    [Fact]
    public void An_action_outside_the_four_leaves_the_event_where_it_is()
    {
        // Neither reference switch has a default arm, so nothing happens at all: the event is not
        // replaced and not popped. Reproduced rather than repaired.
        var trial = Trial(successAction: (TrialAction)9, successChain: 50,
                          successTransfer: Somewhere);

        var outcome = Resolve(trial, succeeded: true);

        Assert.False(outcome.Chains);
        Assert.Null(outcome.GoTo);
        Assert.Null(outcome.Destination);
    }

    [Fact]
    public void The_outcome_reports_which_branch_it_is()
    {
        Assert.True(Resolve(Trial(), succeeded: true).Succeeded);
        Assert.False(Resolve(Trial(), succeeded: false).Succeeded);
    }
}
