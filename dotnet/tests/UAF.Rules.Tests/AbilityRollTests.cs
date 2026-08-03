using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers rolling a new character's ability scores.
/// </summary>
public class AbilityRollTests
{
    /// <summary>Hands back a fixed sequence, so a "best of three" is a fact rather than a range.</summary>
    private static Func<int, int, int> Sequence(params int[] totals)
    {
        int at = 0;
        return (_, _) => totals[at++ % totals.Length];
    }

    [Fact]
    public void A_score_is_the_best_of_three_rolls()
    {
        // Why new characters come out so uniformly good, and why lowering an ability's dice moves
        // the average far less than it looks like it should.
        Assert.Equal(15, AbilityRoll.Legacy(Sequence(9, 15, 11)));
        Assert.Equal(3, AbilityRoll.Attempts);
    }

    [Fact]
    public void The_legacy_roll_is_three_six_sided_dice()
    {
        int sides = 0;
        int count = 0;

        AbilityRoll.Legacy((c, s) => { count = c; sides = s; return 10; });

        Assert.Equal(3, count);
        Assert.Equal(6, sides);
    }

    [Fact]
    public void The_modern_roll_asks_the_ability_for_its_own_dice()
    {
        int asked = 0;

        int score = AbilityRoll.Modern(() => { asked++; return asked * 5; });

        Assert.Equal(3, asked);
        Assert.Equal(15, score);
    }

    [Fact]
    public void An_attempt_that_does_not_roll_counts_as_zero()
    {
        // RollAbility answering false leaves that attempt at zero rather than skipping it, so an
        // ability whose dice never roll produces a score of 0 and not a refusal.
        Assert.Equal(0, AbilityRoll.Modern(() => null));

        int at = 0;
        Assert.Equal(7, AbilityRoll.Modern(() => at++ == 1 ? 7 : null));
    }

    [Fact]
    public void Exceptional_strength_needs_exactly_eighteen()
    {
        // The test is equality, so racial or magical strength above 18 skips the percentile
        // rather than maximising it.
        Assert.True(AbilityRoll.QualifiesForStrengthBonus(18));
        Assert.False(AbilityRoll.QualifiesForStrengthBonus(17));
        Assert.False(AbilityRoll.QualifiesForStrengthBonus(19));
    }

    [Fact]
    public void A_class_minimum_only_ever_raises_a_score()
    {
        // Applied after the roll: a character who rolls below what their class demands is given
        // the minimum rather than re-rolled or refused.
        Assert.Equal(9, AbilityRoll.AtLeast(6, 9));
        Assert.Equal(14, AbilityRoll.AtLeast(14, 9));
    }
}
