using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Confiscating the party's goods (<c>TAKE_PARTY_ITEMS_DATA</c>).
/// </summary>
/// <remarks>
/// <b>This type appears zero times across the corpus</b> — 6,236 events over 23 levels — so every
/// assertion here is transcription from <c>Party.cpp</c> rather than observation, and the awkward
/// parts are reproduced rather than tidied.
/// </remarks>
public class EventTakeItemsTests
{
    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static CharacterRecord Member() =>
        new(0, 0, "human", 0, "fighter", 0, 0, 0, "", 0, "Aramil", "",
            0, 0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [new BaseclassStats("fighter", 0, 0, 0, 0)], [], [], 0, 0, 0,
            null, 0, null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

    private static Party Roster(int members = 1)
    {
        var party = new Party { Pooled = new Purse(MoneyRules.Default) };
        for (int i = 0; i < members; i++)
        {
            party.Add(new Character(Member(), MoneyRules.Default));
        }
        return party;
    }

    private static ItemInstance Item(string id, int quantity = 1, uint readyLocation = 0,
                                     byte cursed = 0) =>
        new(0, id, 0, readyLocation, quantity, 0, 0, cursed, 0);

    private static TakePartyItemsEvent Take(
        TakeItemsAction what = TakeItemsAction.Inventory,
        TakeItemsAffects affects = TakeItemsAffects.Party,
        TakeQuantity itemQty = TakeQuantity.Specified,
        TakeQuantity moneyQty = TakeQuantity.Specified,
        TakeQuantity gemQty = TakeQuantity.Specified,
        TakeQuantity jewelQty = TakeQuantity.Specified,
        int platinum = 0, int gems = 0, int jewelry = 0, int itemCount = 1,
        ItemClass moneyType = ItemClass.Platinum,
        int storeItems = 0,
        params ItemInstance[] items) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.TakePartyItems, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            storeItems, 0, (byte)what, (int)affects,
            (int)itemQty, (int)moneyQty, (int)gemQty, (int)jewelQty,
            platinum, gems, jewelry, itemCount, (int)moneyType, 0,
            new ItemList(items, new ReadyItems(new int[12])));

    private static int NoDice(int sides) =>
        throw new InvalidOperationException("this path must not roll");

    // ---- the denomination, which is the opposite of the sibling event's ---------------------------

    [Fact]
    public void The_money_type_is_taken_at_face_value_rather_than_defaulted_to_platinum()
    {
        // WHO_PAYS restores PlatinumType for a stored 0 because its field was gated at 0.912 and a
        // pre-0.912 design has no value on disk. THIS event has no gate on moneyType at all --
        // both serializers read it unconditionally and only WhichVault, two lines later, is gated
        // (GameEvent.cpp:8368). So the value on disk is always what the design authored, and
        // substituting a default would silently rewrite it.
        Assert.Equal(ItemClass.Gold, EventTakeItems.Currency(Take(moneyType: ItemClass.Gold)));
        Assert.Equal((ItemClass)0, EventTakeItems.Currency(Take(moneyType: (ItemClass)0)));
    }

    // ---- the quantity rules ----------------------------------------------------------------------

    [Fact]
    public void Nothing_is_taken_from_someone_who_has_none_not_even_under_take_all()
    {
        // The `data == 0` guard is the first line of TakePartyItemQty, above the switch
        // (Party.cpp:2456), so TakeAll returns 0 rather than falling through the arithmetic.
        Assert.Equal(0, EventTakeItems.Quantity(TakeQuantity.All, 5, available: 0, NoDice));
        Assert.Equal(0, EventTakeItems.Quantity(TakeQuantity.Specified, 5, available: 0, NoDice));
    }

    [Theory]
    [InlineData(TakeQuantity.Specified, 3, 10, 3)]
    [InlineData(TakeQuantity.Specified, 30, 10, 10)]      // clamped to what is held
    [InlineData(TakeQuantity.All, 3, 10, 10)]             // the authored number is ignored
    [InlineData(TakeQuantity.Percent, 50, 10, 5)]
    [InlineData(TakeQuantity.Percent, 33, 10, 3)]         // truncates toward zero
    public void The_four_rules_measure_what_they_say(TakeQuantity type, int amount,
                                                     int available, int expected)
    {
        Assert.Equal(expected, EventTakeItems.Quantity(type, amount, available, NoDice));
    }

    [Fact]
    public void A_random_take_of_no_sides_rolls_zero_rather_than_one()
    {
        // RollDice returns its bonus for a non-positive die (Globals.cpp:4927), so this takes
        // nothing -- where a roller floored at 1 would take one.
        Assert.Equal(0, EventTakeItems.Quantity(TakeQuantity.Random, 0, available: 10, NoDice));
        Assert.Equal(4, EventTakeItems.Quantity(TakeQuantity.Random, 6, 10, _ => 4));
    }

    [Fact]
    public void An_unrecognised_rule_takes_nothing()
    {
        // The switch is defaultless and qty was initialised to 0.
        Assert.Equal(0, EventTakeItems.Quantity((TakeQuantity)99, 5, available: 10, NoDice));
    }

    // ---- who it falls on -------------------------------------------------------------------------

    [Fact]
    public void Only_the_random_mode_consumes_a_roll()
    {
        // The roll sits INSIDE the switch, the reverse of HEAL_PARTY_DATA, whose rndDude is drawn
        // above its own switch and so moves the generator on for every heal event. Anything
        // replaying a recorded run has to get this right in both directions.
        var party = Roster(3);

        Assert.Equal(-1, EventTakeItems.Victim(Take(affects: TakeItemsAffects.Party), party, NoDice));

        party.ActiveCharacter = 2;
        Assert.Equal(2, EventTakeItems.Victim(
            Take(affects: TakeItemsAffects.ActiveCharacter), party, NoDice));

        Assert.Equal(1, EventTakeItems.Victim(
            Take(affects: TakeItemsAffects.RandomCharacter), party, _ => 2));
    }

    [Fact]
    public void An_empty_party_rolls_nothing_and_lands_on_the_whole_party_sentinel()
    {
        Assert.Equal(-1, EventTakeItems.Victim(
            Take(affects: TakeItemsAffects.RandomCharacter), Roster(0), NoDice));
    }

    [Fact]
    public void An_affect_value_outside_the_enum_is_the_whole_party()
    {
        // `dude` is initialised to -1 and the switch has no default (Party.cpp:2180).
        Assert.Equal(-1, EventTakeItems.Victim(
            Take(affects: (TakeItemsAffects)7), Roster(2), NoDice));
    }

    // ---- inventory -------------------------------------------------------------------------------

    [Fact]
    public void A_named_take_removes_up_to_the_authored_count()
    {
        var party = Roster();
        party.Members[0].Items.Add(Item("Rope", quantity: 3));

        var outcome = EventTakeItems.Apply(
            Take(itemCount: 2, items: Item("Rope")), party, NoDice);

        Assert.Equal(2, outcome.Items.Count);
        Assert.Equal(1, party.Members[0].Items[0].Quantity);
    }

    [Fact]
    public void Item_names_are_matched_case_sensitively()
    {
        // GetListKeyByItemName compares two ITEM_IDs, which is CString::operator== -- a plain
        // strcmp. This differs deliberately from Party.HasItem, which folds case.
        var party = Roster();
        party.Members[0].Items.Add(Item("Rope"));

        var outcome = EventTakeItems.Apply(Take(items: Item("rope")), party, NoDice);

        Assert.Empty(outcome.Items);
        Assert.Single(party.Members[0].Items);
    }

    [Fact]
    public void Random_and_percent_take_no_items_at_all()
    {
        // "not used for items" -- Party.cpp:2261 -- and the editor drops both from that one combo.
        // So an event carrying either still takes money and valuables and no inventory.
        foreach (var rule in new[] { TakeQuantity.Random, TakeQuantity.Percent })
        {
            var party = Roster();
            party.Members[0].Items.Add(Item("Rope", quantity: 5));

            var outcome = EventTakeItems.Apply(
                Take(itemQty: rule, itemCount: 5, items: Item("Rope")), party, NoDice);

            Assert.Empty(outcome.Items);
            Assert.Single(party.Members[0].Items);
        }
    }

    [Fact]
    public void Take_all_empties_the_pack_and_keeps_whole_stacks()
    {
        var party = Roster();
        party.Members[0].Items.Add(Item("Rope", quantity: 5));
        party.Members[0].Items.Add(Item("Torch", quantity: 2));

        var outcome = EventTakeItems.Apply(Take(itemQty: TakeQuantity.All), party, NoDice);

        Assert.Equal([5, 2], outcome.Items.Select(i => i.Quantity));
        Assert.Empty(party.Members[0].Items);
    }

    [Fact]
    public void A_readied_cursed_item_survives_take_all_and_the_walk_moves_past_it()
    {
        // DeleteItem refuses when UnReady fails, and the walk steps over it rather than looping.
        var party = Roster();
        party.Members[0].Items.Add(Item("Cursed Sword", readyLocation: 1, cursed: 1));
        party.Members[0].Items.Add(Item("Rope"));

        var outcome = EventTakeItems.Apply(Take(itemQty: TakeQuantity.All), party, NoDice);

        Assert.Equal(["Rope"], outcome.Items.Select(i => i.ItemId));
        Assert.Equal(["Cursed Sword"], party.Members[0].Items.Select(i => i.ItemId));
    }

    // ---- gems and jewellery ----------------------------------------------------------------------

    [Fact]
    public void Gems_are_counted_never_valued_and_come_off_the_head()
    {
        // A one-gem take can carry off the 5,000gp stone and leave the 10gp one.
        var party = Roster();
        party.Members[0].Purse.AddGem(new GemType(1, 5000));
        party.Members[0].Purse.AddGem(new GemType(2, 10));

        var outcome = EventTakeItems.Apply(
            Take(what: TakeItemsAction.Gems, gems: 1), party, NoDice);

        Assert.Equal([5000], outcome.Gems.Select(g => g.Value));
        Assert.Equal([10], party.Members[0].Purse.Gems.Select(g => g.Value));
    }

    // ---- money -----------------------------------------------------------------------------------

    [Fact]
    public void The_whole_party_is_each_charged_in_full_rather_than_a_share()
    {
        // Every loop runs the same take against every character in turn, so three members and a
        // 10-coin take is 30 coins. The reference's own comment at Party.cpp:2414 says "take
        // equally from all party members", which describes something it does not do.
        var party = Roster(3);
        foreach (var who in party.Members)
        {
            who.Purse.Add(MoneyRules.Default.BaseType, 100);
        }

        var outcome = EventTakeItems.Apply(
            Take(what: TakeItemsAction.Money, platinum: 10,
                 moneyType: MoneyRules.Default.BaseType), party, NoDice);

        Assert.Equal(30, outcome.Money);
        Assert.All(party.Members, w => Assert.Equal(90, (int)w.Purse.Total()));
    }

    [Fact]
    public void A_percentage_of_money_is_a_percentage_of_the_base_converted_amount()
    {
        // Reproduced, not repaired: qty is ConvertToBase(platinum, moneyType) BEFORE the four
        // rules see it (Party.cpp:2393), so a percentage authored in a coin above the base is
        // multiplied by the exchange rate and empties the purse. It behaves as authored only when
        // moneyType IS the base coin, where the conversion is the identity.
        var party = Roster();
        party.Members[0].Purse.Add(MoneyRules.Default.BaseType, 100);

        var outcome = EventTakeItems.Apply(
            Take(what: TakeItemsAction.Money, moneyQty: TakeQuantity.Percent, platinum: 50,
                 moneyType: MoneyRules.Default.BaseType), party, NoDice);

        Assert.Equal(50, outcome.Money);                 // the identity case, as authored
    }

    [Fact]
    public void Nothing_is_deposited_for_a_vault_unless_the_event_stores()
    {
        // The reference only performs the coin conversion when StoreItems is set.
        var party = Roster();
        party.Members[0].Purse.Add(MoneyRules.Default.BaseType, 100);

        var kept = EventTakeItems.Apply(
            Take(what: TakeItemsAction.Money, platinum: 10,
                 moneyType: MoneyRules.Default.BaseType), party, NoDice);

        Assert.Empty(kept.MoneyForVault);

        party.Members[0].Purse.Add(MoneyRules.Default.BaseType, 100);
        var stored = EventTakeItems.Apply(
            Take(what: TakeItemsAction.Money, platinum: 10,
                 moneyType: MoneyRules.Default.BaseType, storeItems: 1), party, NoDice);

        Assert.NotEmpty(stored.MoneyForVault);
    }

    [Fact]
    public void The_pooled_purse_pays_first_and_draining_it_un_pools_the_party()
    {
        // payForItem spends from the pool whenever the party has pooled and it covers the charge,
        // and party.moneyPooled is then recomputed from whether the pool is empty.
        var party = Roster();
        party.Members[0].Purse.Add(MoneyRules.Default.BaseType, 100);
        party.Pooled.Add(MoneyRules.Default.BaseType, 10);
        party.MoneyPooled = 1;

        EventTakeItems.Apply(
            Take(what: TakeItemsAction.Money, platinum: 10,
                 moneyType: MoneyRules.Default.BaseType), party, NoDice);

        Assert.Equal(100, (int)party.Members[0].Purse.Total());
        Assert.Equal(0, (int)party.Pooled.Total());
        Assert.Equal(0, party.MoneyPooled);
    }

    // ---- the bitmask -----------------------------------------------------------------------------

    [Fact]
    public void The_four_classes_are_independent_bits()
    {
        var party = Roster();
        party.Members[0].Items.Add(Item("Rope"));
        party.Members[0].Purse.AddGem(new GemType(1, 10));

        var outcome = EventTakeItems.Apply(
            Take(what: TakeItemsAction.Inventory | TakeItemsAction.Gems,
                 gems: 1, items: Item("Rope")), party, NoDice);

        Assert.Single(outcome.Items);
        Assert.Single(outcome.Gems);
        Assert.Empty(party.Members[0].Items);
        Assert.Empty(party.Members[0].Purse.Gems);
    }

    [Fact]
    public void An_event_that_takes_nothing_removes_nothing()
    {
        var party = Roster();
        party.Members[0].Items.Add(Item("Rope"));

        var outcome = EventTakeItems.Apply(Take(what: 0, items: Item("Rope")), party, NoDice);

        Assert.Empty(outcome.Items);
        Assert.Single(party.Members[0].Items);
    }
}
