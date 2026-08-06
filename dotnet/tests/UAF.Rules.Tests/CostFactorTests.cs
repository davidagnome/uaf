using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers the one price scale every service in the game shares.</summary>
public class CostFactorTests
{
    [Fact]
    public void Free_is_the_only_way_to_pay_nothing()
    {
        Assert.Equal(0, Prices.Apply(CostFactor.Free, 500));
        Assert.Equal(0, Prices.Apply(CostFactor.Free, 1));
    }

    [Fact]
    public void Every_other_factor_floors_at_one()
    {
        // A one-coin spell at a hundredth still costs a coin, so a temple meaning to give
        // something away has to say Free rather than dividing enough.
        Assert.Equal(1, Prices.Apply(CostFactor.Divide100, 1));
        Assert.Equal(1, Prices.Apply(CostFactor.Divide100, 50));
        Assert.Equal(1, Prices.Apply(CostFactor.Divide2, 1));
    }

    [Fact]
    public void Normal_leaves_the_price_alone()
    {
        Assert.Equal(250, Prices.Apply(CostFactor.Normal, 250));
    }

    [Fact]
    public void It_truncates_rather_than_rounding()
    {
        // The scaled price is a double cast to an integer, so three halved is one.
        Assert.Equal(1, Prices.Apply(CostFactor.Divide2, 3));
        Assert.Equal(33, Prices.Apply(CostFactor.Divide3, 100));
    }

    [Theory]
    [InlineData(CostFactor.Divide100, 1000, 10)]
    [InlineData(CostFactor.Divide50, 1000, 20)]
    [InlineData(CostFactor.Divide20, 1000, 50)]
    [InlineData(CostFactor.Divide10, 1000, 100)]
    [InlineData(CostFactor.Divide5, 1000, 200)]
    [InlineData(CostFactor.Divide4, 1000, 250)]
    [InlineData(CostFactor.Divide2, 1000, 500)]
    [InlineData(CostFactor.Multiply1_5, 100, 150)]
    [InlineData(CostFactor.Multiply2, 100, 200)]
    [InlineData(CostFactor.Multiply10, 100, 1000)]
    [InlineData(CostFactor.Multiply100, 100, 10000)]
    public void The_ladder_scales_as_its_name_says(CostFactor factor, int price, int expected)
    {
        Assert.Equal(expected, Prices.Apply(factor, price));
    }

    [Fact]
    public void The_ordinals_are_the_wire_order()
    {
        // Designs store the number, so the twenty entries have to keep their positions.
        Assert.Equal(0, (int)CostFactor.Free);
        Assert.Equal(10, (int)CostFactor.Normal);
        Assert.Equal(19, (int)CostFactor.Multiply100);
        Assert.Equal(20, Prices.FactorCount);
    }

    [Fact]
    public void An_ordinal_the_engine_cannot_place_is_normal_and_not_free()
    {
        // A stored value out of range should not silently make a service free.
        Assert.Equal(CostFactor.Normal, Prices.FactorOf(-1));
        Assert.Equal(CostFactor.Normal, Prices.FactorOf(99));
        Assert.Equal(CostFactor.Free, Prices.FactorOf(0));
        Assert.Equal(CostFactor.Multiply100, Prices.FactorOf(19));
    }

    [Fact]
    public void A_price_of_nothing_still_costs_one_unless_it_is_free()
    {
        Assert.Equal(1, Prices.Apply(CostFactor.Normal, 0));
        Assert.Equal(0, Prices.Apply(CostFactor.Free, 0));
    }
}
