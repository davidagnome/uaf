using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers <see cref="Initiative"/>.</summary>
public class InitiativeTests
{
    [Theory]
    [InlineData(1, 9)]
    [InlineData(5, 13)]
    [InlineData(10, 18)]
    public void An_unsurprised_roll_lands_in_the_nine_to_eighteen_range(int roll, int expected) =>
        Assert.Equal(expected, Initiative.Roll(Surprise.Neither, isPartySide: true, roll));

    [Fact]
    public void Surprise_replaces_the_roll_rather_than_modifying_it()
    {
        // A surprised side takes the last slot outright and the other the first; the die is never
        // consulted. Treating surprise as a bonus would leave the outcome uncertain where the
        // reference makes it certain.
        Assert.Equal(Initiative.Last, Initiative.Roll(Surprise.Party, isPartySide: true, roll: 1));
        Assert.Equal(Initiative.First, Initiative.Roll(Surprise.Party, isPartySide: false, roll: 10));

        Assert.Equal(Initiative.First, Initiative.Roll(Surprise.Monsters, isPartySide: true, roll: 10));
        Assert.Equal(Initiative.Last, Initiative.Roll(Surprise.Monsters, isPartySide: false, roll: 1));
    }

    [Fact]
    public void A_surprised_party_acts_after_the_monsters()
    {
        int party = Initiative.Roll(Surprise.Party, isPartySide: true);
        int monster = Initiative.Roll(Surprise.Party, isPartySide: false);

        // Lower acts earlier, so the monsters' number must be the smaller one.
        Assert.True(monster < party);
    }

    [Fact]
    public void A_roll_outside_the_range_is_clamped()
    {
        Assert.Equal(Initiative.First, Initiative.Roll(Surprise.Neither, true, roll: -50));
        Assert.Equal(Initiative.Last, Initiative.Roll(Surprise.Neither, true, roll: 999));
    }

    [Fact]
    public void Lower_initiative_acts_first()
    {
        Assert.Equal([2, 0, 1], Initiative.Order([12, 18, 9]));
    }

    [Fact]
    public void Ties_keep_their_original_order()
    {
        // The reference bubble sorts with a strict `>`, so equal initiatives never swap. Ties are
        // common on a ten-sided range with a whole party rolling, and an unstable sort would
        // reorder them -- changing who strikes first, invisibly, until a save game diverges.
        Assert.Equal([0, 1, 2, 3], Initiative.Order([10, 10, 10, 10]));
        Assert.Equal([1, 3, 0, 2], Initiative.Order([12, 9, 12, 9]));
    }

    [Fact]
    public void An_empty_field_orders_to_nothing()
    {
        Assert.Empty(Initiative.Order([]));
    }

    [Fact]
    public void A_whole_round_orders_end_to_end()
    {
        // Four party members and two monsters, with the party surprised.
        int[] initiatives =
        [
            Initiative.Roll(Surprise.Party, isPartySide: true),
            Initiative.Roll(Surprise.Party, isPartySide: true),
            Initiative.Roll(Surprise.Party, isPartySide: false),
            Initiative.Roll(Surprise.Party, isPartySide: false),
        ];

        // Both monsters act before both party members, and each pair keeps its own order.
        Assert.Equal([2, 3, 0, 1], Initiative.Order(initiatives));
    }
}
