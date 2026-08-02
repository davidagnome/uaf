using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers saving throws: the d20 against the score, and what a save is worth.</summary>
public class SavingThrowTests
{
    // ---- the roll ------------------------------------------------------------------------------

    [Fact]
    public void A_roll_below_the_score_fails_the_save()
    {
        Assert.False(SavingThrow.DidSaveVersus(score: 14, roll: 13));
    }

    [Fact]
    public void A_roll_equal_to_the_score_saves()
    {
        // The reference tests `roll < score` for failure, so the boundary belongs to the target.
        Assert.True(SavingThrow.DidSaveVersus(score: 14, roll: 14));
    }

    [Fact]
    public void The_score_is_capped_at_twenty_so_a_save_is_never_impossible()
    {
        Assert.True(SavingThrow.DidSaveVersus(score: 40, roll: 20));
    }

    [Fact]
    public void A_score_of_zero_saves_on_anything()
    {
        // No floor on the live path -- the max(score, 1) beside the cap is in the commented-out
        // script block.
        Assert.True(SavingThrow.DidSaveVersus(score: 0, roll: 1));
    }

    [Fact]
    public void Protections_add_to_the_roll()
    {
        Assert.Equal(0, SavingThrow.RollBonus());
        Assert.Equal(2, SavingThrow.RollBonus(protectedFromAlignment: true));
        Assert.Equal(1, SavingThrow.RollBonus(shielded: true));
        Assert.Equal(2, SavingThrow.RollBonus(displaced: true));
        Assert.Equal(5, SavingThrow.RollBonus(true, true, true));
    }

    [Fact]
    public void The_bonus_can_turn_a_failed_save_into_a_made_one()
    {
        Assert.False(SavingThrow.DidSaveVersus(score: 14, roll: 12));
        Assert.True(SavingThrow.DidSaveVersus(score: 14, roll: 12, rollBonus: 2));
    }

    // ---- magic resistance ----------------------------------------------------------------------

    [Fact]
    public void Magic_resistance_is_checked_before_the_save_and_counts_as_one()
    {
        // Resistance short-circuits the d20 entirely: the target saves without rolling.
        Assert.True(SavingThrow.DidSaveVersus(score: 20, roll: 1,
                                              magicResistance: 40, resistanceRoll: 40));
    }

    [Fact]
    public void Failing_the_resistance_roll_falls_through_to_the_ordinary_save()
    {
        Assert.False(SavingThrow.DidSaveVersus(score: 20, roll: 1,
                                               magicResistance: 40, resistanceRoll: 41));
    }

    [Fact]
    public void No_resistance_means_the_roll_is_not_taken_at_all()
    {
        Assert.False(SavingThrow.DidSaveVersus(score: 20, roll: 1,
                                               magicResistance: 0, resistanceRoll: 1));
    }

    [Fact]
    public void Resistance_is_not_immunity()
    {
        // A resisted SaveForHalf spell still does half, because resistance produces a save rather
        // than a bypass.
        var outcome = SavingThrow.Resolve(SaveResult.SaveForHalf, score: 20, roll: 1,
                                          magicResistance: 100, resistanceRoll: 1);

        Assert.True(outcome.Saved);
        Assert.Equal(0.5, outcome.Change);
        Assert.False(outcome.NoEffect);
    }

    // ---- what a save is worth ------------------------------------------------------------------

    [Fact]
    public void A_failed_save_always_takes_the_full_effect()
    {
        foreach (var result in new[] { SaveResult.NoSave, SaveResult.SaveNegates,
                                       SaveResult.SaveForHalf })
        {
            var outcome = SavingThrow.Resolve(result, saved: false);
            Assert.False(outcome.Saved);
            Assert.False(outcome.NoEffect);
            Assert.Equal(1.0, outcome.Change);
        }
    }

    [Fact]
    public void No_save_means_the_spell_lands_even_when_the_target_makes_its_roll()
    {
        var outcome = SavingThrow.Resolve(SaveResult.NoSave, saved: true);

        Assert.True(outcome.Saved);
        Assert.False(outcome.NoEffect);
        Assert.Equal(1.0, outcome.Change);
    }

    [Fact]
    public void Save_negates_suppresses_the_spell_entirely()
    {
        var outcome = SavingThrow.Resolve(SaveResult.SaveNegates, saved: true);

        Assert.True(outcome.NoEffect);
        Assert.Equal(0.0, outcome.Change);
    }

    [Fact]
    public void Save_for_half_halves_the_change_but_leaves_the_spell_in_effect()
    {
        // NoEffect stays false, which matters: it is what lets non-numeric parts of the spell
        // still apply.
        var outcome = SavingThrow.Resolve(SaveResult.SaveForHalf, saved: true);

        Assert.False(outcome.NoEffect);
        Assert.Equal(0.5, outcome.Change);
    }

    // ---- the THAC0 branch ----------------------------------------------------------------------

    [Fact]
    public void A_thac0_spell_essentially_never_lands()
    {
        // The reference compares the roll against `AC - THAC0` where the arithmetic wants
        // `THAC0 - AC`. With any ordinary competence and armour the threshold is negative, so
        // every roll clears it and the branch that runs is the one setting noEffectWhatsoever.
        for (int roll = 1; roll <= 20; roll++)
        {
            var outcome = SavingThrow.ResolveThac0(roll, casterThac0: 18, targetArmorClass: 6);
            Assert.True(outcome.NoEffect);
            Assert.Equal(0.0, outcome.Change);
        }
    }

    [Fact]
    public void The_thac0_branch_only_lands_when_armour_class_exceeds_thac0_by_the_roll()
    {
        // Reachable only with the operands as transposed as they are: armour class well above the
        // caster's THAC0, which no ordinary character or monster has.
        var outcome = SavingThrow.ResolveThac0(roll: 5, casterThac0: 2, targetArmorClass: 10);

        Assert.False(outcome.Saved);
        Assert.False(outcome.NoEffect);
        Assert.Equal(1.0, outcome.Change);
    }

    [Fact]
    public void The_script_bonus_reaches_the_thac0_branch_and_nothing_else()
    {
        // DidSaveVersus takes a bonus parameter and never reads it, so a design's SavingThrow
        // script is silently dropped for the other four save types.
        var ordinary = SavingThrow.Resolve(SaveResult.SaveNegates, score: 14, roll: 12,
                                           scriptBonus: 5);
        Assert.False(ordinary.Saved);

        var withRollBonus = SavingThrow.Resolve(SaveResult.SaveNegates, score: 14, roll: 12,
                                                rollBonus: 5);
        Assert.True(withRollBonus.Saved);
    }

    [Fact]
    public void The_thac0_branch_ignores_the_save_score_and_magic_resistance()
    {
        // Both would have produced a save on the ordinary path: the score is 1, and the
        // resistance is total. The THAC0 branch consults neither.
        var outcome = SavingThrow.Resolve(SaveResult.UseThac0, score: 1, roll: 5,
                                          magicResistance: 100, resistanceRoll: 1,
                                          casterThac0: 2, targetArmorClass: 10);

        Assert.False(outcome.Saved);
        Assert.Equal(1.0, outcome.Change);
    }
}
