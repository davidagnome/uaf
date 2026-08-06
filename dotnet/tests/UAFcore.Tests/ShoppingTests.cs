using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers buying from a shop: what things weigh, and what a purchase does.</summary>
public class ShoppingTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>An item record carrying only what buying reads.</summary>
    private static ItemRecord Record(int cost = 0, int encumbrance = 0, int bundle = 0,
                                     int charges = 0, int cursed = 0) =>
        new(new ItemNames(0, "", "", "", "", "", ""),
            HitArt: null, MissileArt: null,
            new ItemScalars("", 0, cost, encumbrance, 0, cursed, bundle, charges),
            new ItemCombat(ReadiedLocation.WeaponHand, 1, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, 0, 0,
                         new SpecabBlock([], [], []), []));

    private static Character Member(int gold = 1000, int maxEncumbrance = 1000,
                                    CharacterStatus status = CharacterStatus.Okay)
    {
        var record = new CharacterRecord(
            0, 0, 0, "human", 0, "fighter", 0, 0, (int)status, "", 0, "Aramil", "",
            0, 0, 0, maxEncumbrance, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [], [], [], 0, 0, 0, null, 0,
            null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

        var who = new Character(record, MoneyRules.Default);
        who.Purse.Add(MoneyRules.Default.BaseType, gold);
        return who;
    }

    private static Party WithMember(Character who)
    {
        var party = new Party();
        party.Add(who);
        return party;
    }

    private static Func<string, ItemRecord?> Only(string id, ItemRecord record) =>
        candidate => candidate == id ? record : null;

    private static int Coins(Character who) =>
        who.Purse[MoneyRules.Default.BaseType];

    // ---- what things weigh -------------------------------------------------------------------

    [Fact]
    public void A_bundles_stated_weight_is_for_the_whole_bundle()
    {
        // 20 arrows weighing 2 -- 0.1 each, and the quantity multiplies back up.
        var quiver = Record(encumbrance: 2, bundle: 20);

        Assert.Equal(2, Shopping.ItemWeight("arrow", quiver, 20, coinsPerUnit: 100));
        Assert.Equal(1, Shopping.ItemWeight("arrow", quiver, 10, coinsPerUnit: 100));
    }

    [Fact]
    public void Part_of_a_bundle_can_weigh_nothing()
    {
        // 0.1 truncates to 0, so nine arrows are free to carry and the tenth costs a whole unit.
        var quiver = Record(encumbrance: 2, bundle: 20);

        Assert.Equal(0, Shopping.ItemWeight("arrow", quiver, 1, coinsPerUnit: 100));
        Assert.Equal(0, Shopping.ItemWeight("arrow", quiver, 9, coinsPerUnit: 100));
    }

    [Fact]
    public void An_unbundled_item_weighs_what_it_says()
    {
        var sword = Record(encumbrance: 60, bundle: 0);

        Assert.Equal(60, Shopping.ItemWeight("sword", sword, 1, coinsPerUnit: 100));
        Assert.Equal(120, Shopping.ItemWeight("sword", sword, 2, coinsPerUnit: 100));
    }

    [Fact]
    public void Nothing_and_nothing_known_weigh_nothing()
    {
        var sword = Record(encumbrance: 60);

        Assert.Equal(0, Shopping.ItemWeight("sword", sword, 0, coinsPerUnit: 100));
        Assert.Equal(0, Shopping.ItemWeight("sword", sword, -1, coinsPerUnit: 100));
        Assert.Equal(0, Shopping.ItemWeight("ghost", null, 5, coinsPerUnit: 100));
    }

    [Fact]
    public void A_single_gem_weighs_a_whole_unit()
    {
        // The money branch floors at 1, so one of anything is a unit -- and a hundred of them,
        // at a hundred per unit, are also one.
        Assert.Equal(1, Shopping.ItemWeight("_$GEM$_", null, 1, coinsPerUnit: 100));
        Assert.Equal(1, Shopping.ItemWeight("_$GEM$_", null, 100, coinsPerUnit: 100));
        Assert.Equal(2, Shopping.ItemWeight("_$GEM$_", null, 200, coinsPerUnit: 100));
    }

    [Fact]
    public void Money_weighs_a_unit_each_when_the_design_gives_no_rate()
    {
        // GetWeight() of 0 skips the division entirely and returns the raw count -- the opposite
        // of the purse's own branch, which makes coins weightless in the same case.
        Assert.Equal(200, Shopping.ItemWeight("_$GEM$_", null, 200, coinsPerUnit: 0));
    }

    [Fact]
    public void An_empty_purse_weighs_one_unit()
    {
        // 0/100 floored at 1. Every character in a design that weighs coins carries a unit of
        // nothing.
        var purse = new Purse(MoneyRules.Default);

        Assert.Equal(1, Shopping.PurseWeight(purse, coinsPerUnit: 100));
    }

    [Fact]
    public void A_purse_weighs_nothing_when_coins_weigh_nothing()
    {
        var purse = new Purse(MoneyRules.Default);
        purse.Add(MoneyRules.Default.BaseType, 10_000);

        Assert.Equal(0, Shopping.PurseWeight(purse, coinsPerUnit: 0));
    }

    [Fact]
    public void A_gem_weighs_as_much_as_a_coin()
    {
        var purse = new Purse(MoneyRules.Default);
        purse.Add(MoneyRules.Default.BaseType, 98);
        purse.AddGem(new GemType(0, 5000));
        purse.AddJewelry(new GemType(0, 5000));

        // 98 coins + 1 gem + 1 piece = 100 pieces, whatever the gems appraised at.
        Assert.Equal(1, Shopping.PurseWeight(purse, coinsPerUnit: 100));
    }

    [Fact]
    public void What_a_character_carries_is_the_purse_plus_the_pack()
    {
        var who = Member(gold: 100);
        who.Items.Add(new ItemInstance(1, "sword", 0, Inventory.NotReady, 1, 1, 0, 0, 0));

        int carried = Shopping.Carried(who, coinsPerUnit: 100,
                                       Only("sword", Record(encumbrance: 60)));

        Assert.Equal(61, carried);          // one unit of coins, sixty of sword
    }

    // ---- buying ------------------------------------------------------------------------------

    [Fact]
    public void A_dead_buyer_buys_nothing()
    {
        var who = Member(status: CharacterStatus.Dead);
        var sword = Record(cost: 10);

        var refusal = Shopping.Buy(who, WithMember(who), "sword", sword, CostFactor.Normal,
                                   100, Only("sword", sword));

        Assert.Equal(BuyRefusal.NotWell, refusal);
        Assert.Empty(who.Items);
    }

    [Fact]
    public void An_id_the_design_lost_buys_nothing()
    {
        var who = Member();

        var refusal = Shopping.Buy(who, WithMember(who), "ghost", null, CostFactor.Normal,
                                   100, _ => null);

        Assert.Equal(BuyRefusal.UnknownItem, refusal);
        Assert.Empty(who.Items);
    }

    [Fact]
    public void A_price_beyond_the_purse_leaves_it_untouched()
    {
        var who = Member(gold: 5);
        var sword = Record(cost: 10);

        var refusal = Shopping.Buy(who, WithMember(who), "sword", sword, CostFactor.Normal,
                                   100, Only("sword", sword));

        Assert.Equal(BuyRefusal.NotEnoughMoney, refusal);
        Assert.Empty(who.Items);
        Assert.Equal(5, Coins(who));
    }

    [Fact]
    public void The_shops_factor_is_what_gets_charged()
    {
        var who = Member(gold: 100);
        var sword = Record(cost: 40);

        var refusal = Shopping.Buy(who, WithMember(who), "sword", sword, CostFactor.Divide2,
                                   100, Only("sword", sword));

        Assert.Equal(BuyRefusal.None, refusal);
        Assert.Equal(80, Coins(who));
        Assert.Equal(20, who.Items[0].Paid);   // what the shop charged, not the database price
    }

    [Fact]
    public void A_free_shop_charges_nothing_and_still_hands_it_over()
    {
        var who = Member(gold: 0);
        var sword = Record(cost: 40);

        var refusal = Shopping.Buy(who, WithMember(who), "sword", sword, CostFactor.Free,
                                   100, Only("sword", sword));

        Assert.Equal(BuyRefusal.None, refusal);
        Assert.Single(who.Items);
        Assert.Equal(0, who.Items[0].Paid);
    }

    [Fact]
    public void Bought_goods_are_identified_and_carry_the_records_charges_and_curse()
    {
        var who = Member();
        var wand = Record(cost: 10, charges: 7, cursed: 1);

        Shopping.Buy(who, WithMember(who), "wand", wand, CostFactor.Normal,
                     100, Only("wand", wand));

        var bought = who.Items[0];
        Assert.Equal("wand", bought.ItemId);
        Assert.Equal(1, bought.Identified);
        Assert.Equal(7, bought.Charges);
        Assert.Equal(1, bought.Cursed);
        Assert.Equal(Inventory.NotReady, bought.ReadyLocation);
    }

    [Fact]
    public void A_bundle_arrives_whole()
    {
        var who = Member();
        var quiver = Record(cost: 10, bundle: 20);

        Shopping.Buy(who, WithMember(who), "arrow", quiver, CostFactor.Normal,
                     100, Only("arrow", quiver));

        Assert.Equal(20, who.Items[0].Quantity);
    }

    [Fact]
    public void Nothing_stacks()
    {
        // AddItem is called with auto-join off, so ten daggers are ten rows with ten keys.
        var who = Member();
        var dagger = Record(cost: 10);
        var party = WithMember(who);

        Shopping.Buy(who, party, "dagger", dagger, CostFactor.Normal, 100, Only("dagger", dagger));
        Shopping.Buy(who, party, "dagger", dagger, CostFactor.Normal, 100, Only("dagger", dagger));

        Assert.Equal(2, who.Items.Count);
        Assert.Equal([1, 2], who.Items.Select(i => i.Key));   // keys start at 1, never 0
    }

    [Fact]
    public void One_of_them_being_too_heavy_says_so()
    {
        var who = Member(gold: 1000, maxEncumbrance: 50);
        var anvil = Record(cost: 10, encumbrance: 60);

        var refusal = Shopping.Buy(who, WithMember(who), "anvil", anvil, CostFactor.Normal,
                                   100, Only("anvil", anvil));

        Assert.Equal(BuyRefusal.TooMuchWeight, refusal);
        Assert.Empty(who.Items);
        Assert.Equal(1000, Coins(who));
    }

    [Fact]
    public void A_bundle_too_heavy_to_carry_reports_the_wrong_error()
    {
        // The first gate weighs ONE arrow -- 100/20 = 5, which fits. addCharacterItem then weighs
        // the bundle -- 100 -- and refuses, setting TooMuchWeight, and buyItem's else overwrites
        // that with MaxItemsReached. The message a player sees is about item count, not weight.
        var who = Member(gold: 1000, maxEncumbrance: 50);
        var quiver = Record(cost: 10, encumbrance: 100, bundle: 20);

        var refusal = Shopping.Buy(who, WithMember(who), "arrow", quiver, CostFactor.Normal,
                                   100, Only("arrow", quiver));

        Assert.Equal(BuyRefusal.MaxItemsReached, refusal);
        Assert.Empty(who.Items);
        Assert.Equal(1000, Coins(who));
    }

    [Fact]
    public void What_is_already_carried_counts_against_the_next_purchase()
    {
        var who = Member(gold: 1000, maxEncumbrance: 100);
        var sword = Record(cost: 10, encumbrance: 60);
        var party = WithMember(who);

        Assert.Equal(BuyRefusal.None,
                     Shopping.Buy(who, party, "sword", sword, CostFactor.Normal, 0,
                                  Only("sword", sword)));

        // 60 carried plus 60 more is over 100.
        Assert.Equal(BuyRefusal.TooMuchWeight,
                     Shopping.Buy(who, party, "sword", sword, CostFactor.Normal, 0,
                                  Only("sword", sword)));

        Assert.Single(who.Items);
    }

    // ---- the pool ----------------------------------------------------------------------------

    [Fact]
    public void Pooled_money_is_spent_before_the_characters_own()
    {
        var who = Member(gold: 100);
        var party = WithMember(who);
        party.MoneyPooled = 1;
        party.Pooled.Add(MoneyRules.Default.BaseType, 500);

        var sword = Record(cost: 40);
        Shopping.Buy(who, party, "sword", sword, CostFactor.Normal, 100, Only("sword", sword));

        Assert.Equal(460, party.Pooled[MoneyRules.Default.BaseType]);
        Assert.Equal(100, Coins(who));
    }

    [Fact]
    public void Spending_the_pool_dry_unpools_the_party()
    {
        var who = Member(gold: 100);
        var party = WithMember(who);
        party.MoneyPooled = 1;
        party.Pooled.Add(MoneyRules.Default.BaseType, 40);

        var sword = Record(cost: 40);
        Shopping.Buy(who, party, "sword", sword, CostFactor.Normal, 100, Only("sword", sword));

        Assert.Equal(0, party.MoneyPooled);
        Assert.Equal(100, Coins(who));
    }

    [Fact]
    public void A_pool_too_small_falls_through_to_the_character()
    {
        var who = Member(gold: 100);
        var party = WithMember(who);
        party.MoneyPooled = 1;
        party.Pooled.Add(MoneyRules.Default.BaseType, 10);

        var sword = Record(cost: 40);
        var refusal = Shopping.Buy(who, party, "sword", sword, CostFactor.Normal,
                                   100, Only("sword", sword));

        Assert.Equal(BuyRefusal.None, refusal);
        Assert.Equal(10, party.Pooled[MoneyRules.Default.BaseType]);   // untouched
        Assert.Equal(60, Coins(who));
    }
}
