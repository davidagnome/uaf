using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the experience table read in both directions.</summary>
public class BaseclassTableTests
{
    /// <summary>Levels 1 to 4 at 0, 2000, 4000 and 8000.</summary>
    private static readonly uint[] Levels = [0, 2000, 4000, 8000];

    // ---- what a level costs ------------------------------------------------------------------

    [Fact]
    public void A_levels_cost_is_its_entry()
    {
        Assert.Equal(0, BaseclassTable.CostOfLevel(Levels, 1));
        Assert.Equal(2000, BaseclassTable.CostOfLevel(Levels, 2));
        Assert.Equal(8000, BaseclassTable.CostOfLevel(Levels, 4));
    }

    [Fact]
    public void Levels_are_one_based_and_the_table_is_not()
    {
        // Level 1 is entry zero, so a level outside the table answers nothing rather than reading
        // off the end.
        Assert.Equal(0, BaseclassTable.CostOfLevel(Levels, 0));
        Assert.Equal(0, BaseclassTable.CostOfLevel(Levels, -3));
        Assert.Equal(0, BaseclassTable.CostOfLevel(Levels, 5));
    }

    // ---- what experience reaches -------------------------------------------------------------

    [Fact]
    public void Experience_reaches_the_last_level_it_has_paid_for()
    {
        Assert.Equal(1, BaseclassTable.LevelReached(Levels, 0));
        Assert.Equal(1, BaseclassTable.LevelReached(Levels, 1999));
        Assert.Equal(2, BaseclassTable.LevelReached(Levels, 2000));
        Assert.Equal(3, BaseclassTable.LevelReached(Levels, 7999));
        Assert.Equal(4, BaseclassTable.LevelReached(Levels, 8000));
    }

    [Fact]
    public void Exactly_meeting_an_entry_reaches_that_level()
    {
        // The test is >=, so the experience a level costs is enough to be it rather than one short.
        Assert.Equal(2, BaseclassTable.LevelReached(Levels, BaseclassTable.CostOfLevel(Levels, 2)));
    }

    [Fact]
    public void Experience_past_the_table_stops_at_its_top()
    {
        Assert.Equal(4, BaseclassTable.LevelReached(Levels, 900000));
    }

    [Fact]
    public void A_negative_experience_reaches_no_level_at_all()
    {
        // Zero, not one -- the first entry costs nothing but still has to be paid.
        Assert.Equal(0, BaseclassTable.LevelReached(Levels, -1));
    }

    [Fact]
    public void A_mis_sorted_table_is_read_only_as_far_as_its_first_fall()
    {
        // Counted forwards and stopping at the first entry not yet paid for, which is the
        // reference's own shape.
        uint[] jumbled = [0, 2000, 90000, 4000];

        Assert.Equal(2, BaseclassTable.LevelReached(jumbled, 8000));
    }

    [Fact]
    public void An_empty_table_answers_nothing_in_either_direction()
    {
        Assert.Equal(0, BaseclassTable.LevelReached([], 5000));
        Assert.Equal(0, BaseclassTable.CostOfLevel([], 1));
    }

    // ---- the one entry point -----------------------------------------------------------------

    [Fact]
    public void The_flag_chooses_which_question_is_asked()
    {
        Assert.Equal(2000, BaseclassTable.Read(Levels, 2, wantExperience: true));
        Assert.Equal(2, BaseclassTable.Read(Levels, 2000, wantExperience: false));
    }
}
