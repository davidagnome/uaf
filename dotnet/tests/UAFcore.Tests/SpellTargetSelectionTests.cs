using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers choosing a spell's targets against its limits.</summary>
public class SpellTargetSelectionTests
{
    private static SpellTargetSelection Units(SpellTargeting targeting = SpellTargeting.SelectedByCount,
                                              int maxTargets = 3, int maxRange = 0,
                                              int maxHitDice = 0, int combatants = 8) =>
        new(targeting,
            new SpellTargetingSetup(maxTargets, maxRange, 0, 0,
                                    SelectingUnits: true, maxHitDice, IsArea: false),
            combatants);

    private static SpellTargetSelection Area(SpellTargeting targeting = SpellTargeting.AreaCircle,
                                             int maxTargets = 4, int maxRange = 10) =>
        new(targeting,
            new SpellTargetingSetup(maxTargets, maxRange, 3, 3,
                                    SelectingUnits: false, 0, IsArea: true),
            combatantCount: 8);

    // ---- taking combatants ---------------------------------------------------------------------

    [Fact]
    public void Targets_are_taken_up_to_the_maximum()
    {
        var pick = Units(maxTargets: 2);

        Assert.True(pick.Add(1));
        Assert.True(pick.Add(2));
        Assert.False(pick.Add(3));
        Assert.Equal([1, 2], pick.Targets);
    }

    [Fact]
    public void The_same_combatant_cannot_be_taken_twice()
    {
        var pick = Units();

        Assert.True(pick.Add(1));
        Assert.False(pick.Add(1));
        Assert.Single(pick.Targets);
    }

    [Fact]
    public void A_zero_maximum_means_no_limit_rather_than_none_allowed()
    {
        // All three limits are guarded by `> 0` in the reference, which is what lets the hit-dice
        // mode zero MaxTargets and still work.
        var pick = Units(targeting: SpellTargeting.SelectByHitDice,
                         maxTargets: 0, maxHitDice: 20);

        Assert.True(pick.Add(1, hitDice: 2));
        Assert.True(pick.Add(2, hitDice: 2));
    }

    [Fact]
    public void A_target_out_of_range_is_refused()
    {
        var pick = Units(maxRange: 5);

        Assert.False(pick.Add(1, distance: 6));
        Assert.True(pick.Add(1, distance: 5));
    }

    [Fact]
    public void A_zero_range_is_no_range_limit()
    {
        var pick = Units(maxRange: 0);

        Assert.True(pick.Add(1, distance: 9999));
    }

    // ---- the hit-dice budget -------------------------------------------------------------------

    [Fact]
    public void Hit_dice_accumulate_until_the_budget_would_be_exceeded()
    {
        var pick = Units(targeting: SpellTargeting.SelectByHitDice, maxTargets: 0, maxHitDice: 8);

        Assert.True(pick.Add(1, hitDice: 5));
        Assert.False(pick.Add(2, hitDice: 4));   // 9 would exceed 8
        Assert.True(pick.Add(3, hitDice: 3));    // 8 exactly is allowed
        Assert.Equal(8, pick.HitDiceUsed);
    }

    [Fact]
    public void A_target_that_exactly_reaches_the_budget_both_lands_and_ends_the_selection()
    {
        var pick = Units(targeting: SpellTargeting.SelectByHitDice, maxTargets: 0, maxHitDice: 4);

        Assert.True(pick.Add(1, hitDice: 4));
        Assert.True(pick.HitDiceLimitReached);
        Assert.True(pick.AllChosen);
    }

    [Fact]
    public void Only_the_hit_dice_mode_accumulates_hit_dice()
    {
        // Otherwise the budget could leak into a spell that does not use it.
        var pick = Units(targeting: SpellTargeting.SelectedByCount, maxHitDice: 8);

        pick.Add(1, hitDice: 5);

        Assert.Equal(0, pick.HitDiceUsed);
        Assert.False(pick.HitDiceLimitReached);
    }

    // ---- when the selection is finished --------------------------------------------------------

    [Fact]
    public void Filling_the_quota_finishes_the_selection()
    {
        var pick = Units(maxTargets: 2);

        pick.Add(1);
        Assert.False(pick.AllChosen);
        pick.Add(2);
        Assert.True(pick.AllChosen);
    }

    [Fact]
    public void Running_out_of_combatants_finishes_it_too()
    {
        // A spell allowed six targets in a fight with three stops after three, rather than leaving
        // the player pressing EXIT.
        var pick = Units(maxTargets: 6, combatants: 3);

        pick.Add(1);
        pick.Add(2);
        pick.Add(3);

        Assert.True(pick.AllChosen);
    }

    // ---- map targets ---------------------------------------------------------------------------

    [Fact]
    public void An_area_cast_takes_one_square_and_no_more()
    {
        var pick = Area();

        Assert.True(pick.AddMapTarget(4, 7));
        Assert.False(pick.AddMapTarget(5, 8));
        Assert.Equal((4, 7), pick.MapTarget);
        Assert.True(pick.AllChosen);
    }

    [Fact]
    public void An_area_cast_refuses_combatants_and_a_unit_cast_refuses_squares()
    {
        Assert.False(Area().Add(1));
        Assert.False(Units().AddMapTarget(4, 7));
    }

    [Fact]
    public void A_negative_square_is_not_a_target()
    {
        var pick = Area();

        Assert.False(pick.AddMapTarget(-1, 7));
        Assert.Null(pick.MapTarget);
    }

    // ---- validity ------------------------------------------------------------------------------

    [Theory]
    [InlineData(SpellTargeting.Self)]
    [InlineData(SpellTargeting.SelectedByCount)]
    [InlineData(SpellTargeting.WholeParty)]
    [InlineData(SpellTargeting.TouchedTargets)]
    public void A_counted_spell_needs_a_target_count(SpellTargeting targeting)
    {
        Assert.True(Units(targeting, maxTargets: 1).IsValid);
        Assert.False(Units(targeting, maxTargets: 0).IsValid);
    }

    [Fact]
    public void A_hit_dice_spell_needs_a_budget_rather_than_a_count()
    {
        Assert.True(Units(SpellTargeting.SelectByHitDice, maxTargets: 0, maxHitDice: 8).IsValid);
        Assert.False(Units(SpellTargeting.SelectByHitDice, maxTargets: 9, maxHitDice: 0).IsValid);
    }

    [Fact]
    public void An_area_spell_needs_both_a_range_and_a_count()
    {
        // A design leaving an area spell's quantity at zero cannot cast it, and the setup does not
        // fill that in. The reference dies and abandons the cast.
        Assert.True(Area(maxTargets: 4, maxRange: 10).IsValid);
        Assert.False(Area(maxTargets: 0, maxRange: 10).IsValid);
        Assert.False(Area(maxTargets: 4, maxRange: 0).IsValid);
    }

    // ---- the menu ------------------------------------------------------------------------------

    [Fact]
    public void The_title_counts_down_as_targets_are_taken()
    {
        var pick = Units(maxTargets: 3);

        Assert.Equal("CHOOSE 3 TARGETS", pick.RemainingText());
        pick.Add(1);
        Assert.Equal("CHOOSE 2 TARGETS", pick.RemainingText());
    }

    [Fact]
    public void The_title_is_clamped_to_how_many_combatants_there_are()
    {
        var pick = Units(maxTargets: 9, combatants: 4);

        Assert.Equal("CHOOSE 4 TARGETS", pick.RemainingText());
    }

    [Fact]
    public void The_hit_dice_title_shows_what_is_left_not_what_is_spent()
    {
        var pick = Units(SpellTargeting.SelectByHitDice, maxTargets: 0, maxHitDice: 8);
        pick.Add(1, hitDice: 2.5);

        Assert.Equal("CHOOSE 5.5 HIT DICE", pick.RemainingText());
    }

    [Theory]
    [InlineData(SpellTargeting.AreaCircle, "CHOOSE CENTER OF CIRCLE")]
    [InlineData(SpellTargeting.AreaSquare, "CHOOSE CENTER OF SQUARE")]
    [InlineData(SpellTargeting.AreaCone, "CHOOSE START OF CONE")]
    [InlineData(SpellTargeting.AreaLinePickStart, "CHOOSE START OF LINE")]
    [InlineData(SpellTargeting.AreaLinePickEnd, "CHOOSE END OF LINE")]
    public void Each_area_shape_asks_for_its_own_thing(SpellTargeting targeting, string expected)
    {
        Assert.Equal(expected, Area(targeting).RemainingText());
    }

    // ---- leaving -------------------------------------------------------------------------------

    [Fact]
    public void Leaving_with_nothing_chosen_would_abandon_the_spell()
    {
        Assert.True(Units().ExitWouldAbandon);
        Assert.True(Area().ExitWouldAbandon);
    }

    [Fact]
    public void Fewer_targets_than_the_maximum_is_a_perfectly_good_cast()
    {
        // EXIT takes it without asking; only an empty selection prompts.
        var pick = Units(maxTargets: 3);
        pick.Add(1);

        Assert.False(pick.ExitWouldAbandon);
    }
}
