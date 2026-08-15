using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events;

/// <summary>
/// One way out of an event: a labelled reference to another event's id.
/// </summary>
/// <param name="Label">
/// What the designer chose, in their words where possible — an option's own text rather than
/// "option 3".
/// </param>
/// <param name="Target">The referenced <c>GameEvent::id</c>. Zero means "none"; see the remarks on
/// <see cref="EventChainLinks"/>.</param>
/// <param name="Taken">
/// Whether this edge is live given the event's <c>chainTrigger</c>. Only the two base links can be
/// dead; a type-specific chain is followed by the event's own code and never consults the trigger.
/// </param>
public sealed record EventChainLink(string Label, uint Target, bool Taken = true);

/// <summary>
/// Every event a given event can hand control to.
/// </summary>
/// <remarks>
/// <para>
/// This is the port of the editor's <c>GetEVChainText</c>/<c>GetEVChain</c> pair — the base at
/// <c>Shared/GameEvent.cpp:3261</c> and fourteen overrides after it. It is the virtual that lets
/// the event viewer draw an event's children without knowing its type, and it is the whole reason
/// the original's main view is a <b>tree</b> rather than a list (<c>IDC_EVENTTREE</c>,
/// <c>UAFWinEd.rc:2165</c>): following chains is what the tool is for. The slot names below are
/// the original's own strings, so a node reads the same in both editors.
/// </para>
/// <para>
/// <b>The links on <see cref="GameEventBase"/> are the minority.</b> Fourteen concrete types carry
/// chain ids of their own — a question's five buttons, a logic block's true and false arms, a small
/// town's six services, a random event's weighted branches — and none are reachable from the base
/// record. An editor showing only <c>chainEventHappen</c> and <c>chainEventNotHappen</c> would draw
/// a forest of isolated nodes for a design like Case.dsn, where 113 question lists carry the
/// structure.
/// </para>
/// <para>
/// <b>Zero is not a target.</b> Every consumer in the engine guards with <c>&gt; 0</c>
/// (<see cref="EventChain"/>), so an id of 0 is the null and event id 0 can never be chained to.
/// Links with a zero target are dropped here rather than shown as broken.
/// </para>
/// <para>
/// <b>This shows both base slots where the original shows one.</b> <c>GetEVChainText</c> emits a
/// single "Normal Chain" node, resolving to <c>chainEventHappen</c> under
/// <see cref="ChainTrigger.Always"/> or <see cref="ChainTrigger.IfHappened"/> and to
/// <c>chainEventNotHappen</c> under <see cref="ChainTrigger.IfNotHappened"/> — so the id that is
/// not currently selected is <i>invisible in the tree</i> even though it is stored and will come
/// back the moment the trigger changes. Real designs carry values in both. Reporting the unused one
/// with <see cref="EventChainLink.Taken"/> false is the answer to "why does my branch never fire",
/// which the original's tree cannot show at all.
/// </para>
/// </remarks>
public static class EventChainLinks
{
    /// <summary>Every outgoing link of an event, base and type-specific, in that order.</summary>
    public static IReadOnlyList<EventChainLink> Of(IGameEvent body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var links = new List<EventChainLink>();
        var trigger = (ChainTrigger)body.Base.Control.ChainTrigger;

        // Reachability of the two base links is decided by the chain trigger, and asymmetrically:
        // Always uses the happened id on BOTH paths, so the not-happened id is dead unless the
        // trigger is IfNotHappened. The original names whichever one is live "Normal Chain".
        Add(links, "Normal Chain", (uint)body.Base.ChainEventHappen,
            trigger is ChainTrigger.Always or ChainTrigger.IfHappened);
        Add(links, "Normal Chain (not happened)", (uint)body.Base.ChainEventNotHappen,
            trigger == ChainTrigger.IfNotHappened);

        AddTypeSpecific(links, body);

        return links;
    }

    private static void AddTypeSpecific(List<EventChainLink> links, IGameEvent body)
    {
        switch (body)
        {
            case ChainEvent chain:
                Add(links, "Chained Event", chain.Chain);
                break;

            case QuestionEvent question:
                // "Button %i Chain" is the original's label for both QUESTION_LIST_DATA
                // (GameEvent.cpp:13460) and QUESTION_BUTTON_DATA (:13379). The designer's own
                // button text is appended, which is what makes a question's subtree readable.
                for (int i = 0; i < question.Options.Count; i++)
                {
                    var option = question.Options[i];
                    Add(links, Option(i, option.Label, option.Present), option.Chain);
                }

                break;

            case YesNoEvent yesNo:
                Add(links, "Yes Chain", yesNo.YesChain);
                Add(links, "No Chain", yesNo.NoChain);
                break;

            case RandomEvent random:
                for (int i = 0; i < random.Branches.Count; i++)
                {
                    Add(links, $"Random Chain {i + 1} ({random.Branches[i].Chance}%)",
                        random.Branches[i].Chain);
                }

                break;

            case LogicBlockEvent logic:
                // m_NoChain is a three-way: 0 Suppress, 1 Normal, 2 Conditional
                // (GetLogicBlockChainConditionText, LogicBlock.cpp:198). Only under Conditional do
                // the true/false arms apply at all, and each is further gated by its own flag --
                // which is why the editor enables those two checkboxes only when m_NoChain == 2
                // (SetControlStates, LogicBlock.cpp:525).
                bool conditional = logic.NoChain == 2;
                Add(links, "True Chain", logic.TrueChain, conditional && logic.ChainIfTrue != 0);
                Add(links, "False Chain", logic.FalseChain, conditional && logic.ChainIfFalse != 0);
                break;

            case QuestEvent quest:
                Add(links, "Accept Chain", quest.AcceptChain);
                Add(links, "Reject Chain", quest.RejectChain);
                break;

            case PasswordEvent password:
                Add(links, "Pswd Success Chain", password.SuccessChain);
                Add(links, "Pswd Fail Chain", password.FailChain);
                break;

            case WhoTriesEvent tries:
                Add(links, "Try Success Chain", tries.SuccessChain);
                Add(links, "Try Fail Chain", tries.FailChain);
                break;

            case WhoPaysEvent pays:
                Add(links, "Pay Success Chain", pays.SuccessChain);
                Add(links, "Pay Fail Chain", pays.FailChain);
                break;

            case SmallTownEvent town:
                Add(links, "Temple Chain", town.TempleChain);
                Add(links, "Training Hall Chain", town.TrainingHallChain);
                Add(links, "Shop Chain", town.ShopChain);
                Add(links, "Inn Chain", town.InnChain);
                // The runtime menu entry is labelled PUB; the field and the editor say Tavern
                // (RunEvent.cpp:4562, GameEvent.cpp:12730).
                Add(links, "Tavern Chain", town.TavernChain);
                Add(links, "Vault Chain", town.VaultChain);
                break;

            case TempleEvent temple:
                Add(links, "Donate Chain", temple.DonationChain);
                break;

            case TavernEvent tavern:
                // "Drunk", not "Drink" -- the label is the original's (GameEvent.cpp:12421) and the
                // field is drinkChain, fired once the party has drunk past drinkPointTrigger.
                Add(links, "Fight Chain", tavern.FightChain);
                Add(links, "Drunk Chain", tavern.DrinkChain);
                break;

            case EncounterEvent encounter:
                for (int i = 0; i < encounter.Options.Count; i++)
                {
                    var option = encounter.Options[i];
                    Add(links, Option(i, option.Label, option.Present), option.Chain);
                }

                Add(links, "Combat Chain", encounter.CombatChain);
                Add(links, "Talk Chain", encounter.TalkChain);
                Add(links, "Escape Chain", encounter.EscapeChain);
                break;

            case FlowControlEvent flow:
                // NOT in the original's tree: FLOW_CONTROL_EVENT_DATA has no GetEVChainText
                // override, so a goto or call destination is invisible there. It is a real edge --
                // destinationID is an event id resolved from the marker name -- and the actions
                // that use it are goto (2) and call (3); none, return and pop ignore it
                // (ACTIONText, Globtext.cpp:215).
                Add(links, "Destination", flow.DestinationId, flow.Action is 2 or 3);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// A question or encounter button's slot label, carrying the designer's own text.
    /// </summary>
    /// <remarks>
    /// <c>present</c> being clear does not delete the option — it is stored, chained and simply not
    /// drawn, so it is shown struck-through in words rather than dropped.
    /// </remarks>
    private static string Option(int index, string label, int present)
    {
        string text = string.IsNullOrWhiteSpace(label)
            ? $"Button {index + 1} Chain"
            : $"Button {index + 1} Chain: {label.Trim()}";

        return present != 0 ? text : $"{text} (not present)";
    }

    private static void Add(List<EventChainLink> links, string label, uint target,
                            bool taken = true)
    {
        if (target > 0)
        {
            links.Add(new EventChainLink(label, target, taken));
        }
    }
}
