using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Paying a toll (<c>WHO_PAYS_EVENT_DATA</c>).
/// </summary>
/// <remarks>
/// <para>
/// The corpus contributes almost nothing here: six designs hold <b>one</b> <c>WHO_PAYS</c> event
/// between them — SomethingWild's Level002 event 65, 50 gold with no gems, chaining on success and
/// doing nothing on failure. So most of what follows covers branches no shipped design reaches,
/// and the reference is the only authority for them.
/// </para>
/// <para>
/// The money is the part worth being careful about. Coin prices convert through the design's
/// exchange rates; gem and jewellery prices are counts and ignore appraisals entirely. Those are
/// different mechanisms in the same event, and the tests keep them apart.
/// </para>
/// </remarks>
public class EventWhoPaysTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static readonly TransferData NoTransfer = new(0, 0, 0, 0, 0, 0);

    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static WhoPaysEvent Toll(
        int platinum = 0, int gems = 0, int jewels = 0, int impossible = 0,
        ItemClass currency = ItemClass.Platinum,
        PasswordAction successAction = PasswordAction.NoAction,
        PasswordAction failAction = PasswordAction.NoAction,
        uint successChain = 0, uint failChain = 0,
        TransferData? successTransfer = null, TransferData? failTransfer = null) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.WhoPays, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            impossible, gems, jewels, platinum,
            successChain, (int)successAction, (int)failAction, failChain, (int)currency,
            successTransfer ?? NoTransfer, failTransfer ?? NoTransfer);

    /// <summary>A member with only the fields this event reads — which is to say, none.</summary>
    private static CharacterRecord Member(string name = "Aramil") =>
        new(0, 0, "human", 0, "fighter", 0, 0, 0, "", 0, name, "",
            0, 0, 0, 0, 0, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [new BaseclassStats("fighter", 0, 0, 0, 0)], [], [], 0, 0, 0,
            null, 0, null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

    private static (Party Party, Character Payer) Roster()
    {
        var party = new Party { Pooled = new Purse(MoneyRules.Default) };
        var payer = new Character(Member(), MoneyRules.Default);
        party.Add(payer);
        return (party, payer);
    }

    private static WhoPaysOutcome Resolve(WhoPaysEvent toll, int chose, Party party,
                                          Character payer, Func<uint, bool>? valid = null) =>
        EventWhoPays.Resolve(toll, chose, payer, party, valid ?? (_ => true));

    // ---- the denomination ------------------------------------------------------------------------

    [Fact]
    public void A_money_type_of_zero_means_platinum_rather_than_no_coin()
    {
        // The field only entered the format at 0.912 and the reader leaves it 0 below that, but
        // the reference's Clear() sets PlatinumType in the constructor and Serialize never
        // overwrites it there. ItemClass 0 is `Item`, which the money system aborts on -- so
        // passing the zero through would take down every pre-0.912 toll.
        Assert.Equal(ItemClass.Platinum, EventWhoPays.Currency(Toll()));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoneyRules.IndexOf(ItemClass.Item));
    }

    [Fact]
    public void A_stored_denomination_is_used_as_it_stands()
    {
        Assert.Equal(ItemClass.Gold, EventWhoPays.Currency(Toll(currency: ItemClass.Gold)));
    }

    // ---- the coin price --------------------------------------------------------------------------

    [Fact]
    public void A_price_in_gold_is_paid_out_of_platinum_at_the_designs_own_rate()
    {
        // The real trap in this event. Platinum is rate 1 and gold rate 5, so ten platinum is
        // exactly fifty gold -- comparing the named coin's count against the price instead would
        // refuse a character who can easily afford it.
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Platinum] = 10;

        var toll = Toll(platinum: 50, currency: ItemClass.Gold);

        Assert.True(EventWhoPays.CanPay(toll, payer, party));
        Assert.Equal(WhoPaysResult.Paid, Resolve(toll, EventWhoPays.PayEntry, party, payer).Result);
        Assert.True(payer.Purse.IsEmpty);
    }

    [Fact]
    public void One_platinum_short_is_short()
    {
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Platinum] = 9;

        var toll = Toll(platinum: 50, currency: ItemClass.Gold);

        Assert.False(EventWhoPays.CanPay(toll, payer, party));

        var outcome = Resolve(toll, EventWhoPays.PayEntry, party, payer);

        Assert.Equal(WhoPaysResult.CannotPay, outcome.Result);
        Assert.Equal(9, payer.Purse[ItemClass.Platinum]);    // nothing was taken
    }

    [Fact]
    public void A_price_of_zero_is_free_and_so_is_a_negative_one()
    {
        // Every one of the three tests is guarded by `> 0` and `enough` starts true, so a toll with
        // no price always succeeds. A negative price is not a payout -- just another way of
        // charging nothing.
        var (party, payer) = Roster();

        Assert.True(EventWhoPays.CanPay(Toll(), payer, party));
        Assert.True(EventWhoPays.CanPay(Toll(platinum: -50, gems: -1, jewels: -1), payer, party));

        EventWhoPays.Take(Toll(platinum: -50, gems: -1, jewels: -1), payer, party);
        Assert.True(payer.Purse.IsEmpty);
    }

    [Fact]
    public void A_price_in_a_denomination_the_design_never_configured_is_free()
    {
        // A reference bug, not a guard: HaveEnough converts the price to the base coin first, an
        // inactive coin converts to 0, and 0 is always affordable -- then Subtract refuses to touch
        // an inactive denomination, so nothing is charged. Reachable through a design that drops a
        // coin type after the event was authored.
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Platinum] = 1;

        var toll = Toll(platinum: 9_999, currency: ItemClass.Coin6);

        Assert.False(MoneyRules.Default.IsActive(ItemClass.Coin6));
        Assert.True(EventWhoPays.CanPay(toll, payer, party));

        Assert.Equal(WhoPaysResult.Paid, Resolve(toll, EventWhoPays.PayEntry, party, payer).Result);
        Assert.Equal(1, payer.Purse[ItemClass.Platinum]);    // and the toll cost nothing
    }

    // ---- the pooled purse ------------------------------------------------------------------------

    [Fact]
    public void Pooled_coins_are_spent_before_the_characters_own()
    {
        var (party, payer) = Roster();
        party.MoneyPooled = 1;
        party.Pooled[ItemClass.Platinum] = 20;
        payer.Purse[ItemClass.Platinum] = 20;

        Resolve(Toll(platinum: 50, currency: ItemClass.Gold), EventWhoPays.PayEntry, party, payer);

        Assert.Equal(10, party.Pooled[ItemClass.Platinum]);
        Assert.Equal(20, payer.Purse[ItemClass.Platinum]);   // untouched
    }

    [Fact]
    public void Paying_the_pool_dry_un_pools_the_party()
    {
        // `party.moneyPooled = !(poolSack.IsEmpty())` runs after every pooled payment, and the flag
        // is what the next toll consults -- so emptying the pool sends the next charge straight to
        // a character.
        var (party, payer) = Roster();
        party.MoneyPooled = 1;
        party.Pooled[ItemClass.Platinum] = 10;

        Resolve(Toll(platinum: 50, currency: ItemClass.Gold), EventWhoPays.PayEntry, party, payer);

        Assert.True(party.Pooled.IsEmpty);
        Assert.Equal(0, party.MoneyPooled);
    }

    [Fact]
    public void A_pool_still_holding_a_gem_stays_pooled_even_with_no_coins_left()
    {
        // IsEmpty counts gems and jewellery, not just coins.
        var (party, payer) = Roster();
        party.MoneyPooled = 1;
        party.Pooled[ItemClass.Platinum] = 10;
        party.Pooled.AddGem(new GemType(1, 50));

        Resolve(Toll(platinum: 50, currency: ItemClass.Gold), EventWhoPays.PayEntry, party, payer);

        Assert.Equal(1, party.MoneyPooled);
    }

    [Fact]
    public void An_unpooled_party_ignores_the_common_purse_entirely()
    {
        var (party, payer) = Roster();
        party.MoneyPooled = 0;
        party.Pooled[ItemClass.Platinum] = 1_000;

        var toll = Toll(platinum: 50, currency: ItemClass.Gold);

        Assert.False(EventWhoPays.CanPay(toll, payer, party));
        Assert.Equal(1_000, party.Pooled[ItemClass.Platinum]);
    }

    // ---- gems and jewellery ----------------------------------------------------------------------

    [Fact]
    public void Gems_are_counted_never_valued()
    {
        // Two worthless stones pay a two-gem toll; one priceless stone does not.
        var (party, payer) = Roster();
        payer.Purse.AddGem(new GemType(1, 1));
        payer.Purse.AddGem(new GemType(2, 1));

        Assert.True(EventWhoPays.CanPay(Toll(gems: 2), payer, party));

        var (otherParty, rich) = Roster();
        rich.Purse.AddGem(new GemType(1, 100_000));

        Assert.False(EventWhoPays.CanPay(Toll(gems: 2), rich, otherParty));
    }

    [Fact]
    public void The_gems_taken_are_the_oldest_and_not_the_cheapest()
    {
        var (party, payer) = Roster();
        payer.Purse.AddGem(new GemType(1, 5_000));
        payer.Purse.AddGem(new GemType(2, 10));
        payer.Purse.AddGem(new GemType(3, 10));

        Resolve(Toll(gems: 1), EventWhoPays.PayEntry, party, payer);

        Assert.Equal([10, 10], payer.Purse.Gems.Select(g => g.Value));
    }

    [Fact]
    public void Jewellery_is_a_separate_count_from_gems()
    {
        var (party, payer) = Roster();
        payer.Purse.AddGem(new GemType(1, 10));
        payer.Purse.AddGem(new GemType(2, 10));

        // Two gems do not satisfy a one-piece jewellery price.
        Assert.False(EventWhoPays.CanPay(Toll(jewels: 1), payer, party));

        payer.Purse.AddJewelry(new GemType(3, 10));
        Assert.True(EventWhoPays.CanPay(Toll(jewels: 1), payer, party));

        Resolve(Toll(jewels: 1), EventWhoPays.PayEntry, party, payer);

        Assert.Empty(payer.Purse.Jewelry);
        Assert.Equal(2, payer.Purse.Gems.Count);            // the gems are untouched
    }

    [Fact]
    public void Pooled_gems_cannot_pay_a_gem_price()
    {
        // The asymmetry: coins may come from the pool and valuables may not. `enoughMoney` tests
        // gems against `money.NumGems()` -- this character's own purse -- with no pooled
        // equivalent anywhere in the reference.
        var (party, payer) = Roster();
        party.MoneyPooled = 1;
        party.Pooled.AddGem(new GemType(1, 10));
        party.Pooled.AddGem(new GemType(2, 10));

        Assert.False(EventWhoPays.CanPay(Toll(gems: 1), payer, party));
    }

    [Fact]
    public void A_coin_price_and_a_gem_price_must_both_be_met()
    {
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Platinum] = 10;

        // The coins are there and the gem is not.
        Assert.False(EventWhoPays.CanPay(Toll(platinum: 50, gems: 1, currency: ItemClass.Gold),
                                         payer, party));

        payer.Purse.AddGem(new GemType(1, 10));
        Assert.True(EventWhoPays.CanPay(Toll(platinum: 50, gems: 1, currency: ItemClass.Gold),
                                        payer, party));
    }

    // ---- choosing, and refusing ------------------------------------------------------------------

    [Fact]
    public void Exiting_takes_the_fail_branch_without_charging_anyone()
    {
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Platinum] = 1_000;

        var outcome = Resolve(Toll(platinum: 1, failAction: PasswordAction.ChainEvent,
                                   failChain: 60),
                              EventWhoPays.ExitEntry, party, payer);

        Assert.Equal(WhoPaysResult.NobodyPays, outcome.Result);
        Assert.Equal(60u, outcome.GoTo);
        Assert.Equal(1_000, payer.Purse[ItemClass.Platinum]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Any_menu_entry_but_the_first_exits(int chose)
    {
        // The reference tests `menu.currentItem() == 1` and everything else falls to the else.
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Platinum] = 1_000;

        Assert.Equal(WhoPaysResult.NobodyPays,
                     Resolve(Toll(platinum: 1), chose, party, payer).Result);
    }

    [Fact]
    public void Exiting_and_failing_take_the_same_branch_but_are_reported_apart()
    {
        // Only the failure passes through TASK_WhoPaysFailure, and that state is what draws Text3.
        // The branch itself is identical, so the distinction exists purely so a caller knows
        // whether to show the text.
        var (party, payer) = Roster();
        var toll = Toll(platinum: 50, failAction: PasswordAction.ChainEvent, failChain: 60);

        var exited = Resolve(toll, EventWhoPays.ExitEntry, party, payer);
        var failed = Resolve(toll, EventWhoPays.PayEntry, party, payer);

        Assert.Equal(WhoPaysResult.NobodyPays, exited.Result);
        Assert.Equal(WhoPaysResult.CannotPay, failed.Result);
        Assert.Equal(exited.GoTo, failed.GoTo);
    }

    [Fact]
    public void An_impossible_toll_fails_however_rich_the_payer_is()
    {
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Platinum] = 1_000;

        var outcome = Resolve(Toll(platinum: 1, impossible: 1), EventWhoPays.PayEntry,
                              party, payer);

        Assert.Equal(WhoPaysResult.CannotPay, outcome.Result);
        Assert.Equal(1_000, payer.Purse[ItemClass.Platinum]);

        // The flag lives in OnKeypress, not in enoughMoney -- so the purse itself still says yes.
        Assert.True(EventWhoPays.CanPay(Toll(platinum: 1, impossible: 1), payer, party));
    }

    [Fact]
    public void An_impossible_toll_exited_is_still_an_exit()
    {
        var (party, payer) = Roster();

        Assert.Equal(WhoPaysResult.NobodyPays,
                     Resolve(Toll(impossible: 1), EventWhoPays.ExitEntry, party, payer).Result);
    }

    // ---- branching -------------------------------------------------------------------------------

    [Fact]
    public void No_action_follows_the_ordinary_chain()
    {
        var (party, payer) = Roster();

        var outcome = Resolve(Toll(successAction: PasswordAction.NoAction, successChain: 50),
                              EventWhoPays.PayEntry, party, payer);

        Assert.Equal(WhoPaysResult.Paid, outcome.Result);
        Assert.Null(outcome.GoTo);                          // the chain field is not even read
        Assert.Null(outcome.Teleport);
        Assert.False(outcome.BackUpOneStep);
        Assert.False(outcome.Stuck);
    }

    [Fact]
    public void Success_and_failure_read_their_own_chain()
    {
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Platinum] = 10;

        var toll = Toll(platinum: 50, currency: ItemClass.Gold,
                        successAction: PasswordAction.ChainEvent, successChain: 50,
                        failAction: PasswordAction.ChainEvent, failChain: 60);

        Assert.Equal(50u, Resolve(toll, EventWhoPays.PayEntry, party, payer).GoTo);

        // The purse is empty now, so the same call fails.
        Assert.Equal(60u, Resolve(toll, EventWhoPays.PayEntry, party, payer).GoTo);
    }

    [Fact]
    public void A_chain_of_zero_falls_back_on_the_ordinary_chain_rather_than_ending_the_run()
    {
        var (party, payer) = Roster();

        var outcome = Resolve(Toll(successAction: PasswordAction.ChainEvent, successChain: 0),
                              EventWhoPays.PayEntry, party, payer);

        Assert.Null(outcome.GoTo);
        Assert.False(outcome.Stuck);
    }

    [Fact]
    public void And_so_does_an_unreachable_one()
    {
        // THE difference from QUEST_EVENT_DATA. A quest event pushes a do-nothing event when its
        // branch names nothing reachable, which ends the run; ChainOrQuit (RunEvent.cpp:931) falls
        // back on ChainHappened instead. Assuming the quest rule here would silently truncate
        // every toll whose chain target was deleted.
        var (party, payer) = Roster();

        var outcome = Resolve(Toll(successAction: PasswordAction.ChainEvent, successChain: 404),
                              EventWhoPays.PayEntry, party, payer, valid: _ => false);

        Assert.Null(outcome.GoTo);
        Assert.False(outcome.Stuck);
    }

    [Fact]
    public void A_teleport_ends_the_run_at_its_destination()
    {
        var (party, payer) = Roster();
        var destination = new TransferData(0, 0, 3, 7, 9, 1);

        var outcome = Resolve(Toll(successAction: PasswordAction.Teleport, successChain: 50,
                                   successTransfer: destination),
                              EventWhoPays.PayEntry, party, payer);

        // HandleTransfer destroys the event, so nothing chains -- not the branch's own chain and
        // not the ordinary one.
        Assert.Equal(destination, outcome.Teleport);
        Assert.Null(outcome.GoTo);
    }

    [Fact]
    public void Each_branch_teleports_to_its_own_destination()
    {
        var (party, payer) = Roster();
        var onSuccess = new TransferData(0, 0, 1, 1, 1, 0);
        var onFailure = new TransferData(0, 0, 2, 2, 2, 0);

        var toll = Toll(platinum: 50,
                        successAction: PasswordAction.Teleport, successTransfer: onSuccess,
                        failAction: PasswordAction.Teleport, failTransfer: onFailure);

        Assert.Equal(onFailure, Resolve(toll, EventWhoPays.PayEntry, party, payer).Teleport);
        Assert.Equal(onFailure, Resolve(toll, EventWhoPays.ExitEntry, party, payer).Teleport);

        payer.Purse[ItemClass.Platinum] = 50;
        Assert.Equal(onSuccess, Resolve(toll, EventWhoPays.PayEntry, party, payer).Teleport);
    }

    [Fact]
    public void Backing_up_a_step_still_chains_afterwards()
    {
        var (party, payer) = Roster();

        var outcome = Resolve(Toll(successAction: PasswordAction.BackupOneStep, successChain: 50),
                              EventWhoPays.PayEntry, party, payer);

        Assert.True(outcome.BackUpOneStep);
        Assert.Null(outcome.GoTo);                          // the ordinary chain, not chain 50
        Assert.Null(outcome.Teleport);
    }

    [Fact]
    public void An_action_outside_the_enum_leaves_the_event_doing_nothing()
    {
        // All three of the reference's switches are defaultless, so an out-of-range value falls
        // through to a bare return: no chain, no transfer, no state change, and every further
        // Return does the same. Unreachable from an editor-written design -- the field is a raw
        // int off disk -- but reported rather than quietly chained.
        var (party, payer) = Roster();
        var toll = Toll(successAction: (PasswordAction)9, successChain: 50);

        var outcome = Resolve(toll, EventWhoPays.PayEntry, party, payer);

        Assert.True(outcome.Stuck);
        Assert.Null(outcome.GoTo);
        Assert.Null(outcome.Teleport);
        Assert.False(outcome.BackUpOneStep);

        // The money still went, because the charge happens before the branch is looked at.
        Assert.Equal(WhoPaysResult.Paid, outcome.Result);
    }

    // ---- the one the corpus actually contains ----------------------------------------------------

    [Fact]
    public void The_corpus_toll_charges_fifty_gold_and_chains_to_the_next_event()
    {
        // SomethingWild Level002, event 65: the only WHO_PAYS in six designs. 50 gold, no gems,
        // not impossible, success chaining to 66, failure taking NoAction -- and both Text2 and
        // Text3 empty, so both screens auto-press Return.
        var (party, payer) = Roster();
        payer.Purse[ItemClass.Gold] = 50;

        var toll = Toll(platinum: 50, currency: ItemClass.Gold,
                        successAction: PasswordAction.ChainEvent, successChain: 66,
                        failAction: PasswordAction.NoAction);

        var paid = Resolve(toll, EventWhoPays.PayEntry, party, payer);

        Assert.Equal(WhoPaysResult.Paid, paid.Result);
        Assert.Equal(66u, paid.GoTo);
        Assert.True(payer.Purse.IsEmpty);

        var broke = Resolve(toll, EventWhoPays.PayEntry, party, payer);

        Assert.Equal(WhoPaysResult.CannotPay, broke.Result);
        Assert.Null(broke.GoTo);                            // NoAction: the ordinary chain
    }
}
