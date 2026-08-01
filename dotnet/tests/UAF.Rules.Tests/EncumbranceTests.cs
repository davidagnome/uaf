using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers <see cref="Encumbrance"/>.</summary>
public class EncumbranceTests
{
    [Theory]
    [InlineData(1, 1)]           // 350 - 350 = 0, floored to 1
    [InlineData(3, 1)]
    [InlineData(4, 100)]
    [InlineData(7, 200)]
    [InlineData(8, 350)]         // the base band
    [InlineData(11, 350)]
    [InlineData(12, 450)]
    [InlineData(15, 550)]
    [InlineData(16, 700)]
    [InlineData(17, 850)]
    [InlineData(19, 4850)]
    [InlineData(25, 15350)]
    public void The_allowance_follows_the_strength_table(int strength, int expected) =>
        Assert.Equal(expected, Encumbrance.NormalAllowance(strength));

    [Fact]
    public void A_strength_below_four_is_floored_at_one_rather_than_zero()
    {
        // 350 - 350 is exactly zero, and the reference bumps it to 1. It matters because the
        // movement bands divide the carried weight by this: a zero would make such a character
        // maximally encumbered no matter what they carried.
        Assert.Equal(1, Encumbrance.NormalAllowance(3));
        Assert.Equal(1, Encumbrance.MaxMovementFor(carried: 5, strength: 3));
    }

    [Theory]
    [InlineData(0, 1100)]        // an 18 with no percentile
    [InlineData(1, 1350)]
    [InlineData(50, 1350)]
    [InlineData(51, 1600)]
    [InlineData(75, 1600)]
    [InlineData(76, 1850)]
    [InlineData(90, 1850)]
    [InlineData(91, 2350)]
    [InlineData(99, 2350)]
    [InlineData(100, 3350)]      // 18/00, the top band
    public void Exceptional_strength_bands_are_irregular(int mod, int expected) =>
        Assert.Equal(expected, Encumbrance.NormalAllowance(18, mod));

    [Fact]
    public void The_percentile_is_read_as_a_byte()
    {
        // The reference assigns it to a BYTE, so 256 wraps to 0 rather than saturating at the top
        // band. Nothing writes such a value, but the arithmetic is the reference's either way.
        Assert.Equal(Encumbrance.NormalAllowance(18, 0), Encumbrance.NormalAllowance(18, 256));
    }

    [Fact]
    public void An_out_of_range_strength_falls_back_to_the_base_allowance()
    {
        // The default arm of the switch, rather than extrapolating off either end.
        Assert.Equal(Encumbrance.BaseAllowance, Encumbrance.NormalAllowance(0));
        Assert.Equal(Encumbrance.BaseAllowance, Encumbrance.NormalAllowance(99));
        Assert.Equal(Encumbrance.BaseAllowance, Encumbrance.NormalAllowance(-5));
    }

    [Fact]
    public void The_carrying_maximum_is_five_times_the_allowance()
    {
        Assert.Equal(350 * 5, Encumbrance.MaxAllowance(10));
        Assert.Equal(700 * 5, Encumbrance.MaxAllowance(16));
    }

    [Theory]
    [InlineData(0, 12)]          // unencumbered
    [InlineData(350, 12)]        // exactly on the allowance is still unencumbered
    [InlineData(351, 9)]
    [InlineData(700, 9)]
    [InlineData(701, 6)]
    [InlineData(1050, 6)]
    [InlineData(1051, 3)]
    [InlineData(1400, 3)]
    [InlineData(1401, 1)]        // past four times: 1, not 0
    [InlineData(999999, 1)]
    public void Movement_steps_down_in_four_bands_and_then_floors(int carried, int expected) =>
        Assert.Equal(expected, Encumbrance.MaxMovementFor(carried, strength: 10));

    [Fact]
    public void A_stronger_character_carries_the_same_load_faster()
    {
        // The same 800gp: encumbered for a strength 10, unencumbered for an 18/00.
        Assert.Equal(6, Encumbrance.MaxMovementFor(800, strength: 10));
        Assert.Equal(12, Encumbrance.MaxMovementFor(800, strength: 18, strengthMod: 100));
    }
}
