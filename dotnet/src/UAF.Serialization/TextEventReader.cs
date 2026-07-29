using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>TEXT_EVENT_DATA</c> — the shared base plus display options and a sound.</summary>
public sealed record TextEvent(
    GameEventBase Base,
    int WaitForReturn, int ForceBackup, int HighlightText, int Distance, string Sound);

/// <summary>
/// Reads <c>TEXT_EVENT_DATA</c> (<c>GameEvent.cpp:8516</c>).
/// </summary>
/// <remarks>
/// The simplest concrete subclass: three flags, one version-gated enum, and a sound name. Most of
/// the record is the shared <see cref="GameEventReader"/> base — the text the event displays lives
/// in the base's <c>Text</c> fields, not here.
/// </remarks>
public static class TextEventReader
{
    public static TextEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int waitForReturn = ar.ReadInt32();
        int forceBackup = ar.ReadInt32();
        int highlightText = ar.ReadInt32();

        int distance = version >= DesignVersion.V0908 ? ar.ReadInt32() : 0;

        string sound = ArchiveStringConventions.Decode(ar.ReadString());

        return new TextEvent(baseEvent, waitForReturn, forceBackup, highlightText,
                             distance, sound);
    }
}
