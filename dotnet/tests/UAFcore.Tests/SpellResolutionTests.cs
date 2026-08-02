using UAF.Common;
using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers what a spell does to one target.</summary>
public class SpellResolutionTests
{
    private static DicePlus Expression(string text) =>
        new("DP2", text, string.Empty, 0, 0, 0, 0, 0, 1, []);

    /// <summary>One effect aimed at the target, as nearly every real spell effect is.</summary>
    private static UAF.Serialization.SpellEffect Effect(
        string attribute, string change,
        SpellEffectFlags flags = SpellEffectFlags.Target) =>
        new(attribute, (uint)flags, 0, string.Empty, 0, 0, [], 0, 0, Expression(change));

    private static SpellRecord Spell(
        string name = "sleep",
        SaveResult save = SaveResult.NoSave,
        SpellDurationRate duration = SpellDurationRate.Permanent,
        string durationText = "3",
        int cumulative = 0,
        params UAF.Serialization.SpellEffect[] effects) =>
        new(0, name, string.Empty, string.Empty, [],
            Level: 1, CastingTime: 0, CastingTimeType: 0,
            CanTargetFriend: 0, CanTargetEnemy: 1, IsCumulative: cumulative, Restrictions: 0,
            CanBeDispelled: 1, CanMemorize: 1, AllowScribe: 0, AutoScribe: 0,
            Lingers: 0, LingerOnceOnly: 0,
            SaveVersus: (int)SaveVersus.Spell, SaveResult: (int)save, Targeting: 1,
            DurationRate: (int)duration, CastCost: 0, CastPriority: 0,
            Parameters: [], Effects: effects, CastArt: null, Art: [], Sounds: [],
            CastMessage: string.Empty, Scripts: [], EffectDuration: Expression(durationText),
            SpecialAbilities: null!, Attributes: []);

    private static Combatant Dude(int index = 0, bool friendly = true) =>
        new(index, friendly, new CombatantIcon(1, 1), $"c{index}");

    /// <summary>A roller with a fixed face, so a resolution is deterministic.</summary>
    private static Func<int, int> Face(int face) => _ => face;

    // ---- the happy path ------------------------------------------------------------------------

    [Fact]
    public void An_effect_aimed_at_the_target_lands_on_it()
    {
        var target = Dude(1, friendly: false);
        var spell = Spell(effects: Effect("$CHAR_HITPOINTS", "-2d8"));

        var hit = SpellResolution.Invoke(Dude(), target, spell, Face(3));

        Assert.Equal(SpellOutcome.Applied, hit.Outcome);
        Assert.Equal(1, hit.Effects);
        Assert.Equal(-6, target.Effects.Effects[0].Effect.Change);
        Assert.Equal("$CHAR_HITPOINTS", target.Effects.Effects[0].Attribute);
    }

    [Fact]
    public void The_effect_records_which_spell_cast_it()
    {
        var target = Dude(1);
        var spell = Spell("bless", effects: Effect("$CHAR_AC", "-1"));

        SpellResolution.Invoke(Dude(), target, spell, Face(3));

        Assert.Equal("bless", target.Effects.Effects[0].SourceSpell);
    }

    [Fact]
    public void Effects_not_aimed_at_the_target_are_skipped()
    {
        // Only EFFECT_TARGET applies to the target; the rest describe the caster or the map.
        var target = Dude(1);
        var spell = Spell(effects: [Effect("$CHAR_AC", "-1", SpellEffectFlags.Targeter)]);

        var hit = SpellResolution.Invoke(Dude(), target, spell, Face(3));

        Assert.Equal(SpellOutcome.NoEffect, hit.Outcome);
        Assert.Equal(0, target.Effects.Count);
    }

    [Fact]
    public void A_spell_with_no_effects_at_all_lands_but_changes_nothing()
    {
        // Most spells in SomethingWild are like this -- 239 of 377 carry no effect entries and work
        // entirely through scripts.
        var hit = SpellResolution.Invoke(Dude(), Dude(1), Spell(), Face(3));

        Assert.Equal(SpellOutcome.NoEffect, hit.Outcome);
    }

    [Fact]
    public void An_expression_the_engine_cannot_compile_adds_nothing()
    {
        var target = Dude(1);
        var spell = Spell(effects: Effect("$CHAR_HITPOINTS", ".5*level"));

        var hit = SpellResolution.Invoke(Dude(), target, spell, Face(3));

        Assert.Equal(SpellOutcome.NoEffect, hit.Outcome);
        Assert.Equal(0, target.Effects.Count);
    }

    [Fact]
    public void The_casters_level_scales_the_effect()
    {
        var target = Dude(1);
        var spell = Spell(effects: Effect("$CHAR_HITPOINTS", "-(1d6)*level"));

        SpellResolution.Invoke(Dude(), target, spell, Face(3), casterLevel: 4);

        Assert.Equal(-12, target.Effects.Effects[0].Effect.Change);
    }

    // ---- stacking ------------------------------------------------------------------------------

    [Fact]
    public void A_non_cumulative_spell_refuses_a_target_that_already_has_it()
    {
        var target = Dude(1);
        var spell = Spell("bless", effects: Effect("$CHAR_AC", "-1"));

        Assert.Equal(SpellOutcome.Applied,
                     SpellResolution.Invoke(Dude(), target, spell, Face(3)).Outcome);
        Assert.Equal(SpellOutcome.AlreadyAffected,
                     SpellResolution.Invoke(Dude(), target, spell, Face(3)).Outcome);
        Assert.Equal(1, target.Effects.Count);
    }

    [Fact]
    public void The_refusal_happens_before_anything_is_rolled()
    {
        // The check is the first thing in the function -- before the scripts and before the save --
        // so a second casting is not merely wasted, it never rolls.
        var target = Dude(1);
        var spell = Spell("bless", save: SaveResult.SaveNegates,
                          effects: Effect("$CHAR_AC", "-1"));

        // A face of 1 fails the save, so the first casting actually lands and the target is left
        // carrying the spell.
        Assert.Equal(SpellOutcome.Applied,
                     SpellResolution.Invoke(Dude(), target, spell, Face(1)).Outcome);

        int rolls = 0;
        SpellResolution.Invoke(Dude(), target, spell, _ => { rolls++; return 1; });

        Assert.Equal(0, rolls);
    }

    [Fact]
    public void A_cumulative_spell_may_be_cast_again()
    {
        var target = Dude(1);
        var spell = Spell("bless", cumulative: 1,
                          effects: Effect("$CHAR_AC", "-1",
                                          SpellEffectFlags.Target | SpellEffectFlags.Cumulative));

        SpellResolution.Invoke(Dude(), target, spell, Face(3));
        var second = SpellResolution.Invoke(Dude(), target, spell, Face(3));

        Assert.Equal(SpellOutcome.Applied, second.Outcome);
        Assert.Equal(2, target.Effects.Count);
    }

    [Fact]
    public void A_different_spell_touching_the_same_attribute_is_a_separate_question()
    {
        // The spell-level check is by source; the per-attribute rule inside SpellEffectList is
        // independent of it, and that is the one that refuses this.
        var target = Dude(1);
        SpellResolution.Invoke(Dude(), target, Spell("bless", effects: Effect("$CHAR_AC", "-1")),
                               Face(3));
        var other = SpellResolution.Invoke(Dude(), target,
                                           Spell("shield", effects: Effect("$CHAR_AC", "-2")),
                                           Face(3));

        Assert.Equal(SpellOutcome.NoEffect, other.Outcome);
        Assert.Equal(1, target.Effects.Count);
    }

    // ---- saving throws -------------------------------------------------------------------------

    [Fact]
    public void A_no_save_spell_never_rolls_a_save()
    {
        // Two thirds of the spells in every shipped design are NoSave, and the reference guards the
        // call rather than rolling and ignoring the answer.
        int rolls = 0;
        var spell = Spell(save: SaveResult.NoSave, effects: Effect("$CHAR_AC", "-1"));

        SpellResolution.Invoke(Dude(), Dude(1), spell, _ => { rolls++; return 1; });

        Assert.Equal(0, rolls);
    }

    [Fact]
    public void A_made_save_against_a_negating_spell_stops_it_dead()
    {
        var target = Dude(1);
        var spell = Spell(save: SaveResult.SaveNegates, effects: Effect("$CHAR_HITPOINTS", "-2d8"));

        var hit = SpellResolution.Invoke(Dude(), target, spell, Face(20), saveScore: 14);

        Assert.Equal(SpellOutcome.Saved, hit.Outcome);
        Assert.True(hit.Saved);
        Assert.Equal(0, target.Effects.Count);
    }

    [Fact]
    public void A_failed_save_lets_the_spell_through()
    {
        var target = Dude(1);
        var spell = Spell(save: SaveResult.SaveNegates, effects: Effect("$CHAR_HITPOINTS", "-1"));

        var hit = SpellResolution.Invoke(Dude(), target, spell, Face(1), saveScore: 14);

        Assert.Equal(SpellOutcome.Applied, hit.Outcome);
        Assert.False(hit.Saved);
    }

    [Fact]
    public void Save_for_half_lands_in_full_because_the_multiplier_is_never_read()
    {
        // DoesSavingThrowSucceed computes changeResult/2 into a struct nothing consults again --
        // see the plan's saving-throw section. SaveForHalf therefore behaves as NoSave.
        var target = Dude(1);
        var spell = Spell(save: SaveResult.SaveForHalf, effects: Effect("$CHAR_HITPOINTS", "-8"));

        var hit = SpellResolution.Invoke(Dude(), target, spell, Face(20), saveScore: 14);

        Assert.Equal(SpellOutcome.Applied, hit.Outcome);
        Assert.True(hit.Saved);
        Assert.Equal(-8, target.Effects.Effects[0].Effect.Change);
    }

    [Fact]
    public void Magic_resistance_saves_without_a_d20()
    {
        var target = Dude(1);
        target.MagicResistance = 100;
        var spell = Spell(save: SaveResult.SaveNegates, effects: Effect("$CHAR_HITPOINTS", "-8"));

        // Every roll is a 1: the d20 would fail the save, but the d100 resistance roll saves first.
        var hit = SpellResolution.Invoke(Dude(), target, spell, Face(1), saveScore: 14);

        Assert.Equal(SpellOutcome.Saved, hit.Outcome);
    }

    // ---- the script hook -----------------------------------------------------------------------

    [Fact]
    public void A_refusing_attack_script_stops_the_spell_before_the_save()
    {
        var target = Dude(1);
        var spell = Spell(save: SaveResult.SaveNegates, effects: Effect("$CHAR_AC", "-1"));

        var hit = SpellResolution.Invoke(Dude(), target, spell, Face(1),
                                         attackSucceeds: (_, _) => false);

        Assert.Equal(SpellOutcome.Refused, hit.Outcome);
        Assert.Equal(0, target.Effects.Count);
    }

    // ---- duration ------------------------------------------------------------------------------

    [Fact]
    public void A_timed_spell_stops_a_number_of_rounds_after_it_lands()
    {
        // One combat round is one game minute, so a three-round spell expires three minutes on.
        var target = Dude(1);
        var spell = Spell(duration: SpellDurationRate.InRounds, durationText: "3",
                          effects: Effect("$CHAR_AC", "-1"));

        SpellResolution.Invoke(Dude(), target, spell, Face(3), elapsedMinutes: 10);

        Assert.Equal(13, target.Effects.Effects[0].StopTime);
    }

    [Fact]
    public void A_permanent_spell_has_no_stop_time()
    {
        var target = Dude(1);
        var spell = Spell(duration: SpellDurationRate.Permanent,
                          effects: Effect("$CHAR_AC", "-1"));

        SpellResolution.Invoke(Dude(), target, spell, Face(3));

        Assert.Null(target.Effects.Effects[0].StopTime);
    }

    [Fact]
    public void A_duration_is_itself_a_dice_expression_rolled_once()
    {
        var target = Dude(1);
        var spell = Spell(duration: SpellDurationRate.InRounds, durationText: "2d4",
                          effects: Effect("$CHAR_AC", "-1"));

        SpellResolution.Invoke(Dude(), target, spell, Face(3));

        Assert.Equal(6, target.Effects.Effects[0].StopTime);
    }

    // ---- a whole cast --------------------------------------------------------------------------

    [Fact]
    public void Every_target_of_a_cast_shares_one_active_spell_entry()
    {
        // Allocated before the loop in the reference, so a fireball expires from everyone at once
        // rather than wearing off piecemeal.
        var targets = new[] { Dude(1), Dude(2), Dude(3) };
        var spell = Spell(duration: SpellDurationRate.InRounds,
                          effects: Effect("$CHAR_HITPOINTS", "-1d8"));

        var hits = SpellResolution.InvokeAll(Dude(), targets, spell, Face(3), activeSpellKey: 7);

        Assert.Equal(3, hits.Count);
        Assert.All(hits, h => Assert.Equal(SpellOutcome.Applied, h.Outcome));
        Assert.All(targets, t => Assert.Equal(7, t.Effects.Effects[0].Parent));
    }

    [Fact]
    public void Each_target_rolls_its_own_save()
    {
        var tough = Dude(1);
        var frail = Dude(2);
        var spell = Spell(save: SaveResult.SaveNegates, effects: Effect("$CHAR_HITPOINTS", "-8"));

        var hits = SpellResolution.InvokeAll(Dude(), [tough, frail], spell, Face(10),
                                             saveScoreOf: c => c.Index == 1 ? 5 : 20);

        Assert.Equal(SpellOutcome.Saved, hits[0].Outcome);
        Assert.Equal(SpellOutcome.Applied, hits[1].Outcome);
    }
}
