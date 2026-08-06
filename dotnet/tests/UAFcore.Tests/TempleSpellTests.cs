using UAF.Rules;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers what a temple offers to cast, and at what price.</summary>
public class TempleSpellTests
{
    private static SpellList Book(params (string Id, int Level, int Memorized)[] spells)
    {
        var book = new SpellList();

        foreach (var (id, level, memorized) in spells)
        {
            book.Add(id, level, memorized);
        }

        return book;
    }

    /// <summary>Every spell costs a hundred and is named after its id.</summary>
    private static (string, int, int)? Flat(string id) => (id.ToUpperInvariant(), LevelOf(id), 100);

    private static int LevelOf(string id) => id.Length;

    [Fact]
    public void Only_memorised_copies_are_offered()
    {
        // A spell the temple has in its book but none ready is not on the list -- it is out of it
        // until it memorises again.
        var offered = TempleSpells.Offered(
            Book(("a", 1, 2), ("bb", 2, 0)), CostFactor.Normal, maxLevel: 9, Flat);

        Assert.Single(offered);
        Assert.Equal("a", offered[0].SpellId);
    }

    [Fact]
    public void The_price_goes_through_the_temples_cost_factor()
    {
        var offered = TempleSpells.Offered(
            Book(("a", 1, 1)), CostFactor.Divide4, maxLevel: 9, Flat);

        Assert.Equal(25, offered[0].Cost);
    }

    [Fact]
    public void A_free_temple_charges_nothing()
    {
        var offered = TempleSpells.Offered(
            Book(("a", 1, 1)), CostFactor.Free, maxLevel: 9, Flat);

        Assert.Equal(0, offered[0].Cost);
    }

    [Fact]
    public void A_generous_but_not_free_temple_still_charges_a_coin()
    {
        var offered = TempleSpells.Offered(
            Book(("a", 1, 1)), CostFactor.Divide100, maxLevel: 9, _ => ("A", 1, 1));

        Assert.Equal(1, offered[0].Cost);
    }

    [Fact]
    public void A_spell_above_the_temples_maximum_level_is_not_offered()
    {
        var offered = TempleSpells.Offered(
            Book(("a", 1, 1), ("bbbbb", 5, 1)), CostFactor.Normal, maxLevel: 3, Flat);

        Assert.Single(offered);
        Assert.Equal("a", offered[0].SpellId);
    }

    [Fact]
    public void A_spell_the_design_no_longer_has_is_skipped()
    {
        var offered = TempleSpells.Offered(
            Book(("gone", 1, 1)), CostFactor.Normal, maxLevel: 9, _ => null);

        Assert.Empty(offered);
    }

    [Fact]
    public void The_name_shown_comes_from_the_design_and_not_the_book()
    {
        var offered = TempleSpells.Offered(
            Book(("cure", 1, 1)), CostFactor.Normal, maxLevel: 9,
            _ => ("Cure Light Wounds", 1, 50));

        Assert.Equal("Cure Light Wounds", offered[0].Name);
        Assert.Equal("cure", offered[0].SpellId);
    }

    [Fact]
    public void An_empty_book_offers_nothing()
    {
        Assert.Empty(TempleSpells.Offered(Book(), CostFactor.Normal, 9, Flat));
    }
}
