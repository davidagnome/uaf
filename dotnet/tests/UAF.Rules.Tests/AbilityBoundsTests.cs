using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers the range an ability score is clamped into.</summary>
public class AbilityBoundsTests
{
    [Theory]
    [InlineData(AbilityScore.Strength)]
    [InlineData(AbilityScore.Intelligence)]
    [InlineData(AbilityScore.Wisdom)]
    [InlineData(AbilityScore.Dexterity)]
    [InlineData(AbilityScore.Constitution)]
    [InlineData(AbilityScore.Charisma)]
    public void The_six_rolled_scores_all_run_three_to_twenty_five(AbilityScore ability)
    {
        Assert.Equal(3, AbilityBounds.Min(ability));
        Assert.Equal(25, AbilityBounds.Max(ability));
    }

    [Fact]
    public void The_strength_percentile_has_its_own_range()
    {
        // A percentage rather than a score, so it starts at nothing and runs to a hundred.
        Assert.Equal(0, AbilityBounds.Min(AbilityScore.StrengthMod));
        Assert.Equal(100, AbilityBounds.Max(AbilityScore.StrengthMod));
    }

    [Fact]
    public void A_score_inside_its_range_is_left_alone()
    {
        Assert.Equal(14, AbilityBounds.Limit(14, AbilityScore.Strength));
    }

    [Fact]
    public void A_score_outside_its_range_is_pulled_back()
    {
        Assert.Equal(3, AbilityBounds.Limit(-40, AbilityScore.Strength));
        Assert.Equal(25, AbilityBounds.Limit(900, AbilityScore.Strength));
    }

    [Fact]
    public void The_percentile_clamps_to_zero_rather_than_three()
    {
        Assert.Equal(0, AbilityBounds.Limit(-1, AbilityScore.StrengthMod));
        Assert.Equal(100, AbilityBounds.Limit(101, AbilityScore.StrengthMod));
    }
}
