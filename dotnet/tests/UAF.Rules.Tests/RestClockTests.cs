using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers the tally of unbroken rest and the hit point it earns.</summary>
public class RestClockTests
{
    private const int Day = RestClock.MinutesPerDay;

    [Fact]
    public void A_full_day_of_rest_is_worth_one_hit_point()
    {
        var clock = new RestClock();

        Assert.Equal(0, clock.Advance(Day - 1, resting: true));
        Assert.Equal(1, clock.Advance(1, resting: true));
    }

    [Fact]
    public void Time_awake_earns_nothing()
    {
        var clock = new RestClock();

        Assert.Equal(0, clock.Advance(Day * 3, resting: false));
        Assert.Equal(0, clock.MinutesRested);
    }

    [Fact]
    public void A_single_waking_minute_throws_the_whole_tally_away()
    {
        // Not reduced -- zeroed. Twenty-three hours of rest interrupted for a minute is worth
        // nothing at all.
        var clock = new RestClock();

        clock.Advance(Day - 1, resting: true);
        Assert.Equal(Day - 1, clock.MinutesRested);

        clock.Advance(1, resting: false);
        Assert.Equal(0, clock.MinutesRested);

        Assert.Equal(0, clock.Advance(Day - 1, resting: true));
    }

    [Fact]
    public void The_remainder_carries_past_a_day()
    {
        // Reduced by a day rather than cleared, so two days of unbroken rest is two hit points.
        var clock = new RestClock();

        Assert.Equal(1, clock.Advance(Day + 30, resting: true));
        Assert.Equal(30, clock.MinutesRested);

        Assert.Equal(1, clock.Advance(Day - 30, resting: true));
    }

    [Fact]
    public void At_most_one_hit_point_a_cycle_however_long_the_cycle()
    {
        // The reference tests once and subtracts once rather than looping, so a fortnight in one
        // step is a single point with thirteen days still on the tally.
        var clock = new RestClock();

        Assert.Equal(1, clock.Advance(Day * 14, resting: true));
        Assert.Equal(Day * 13, clock.MinutesRested);
    }

    [Fact]
    public void Nothing_elapsed_does_nothing()
    {
        var clock = new RestClock();
        clock.Advance(60, resting: true);

        Assert.Equal(0, clock.Advance(0, resting: false));
        Assert.Equal(60, clock.MinutesRested);          // not even the reset runs
    }

    [Fact]
    public void The_total_counts_every_minute_either_way()
    {
        var clock = new RestClock();

        clock.Advance(100, resting: true);
        clock.Advance(50, resting: false);

        Assert.Equal(150, clock.MinuteTotal);
        Assert.Equal(0, clock.MinutesRested);
    }

    [Fact]
    public void Loading_a_game_starts_the_clock_again()
    {
        var clock = new RestClock();
        clock.Advance(Day - 1, resting: true);

        clock.Reset();

        Assert.Equal(0, clock.MinutesRested);
        Assert.Equal(0, clock.MinuteTotal);
    }
}
