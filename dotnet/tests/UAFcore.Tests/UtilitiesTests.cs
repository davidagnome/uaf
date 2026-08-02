using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The design's arithmetic on its own counters (<c>UTILITIES_EVENT_DATA</c>).
/// </summary>
/// <remarks>
/// 280 across the corpus, and it draws nothing — every special item, key and quest carries a
/// stage, and this reads, writes and compares them.
/// </remarks>
public class UtilitiesTests
{
    private const int Id = 5;

    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static UtilitiesEvent Event(
        MathOperation operation = MathOperation.None,
        byte mathType = SpecialItems.ItemFlag, int mathIndex = -1, ushort amount = 0,
        MultiItemCheck check = MultiItemCheck.None,
        byte resultType = SpecialItems.ItemFlag, int resultIndex = -1,
        int endPlay = 0,
        params SpecialObjectEvent[] items) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.Utilities, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            endPlay, (int)operation, (int)check, mathType, resultType, amount,
            mathIndex, resultIndex, items);

    private static SpecialObjectEvent Entry(int index, byte type = SpecialItems.ItemFlag) =>
        new(type, 0, index, 0);

    private static WorldState World()
    {
        var world = WorldState.FromDesign([], [], []);
        world.SetSpecialItemStage(Id, 0);
        world.SetKeyStage(Id, 0);
        world.SetQuest(Id, QuestState.NotStarted, 0);
        world.SetSpecialItemStage(9, 0);
        return world;
    }

    // ---- arithmetic ------------------------------------------------------------------------------

    [Fact]
    public void Stored_in_writes_the_amount_outright()
    {
        var world = World();

        Utilities.Run(Event(MathOperation.StoredIn, mathIndex: Id, amount: 42), world);

        Assert.Equal(42, world.SpecialItemStage(Id));
    }

    [Fact]
    public void Added_to_and_subtracted_from_clamp_to_a_word()
    {
        var world = World();
        world.SetSpecialItemStage(Id, 65530);

        Utilities.Run(Event(MathOperation.AddedTo, mathIndex: Id, amount: 100), world);
        Assert.Equal(Utilities.MaxStage, world.SpecialItemStage(Id));

        Utilities.Run(Event(MathOperation.SubtractFrom, mathIndex: Id, amount: 65535), world);
        Assert.Equal(0, world.SpecialItemStage(Id));

        // ...and does not go below zero.
        Utilities.Run(Event(MathOperation.SubtractFrom, mathIndex: Id, amount: 5), world);
        Assert.Equal(0, world.SpecialItemStage(Id));
    }

    [Fact]
    public void A_negative_index_switches_the_arithmetic_off()
    {
        var world = World();

        Utilities.Run(Event(MathOperation.StoredIn, mathIndex: -1, amount: 42), world);

        Assert.Equal(0, world.SpecialItemStage(Id));
    }

    [Fact]
    public void Adding_to_a_quest_re_derives_its_state_from_the_stage()
    {
        // Quests go through IncStage, which is a different operation from the plain clamped add
        // that items and keys get.
        var world = World();

        Utilities.Run(Event(MathOperation.AddedTo, mathType: Utilities.QuestFlag,
                            mathIndex: Id, amount: 3), world);

        Assert.Equal(3, world.QuestStageOf(Id));
        Assert.Equal(QuestState.InProgress, world.QuestStateOf(Id));
    }

    [Fact]
    public void Adding_enough_to_a_quest_completes_it()
    {
        var world = World();

        Utilities.Run(Event(MathOperation.AddedTo, mathType: Utilities.QuestFlag,
                            mathIndex: Id, amount: 65535), world);

        // Clamped to the completed sentinel rather than to 65535, which is what keeps it from
        // landing on the failed one just above it.
        Assert.Equal(Utilities.QuestCompletedStage, world.QuestStageOf(Id));
        Assert.Equal(QuestState.Complete, world.QuestStateOf(Id));
        Assert.NotEqual(Utilities.QuestFailedStage, world.QuestStageOf(Id));
    }

    [Fact]
    public void Adding_to_an_already_complete_quest_does_nothing_at_all()
    {
        var world = World();
        world.SetQuest(Id, QuestState.Complete, Utilities.QuestCompletedStage);

        Utilities.Run(Event(MathOperation.AddedTo, mathType: Utilities.QuestFlag,
                            mathIndex: Id, amount: 1), world);

        Assert.Equal(Utilities.QuestCompletedStage, world.QuestStageOf(Id));
        Assert.Equal(QuestState.Complete, world.QuestStateOf(Id));
    }

    [Fact]
    public void Subtracting_from_a_quest_has_no_such_guard()
    {
        // Subtraction takes the plain clamped path for all three stores, so it can drop a quest
        // out of completion without touching its state. The asymmetry is the reference's.
        var world = World();
        world.SetQuest(Id, QuestState.Complete, Utilities.QuestCompletedStage);

        Utilities.Run(Event(MathOperation.SubtractFrom, mathType: Utilities.QuestFlag,
                            mathIndex: Id, amount: 1), world);

        Assert.Equal(Utilities.QuestCompletedStage - 1, world.QuestStageOf(Id));
        Assert.Equal(QuestState.Complete, world.QuestStateOf(Id));
    }

    // ---- the check -------------------------------------------------------------------------------

    [Fact]
    public void All_items_needs_every_one_of_them()
    {
        var world = World();
        world.SetSpecialItemStage(Id, 1);

        Assert.False(Utilities.Run(
            Event(check: MultiItemCheck.AllItems, items: [Entry(Id), Entry(9)]), world).Activated);

        world.SetSpecialItemStage(9, 1);
        Assert.True(Utilities.Run(
            Event(check: MultiItemCheck.AllItems, items: [Entry(Id), Entry(9)]), world).Activated);
    }

    [Fact]
    public void At_least_one_needs_only_one()
    {
        var world = World();
        world.SetSpecialItemStage(9, 1);

        Assert.True(Utilities.Run(
            Event(check: MultiItemCheck.AtLeastOneItem, items: [Entry(Id), Entry(9)]),
            world).Activated);
    }

    [Fact]
    public void An_empty_list_never_activates_under_either_check()
    {
        // "All of nothing" would be vacuously true; the reference writes activate = FALSE for the
        // empty case rather than letting the loop decide.
        var world = World();

        Assert.False(Utilities.Run(Event(check: MultiItemCheck.AllItems), world).Activated);
        Assert.False(Utilities.Run(Event(check: MultiItemCheck.AtLeastOneItem), world).Activated);
    }

    [Fact]
    public void A_blank_entry_is_skipped_rather_than_failed()
    {
        // So a list of nothing but blanks passes AllItems -- the list is non-empty, so the
        // empty-list rule does not save it.
        var world = World();

        Assert.True(Utilities.Run(
            Event(check: MultiItemCheck.AllItems, items: [Entry(-1)]), world).Activated);
    }

    [Fact]
    public void A_quest_counts_as_held_on_its_state_where_an_item_counts_on_its_stage()
    {
        var world = World();
        world.SetQuest(Id, QuestState.Failed, 0);        // stage 0, but the state has moved

        Assert.True(Utilities.Run(
            Event(check: MultiItemCheck.AtLeastOneItem,
                  items: [Entry(Id, Utilities.QuestFlag)]), world).Activated);
    }

    // ---- the award -------------------------------------------------------------------------------

    [Fact]
    public void Activation_increments_the_result_item()
    {
        var world = World();
        world.SetSpecialItemStage(Id, 1);
        world.SetSpecialItemStage(9, 3);

        Utilities.Run(Event(check: MultiItemCheck.AllItems, resultIndex: 9, items: [Entry(Id)]),
                      world);

        Assert.Equal(4, world.SpecialItemStage(9));
    }

    [Fact]
    public void A_result_quest_is_set_to_one_rather_than_incremented()
    {
        // The quest branch writes a literal 1, so awarding the same quest twice does not advance.
        var world = World();
        world.SetSpecialItemStage(Id, 1);
        world.SetQuest(Id, QuestState.InProgress, 6);

        var e = Event(check: MultiItemCheck.AllItems, resultType: Utilities.QuestFlag,
                      resultIndex: Id, items: [Entry(Id)]);

        Utilities.Run(e, world);
        Assert.Equal(1, world.QuestStageOf(Id));

        Utilities.Run(e, world);
        Assert.Equal(1, world.QuestStageOf(Id));
    }

    [Fact]
    public void A_failed_check_awards_nothing()
    {
        var world = World();
        world.SetSpecialItemStage(9, 3);

        Utilities.Run(Event(check: MultiItemCheck.AllItems, resultIndex: 9, items: [Entry(Id)]),
                      world);

        Assert.Equal(3, world.SpecialItemStage(9));
    }

    [Fact]
    public void End_play_is_reported_to_the_caller()
    {
        // The reference pushes EXIT_DATA here, which is the only route a design has to ending
        // the game.
        Assert.True(Utilities.Run(Event(endPlay: 1), World()).EndsPlay);
        Assert.False(Utilities.Run(Event(), World()).EndsPlay);
    }
}
