using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Giving and taking the design's plot tokens (<c>SPECIAL_ITEM_KEY_EVENT_DATA</c>).
/// </summary>
/// <remarks>
/// Special items and keys are <b>global</b>, not carried by a character — they are what
/// <see cref="EventTrigger"/>'s <c>PartyHaveSpecialItem</c> family tests, so a design gates doors
/// and conversations on them.
/// </remarks>
public class SpecialItemsTests
{
    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static SpecialItemEvent Event(params SpecialObjectEvent[] items) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.SpecialItem, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            items, ForceExit: 0, WaitForReturn: 0);

    private static SpecialObjectEvent Give(int index, byte kind = SpecialItems.ItemFlag) =>
        new(kind, SpecialItems.Give, index, 0);

    private static SpecialObjectEvent Take(int index, byte kind = SpecialItems.ItemFlag) =>
        new(kind, SpecialItems.Take, index, 0);

    /// <summary>A world where items 1–3 and keys 1–2 are defined and none is held.</summary>
    private static WorldState World()
    {
        var world = WorldState.FromDesign([], [], []);
        foreach (int id in new[] { 1, 2, 3 })
        {
            world.SetSpecialItemStage(id, 0);
        }
        world.SetKeyStage(1, 0);
        world.SetKeyStage(2, 0);
        return world;
    }

    [Fact]
    public void Giving_an_item_sets_its_stage_to_one()
    {
        var world = World();

        Assert.Equal(1, SpecialItems.Apply(Event(Give(2)), world));

        Assert.True(world.HasSpecialItem(2));
        Assert.Equal(1, world.SpecialItemStage(2));
    }

    [Fact]
    public void Taking_an_item_sets_its_stage_to_zero_rather_than_removing_it()
    {
        // The stage doubles as the possession flag, so "not held" is stage 0 and the item stays
        // defined -- which is what lets a later event give it back.
        var world = World();
        world.SetSpecialItemStage(2, 1);

        Assert.Equal(1, SpecialItems.Apply(Event(Take(2)), world));

        Assert.False(world.HasSpecialItem(2));
        Assert.Equal(0, world.SpecialItemStage(2));
    }

    [Fact]
    public void Giving_an_item_the_party_already_holds_does_not_rewind_its_stage()
    {
        // The reference guards with if (!hasSpecialItem(...)) before calling SetStage(item, 1), so
        // a re-give leaves an item that has progressed past stage 1 where it is.
        var world = World();
        world.SetSpecialItemStage(3, 4);

        Assert.Equal(0, SpecialItems.Apply(Event(Give(3)), world));

        Assert.Equal(4, world.SpecialItemStage(3));
    }

    [Fact]
    public void Taking_something_the_party_does_not_have_is_silent()
    {
        var world = World();

        Assert.Equal(0, SpecialItems.Apply(Event(Take(1)), world));

        Assert.False(world.HasSpecialItem(1));
    }

    [Fact]
    public void An_item_the_design_does_not_define_is_skipped_not_created()
    {
        // "Bogus special item index" in the reference: an event left pointing at a deleted item
        // does nothing rather than resurrecting it.
        var world = World();

        Assert.Equal(0, SpecialItems.Apply(Event(Give(99)), world));

        Assert.False(world.HasSpecialItem(99));
        Assert.False(world.DefinesSpecialItem(99));
    }

    [Fact]
    public void Keys_are_a_separate_store_from_items()
    {
        // Same index, different flag: giving key 1 must not give item 1.
        var world = World();

        SpecialItems.Apply(Event(Give(1, SpecialItems.KeyFlag)), world);

        Assert.True(world.HasKey(1));
        Assert.False(world.HasSpecialItem(1));
    }

    [Fact]
    public void A_list_is_applied_in_order_so_give_then_take_leaves_nothing()
    {
        var world = World();

        Assert.Equal(2, SpecialItems.Apply(Event(Give(1), Take(1)), world));

        Assert.False(world.HasSpecialItem(1));
    }

    [Theory]
    [InlineData((byte)0x00)]                             // no operation
    [InlineData((byte)0x03)]                             // neither give nor take
    public void An_unknown_operation_does_nothing(byte operation)
    {
        var world = World();

        Assert.Equal(0, SpecialItems.Apply(
            Event(new SpecialObjectEvent(SpecialItems.ItemFlag, operation, 1, 0)), world));

        Assert.False(world.HasSpecialItem(1));
    }

    [Fact]
    public void An_unknown_item_type_does_nothing_either()
    {
        var world = World();

        Assert.Equal(0, SpecialItems.Apply(
            Event(new SpecialObjectEvent(0x04, SpecialItems.Give, 1, 0)), world));

        Assert.False(world.HasSpecialItem(1));
        Assert.False(world.HasKey(1));
    }
}
