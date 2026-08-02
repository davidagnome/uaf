using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Which steps of a <c>GUIDED_TOUR</c> run, and where it may start.
/// </summary>
/// <remarks>
/// A guided tour is a design's cutscene — it takes the controls away and walks the party somewhere,
/// captioning the trip. 138 across the corpus.
/// </remarks>
public class GuidedTourTests
{
    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static GuidedTour Tour(int x = 0, int y = 0, int useStart = 0,
                                  params (TourMove Move, string Text)[] steps)
    {
        var list = steps.Select(s => new TourStep(s.Text, (int)s.Move)).ToList();

        // The array is fixed-size on the wire, so the unused slots are real blanks.
        while (list.Count < GuidedTourReader.MaxSteps)
        {
            list.Add(new TourStep(string.Empty, (int)TourMove.NoMove));
        }

        return new GuidedTour(
            new GameEventBase(Control(), NoPic, NoPic, (int)EventType.GuidedTour, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            x, y, Facing: 0, UseStartLocation: useStart, ExecuteEvent: 0, list);
    }

    [Fact]
    public void Only_the_steps_that_move_are_run()
    {
        var tour = Tour(steps: [(TourMove.Forward, "north"), (TourMove.Left, ""),
                                (TourMove.Pause, "look")]);

        Assert.Equal([TourMove.Forward, TourMove.Left, TourMove.Pause],
                     GuidedTourPath.Steps(tour).Select(s => (TourMove)s.Step));
    }

    [Fact]
    public void A_blank_slot_in_the_middle_does_not_end_the_tour()
    {
        // The reference advances past every TStep_NoMove before testing whether the tour is over,
        // so TourOver() reduces to "ran off the end of the array". Treating a blank as the end
        // would truncate any tour with a hole in it.
        var tour = Tour(steps: [(TourMove.Forward, ""), (TourMove.NoMove, ""),
                                (TourMove.Right, "")]);

        Assert.Equal([TourMove.Forward, TourMove.Right],
                     GuidedTourPath.Steps(tour).Select(s => (TourMove)s.Step));
    }

    [Fact]
    public void A_tour_of_nothing_but_blanks_runs_no_steps()
    {
        Assert.Empty(GuidedTourPath.Steps(Tour()));
    }

    [Fact]
    public void The_whole_fixed_array_is_available_to_a_design()
    {
        var full = Tour(steps: [.. Enumerable.Repeat((TourMove.Forward, ""),
                                                     GuidedTourReader.MaxSteps)]);

        Assert.Equal(GuidedTourReader.MaxSteps, GuidedTourPath.Steps(full).Count);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(9, 9, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(10, 0, false)]
    [InlineData(0, 10, false)]
    public void The_starting_square_is_checked_against_the_level(int x, int y, bool expected)
    {
        // The reference abandons the event when this fails -- PopEvent, before a single step runs.
        // It does not clamp, and it does not fall through to the chain.
        Assert.Equal(expected, GuidedTourPath.StartIsValid(Tour(x, y), width: 10, height: 10));
    }
}
