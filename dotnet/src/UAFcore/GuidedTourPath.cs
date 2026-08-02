using UAF.Serialization;

namespace UAFcore;

/// <summary>One move in a guided tour (<c>eventStepType</c>, <c>GameEvent.h:94</c>).</summary>
public enum TourMove
{
    /// <summary>An unused slot. Skipped, not a terminator — see <see cref="GuidedTourPath"/>.</summary>
    NoMove = 0,

    /// <summary>Show this step's text and wait.</summary>
    Pause = 1,

    Forward = 2,
    Left = 3,
    Right = 4,
}

/// <summary>
/// Which steps of a <c>GUIDED_TOUR</c> actually run
/// (<c>GUIDED_TOUR::TakeNextStep</c>, <c>RunEvent.cpp:14652</c>).
/// </summary>
/// <remarks>
/// <para>
/// A guided tour is a design's cutscene: it takes the party's controls away and walks them somewhere,
/// captioning the trip. 138 of them across the corpus.
/// </para>
/// <para>
/// <b>An unused slot is skipped, not a terminator.</b> The step array is a fixed
/// <see cref="GuidedTourReader.MaxSteps"/> entries and the reference's loop advances past every
/// <c>TStep_NoMove</c> before testing whether the tour is over — so a gap in the middle of a
/// design's steps does not end the tour, and <c>TourOver()</c> reduces to "ran off the end of the
/// array". Treating a blank as the end would truncate any tour with a hole in it.
/// </para>
/// </remarks>
public static class GuidedTourPath
{
    /// <summary>The steps that will run, in order.</summary>
    public static List<TourStep> Steps(GuidedTour tour)
    {
        ArgumentNullException.ThrowIfNull(tour);

        return [.. tour.Steps.Where(s => (TourMove)s.Step != TourMove.NoMove)];
    }

    /// <summary>
    /// Whether the tour's own starting square is usable on a map of this size.
    /// </summary>
    /// <remarks>
    /// The reference checks the tour's coordinates against the level's dimensions and
    /// <b>abandons the event</b> if they are out of range — <c>PopEvent()</c>, before a single step
    /// runs. It does not clamp and it does not fall through to the chain, so a design whose tour
    /// names a square on a different level simply does nothing.
    /// </remarks>
    public static bool StartIsValid(GuidedTour tour, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(tour);

        return tour.TourX >= 0 && tour.TourX < width
               && tour.TourY >= 0 && tour.TourY < height;
    }
}
