using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers the arithmetic a running rest needs: how much is left, and how fast it is spent.
/// </summary>
/// <remarks>
/// <b>The field editing is not here.</b> <c>RestTimeFormTests</c> already covers the carrying
/// increment and the refusing decrement — that form was built rounds ago and had no screen behind
/// it, and this was very nearly written a second time before the name collided.
/// </remarks>
public class RestDurationTests
{
    [Fact]
    public void The_total_is_the_three_fields_together()
    {
        Assert.Equal((2 * 1440) + (3 * 60) + 4, new RestDuration(2, 3, 4).TotalMinutes);
        Assert.True(new RestDuration(0, 0, 0).Elapsed);
        Assert.False(new RestDuration(0, 0, 1).Elapsed);
    }

    [Fact]
    public void Subtracting_re_splits_what_is_left()
    {
        var left = new RestDuration(1, 0, 0).Less(90);

        Assert.Equal(new RestDuration(0, 22, 30), left);
    }

    [Fact]
    public void Subtracting_more_than_remains_lands_on_nothing()
    {
        Assert.Equal(new RestDuration(0, 0, 0), new RestDuration(0, 0, 30).Less(500));
        Assert.True(new RestDuration(0, 1, 0).Less(60).Elapsed);
    }

    // ---- the tick ladder -------------------------------------------------------------------------

    [Theory]
    [InlineData(60, 0, 0, 14 * 1440)]     // 60 days -> a fortnight a cycle
    [InlineData(30, 0, 0, 2 * 1440)]      // 30 days -> two days
    [InlineData(2, 0, 0, 1440)]           // 2 days  -> a day
    [InlineData(1, 0, 0, 120)]            // 24 hours -> two hours
    [InlineData(0, 12, 0, 60)]            // 12 hours -> an hour
    [InlineData(0, 6, 0, 30)]
    [InlineData(0, 1, 0, 15)]
    [InlineData(0, 0, 30, 5)]
    [InlineData(0, 0, 15, 1)]
    [InlineData(0, 0, 5, 1)]
    public void Time_passes_faster_the_more_of_it_is_left(int days, int hours, int minutes,
                                                          int delta)
    {
        Assert.Equal(delta, new RestDuration(days, hours, minutes).MinuteDelta());
    }

    [Fact]
    public void The_final_guard_cannot_fire_with_the_ladder_that_runs()
    {
        // "if (minuteTotal < minuteDelta) minuteDelta = 1" -- but every live rung returns a delta
        // no larger than the threshold that selected it, so the remainder is never below it. The
        // guard protects the four commented-out rungs at the top, which took a proportion of what
        // was left rather than a fixed step.
        foreach (int total in new[] { 1, 14, 15, 29, 30, 31, 59, 60, 719, 720, 1439, 1440 })
        {
            var duration = new RestDuration(0, 0, total);
            Assert.True(duration.MinuteDelta() <= total,
                        $"{total} minutes left, stepping {duration.MinuteDelta()}");
        }
    }

    [Fact]
    public void Ticking_down_lands_exactly_on_zero()
    {
        var duration = new RestDuration(0, 7, 23);

        for (int i = 0; i < 1000 && !duration.Elapsed; i++)
        {
            duration = duration.Less(duration.MinuteDelta());
        }

        Assert.Equal(new RestDuration(0, 0, 0), duration);
    }

    [Fact]
    public void Nothing_left_advances_nothing()
    {
        Assert.Equal(0, new RestDuration(0, 0, 0).MinuteDelta());
    }

    [Fact]
    public void Anything_above_sixty_days_still_steps_a_fortnight()
    {
        // The four rungs above this one are commented out in the reference, so a thousand-day
        // rest is not proportionally faster -- it just takes many more cycles.
        Assert.Equal(14 * 1440, new RestDuration(1000, 0, 0).MinuteDelta());
    }
}
