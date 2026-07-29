using UAF.Common;

namespace UAF.Serialization;

/// <summary>One step of a guided tour: a caption and a movement code.</summary>
public sealed record TourStep(string Text, int Step);

/// <summary>A <c>GUIDED_TOUR</c> — a scripted walk through the level.</summary>
public sealed record GuidedTour(
    GameEventBase Base,
    int TourX, int TourY, int Facing, int UseStartLocation, int ExecuteEvent,
    IReadOnlyList<TourStep> Steps);

/// <summary>
/// Reads <c>GUIDED_TOUR</c> (<c>GameEvent.cpp:7125</c>) and its steps.
/// </summary>
/// <remarks>
/// <b>The steps are a fixed-size array, not a counted list</b>, and the loop sits outside the
/// storing/loading branch (<c>GameEvent.cpp:7146</c>). All <see cref="MaxSteps"/> are always on the
/// wire regardless of how many the design actually uses — unused ones carry the blank sentinel.
/// </remarks>
public static class GuidedTourReader
{
    /// <summary>Every tour writes exactly this many steps (<c>GameEvent.h:46</c>).</summary>
    public const int MaxSteps = 24;

    public static GuidedTour Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int tourX = ar.ReadInt32();
        int tourY = ar.ReadInt32();
        int facing = ar.ReadInt32();
        int useStartLocation = ar.ReadInt32();
        int executeEvent = ar.ReadInt32();

        // Outside the branch, and unconditional -- no count precedes it.
        var steps = new List<TourStep>(MaxSteps);
        for (int i = 0; i < MaxSteps; i++)
        {
            steps.Add(new TourStep(
                ArchiveStringConventions.Decode(ar.ReadString()),
                ar.ReadInt32()));
        }

        return new GuidedTour(baseEvent, tourX, tourY, facing, useStartLocation,
                              executeEvent, steps);
    }
}
