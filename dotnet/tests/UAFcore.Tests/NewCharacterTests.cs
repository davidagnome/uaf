using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers what a new character starts with, beyond its rolled abilities.
/// </summary>
public class NewCharacterTests
{
    private static readonly DicePlus NoDice = new("", "", "", 0, 0, 0, 0, 0, 0, []);

    private static ClassRecord Class(string name, string[] baseclasses,
                                     params ItemInstance[] equipment) =>
        new("ClassV1", 0, name, baseclasses, new SpecabBlock([], [], []), [], NoDice,
            new ItemList(equipment, ReadyItems.Empty), "");

    private static ItemInstance Item(string id) =>
        new(0, id, 0, Inventory.NotReady, 1, 1, 0, 0, 0);

    // ---- money ---------------------------------------------------------------------------------

    [Fact]
    public void The_start_platinum_field_is_not_platinum()
    {
        // The amount goes in at the design's own base denomination, so a design whose currency is
        // copper starts its characters with that many copper pieces. The field name is a leftover
        // from when the denominations were fixed.
        var money = MoneyRules.Default;

        var purse = NewCharacter.StartingMoney(coins: 250, gems: 0, jewelry: 0, money);

        Assert.Equal(250, purse[money.BaseType]);
    }

    [Fact]
    public void Gems_and_jewellery_come_out_as_counts()
    {
        var purse = NewCharacter.StartingMoney(coins: 0, gems: 2, jewelry: 3,
                                               MoneyRules.Default);

        Assert.Equal(2, purse.Gems.Count);
        Assert.Equal(3, purse.Jewelry.Count);
    }

    [Fact]
    public void A_design_that_gives_nothing_makes_an_empty_purse()
    {
        Assert.True(NewCharacter.StartingMoney(0, 0, 0, MoneyRules.Default).IsEmpty);
    }

    // ---- equipment and baseclasses -------------------------------------------------------------

    [Fact]
    public void Equipment_is_copied_off_the_class_wholesale()
    {
        // No per-baseclass contribution and no merging: a multi-class character gets its class
        // record's list and nothing from the baseclasses underneath it.
        var record = Class("Fighter/Mage", ["fighter", "mage"], Item("Sword"), Item("Robe"));

        Assert.Equal(["Sword", "Robe"],
                     NewCharacter.StartingEquipment(record).Select(i => i.ItemId));
    }

    [Fact]
    public void A_class_the_design_does_not_define_gives_nothing()
    {
        Assert.Empty(NewCharacter.StartingEquipment(null));
        Assert.Empty(NewCharacter.BaseclassRows(null));
    }

    [Fact]
    public void One_row_per_baseclass_all_at_first_level()
    {
        // The starting experience is given afterwards and the levelling walks up from there, so a
        // character who begins above first level does so by being awarded experience and trained,
        // not by being created at that level.
        var rows = NewCharacter.BaseclassRows(Class("Fighter/Mage", ["fighter", "mage"]));

        Assert.Equal(["fighter", "mage"], rows.Select(r => r.BaseclassId));
        Assert.All(rows, r => Assert.Equal(1, r.CurrentLevel));
        Assert.All(rows, r => Assert.Equal(0, r.Experience));
        Assert.All(rows, r => Assert.Equal(0, r.PreviousLevel));
    }

    // ---- age and birthday ----------------------------------------------------------------------

    [Fact]
    public void A_birthday_is_a_day_of_the_year()
    {
        int count = 0;
        int sides = 0;

        NewCharacter.Birthday((c, s) => { count = c; sides = s; return 200; });

        Assert.Equal(1, count);
        Assert.Equal(365, sides);
    }

    [Fact]
    public void The_start_age_floor_only_applies_when_it_is_positive()
    {
        // START_AGE is design configuration; a zero or negative one leaves the race's roll alone
        // rather than clamping everything to it.
        Assert.Equal(20, NewCharacter.StartAge(rolledAge: 14, minimumAge: 20));
        Assert.Equal(30, NewCharacter.StartAge(rolledAge: 30, minimumAge: 20));
        Assert.Equal(14, NewCharacter.StartAge(rolledAge: 14, minimumAge: 0));
        Assert.Equal(14, NewCharacter.StartAge(rolledAge: 14, minimumAge: -5));
    }

    [Fact]
    public void A_races_dice_field_rolls_through_the_expression_evaluator()
    {
        // Weight is the field that uses the gender bonus, and it is the shape the corpus is full
        // of: dice, a constant, and a parenthesised multiple of Male.
        var weight = NoDice with { Text = "2d4+34+(1*Male)" };

        Assert.Equal(37, NewCharacter.Roll(weight, (c, _) => c, male: true, out _));
        Assert.Equal(36, NewCharacter.Roll(weight, (c, _) => c, male: false, out _));
    }

    [Fact]
    public void An_empty_or_unsupported_field_rolls_nothing_and_says_which()
    {
        // Null is the reference's own "did not roll" -- GetStartAge returns 0 when Roll answers
        // false, so an empty field and a refused one are the same answer there. Keeping them
        // distinct is what lets a caller say what happened.
        Assert.Null(NewCharacter.Roll(NoDice, (c, _) => c, male: true, out string? empty));
        Assert.Contains("empty", empty);

        var odd = NoDice with { Text = "1d6+CharLevel" };
        Assert.Null(NewCharacter.Roll(odd, (c, _) => c, male: true, out string? why));
        Assert.Contains("CharLevel", why);

        Assert.Null(NewCharacter.Roll(null, (c, _) => c, male: true, out _));
    }

    [Fact]
    public void The_cap_is_applied_after_the_floor_and_they_are_not_reconciled()
    {
        // A race whose maximum age is below the design's minimum starting age produces a
        // character born at its own limit; the two clamps never look at each other.
        int aged = NewCharacter.StartAge(rolledAge: 5, minimumAge: 20);

        Assert.Equal(12, NewCharacter.CapAge(aged, maxAge: 12));
    }
}
