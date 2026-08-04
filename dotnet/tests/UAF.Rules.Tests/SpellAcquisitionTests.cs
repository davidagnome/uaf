using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers learning spells at character creation — the free allowance, the roll, and the two-pass
/// loop that decides when the screen is finished.
/// </summary>
public class SpellAcquisitionTests
{
    private static SpellLevelState Level(int min = 0, int num = 0, int max = 0, int certain = 0,
                                         int available = 10, int acquired = 0) =>
        Filled(new SpellLevelState(new SpellCounts(min, num, max, certain), available), acquired);

    private static SpellLevelState Filled(SpellLevelState level, int acquired)
    {
        for (int i = 0; i < acquired; i++)
        {
            level.Record(true);
        }
        return level;
    }

    /// <summary>The totals row that sits at index 0 — not a spell level.</summary>
    private static SpellLevelState Totals(int min = 0, int max = 99) =>
        new(new SpellCounts(min, 0, max, 0), available: 0);

    private static AcquireProgress Progress(int pass, int current,
                                            params SpellLevelState[] levels) =>
        SpellAcquisition.Progress(levels, current, pass);

    // ---- the free allowance and the roll -------------------------------------------------------

    [Fact]
    public void The_first_certain_spells_need_no_roll()
    {
        var level = Level(certain: 2);

        Assert.True(SpellAcquisition.Acquires(level, probability: 0, _ => 100));
        level.Record(true);

        Assert.True(SpellAcquisition.Acquires(level, probability: 0, _ => 100));
        level.Record(true);

        // The allowance is spent; now a 0% chance really is 0%.
        Assert.False(SpellAcquisition.Acquires(level, probability: 0, _ => 100));
    }

    [Fact]
    public void The_allowance_counts_successes_not_attempts()
    {
        // numAcquired < certain -- so a failed roll does not eat into the free ones.
        var level = Level(certain: 1);
        level.Record(false);

        Assert.True(SpellAcquisition.Acquires(level, probability: 0, _ => 100));
    }

    [Fact]
    public void Past_the_allowance_it_is_a_percentile_roll()
    {
        var level = Level(certain: 0);

        Assert.True(SpellAcquisition.Acquires(level, probability: 50, _ => 50));
        Assert.False(SpellAcquisition.Acquires(level, probability: 50, _ => 51));
        Assert.True(SpellAcquisition.Acquires(level, probability: 100, _ => 100));
    }

    // ---- pass 0: fill every level to its maximum -----------------------------------------------

    [Fact]
    public void Pass_zero_stays_on_a_level_that_is_short_of_its_maximum()
    {
        var progress = Progress(pass: 0, current: 1, Totals(), Level(max: 3, acquired: 1));

        Assert.Equal(AcquireProgress.None, progress);
    }

    [Fact]
    public void Pass_zero_leaves_a_level_that_has_reached_its_maximum()
    {
        var progress = Progress(pass: 0, current: 1,
                                Totals(), Level(max: 3, acquired: 3), Level(max: 3, acquired: 0));

        Assert.Equal(AcquireProgress.ThisLevel, progress);
        Assert.False(progress.HasFlag(AcquireProgress.AllLevels));
    }

    [Fact]
    public void Pass_zero_finishes_when_every_level_is_full()
    {
        var progress = Progress(pass: 0, current: 1,
                                Totals(), Level(max: 2, acquired: 2), Level(max: 2, acquired: 2));

        Assert.True(progress.HasFlag(AcquireProgress.AllLevels));
    }

    [Fact]
    public void The_global_maximum_ends_it_even_with_room_at_a_level()
    {
        // Index 0 is the totals, not a level -- it carries the ceiling across every level at once.
        var progress = Progress(pass: 0, current: 1,
                                Totals(max: 3), Level(max: 9, acquired: 3));

        Assert.True(progress.HasFlag(AcquireProgress.AllLevels));
    }

    [Fact]
    public void A_level_with_nothing_on_offer_is_out_of_the_reckoning()
    {
        // Not short of its maximum and not short of its minimum -- it simply does not count.
        var progress = Progress(pass: 0, current: 1,
                                Totals(), Level(max: 3, acquired: 3),
                                Level(max: 3, acquired: 0, available: 0));

        Assert.True(progress.HasFlag(AcquireProgress.AllLevels));
    }

    // ---- passes 1 onwards: bring the short levels up to the minimum ----------------------------

    [Fact]
    public void A_later_pass_leaves_the_level_showing_even_when_it_is_the_short_one()
    {
        // Surprising, and the reference is unambiguous: in the oneLevelNotMin branch nothing ever
        // clears FinishedThisLevel. So a top-up pass does NOT sit on a level until it is
        // satisfied -- it moves on immediately and comes back around next pass. The sweep is a
        // round robin, which is the only reason "passes 1 -> n" is plural.
        var progress = Progress(pass: 1, current: 1,
                                Totals(), Level(min: 2, max: 5, acquired: 1));

        Assert.Equal(AcquireProgress.ThisLevel, progress);
        Assert.False(progress.HasFlag(AcquireProgress.AllLevels));
    }

    [Fact]
    public void A_later_pass_holds_a_level_open_only_once_every_minimum_is_met()
    {
        // The one branch that does clear ThisLevel: all levels at their minimum, the global floor
        // still unmet, and this level with room. Only then does the screen stay put.
        var progress = Progress(pass: 1, current: 1,
                                Totals(min: 9), Level(min: 1, max: 5, acquired: 1));

        Assert.Equal(AcquireProgress.None, progress);
    }

    [Fact]
    public void A_later_pass_moves_past_a_level_that_has_reached_its_minimum()
    {
        var progress = Progress(pass: 1, current: 1,
                                Totals(min: 9), Level(min: 2, max: 5, acquired: 2),
                                Level(min: 2, max: 5, acquired: 0));

        Assert.Equal(AcquireProgress.ThisLevel, progress);
    }

    [Fact]
    public void A_later_pass_finishes_once_the_global_minimum_is_met()
    {
        var progress = Progress(pass: 1, current: 1,
                                Totals(min: 2), Level(min: 1, max: 5, acquired: 2));

        Assert.True(progress.HasFlag(AcquireProgress.AllLevels));
    }

    [Fact]
    public void A_later_pass_finishes_when_nothing_is_left_to_take()
    {
        // Every level at its minimum, the global floor unmet, and no level below its maximum --
        // there is nowhere left to go.
        var progress = Progress(pass: 1, current: 1,
                                Totals(min: 99), Level(min: 1, max: 1, acquired: 1));

        Assert.True(progress.HasFlag(AcquireProgress.AllLevels));
    }

    [Fact]
    public void A_later_pass_hitting_the_global_maximum_stops()
    {
        var progress = Progress(pass: 1, current: 1,
                                Totals(max: 1), Level(min: 5, max: 9, acquired: 1));

        Assert.True(progress.HasFlag(AcquireProgress.AllLevels));
    }

    // ---- edges ---------------------------------------------------------------------------------

    [Fact]
    public void No_levels_at_all_is_finished()
    {
        Assert.True(SpellAcquisition.Progress([], current: 1, pass: 0)
                                    .HasFlag(AcquireProgress.AllLevels));
    }

    [Fact]
    public void A_totals_row_on_its_own_is_finished()
    {
        // The loop starts at index 1, so a list of just the totals has no levels to fill.
        Assert.True(Progress(pass: 0, current: 1, Totals()).HasFlag(AcquireProgress.AllLevels));
    }
}
