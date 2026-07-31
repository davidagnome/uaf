using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>UTILITIES_EVENT_DATA</c> — arithmetic on quest/item counters.</summary>
public sealed record UtilitiesEvent(
    GameEventBase Base, int EndPlay, int Operation, int ItemCheck,
    byte MathItemType, byte ResultItemType, ushort MathAmount,
    int MathItemIndex, int ResultItemIndex,
    IReadOnlyList<SpecialObjectEvent> Items) : IGameEvent;

/// <summary>
/// Reads <c>UTILITIES_EVENT_DATA</c> (<c>GameEvent.cpp:10712</c>).
/// </summary>
/// <remarks>
/// <para>
/// The densest mix of widths in any event so far: two <c>BYTE</c>s and a <c>WORD</c> between
/// <c>int</c>s (<c>GameEvent.h:2xxx</c>), giving 24 bytes where a uniform reading would take 32.
/// </para>
/// <para>
/// <c>items</c> is a <c>SPECIAL_OBJECT_EVENT_LIST</c> — same name, same type as in
/// <c>SPECIAL_ITEM_KEY_EVENT_DATA</c>, but that is worth confirming per class rather than assuming.
/// It is read outside the storing/loading branch.
/// </para>
/// </remarks>
public static class UtilitiesEventReader
{
    public static UtilitiesEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        int endPlay = ar.ReadInt32();
        int operation = ar.ReadInt32();
        int itemCheck = ar.ReadInt32();
        byte mathItemType = ar.ReadByte();              // BYTE
        byte resultItemType = ar.ReadByte();            // BYTE
        ushort mathAmount = ar.ReadUInt16();            // WORD
        int mathItemIndex = ar.ReadInt32();
        int resultItemIndex = ar.ReadInt32();

        // Outside the branch.
        var items = SpecialItemEventReader.ReadSpecialObjectList(ar);

        return new UtilitiesEvent(baseEvent, endPlay, operation, itemCheck,
                                  mathItemType, resultItemType, mathAmount,
                                  mathItemIndex, resultItemIndex, items);
    }
}
