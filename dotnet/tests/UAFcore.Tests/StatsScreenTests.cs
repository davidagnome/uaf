using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the stats screen's own state — the highlight, the points and the re-roll.</summary>
public class StatsScreenTests
{
    private static AbilityScores Even(int score = 12) =>
        new(score, 0, score, score, score, score, score);

    /// <summary>Hit points that follow constitution, so a change is visible.</summary>
    private static int FromConstitution(AbilityScores scores) => scores.Constitution;

    private static StatsScreen Open(AbilityScores? scores = null,
                                    Func<string, AbilityLimits>? limits = null,
                                    Func<AbilityScores, AbilityScores>? normalise = null,
                                    Func<int?>? strengthDice = null) =>
        new(scores ?? Even(),
            maxHitPoints: 10,
            limits ?? (_ => new AbilityLimits(3, 0, 18, 0)),
            normalise ?? (s => s),
            strengthDice ?? (() => 74),
            FromConstitution);

    [Fact]
    public void Nothing_is_highlighted_until_the_first_tab()
    {
        var screen = Open();

        Assert.Null(screen.Highlighted);
        Assert.False(screen.Raise());
        Assert.False(screen.Lower());

        screen.Tab();
        Assert.Equal(0, screen.Highlighted);
    }

    [Fact]
    public void Tab_cycles_the_six_and_wraps()
    {
        var screen = Open();

        for (int i = 0; i < StatsScreen.Abilities.Length; i++)
        {
            screen.Tab();
            Assert.Equal(i, screen.Highlighted);
        }

        screen.Tab();
        Assert.Equal(0, screen.Highlighted);
    }

    [Fact]
    public void A_point_taken_from_one_score_can_be_spent_on_another()
    {
        var screen = Open();

        screen.Tab();                       // strength
        Assert.True(screen.Lower());
        Assert.Equal(11, screen.Scores.Strength);
        Assert.Equal(1, screen.Available);

        screen.Tab();                       // intelligence
        Assert.True(screen.Raise());
        Assert.Equal(13, screen.Scores.Intelligence);
        Assert.Equal(0, screen.Available);

        Assert.False(screen.Raise());       // and nothing left to spend
    }

    [Fact]
    public void Hit_points_are_recomputed_and_the_character_is_fully_healed()
    {
        // maxHitPoints = 0; DetermineNewCharMaxHitPoints(seed); hitPoints = maxHitPoints -- so a
        // wounded character shuffling one point ends the screen at full health.
        var screen = Open();

        screen.Tab();
        screen.Lower();                     // strength down, constitution untouched
        Assert.Equal(12, screen.MaxHitPoints);
        Assert.Equal(screen.MaxHitPoints, screen.HitPoints);

        for (int i = 0; i < 4; i++)
        {
            screen.Tab();                   // on to constitution
        }

        screen.Raise();
        Assert.Equal(13, screen.MaxHitPoints);
        Assert.Equal(13, screen.HitPoints);
    }

    [Fact]
    public void Reaching_eighteen_takes_the_class_percentile()
    {
        var screen = Open(new AbilityScores(17, 0, 12, 12, 12, 12, 12));

        screen.Tab();                       // intelligence would be next, so pay from there first
        screen.Tab();
        screen.Lower();
        Assert.Equal(1, screen.Available);

        screen.Tab();
        screen.Tab();
        screen.Tab();
        screen.Tab();
        screen.Tab();                       // back round to strength
        Assert.Equal(0, screen.Highlighted);

        Assert.True(screen.Raise());
        Assert.Equal(18, screen.Scores.Strength);
        Assert.Equal(74, screen.Scores.StrengthMod);
    }

    [Fact]
    public void Dropping_off_eighteen_clears_the_percentile()
    {
        var screen = Open(new AbilityScores(18, 74, 12, 12, 12, 12, 12));

        screen.Tab();
        Assert.True(screen.Lower());

        Assert.Equal(17, screen.Scores.Strength);
        Assert.Equal(0, screen.Scores.StrengthMod);
    }

    [Fact]
    public void A_race_limit_tighter_than_the_class_one_spends_no_point()
    {
        // The class allows 18; the race caps this score at 17. The press is allowed, the score
        // comes straight back, and the point is not charged.
        var screen = Open(new AbilityScores(17, 0, 12, 12, 12, 12, 12),
                          normalise: s => s with { Strength = Math.Min(s.Strength, 17) });

        screen.Tab();
        screen.Tab();
        screen.Lower();                     // a point from intelligence
        Assert.Equal(1, screen.Available);

        for (int i = 0; i < 5; i++)
        {
            screen.Tab();                   // round to strength
        }

        Assert.True(screen.Raise());        // the screen redraws
        Assert.Equal(17, screen.Scores.Strength);
        Assert.Equal(1, screen.Available);  // and the point survives
    }

    [Fact]
    public void Only_the_score_being_changed_goes_through_the_clamps()
    {
        // A character already outside its limits keeps its other scores exactly as they were --
        // an unrelated keypress does not quietly correct them.
        var screen = Open(new AbilityScores(12, 0, 25, 12, 12, 12, 12),
                          normalise: s => s with { Intelligence = Math.Min(s.Intelligence, 18) });

        screen.Tab();
        screen.Lower();

        Assert.Equal(11, screen.Scores.Strength);
        Assert.Equal(25, screen.Scores.Intelligence);
    }

    [Fact]
    public void A_reroll_replaces_everything_and_clears_the_screen()
    {
        var screen = Open();

        screen.Tab();
        screen.Lower();
        Assert.Equal(1, screen.Available);

        Assert.True(screen.Reroll(Even(15), maxHitPoints: 9));

        Assert.Equal(15, screen.Scores.Strength);
        Assert.Equal(9, screen.MaxHitPoints);
        Assert.Equal(9, screen.HitPoints);
        Assert.Equal(0, screen.Available);
        Assert.Null(screen.Highlighted);
    }

    [Fact]
    public void A_failed_reroll_leaves_the_character_alone()
    {
        // The reference tests GetMaxHitPoints() == 0 after generating and copies the pre-roll
        // character back over the failure.
        var screen = Open();

        screen.Tab();
        screen.Lower();

        Assert.False(screen.Reroll(Even(18), maxHitPoints: 0));
        Assert.False(screen.Reroll(null, maxHitPoints: 12));

        Assert.Equal(11, screen.Scores.Strength);
        Assert.Equal(1, screen.Available);
    }

    [Fact]
    public void A_reroll_does_not_refund_the_points_it_discards()
    {
        // The available count is reset by CHOOSESTATS_initial, which the re-roll path calls -- so
        // points spent before a re-roll are simply gone, along with the scores they bought.
        var screen = Open();

        screen.Tab();
        screen.Lower();
        screen.Tab();
        screen.Raise();
        Assert.Equal(0, screen.Available);

        screen.Reroll(Even(14), maxHitPoints: 11);
        Assert.Equal(0, screen.Available);
        Assert.Equal(14, screen.Scores.Strength);
    }
}
