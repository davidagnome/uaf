using UAF.Rules;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// What a branch does once it has been chosen (<c>passwordActionType</c>,
/// <c>GameEvent.h:327</c>).
/// </summary>
/// <remarks>
/// Named after the reference's own type, which is shared by <c>PASSWORD_DATA</c>,
/// <c>WHO_TRIES_EVENT_DATA</c> and <c>WHO_PAYS_EVENT_DATA</c> alike — so it is not a who-pays enum
/// even though this is the first port to need it.
/// </remarks>
public enum PasswordAction
{
    /// <summary>Follow the event's ordinary chain (<c>ChainHappened</c>).</summary>
    NoAction = 0,

    /// <summary>Jump to the branch's own chain, falling back on the ordinary one.</summary>
    ChainEvent = 1,

    /// <summary>Move the party to the branch's transfer. The event is destroyed, so nothing chains.</summary>
    Teleport = 2,

    /// <summary>Step the party back off the square, then follow the ordinary chain.</summary>
    BackupOneStep = 3,
}

/// <summary>Which of the three ways a toll ended.</summary>
public enum WhoPaysResult
{
    /// <summary>A character was chosen and met the price. The money has been taken.</summary>
    Paid,

    /// <summary>
    /// A character was chosen and could not meet the price, or the toll is
    /// <see cref="WhoPaysEvent.Impossible"/>. The caller shows <c>Text3</c> before continuing.
    /// </summary>
    CannotPay,

    /// <summary>The player chose EXIT. The caller shows <b>nothing</b> — see the class remarks.</summary>
    NobodyPays,
}

/// <summary>What a toll decided.</summary>
/// <param name="Result">Which of the three ways it ended.</param>
/// <param name="GoTo">The event to run instead, or null to follow the ordinary chain.</param>
/// <param name="Teleport">
/// Where the party is sent, or null. Non-null means the run ends here: <c>HandleTransfer</c>
/// destroys the event, so neither <paramref name="GoTo"/> nor the ordinary chain is reached.
/// </param>
/// <param name="BackUpOneStep">Whether the party is stepped off the square before chaining.</param>
/// <param name="Stuck">
/// The event did nothing at all and is still on screen — see <see cref="EventWhoPays.Resolve"/>.
/// </param>
/// <remarks>
/// <b>There is no <c>Stop</c>, unlike <see cref="QuestOutcome"/>.</b> A quest event pushes a
/// do-nothing event when its branch names nothing reachable, which ends the run; a toll calls
/// <c>ChainOrQuit</c> (<c>RunEvent.cpp:931</c>), which falls back on <c>ChainHappened</c> in
/// exactly that case. So an unreachable chain here is the same as no chain, and neither ends
/// anything.
/// </remarks>
public readonly record struct WhoPaysOutcome(
    WhoPaysResult Result, uint? GoTo, TransferData? Teleport, bool BackUpOneStep, bool Stuck);

/// <summary>
/// Runs a <c>WHO_PAYS_EVENT_DATA</c> — a toll (<c>WHO_PAYS_EVENT_DATA::OnKeypress</c>,
/// <c>RunEvent.cpp:10265</c>).
/// </summary>
/// <remarks>
/// <para>
/// The player picks a party member on the horizontal roster and then either WHO PAYS or EXIT
/// (<c>WhoPaysMenuData</c>, <c>GameMenu.cpp:189</c>). <b>One character pays, not the party</b> —
/// <c>GetActiveChar</c> (<c>RunEvent.cpp:166</c>) resolves the selection and every charge lands on
/// that character's own purse, with the party's pooled coins consulted only as described below.
/// </para>
/// <para>
/// <b>The EXIT path skips the failure text.</b> Both EXIT and a character who cannot pay run
/// <c>failAction</c>, but only the second passes through <c>TASK_WhoPaysFailure</c> and so only the
/// second draws <c>Text3</c> (<c>:10310</c> versus <c>:10336</c>). <see cref="WhoPaysResult"/>
/// separates them for that reason alone — the branch taken is identical.
/// </para>
/// <para>
/// <b>The money is taken a keypress before the branch runs.</b> The reference charges the character
/// at <c>:10353</c>, while it is putting the success text on screen, and only runs
/// <c>successAction</c> on the <i>next</i> Return. This port does both in one call, which differs
/// only for a run abandoned between the two — where the reference leaves the party poorer with the
/// branch never taken.
/// </para>
/// <para>
/// <b>Every branch is live code, but almost none of it is exercised by a shipped design.</b> The
/// six-design corpus holds exactly one <c>WHO_PAYS</c> event — SomethingWild's Level002, event 65:
/// 50 gold, no gems, not impossible, success chaining to event 66 and failure taking
/// <see cref="PasswordAction.NoAction"/>, with both <c>Text2</c> and <c>Text3</c> empty so both
/// screens auto-press Return. So <see cref="WhoPaysEvent.Impossible"/>, the gem and jewellery
/// prices, <see cref="PasswordAction.Teleport"/> and <see cref="PasswordAction.BackupOneStep"/>
/// have no coverage outside this port's own tests.
/// </para>
/// </remarks>
public static class EventWhoPays
{
    /// <summary>The WHO PAYS entry — one-based, as <c>menu.currentItem()</c> reports it.</summary>
    public const int PayEntry = 1;

    /// <summary>The EXIT entry.</summary>
    /// <remarks>
    /// Only <see cref="PayEntry"/> is tested for (<c>:10292</c>), so <b>any</b> other entry exits.
    /// </remarks>
    public const int ExitEntry = 2;

    /// <summary>
    /// The denomination the price is quoted in (<c>WHO_PAYS_EVENT_DATA::moneyType</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zero means platinum, not "no coin".</b> The field only entered the format at design
    /// version 0.912 (<c>GameEvent.cpp:8817</c>) and the reader leaves it at 0 below that — but the
    /// reference does not: <c>Clear()</c> sets <c>moneyType = PlatinumType</c> in the constructor
    /// (<c>GameEvent.cpp:14894</c>) and <c>Serialize</c> only overwrites it above the gate, so an
    /// older design runs on platinum. Passing the 0 through would be
    /// <see cref="ItemClass.Item"/> — not a denomination at all — and
    /// <c>MoneyRules.IndexOf</c> aborts on it.
    /// </para>
    /// <para>
    /// Above the gate a 0 cannot occur: the editor's combo is filled only from denominations the
    /// design has configured (<c>WhoPaysDlg.cpp:156</c>), so every stored value is a real coin.
    /// </para>
    /// </remarks>
    public static ItemClass Currency(WhoPaysEvent toll)
    {
        ArgumentNullException.ThrowIfNull(toll);

        return toll.MoneyType == 0 ? ItemClass.Platinum : (ItemClass)toll.MoneyType;
    }

    /// <summary>
    /// Whether a character can meet the price (<c>CHARACTER::enoughMoney</c>,
    /// <c>Shared/Char.cpp:6726</c>).
    /// </summary>
    /// <param name="payer">The character the player selected. Only this one is consulted.</param>
    /// <param name="party">Wanted for <c>moneyPooled</c> and the common purse.</param>
    /// <remarks>
    /// <para>
    /// <b>Coins may come from the pool, gems and jewellery may not.</b> The coin test is
    /// "pooled and the pool covers it, <i>or</i> this character covers it"; the two valuables are
    /// tested against <c>money.NumGems()</c> and <c>money.NumJewelry()</c> — this character's own
    /// purse — with no pooled equivalent anywhere. A party that pooled every gem it owns therefore
    /// cannot pay a gem toll at all.
    /// </para>
    /// <para>
    /// <b>Gems and jewellery are counted, never valued.</b> The price is a number of stones, so the
    /// appraisal each one carries is irrelevant to whether the party can pay and to what it costs
    /// them. Compare the coin test, which converts through the design's exchange rates and so lets
    /// a character holding nothing but platinum pay a price quoted in gold.
    /// </para>
    /// <para>
    /// <b>A price of zero or less is free, and that includes a negative one.</b> Each of the three
    /// tests is guarded by <c>&gt; 0</c> and <c>enough</c> starts at <c>true</c>, so a toll with no
    /// price at all always succeeds — and a negative price is not a payout, just another way of
    /// charging nothing.
    /// </para>
    /// <para>
    /// <b>A price in a denomination the design has not configured is free.</b>
    /// <c>Purse.HaveEnough</c> converts the amount to the base coin first, an inactive coin
    /// converts to 0, and 0 is always affordable — then <see cref="Take"/>'s
    /// <c>Purse.Subtract</c> refuses to touch an inactive denomination and nothing is charged. A
    /// reference bug rather than a guard, and reproduced: it is reachable through a design that
    /// removes a coin type after the event was authored.
    /// </para>
    /// </remarks>
    public static bool CanPay(WhoPaysEvent toll, Character payer, Party party)
    {
        ArgumentNullException.ThrowIfNull(toll);
        ArgumentNullException.ThrowIfNull(payer);
        ArgumentNullException.ThrowIfNull(party);

        var currency = Currency(toll);
        bool enough = true;

        if (toll.Platinum > 0)
        {
            enough = party.MoneyPooled != 0 && party.Pooled.HaveEnough(currency, toll.Platinum);

            if (!enough)
            {
                enough = payer.Purse.HaveEnough(currency, toll.Platinum);
            }
        }

        if (enough && toll.Gems > 0)
        {
            enough = payer.Purse.Gems.Count >= toll.Gems;
        }

        if (enough && toll.Jewels > 0)
        {
            enough = payer.Purse.Jewelry.Count >= toll.Jewels;
        }

        return enough;
    }

    /// <summary>
    /// Charges the price (<c>CHARACTER::payForItem</c>, <c>Shared/Char.cpp:6758</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Paying the pool dry un-pools the party.</b> <c>party.moneyPooled = !poolSack.IsEmpty()</c>
    /// runs after every pooled payment, so the last coin out of the common purse also turns the
    /// flag off — and the flag is what <see cref="CanPay"/> consults, so the next toll goes
    /// straight to the character. Note the emptiness test counts gems and jewellery too, so a pool
    /// holding one gem and no coins stays pooled.
    /// </para>
    /// <para>
    /// <b>The pool is re-tested here rather than trusted.</b> <c>payForItem</c> asks
    /// <c>HaveEnough</c> again before spending from it and otherwise charges the character —
    /// which is what keeps this in step with <see cref="CanPay"/>'s two-sided test rather than
    /// spending from whichever purse was checked.
    /// </para>
    /// <para>
    /// <b>Not ported: the encumbrance recalculation.</b> The reference ends with
    /// <c>determineEffectiveEncumbrance()</c> and <c>determineMaxMovement()</c>, because coins have
    /// weight and a character who has just paid a toll can move further. A
    /// <see cref="Character"/> here carries no live encumbrance for those to write to, so the
    /// side effect is dropped rather than approximated — nothing in this port reads it yet.
    /// </para>
    /// </remarks>
    public static void Take(WhoPaysEvent toll, Character payer, Party party)
    {
        ArgumentNullException.ThrowIfNull(toll);
        ArgumentNullException.ThrowIfNull(payer);
        ArgumentNullException.ThrowIfNull(party);

        var currency = Currency(toll);

        if (toll.Platinum > 0)
        {
            if (party.MoneyPooled != 0 && party.Pooled.HaveEnough(currency, toll.Platinum))
            {
                party.Pooled.Subtract(currency, toll.Platinum);
                party.MoneyPooled = party.Pooled.IsEmpty ? 0 : 1;
            }
            else
            {
                payer.Purse.Subtract(currency, toll.Platinum);
            }
        }

        if (toll.Gems > 0)
        {
            payer.Purse.RemoveGems(toll.Gems);
        }

        if (toll.Jewels > 0)
        {
            payer.Purse.RemoveJewelry(toll.Jewels);
        }
    }

    /// <summary>
    /// Charges the toll if it can be charged, and says what should happen next.
    /// </summary>
    /// <param name="chose">
    /// The menu entry, <b>one-based</b>: <see cref="PayEntry"/> pays and anything else exits.
    /// </param>
    /// <param name="payer">
    /// The character the roster selection landed on. Required even when
    /// <paramref name="chose"/> exits, because the reference resolves <c>GetActiveChar</c> at
    /// <c>:10274</c> — above the branch — and a toll with no party to charge is not a state the
    /// engine can reach.
    /// </param>
    /// <param name="isValidEvent">Whether an event id names something the level can run.</param>
    /// <remarks>
    /// <para>
    /// <b><see cref="WhoPaysEvent.Impossible"/> only matters once someone has been chosen.</b> It
    /// is tested inside the WHO PAYS arm (<c>:10294</c>), so an impossible toll still asks, still
    /// lets the player pick a character, and still shows the failure text — where EXIT shows
    /// nothing. It also short-circuits <see cref="CanPay"/> entirely, so the purse is never
    /// consulted and no money moves.
    /// </para>
    /// <para>
    /// <b>An unreachable chain is not a dead end.</b> <c>ChainOrQuit</c>
    /// (<c>RunEvent.cpp:931</c>) falls back on <c>ChainHappened</c> when the id is 0 <i>or</i>
    /// names no event, so both cases follow the event's ordinary chain. This is where
    /// <c>WHO_PAYS</c> parts company with <c>QUEST_EVENT_DATA</c>, which pushes a do-nothing event
    /// for all but its two automatic operations and so ends the run — see
    /// <see cref="Quests.Resolve"/>. Nothing about a toll ends a run.
    /// </para>
    /// <para>
    /// <b>An action value outside the enum leaves the event on screen doing nothing.</b> All three
    /// of the reference's switches over <c>failAction</c> and <c>successAction</c> are
    /// defaultless, so an out-of-range value falls through to a bare <c>return</c> — no chain, no
    /// transfer, no state change — and every further Return does the same. It is worse than it
    /// looks in the two later states, which clear the picture and text first (<c>:10363</c>,
    /// <c>:10384</c>) and so leave a blank screen. The field is a raw <c>int</c> off disk and the
    /// editor writes only 0–3, so this is unreachable from an editor-written design; it is
    /// reported through <see cref="WhoPaysOutcome.Stuck"/> rather than quietly chained, because
    /// chaining would be a different game.
    /// </para>
    /// </remarks>
    public static WhoPaysOutcome Resolve(WhoPaysEvent toll, int chose, Character payer,
                                         Party party, Func<uint, bool> isValidEvent)
    {
        ArgumentNullException.ThrowIfNull(toll);
        ArgumentNullException.ThrowIfNull(payer);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(isValidEvent);

        if (chose != PayEntry)
        {
            return Branch(WhoPaysResult.NobodyPays, toll.FailAction, toll.FailChain,
                          toll.FailTransfer, isValidEvent);
        }

        if (toll.Impossible != 0 || !CanPay(toll, payer, party))
        {
            return Branch(WhoPaysResult.CannotPay, toll.FailAction, toll.FailChain,
                          toll.FailTransfer, isValidEvent);
        }

        Take(toll, payer, party);
        return Branch(WhoPaysResult.Paid, toll.SuccessAction, toll.SuccessChain,
                      toll.SuccessTransfer, isValidEvent);
    }

    private static WhoPaysOutcome Branch(WhoPaysResult result, int action, uint chain,
                                         TransferData transfer, Func<uint, bool> isValidEvent) =>
        (PasswordAction)action switch
        {
            PasswordAction.NoAction =>
                new(result, null, null, BackUpOneStep: false, Stuck: false),

            PasswordAction.ChainEvent =>
                new(result, chain > 0 && isValidEvent(chain) ? chain : null, null,
                    BackUpOneStep: false, Stuck: false),

            PasswordAction.Teleport =>
                new(result, null, transfer, BackUpOneStep: false, Stuck: false),

            PasswordAction.BackupOneStep =>
                new(result, null, null, BackUpOneStep: true, Stuck: false),

            _ => new(result, null, null, BackUpOneStep: false, Stuck: true),
        };
}
