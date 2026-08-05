using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers the range a class allows one ability score to take.</summary>
public class AbilityLimitTests
{
    [Fact]
    public void One_baseclass_is_its_own_requirement()
    {
        Assert.Equal(new AbilityLimits(9, 0, 18, 0),
                     AbilityLimits.Combine([new AbilityLimits(9, 0, 18, 0)]));
    }

    [Fact]
    public void A_baseclass_with_no_requirement_asks_for_three_to_eighteen()
    {
        // A null contributes the default rather than being skipped, so it can still tighten a
        // range that was wider.
        Assert.Equal(AbilityLimits.Default, AbilityLimits.Combine([null]));
        Assert.Equal(new AbilityLimits(3, 0, 18, 0),
                     AbilityLimits.Combine([null, new AbilityLimits(0, 0, 25, 0)]));
    }

    [Fact]
    public void Two_baseclasses_give_the_tightest_of_each_end()
    {
        // A fighter/magic-user has to satisfy both.
        var combined = AbilityLimits.Combine(
            [new AbilityLimits(9, 0, 18, 100), new AbilityLimits(6, 0, 17, 0)]);

        Assert.Equal(new AbilityLimits(9, 0, 17, 0), combined);
    }

    [Fact]
    public void A_tie_on_the_base_value_takes_the_greater_modifier_at_both_ends()
    {
        // Symmetric where the base values are not: the maximum's tie-break also keeps the larger
        // modifier, though the maximum itself keeps the smaller value.
        var combined = AbilityLimits.Combine(
            [new AbilityLimits(9, 5, 18, 50), new AbilityLimits(9, 20, 18, 100)]);

        Assert.Equal(new AbilityLimits(9, 20, 18, 100), combined);
    }

    [Fact]
    public void A_class_with_no_baseclasses_caps_every_score_at_fifteen()
    {
        // The running maximum starts at 9999 as a sentinel and, with nothing to lower it, is
        // packed into a byte: 9999 & 0xff is 15. Below what a 3d6 roll can produce.
        var empty = AbilityLimits.Combine([]);

        Assert.Equal(AbilityLimits.Unbounded, empty);
        Assert.Equal(15, empty.Max);
        Assert.Equal(15, empty.MaxMod);
        Assert.Equal(0, empty.Min);
    }

    [Fact]
    public void A_limit_above_a_byte_wraps_rather_than_saturating()
    {
        Assert.Equal(new AbilityLimits(3, 0, 44, 0), AbilityLimits.Pack(3, 0, 300, 0));
    }

    [Fact]
    public void An_unknown_class_allows_nothing_above_zero()
    {
        // GetAbilityLimits returns the literal 1, so the maximum is zero and no score can rise.
        Assert.Equal(0, AbilityLimits.UnknownClass.Max);
        Assert.Equal(0, AbilityLimits.UnknownClass.Min);
        Assert.Equal(1, AbilityLimits.UnknownClass.MaxMod);
    }
}
