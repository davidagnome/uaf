using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// How a <c>QUEST_EVENT_DATA</c> decides whether the party took the quest
/// (<c>QuestAcceptType</c>, <c>GameEvent.h:2358</c>).
/// </summary>
public enum QuestAccept
{
    /// <summary>Always refused, and the player still has to press a key.</summary>
    Impossible = 0,

    /// <summary>Taken if the player answers Yes.</summary>
    OnYes = 1,

    /// <summary>Taken if the player answers <b>No</b> — not a typo; the question is inverted.</summary>
    OnNo = 2,

    /// <summary>Taken either way, but the player is still asked.</summary>
    OnYesOrNo = 3,

    /// <summary>Always refused, with no question at all.</summary>
    ImpossibleAuto = 4,

    /// <summary>Always taken, with no question at all.</summary>
    AutoAccept = 5,
}

/// <summary>What a quest event decided.</summary>
/// <param name="Accepted">Whether the party took it.</param>
/// <param name="GoTo">The event to run instead, or null to follow the ordinary chain.</param>
/// <param name="Stop">True when the run ends here.</param>
public readonly record struct QuestOutcome(bool Accepted, uint? GoTo, bool Stop);

/// <summary>
/// Runs a <c>QUEST_EVENT_DATA</c> (<c>QUEST_EVENT_DATA::OnKeypress</c>, <c>RunEvent.cpp:12331</c>).
/// </summary>
/// <remarks>
/// <para>
/// The largest remaining event type by corpus frequency — 282 across the four designs. It sets a
/// quest's stage, optionally its state, and branches on whether the party accepted.
/// </para>
/// <para>
/// <b>It does not necessarily touch a quest at all.</b> The packed <c>m_quest</c> field carries a
/// type in its top bits, and the type can be <c>ITEM_FLAG</c> or <c>KEY_FLAG</c> instead — in
/// which case the stage is set on a special item or a key. So this is the second way a design
/// hands out plot tokens, and it shares <see cref="WorldState"/> with
/// <see cref="SpecialItems"/>. The state calls that follow are always on the <i>quest</i> store
/// regardless, which is a genuine asymmetry in the reference and reproduced here.
/// </para>
/// </remarks>
public static class Quests
{
    /// <summary>
    /// Whether the party accepted, given the operation and which menu entry was chosen.
    /// </summary>
    /// <param name="chose">
    /// The menu entry, <b>one-based</b> as <c>menu.currentItem()</c> reports it: 1 is Yes and 2 is
    /// No. Ignored by the four operations that do not ask.
    /// </param>
    /// <remarks>
    /// <b><see cref="QuestAccept.OnNo"/> means the quest is taken when the player says No.</b> It
    /// reads like a mistake and is not: a design uses it for a question phrased as a refusal
    /// ("Leave the old man alone?"). Collapsing it into <c>OnYes</c> would invert every such event.
    /// <para>
    /// Anything outside the enum, and any menu entry that is neither 1 nor 2, is a refusal — the
    /// reference's inner <c>switch</c> leaves <c>accepted</c> at its initial <c>false</c>.
    /// </para>
    /// </remarks>
    public static bool IsAccepted(int operation, int chose) => (QuestAccept)operation switch
    {
        QuestAccept.AutoAccept or QuestAccept.OnYesOrNo => true,
        QuestAccept.Impossible or QuestAccept.ImpossibleAuto => false,
        QuestAccept.OnYes => chose == 1,
        QuestAccept.OnNo => chose == 2,
        _ => false,
    };

    /// <summary>Whether this operation asks the player anything.</summary>
    /// <remarks>
    /// The two automatic forms present text and a Return; the rest present a Yes/No menu
    /// (<c>OnInitialEvent</c>, <c>RunEvent.cpp:12300</c>). Note <see cref="QuestAccept.Impossible"/>
    /// <b>does</b> ask, even though the answer changes nothing.
    /// </remarks>
    public static bool AsksTheQuestion(int operation) =>
        (QuestAccept)operation is not (QuestAccept.AutoAccept or QuestAccept.ImpossibleAuto);

    /// <summary>
    /// Applies the event's effect and says what should happen next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The stage is set before the state, and only on acceptance.</b> A refusal touches nothing
    /// unless <c>failOnRejection</c> is set, in which case the quest is marked failed — and note
    /// that a failure marks the quest even when the event's stage went to an item or a key.
    /// </para>
    /// <para>
    /// <b>"In progress" is only set at stage 1</b>, and only when the event is not completing the
    /// quest. So an event that advances a quest to stage 3 leaves its state alone; a design that
    /// never passes through stage 1 never starts the quest, which is worth knowing before assuming
    /// a quest tracker is broken.
    /// </para>
    /// <para>
    /// <b>An unreachable chain is not the same as no chain.</b> If the branch's target does not
    /// name an event, the two <i>automatic</i> operations fall back on the event's ordinary chain
    /// while the rest end the run — the reference pushes a do-nothing event, which amounts to the
    /// same thing. That asymmetry exists because an automatic quest event has no branch to name.
    /// </para>
    /// </remarks>
    public static QuestOutcome Resolve(QuestEvent quest, bool accepted, WorldState world,
                                       Func<uint, bool> isValidEvent)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(isValidEvent);

        int id = QuestEventReader.QuestId(quest.Quest);
        var operation = (QuestAccept)quest.Operation;

        if (accepted)
        {
            switch (QuestEventReader.QuestType(quest.Quest))
            {
                case SpecialItems.ItemFlag:
                    world.SetSpecialItemStage(id, quest.Stage);
                    break;

                case SpecialItems.KeyFlag:
                    world.SetKeyStage(id, quest.Stage);
                    break;

                default:                                 // QUEST_FLAG, and the 0 that stands for it
                    world.SetQuestStage(id, quest.Stage);
                    break;
            }

            if (quest.CompleteOnAccept != 0)
            {
                world.SetQuestState(id, QuestState.Complete);
            }
            else if (quest.Stage == 1)
            {
                world.SetQuestState(id, QuestState.InProgress);
            }

            return Branch(true, quest.AcceptChain, operation == QuestAccept.AutoAccept,
                          isValidEvent);
        }

        if (quest.FailOnRejection != 0)
        {
            world.SetQuestState(id, QuestState.Failed);
        }

        return Branch(false, quest.RejectChain, operation == QuestAccept.ImpossibleAuto,
                      isValidEvent);
    }

    private static QuestOutcome Branch(bool accepted, uint chain, bool automatic,
                                       Func<uint, bool> isValidEvent) =>
        chain > 0 && isValidEvent(chain)
            ? new QuestOutcome(accepted, chain, Stop: false)
            : new QuestOutcome(accepted, null, Stop: !automatic);
}
