using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers which events have already fired — the state that makes <c>OnceOnly</c> mean anything.
/// </summary>
/// <remarks>
/// The reader has kept these since Phase 1 and nothing ever set one, so a once-only event
/// re-fired every time the party stepped on it.
/// </remarks>
public class EventTriggerFlagTests
{
    [Fact]
    public void An_event_that_has_not_fired_has_not_happened()
    {
        var flags = new EventTriggerFlags();

        Assert.False(flags.HasHappened(0, 7));
    }

    [Fact]
    public void Marking_one_event_does_not_mark_its_neighbours()
    {
        var flags = new EventTriggerFlags();
        flags.MarkHappened(0, 7);

        Assert.True(flags.HasHappened(0, 7));
        Assert.False(flags.HasHappened(0, 8));
    }

    [Fact]
    public void The_flags_are_per_level()
    {
        // Two levels can hold events with the same id, and stepping on one must not disarm the
        // other.
        var flags = new EventTriggerFlags();
        flags.MarkHappened(3, 7);

        Assert.True(flags.HasHappened(3, 7));
        Assert.False(flags.HasHappened(4, 7));
    }

    [Fact]
    public void Global_events_are_recorded_one_past_the_last_level()
    {
        // GLOBAL_ART is MAX_LEVELS, so the global list shares the per-level table rather than
        // having one of its own.
        Assert.Equal(255, EventTriggerFlags.GlobalLevel);

        var flags = new EventTriggerFlags();
        flags.MarkHappened(EventTriggerFlags.GlobalLevel, 12);

        Assert.True(flags.HasHappened(EventTriggerFlags.GlobalLevel, 12));
        Assert.False(flags.HasHappened(0, 12));
    }

    [Fact]
    public void A_negative_level_is_ignored_rather_than_throwing()
    {
        // CheckLevel returns FALSE for one and every caller silently does nothing.
        var flags = new EventTriggerFlags();
        flags.MarkHappened(-1, 7);

        Assert.False(flags.HasHappened(-1, 7));
    }

    [Fact]
    public void Marking_twice_is_marking_once()
    {
        var flags = new EventTriggerFlags();
        flags.MarkHappened(0, 7);
        flags.MarkHappened(0, 7);

        Assert.Single(flags.ToRecords()[0].EventResults);
    }

    // ---- zone step counts ----------------------------------------------------------------------

    [Fact]
    public void Zone_steps_start_at_zero_and_count_up()
    {
        var flags = new EventTriggerFlags();
        Assert.Equal(0u, flags.ZoneSteps(2, 3));

        flags.IncZoneSteps(2, 3);
        flags.IncZoneSteps(2, 3);

        Assert.Equal(2u, flags.ZoneSteps(2, 3));
        Assert.Equal(0u, flags.ZoneSteps(2, 4));
    }

    [Fact]
    public void A_zone_outside_the_sixteen_is_ignored()
    {
        var flags = new EventTriggerFlags();
        flags.IncZoneSteps(0, EventTriggerFlags.ZoneCount);
        flags.IncZoneSteps(0, -1);

        Assert.Empty(flags.ToRecords());
    }

    // ---- projecting to and from a savegame ------------------------------------------------------

    [Fact]
    public void The_records_are_dense_from_level_zero()
    {
        // EVENT_TRIGGER_DATA is a CArray indexed by level and CheckLevel grows it with empty
        // entries, so a flag on level 3 means four records. A sparse projection would read back
        // with every level shifted.
        var flags = new EventTriggerFlags();
        flags.MarkHappened(3, 7);

        var records = flags.ToRecords();

        Assert.Equal(4, records.Count);
        Assert.All(records[..3], r => Assert.Empty(r.EventResults));
        Assert.Single(records[3].EventResults);
    }

    [Fact]
    public void Nothing_recorded_is_no_records_at_all()
    {
        Assert.Empty(new EventTriggerFlags().ToRecords());
    }

    [Fact]
    public void Every_level_carries_its_sixteen_zone_counters()
    {
        // STEP_COUNTER is a raw blit of sixteen unsigned longs, so the array is that size whether
        // or not the party has walked anywhere.
        var flags = new EventTriggerFlags();
        flags.MarkHappened(1, 7);

        Assert.All(flags.ToRecords(),
                   r => Assert.Equal(EventTriggerFlags.ZoneCount, r.StepCounts.Length));
    }

    [Fact]
    public void A_marked_event_stores_the_engines_own_result_value()
    {
        var flags = new EventTriggerFlags();
        flags.MarkHappened(0, 7);

        var stored = flags.ToRecords()[0].EventResults[0];

        Assert.Equal(7u, stored.Key);
        Assert.Equal(EventTriggerFlags.HappenedResult, stored.Result);
        Assert.Equal(0, stored.StatusUnused);
    }

    [Fact]
    public void The_flags_survive_a_round_trip_through_the_savegame_shape()
    {
        var before = new EventTriggerFlags();
        before.MarkHappened(0, 7);
        before.MarkHappened(2, 9);
        before.MarkHappened(2, 11);
        before.IncZoneSteps(2, 5);

        var after = EventTriggerFlags.FromRecords(before.ToRecords());

        Assert.True(after.HasHappened(0, 7));
        Assert.True(after.HasHappened(2, 9));
        Assert.True(after.HasHappened(2, 11));
        Assert.False(after.HasHappened(1, 7));
        Assert.Equal(1u, after.ZoneSteps(2, 5));
    }

    [Fact]
    public void A_result_that_is_not_the_engines_value_reads_as_not_happened()
    {
        // HasEventHappened tests eventResult == HasHappenedAtLeastOnce, not "non-zero" -- so any
        // other value reads as not happened, and treating the field as a flag would disagree with
        // the reference on exactly those files.
        var records = new List<LevelFlags>
        {
            new(new uint[EventTriggerFlags.ZoneCount], [new TriggerFlags(7, 0, 2)]),
        };

        Assert.False(EventTriggerFlags.FromRecords(records).HasHappened(0, 7));
    }

    [Fact]
    public void A_cleared_flag_reads_as_not_happened()
    {
        var records = new List<LevelFlags>
        {
            new(new uint[EventTriggerFlags.ZoneCount], [new TriggerFlags(7, 0, 0)]),
        };

        Assert.False(EventTriggerFlags.FromRecords(records).HasHappened(0, 7));
    }

    // ---- the once-only gate ---------------------------------------------------------------------

    /// <summary>An event control that is or is not once-only; nothing else matters here.</summary>
    private static EventControl Control(int onceOnly) =>
        new(0, 0, onceOnly, 0, 0, "", 0, 0, 0, "", "", "", [], "", 0, 0, 0, "", 0, 0);

    [Fact]
    public void An_ordinary_event_is_never_spent()
    {
        var flags = new EventTriggerFlags();
        flags.MarkHappened(0, 7);

        // The flag is recorded for every event that triggers, but only OnceOnly reads it -- so a
        // design can turn OnceOnly on mid-play and find the history already there.
        Assert.False(EventTrigger.AlreadySpent(Control(onceOnly: 0), 7, 0, flags));
    }

    [Fact]
    public void A_once_only_event_is_spent_after_it_fires()
    {
        var flags = new EventTriggerFlags();

        Assert.False(EventTrigger.AlreadySpent(Control(onceOnly: 1), 7, 0, flags));

        flags.MarkHappened(0, 7);

        Assert.True(EventTrigger.AlreadySpent(Control(onceOnly: 1), 7, 0, flags));
    }

    [Fact]
    public void A_once_only_event_is_spent_only_on_the_level_it_fired_on()
    {
        var flags = new EventTriggerFlags();
        flags.MarkHappened(0, 7);

        Assert.False(EventTrigger.AlreadySpent(Control(onceOnly: 1), 7, 1, flags));
    }

    // ---- what this unblocks ---------------------------------------------------------------------

    [Fact]
    public void Trigger_flags_are_no_longer_on_the_list_of_what_a_save_cannot_carry()
    {
        Assert.DoesNotContain("event trigger flags", SaveGameProjection.Untracked);
        Assert.Contains("the journal", SaveGameProjection.Untracked);
    }
}
