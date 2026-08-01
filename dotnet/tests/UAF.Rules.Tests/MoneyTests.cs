using UAF.Rules;
using UAF.Serialization;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers the currency system: rates, conversion, and what a purse does when money moves.
/// </summary>
/// <remarks>
/// Expectations are worked out from the AD&amp;D rates the reference installs — platinum 1,
/// gold 5, electrum 10, silver 100, copper 1000, meaning "this many coins per platinum" — and from
/// the arithmetic in <c>Money.cpp</c>, not from running this implementation. The rates are the one
/// thing in here a reader can check against the source comment at <c>Money.cpp:515</c>.
/// </remarks>
public class MoneyTests
{
    private static readonly MoneyRules Rules = MoneyRules.Default;

    private static Purse Purse(params (ItemClass Type, int Amount)[] coins)
    {
        var purse = new Purse(Rules);
        foreach (var (type, amount) in coins)
        {
            purse.Add(type, amount);
        }

        return purse;
    }

    // ---- rates and slots ---------------------------------------------------------------------

    [Fact]
    public void The_slot_mapping_is_two_ranges_rather_than_one_offset()
    {
        // The five AD&D types map by subtracting one; the five spares by subtracting seven, because
        // BogusItemType at 11 sits between them. A single arithmetic conversion is wrong.
        Assert.Equal(0, MoneyRules.IndexOf(ItemClass.Platinum));
        Assert.Equal(4, MoneyRules.IndexOf(ItemClass.Copper));
        Assert.Equal(5, MoneyRules.IndexOf(ItemClass.Coin6));
        Assert.Equal(9, MoneyRules.IndexOf(ItemClass.Coin10));

        Assert.Equal(11, (int)ItemClass.BogusItem);
        Assert.Equal(12, (int)ItemClass.Coin6);

        for (int i = 0; i < MoneyRules.MaxCoinTypes; i++)
        {
            Assert.Equal(i, MoneyRules.IndexOf(MoneyRules.ClassOf(i)));
        }
    }

    [Fact]
    public void A_non_coin_class_is_rejected_rather_than_silently_mapped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneyRules.IndexOf(ItemClass.Gem));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneyRules.IndexOf(ItemClass.Item));
    }

    [Fact]
    public void The_base_type_is_the_highest_rate_coin_not_the_one_flagged_base()
    {
        // Two different notions of "base" live in MONEY_DATA_TYPE. The per-coin flag is on
        // platinum; GetBaseType returns the highest RATE, which is copper -- and it is the latter
        // that every total and price comparison goes through.
        Assert.True(Rules[ItemClass.Platinum].IsBase);
        Assert.False(Rules[ItemClass.Copper].IsBase);

        Assert.Equal(ItemClass.Copper, Rules.BaseType);
        Assert.Equal(1000, Rules.HighestRate);
    }

    [Fact]
    public void A_higher_rate_is_a_less_valuable_coin()
    {
        Assert.Equal(1.0, Rules.RateOf(ItemClass.Platinum));
        Assert.Equal(5.0, Rules.RateOf(ItemClass.Gold));
        Assert.Equal(1000.0, Rules.RateOf(ItemClass.Copper));
    }

    // ---- conversion --------------------------------------------------------------------------

    [Fact]
    public void Converting_upward_divides_and_downward_multiplies()
    {
        // 100 silver is 5 gold; 5 gold is 100 silver.
        Assert.Equal(5, Rules.Convert(100, ItemClass.Silver, ItemClass.Gold, out double up));
        Assert.Equal(0, up);

        Assert.Equal(100, Rules.Convert(5, ItemClass.Gold, ItemClass.Silver, out double down));
        Assert.Equal(0, down);
    }

    [Fact]
    public void What_will_not_divide_evenly_comes_back_as_overflow_in_the_source_coin()
    {
        // 105 copper is 10 silver and 5 copper left over -- and the 5 is copper, not silver.
        Assert.Equal(10, Rules.Convert(105, ItemClass.Copper, ItemClass.Silver,
                                       out double overflow));
        Assert.Equal(5, overflow);
    }

    [Fact]
    public void A_caller_ignoring_the_overflow_loses_the_remainder()
    {
        // Worth pinning because it is the failure mode: the convenience overload exists, and using
        // it where a remainder is possible silently destroys money.
        Assert.Equal(10, Rules.Convert(105, ItemClass.Copper, ItemClass.Silver));
    }

    [Fact]
    public void The_same_denomination_converts_one_for_one_and_zero_stays_zero()
    {
        Assert.Equal(42, Rules.Convert(42, ItemClass.Gold, ItemClass.Gold, out double same));
        Assert.Equal(0, same);

        Assert.Equal(0, Rules.Convert(0, ItemClass.Copper, ItemClass.Platinum, out double none));
        Assert.Equal(0, none);
    }

    [Fact]
    public void An_inactive_denomination_destroys_the_amount_rather_than_refusing()
    {
        // Coin6..Coin10 are unconfigured by default, and a conversion through one returns 0 with no
        // error. Reproduced -- a design that removes a coin type really does behave this way.
        var purse = Purse();

        Assert.Equal(0, Rules.Convert(500, ItemClass.Gold, ItemClass.Coin6, out double lost));
        Assert.Equal(0, lost);

        purse.Add(ItemClass.Coin6, 500);
        Assert.Equal(0, purse[ItemClass.Coin6]);
    }

    [Fact]
    public void Two_denominations_at_the_same_rate_exchange_one_for_one()
    {
        var rules = new MoneyRules(
        [
            new Coin(1.0, IsBase: true, "Crown"),
            new Coin(1.0, false, "Sovereign"),
        ]);

        Assert.Equal(7, rules.Convert(7, ItemClass.Platinum, ItemClass.Electrum, out double over));
        Assert.Equal(0, over);
    }

    // ---- totals ------------------------------------------------------------------------------

    [Fact]
    public void A_total_is_counted_in_the_smallest_denomination()
    {
        // 1 platinum + 1 gold + 1 copper = 1000 + 200 + 1 copper.
        var purse = Purse((ItemClass.Platinum, 1), (ItemClass.Gold, 1), (ItemClass.Copper, 1));

        Assert.Equal(1201, purse.Total());
    }

    [Fact]
    public void Gems_and_jewellery_are_not_part_of_the_total()
    {
        // They have to be appraised and sold, so a party holding only gems cannot buy anything.
        var purse = Purse();
        purse.AddGem(new GemType(0, 500));
        purse.AddJewelry(new GemType(0, 900));

        Assert.Equal(0, purse.Total());
        Assert.Equal(500, purse.TotalGemValue());
        Assert.Equal(900, purse.TotalJewelryValue());
        Assert.False(purse.IsEmpty);
    }

    [Fact]
    public void Affordability_compares_totals_so_any_coin_can_pay_any_price()
    {
        var purse = Purse((ItemClass.Platinum, 1));

        Assert.True(purse.HaveEnough(ItemClass.Copper, 1000));
        Assert.False(purse.HaveEnough(ItemClass.Copper, 1001));
        Assert.True(purse.HaveEnough(ItemClass.Gold, 5));
        Assert.False(purse.HaveEnough(ItemClass.Gold, 6));
    }

    // ---- spending ----------------------------------------------------------------------------

    [Fact]
    public void Spending_from_a_denomination_that_covers_it_just_takes_the_coins()
    {
        var purse = Purse((ItemClass.Gold, 10));
        purse.Subtract(ItemClass.Gold, 4);

        Assert.Equal(6, purse[ItemClass.Gold]);
    }

    [Fact]
    public void Nothing_is_taken_when_the_purse_cannot_cover_the_whole_amount()
    {
        // The HaveEnough guard comes first, so a partial payment is never made.
        var purse = Purse((ItemClass.Gold, 3));
        purse.Subtract(ItemClass.Gold, 4);

        Assert.Equal(3, purse[ItemClass.Gold]);
    }

    [Fact]
    public void A_shortfall_is_made_up_from_the_other_denominations()
    {
        // 1 platinum is 5 gold. Paying 3 gold with 1 gold in hand takes the platinum, converts it,
        // and puts the change back -- so the party ends with 0 platinum and 3 gold of change.
        var purse = Purse((ItemClass.Platinum, 1), (ItemClass.Gold, 1));
        Assert.Equal(1200, purse.Total());

        purse.Subtract(ItemClass.Gold, 3);

        // Whatever the change-making does with the denominations, the arithmetic has to balance:
        // 1200 copper less 3 gold (600 copper) is 600 copper.
        Assert.Equal(600, purse.Total());
    }

    [Fact]
    public void Small_change_is_spent_before_a_large_coin_is_broken()
    {
        // The change loop runs from the last slot backwards, which under the defaults is copper
        // first and platinum last.
        var purse = Purse((ItemClass.Platinum, 1), (ItemClass.Copper, 500));
        purse.Subtract(ItemClass.Silver, 5);         // 5 silver = 50 copper

        Assert.Equal(1, purse[ItemClass.Platinum]);  // the platinum is untouched
        Assert.Equal(1450, purse.Total());           // 1000 + 500 - 50
    }

    // ---- rolling up --------------------------------------------------------------------------

    [Fact]
    public void Coins_roll_up_through_value_order_not_slot_order()
    {
        // The slots run platinum, electrum, gold, silver, copper -- but electrum is worth LESS than
        // gold, so a roll-up ordered by slot would convert in the wrong direction. Sorting by rate
        // is what makes 1000 copper become 1 platinum.
        var purse = Purse((ItemClass.Copper, 1000));
        purse.AutoUpConvert();

        Assert.Equal(1, purse[ItemClass.Platinum]);
        Assert.Equal(0, purse[ItemClass.Copper]);
        Assert.Equal(1000, purse.Total());
    }

    [Fact]
    public void What_will_not_roll_up_stops_at_the_denomination_it_reached()
    {
        // 1050 copper rolls to 105 silver, then to 10 electrum with 5 silver behind; the electrum
        // rolls on to 1 platinum. The remainder is left as SILVER, not put back as copper -- each
        // step keeps its own overflow in its own source denomination and the roll-up moves on.
        var purse = Purse((ItemClass.Copper, 1050));
        purse.AutoUpConvert();

        Assert.Equal(1, purse[ItemClass.Platinum]);
        Assert.Equal(5, purse[ItemClass.Silver]);
        Assert.Equal(0, purse[ItemClass.Copper]);
        Assert.Equal(1050, purse.Total());
    }

    [Fact]
    public void Rolling_up_never_changes_what_the_purse_is_worth()
    {
        var purse = Purse((ItemClass.Copper, 733), (ItemClass.Silver, 41), (ItemClass.Gold, 3));
        double before = purse.Total();

        purse.AutoUpConvert();

        Assert.Equal(before, purse.Total());
    }

    [Fact]
    public void An_empty_early_slot_disables_rolling_up_altogether()
    {
        // The scan breaks on the first zero rate rather than skipping it, so a design that leaves
        // an early slot empty gets no roll-up. Ambassador's_Letter is exactly this shape: gold,
        // silver and copper configured, platinum and electrum left out.
        var rules = new MoneyRules(
        [
            Coin.Inactive,                              // platinum, not configured
            Coin.Inactive,                              // electrum, not configured
            new Coin(5.0, false, "Gold"),
            new Coin(100.0, false, "Silver"),
            new Coin(1000.0, false, "Copper"),
        ]);

        Assert.Equal([ItemClass.Gold, ItemClass.Silver, ItemClass.Copper], rules.ActiveTypes);
        Assert.Equal(ItemClass.Copper, rules.BaseType);

        var purse = new UAF.Rules.Purse(rules);
        purse.Add(ItemClass.Copper, 1000);
        purse.AutoUpConvert();

        Assert.Equal(1000, purse[ItemClass.Copper]);
        Assert.Equal(0, purse[ItemClass.Gold]);
    }

    [Fact]
    public void A_design_with_no_coin_flagged_base_still_has_a_working_base_type()
    {
        // Ambassador's_Letter sets the flag on none of its three coins. Anything reading the flag
        // to find the base denomination would find nothing; GetBaseType reads the rate instead.
        var rules = new MoneyRules(
        [
            Coin.Inactive, Coin.Inactive,
            new Coin(5.0, false, "Gold"),
            new Coin(100.0, false, "Silver"),
            new Coin(1000.0, false, "Copper"),
        ]);

        Assert.DoesNotContain(rules.ActiveTypes, t => rules[t].IsBase);
        Assert.Equal(ItemClass.Copper, rules.BaseType);

        var purse = new UAF.Rules.Purse(rules);
        purse.Add(ItemClass.Gold, 1);
        Assert.Equal(200, purse.Total());
    }

    // ---- design configuration ------------------------------------------------------------------

    [Fact]
    public void A_designs_own_currency_replaces_the_defaults_entirely()
    {
        var data = new MoneyData(10, 0, 0, 0, null, null,
        [
            new UAF.Serialization.CoinType(1.0, 1, "Crown"),
            new UAF.Serialization.CoinType(240.0, 0, "Penny"),
        ]);

        var rules = MoneyRules.FromDesign(data);

        Assert.Equal("Crown", rules[ItemClass.Platinum].Name);
        Assert.Equal("Penny", rules[ItemClass.Electrum].Name);

        // Base is the highest rate, so it is the penny -- and 1 crown totals 240.
        Assert.Equal(ItemClass.Electrum, rules.BaseType);

        var purse = new Purse(rules);
        purse.Add(ItemClass.Platinum, 1);
        Assert.Equal(240, purse.Total());
    }

    [Fact]
    public void A_purse_reads_back_from_the_record_a_savegame_carries()
    {
        var sack = new MoneySack([1, 0, 2, 0, 30], [new GemType(0, 50)], []);
        var purse = UAF.Rules.Purse.FromRecord(sack, Rules);

        Assert.Equal(1, purse[ItemClass.Platinum]);
        Assert.Equal(2, purse[ItemClass.Gold]);
        Assert.Equal(30, purse[ItemClass.Copper]);
        Assert.Single(purse.Gems);

        Assert.Equal(1000 + 400 + 30, purse.Total());
    }
}
