using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers <see cref="ArmorClass"/>.</summary>
public class ArmorClassTests
{
    [Theory]
    [InlineData(3, 10)]
    [InlineData(10, 10)]
    [InlineData(14, 10)]         // the threshold itself gives nothing
    [InlineData(15, 9)]
    [InlineData(18, 6)]
    [InlineData(25, -1)]
    public void Dexterity_improves_the_base_one_point_at_a_time(int dexterity, int expected) =>
        Assert.Equal(expected, ArmorClass.Base(dexterity));

    [Fact]
    public void There_is_no_penalty_for_low_dexterity()
    {
        // Unlike the tabletop rules this otherwise follows: below 15 everyone is simply 10.
        Assert.Equal(ArmorClass.Worst, ArmorClass.Base(3));
        Assert.Equal(ArmorClass.Worst, ArmorClass.Base(1));
        Assert.Equal(ArmorClass.Worst, ArmorClass.Base(0));
    }

    [Fact]
    public void Readied_protection_is_a_flat_sum_of_base_and_bonus()
    {
        // Plate mail -6 with a +1 enchantment, and a shield -1.
        Assert.Equal(-8, ArmorClass.Protection([(-6, -1), (-1, 0)]));
        Assert.Equal(0, ArmorClass.Protection([]));
    }

    [Fact]
    public void Nothing_stops_two_suits_of_armour_stacking()
    {
        // The reference applies no slot rules here at all -- it sums every readied item. A design
        // that lets a character ready two suits gets both, and reproducing that is the point.
        Assert.Equal(-12, ArmorClass.Protection([(-6, 0), (-6, 0)]));
    }

    [Fact]
    public void Equipment_and_dexterity_combine()
    {
        // A dexterous fighter in plate: 10 - 3 for an 17 dexterity, then -6 for the armour.
        Assert.Equal(1, ArmorClass.Effective(17, [(-6, 0)]));

        // ...and an unarmoured, unremarkable one is simply 10.
        Assert.Equal(10, ArmorClass.Effective(12, []));
    }

    [Fact]
    public void Items_in_the_pack_contribute_nothing()
    {
        // The caller passes only what is readied; this pins the contract rather than the filter.
        Assert.Equal(10, ArmorClass.Effective(12, []));
        Assert.Equal(4, ArmorClass.Effective(12, [(-6, 0)]));
    }

    [Fact]
    public void The_result_is_clamped_at_both_ends()
    {
        // 10 is the worst possible, whatever the character does.
        Assert.Equal(ArmorClass.Worst, ArmorClass.Effective(10, [(50, 0)]));

        // ...and -500 the best, however much protection stacks up.
        Assert.Equal(ArmorClass.Best, ArmorClass.Effective(10, [(-9999, 0)]));
    }

    [Fact]
    public void The_dexterity_score_is_read_as_a_byte()
    {
        // The reference assigns it to a BYTE before comparing, so 270 wraps to 14 -- below the
        // threshold, and therefore no bonus at all rather than an enormous one.
        Assert.Equal(ArmorClass.Worst, ArmorClass.Base(270));
    }
}
