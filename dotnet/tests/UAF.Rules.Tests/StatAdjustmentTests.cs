using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers raising and lowering an ability score by hand on the stats screen.</summary>
public class StatAdjustmentTests
{
    private static readonly AbilityLimits ThreeToEighteen = new(3, 0, 18, 0);

    [Fact]
    public void With_no_points_nothing_can_rise()
    {
        // The available points start at zero and nothing else adds to them, so this is the state
        // the screen opens in: the only way up is to come down somewhere else first.
        var change = StatAdjustment.Increase(12, available: 0, ThreeToEighteen);

        Assert.False(change.Changed);
        Assert.Equal(12, change.Score);
        Assert.Equal(0, change.Available);
    }

    [Fact]
    public void Lowering_a_score_pays_for_raising_another()
    {
        var down = StatAdjustment.Decrease(12, available: 0, ThreeToEighteen);
        Assert.Equal(11, down.Score);
        Assert.Equal(1, down.Available);

        var up = StatAdjustment.Increase(9, down.Available, ThreeToEighteen);
        Assert.Equal(10, up.Score);
        Assert.Equal(0, up.Available);
    }

    [Fact]
    public void The_class_maximum_and_minimum_stop_it()
    {
        Assert.False(StatAdjustment.Increase(18, available: 5, ThreeToEighteen).Changed);
        Assert.False(StatAdjustment.Decrease(3, available: 0, ThreeToEighteen).Changed);
    }

    [Fact]
    public void A_score_already_past_the_maximum_cannot_rise_further()
    {
        // ">=", not ">" -- a score put above the limit by something else stays where it is.
        Assert.False(StatAdjustment.Increase(19, available: 5, ThreeToEighteen).Changed);
    }

    [Fact]
    public void A_tighter_race_limit_costs_nothing_and_changes_nothing()
    {
        // The guard reads the class maximum; UpdateStats then clamps against the race's, which
        // runs first and can be stricter. The press is allowed, the score comes straight back, and
        // the point is not charged -- which is why the reference computes the cost from the
        // achieved change rather than subtracting one.
        var change = StatAdjustment.Increase(17, available: 1, ThreeToEighteen,
                                             normalise: score => Math.Min(score, 17));

        Assert.True(change.Changed);        // the screen still redraws
        Assert.Equal(17, change.Score);     // and the number does not move
        Assert.Equal(1, change.Available);  // and the point is still there
    }

    [Fact]
    public void A_race_minimum_above_the_class_one_gives_a_point_for_nothing()
    {
        // The decrease credits its point unconditionally; the reference's guard against exactly
        // this is commented out. So a character whose race floors a score can farm points from it.
        var change = StatAdjustment.Decrease(10, available: 0, ThreeToEighteen,
                                             normalise: score => Math.Max(score, 10));

        Assert.Equal(10, change.Score);
        Assert.Equal(1, change.Available);
    }

    // ---- the exceptional-strength percentile ----------------------------------------------------

    [Fact]
    public void Reaching_eighteen_rolls_the_percentile_once()
    {
        int rolls = 0;
        int? Roll() { rolls++; return 74; }

        var (percentile, cached) = StatAdjustment.StrengthPercentile(17, 18, cached: 0, Roll);

        Assert.Equal(74, percentile);
        Assert.Equal(74, cached);
        Assert.Equal(1, rolls);

        // Coming back to 18 later in the same visit reuses it rather than rolling again for a
        // better one.
        var (again, _) = StatAdjustment.StrengthPercentile(17, 18, cached, Roll);
        Assert.Equal(74, again);
        Assert.Equal(1, rolls);
    }

    [Fact]
    public void Dropping_off_eighteen_clears_the_percentile_but_keeps_the_roll()
    {
        var (percentile, cached) = StatAdjustment.StrengthPercentile(18, 17, cached: 74,
                                                                    () => 99);

        Assert.Equal(0, percentile);
        Assert.Equal(74, cached);        // still cached, so climbing back gives 74 again
    }

    [Fact]
    public void A_change_that_never_touches_eighteen_leaves_the_modifier_alone()
    {
        var (percentile, _) = StatAdjustment.StrengthPercentile(12, 13, cached: 0, () => 99);

        Assert.Null(percentile);
    }

    [Fact]
    public void A_class_with_no_strength_dice_gets_nothing_and_keeps_trying()
    {
        // The cache is tested by being zero, so a class whose dice roll nothing re-rolls on every
        // press. It costs nothing and produces nothing.
        int rolls = 0;
        int? Roll() { rolls++; return null; }

        var (percentile, cached) = StatAdjustment.StrengthPercentile(17, 18, cached: 0, Roll);
        Assert.Equal(0, percentile);
        Assert.Equal(0, cached);

        StatAdjustment.StrengthPercentile(17, 18, cached, Roll);
        Assert.Equal(2, rolls);
    }

    [Fact]
    public void A_negative_roll_is_stored_as_zero()
    {
        var (percentile, _) = StatAdjustment.StrengthPercentile(17, 18, cached: 0, () => -5);

        Assert.Equal(0, percentile);
    }
}
