using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers what an unappraised gem turns out to be worth.</summary>
public class AppraisalTests
{
    /// <summary>Every die shows its top face.</summary>
    private static int Max(int count, int sides) => count * sides;

    /// <summary>Every die shows a 1.</summary>
    private static int Min(int count, int _) => count;

    [Fact]
    public void The_maximum_is_never_rolled()
    {
        // sides = |max - min| offset by min - 1, which spans min to max - 1. A design writing
        // 10 to 100 gets 10 to 99. Transcribed rather than corrected -- every price in a shipped
        // design was balanced against it.
        var config = new GemConfig(10, 100, "GEM");

        Assert.Equal(99, Appraisal.Value(config, Max));
        Assert.Equal(10, Appraisal.Value(config, Min));
    }

    [Fact]
    public void A_range_of_nothing_is_the_maximum()
    {
        // How a design pins a fixed value: both ends the same.
        var config = new GemConfig(5, 5, "GEM");

        Assert.Equal(5, Appraisal.Value(config, Max));
        Assert.Equal(5, Appraisal.Value(config, Min));
    }

    [Fact]
    public void A_reversed_range_is_taken_as_its_width()
    {
        // abs(max - min), so a design writing them the wrong way round still gets a spread --
        // offset from the min it wrote, which is the larger number.
        var config = new GemConfig(100, 10, "GEM");

        Assert.Equal(189, Appraisal.Value(config, Max));
        Assert.Equal(100, Appraisal.Value(config, Min));
    }

    [Fact]
    public void A_one_wide_range_lands_on_the_minimum()
    {
        var config = new GemConfig(7, 8, "GEM");

        Assert.Equal(7, Appraisal.Value(config, Max));
        Assert.Equal(7, Appraisal.Value(config, Min));
    }

    // ---- when the entry lights up ----------------------------------------------------------------

    [Fact]
    public void Both_the_service_and_the_purse_have_to_agree()
    {
        // A shop that appraises gems still darkens the entry for a party carrying none.
        Assert.True(Appraisal.CanAppraise(offered: true, held: 1));
        Assert.False(Appraisal.CanAppraise(offered: true, held: 0));
        Assert.False(Appraisal.CanAppraise(offered: false, held: 5));
    }
}
