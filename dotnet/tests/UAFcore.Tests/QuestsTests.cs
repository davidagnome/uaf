using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Taking, advancing and failing quests (<c>QUEST_EVENT_DATA</c>).
/// </summary>
/// <remarks>
/// The largest event type in the corpus that was not executing — 282 across the four designs.
/// </remarks>
public class QuestsTests
{
    private const int QuestId = 7;

    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Packs a type into <c>m_quest</c>'s top nibble the way the format does.</summary>
    private static int Packed(int id, int type) => id | (type << 28);

    private static QuestEvent Quest(
        QuestAccept operation = QuestAccept.AutoAccept,
        ushort stage = 1, int completeOnAccept = 0, int failOnRejection = 0,
        int quest = QuestId, uint acceptChain = 0, uint rejectChain = 0) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.QuestStage, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            (int)operation, completeOnAccept, failOnRejection, quest, stage,
            acceptChain, rejectChain);

    private static WorldState World()
    {
        var world = WorldState.FromDesign([], [], []);
        world.SetQuest(QuestId, QuestState.NotStarted, 0);
        world.SetSpecialItemStage(QuestId, 0);
        world.SetKeyStage(QuestId, 0);
        return world;
    }

    private static QuestOutcome Resolve(QuestEvent quest, bool accepted, WorldState world,
                                        Func<uint, bool>? valid = null) =>
        Quests.Resolve(quest, accepted, world, valid ?? (_ => true));

    // ---- who accepts -----------------------------------------------------------------------------

    [Theory]
    [InlineData(QuestAccept.AutoAccept, 1, true)]
    [InlineData(QuestAccept.AutoAccept, 2, true)]
    [InlineData(QuestAccept.OnYesOrNo, 2, true)]
    [InlineData(QuestAccept.Impossible, 1, false)]
    [InlineData(QuestAccept.ImpossibleAuto, 1, false)]
    [InlineData(QuestAccept.OnYes, 1, true)]
    [InlineData(QuestAccept.OnYes, 2, false)]
    public void The_operation_decides_what_the_answer_means(QuestAccept op, int chose, bool expected)
    {
        Assert.Equal(expected, Quests.IsAccepted((int)op, chose));
    }

    [Fact]
    public void On_no_takes_the_quest_when_the_player_says_no()
    {
        // Not a typo in the reference: a design uses it for a question phrased as a refusal.
        // Collapsing it into OnYes would invert every such event.
        Assert.True(Quests.IsAccepted((int)QuestAccept.OnNo, 2));
        Assert.False(Quests.IsAccepted((int)QuestAccept.OnNo, 1));
    }

    [Fact]
    public void Any_other_menu_entry_is_a_refusal()
    {
        // The reference's inner switch handles only 1 and 2 and leaves `accepted` false.
        Assert.False(Quests.IsAccepted((int)QuestAccept.OnYes, 3));
        Assert.False(Quests.IsAccepted((int)QuestAccept.OnYes, 0));
    }

    [Fact]
    public void Only_the_two_automatic_forms_skip_the_question()
    {
        Assert.False(Quests.AsksTheQuestion((int)QuestAccept.AutoAccept));
        Assert.False(Quests.AsksTheQuestion((int)QuestAccept.ImpossibleAuto));

        // Impossible still asks, even though the answer changes nothing.
        Assert.True(Quests.AsksTheQuestion((int)QuestAccept.Impossible));
        Assert.True(Quests.AsksTheQuestion((int)QuestAccept.OnYes));
    }

    // ---- what acceptance does --------------------------------------------------------------------

    [Fact]
    public void Accepting_at_stage_one_starts_the_quest()
    {
        var world = World();

        Resolve(Quest(stage: 1), accepted: true, world);

        Assert.Equal(1, world.QuestStageOf(QuestId));
        Assert.Equal(QuestState.InProgress, world.QuestStateOf(QuestId));
    }

    [Fact]
    public void Advancing_past_stage_one_moves_the_stage_and_leaves_the_state_alone()
    {
        // "In progress" is only set at stage 1. A design that never passes through it never starts
        // the quest -- worth knowing before assuming a quest tracker is broken.
        var world = World();
        world.SetQuest(QuestId, QuestState.NotStarted, 0);

        Resolve(Quest(stage: 3), accepted: true, world);

        Assert.Equal(3, world.QuestStageOf(QuestId));
        Assert.Equal(QuestState.NotStarted, world.QuestStateOf(QuestId));
    }

    [Fact]
    public void Complete_on_accept_wins_over_the_stage_one_rule()
    {
        var world = World();

        Resolve(Quest(stage: 1, completeOnAccept: 1), accepted: true, world);

        Assert.Equal(QuestState.Complete, world.QuestStateOf(QuestId));
        Assert.Equal(1, world.QuestStageOf(QuestId));
    }

    [Fact]
    public void Refusing_touches_nothing_unless_the_event_says_to_fail()
    {
        var world = World();
        world.SetQuest(QuestId, QuestState.InProgress, 2);

        Resolve(Quest(operation: QuestAccept.Impossible), accepted: false, world);

        Assert.Equal(QuestState.InProgress, world.QuestStateOf(QuestId));
        Assert.Equal(2, world.QuestStageOf(QuestId));
    }

    [Fact]
    public void Fail_on_rejection_marks_the_quest_failed()
    {
        var world = World();
        world.SetQuest(QuestId, QuestState.InProgress, 2);

        Resolve(Quest(failOnRejection: 1), accepted: false, world);

        Assert.Equal(QuestState.Failed, world.QuestStateOf(QuestId));
        Assert.Equal(2, world.QuestStageOf(QuestId));    // the stage is untouched
    }

    // ---- the packed type -------------------------------------------------------------------------

    [Fact]
    public void A_quest_event_can_set_a_special_items_stage_instead()
    {
        // m_quest's top bits carry a type, so this is the second way a design hands out tokens.
        var world = World();

        Resolve(Quest(quest: Packed(QuestId, SpecialItems.ItemFlag), stage: 1),
                accepted: true, world);

        Assert.True(world.HasSpecialItem(QuestId));
        Assert.Equal(0, world.QuestStageOf(QuestId));    // the quest store is not touched
    }

    [Fact]
    public void Or_a_keys()
    {
        var world = World();

        Resolve(Quest(quest: Packed(QuestId, SpecialItems.KeyFlag), stage: 1),
                accepted: true, world);

        Assert.True(world.HasKey(QuestId));
    }

    [Fact]
    public void The_state_calls_always_land_on_the_quest_store_whatever_the_type_is()
    {
        // A genuine asymmetry in the reference: SetStage respects the packed type, SetComplete and
        // SetFailed do not. Reproduced -- a design relying on it would behave differently if the
        // two were made consistent.
        var world = World();

        Resolve(Quest(quest: Packed(QuestId, SpecialItems.ItemFlag), stage: 1,
                      completeOnAccept: 1),
                accepted: true, world);

        Assert.True(world.HasSpecialItem(QuestId));
        Assert.Equal(QuestState.Complete, world.QuestStateOf(QuestId));
    }

    // ---- branching -------------------------------------------------------------------------------

    [Fact]
    public void Acceptance_takes_the_accept_chain()
    {
        var outcome = Resolve(Quest(acceptChain: 50, rejectChain: 60), accepted: true, World());

        Assert.True(outcome.Accepted);
        Assert.Equal(50u, outcome.GoTo);
        Assert.False(outcome.Stop);
    }

    [Fact]
    public void Refusal_takes_the_reject_chain()
    {
        var outcome = Resolve(Quest(operation: QuestAccept.Impossible, acceptChain: 50,
                                    rejectChain: 60),
                              accepted: false, World());

        Assert.Equal(60u, outcome.GoTo);
    }

    [Fact]
    public void An_automatic_accept_with_no_chain_falls_back_on_the_ordinary_one()
    {
        // An automatic quest event has no branch to name, so the reference chains normally.
        var outcome = Resolve(Quest(operation: QuestAccept.AutoAccept, acceptChain: 0),
                              accepted: true, World());

        Assert.Null(outcome.GoTo);
        Assert.False(outcome.Stop);
    }

    [Fact]
    public void An_asked_quest_with_an_unreachable_chain_ends_the_run()
    {
        // The reference pushes a do-nothing event here, which amounts to stopping.
        var outcome = Resolve(Quest(operation: QuestAccept.OnYes, acceptChain: 404),
                              accepted: true, World(), valid: _ => false);

        Assert.Null(outcome.GoTo);
        Assert.True(outcome.Stop);
    }

    [Fact]
    public void An_automatic_refusal_with_no_chain_also_falls_back()
    {
        var outcome = Resolve(Quest(operation: QuestAccept.ImpossibleAuto, rejectChain: 0),
                              accepted: false, World());

        Assert.Null(outcome.GoTo);
        Assert.False(outcome.Stop);
    }
}
