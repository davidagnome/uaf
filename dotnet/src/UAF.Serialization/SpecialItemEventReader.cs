using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// One entry of a <c>SPECIAL_OBJECT_EVENT_LIST</c> (<c>GameEvent.cpp:366</c>).
/// </summary>
/// <remarks>
/// Ten bytes, not sixteen: <c>ItemType</c> and <c>operation</c> are <c>BYTE</c>s
/// (<c>GameEvent.h:713</c>) and only <c>index</c> and <c>id</c> are <c>int</c>s.
/// </remarks>
public sealed record SpecialObjectEvent(byte ItemType, byte Operation, int Index, int Id);

/// <summary>A <c>SPECIAL_ITEM_KEY_EVENT_DATA</c> — a gate opened by carrying certain items.</summary>
public sealed record SpecialItemEvent(
    GameEventBase Base, IReadOnlyList<SpecialObjectEvent> Items,
    int ForceExit, int WaitForReturn);

/// <summary>
/// Reads <c>SPECIAL_ITEM_KEY_EVENT_DATA</c> (<c>GameEvent.cpp:9243</c>).
/// </summary>
/// <remarks>
/// <para>
/// The <c>items</c> member is a <c>SPECIAL_OBJECT_EVENT_LIST</c> — four ints per entry — and
/// <b>not</b> the <c>ITEM_LIST</c> that several other classes call by the same name. Three
/// different classes declare a member called <c>items</c> with three different types, so the member
/// name alone is not enough to identify the layout; read the declaration in the class itself.
/// </para>
/// <para>
/// <b>The branch is asymmetric.</b> Storing always writes <c>forceExit</c> and
/// <c>WaitForReturn</c>; loading reads them only at 0.830 and above — the same write-new/read-old
/// shape as <c>Specab</c>'s 0.920 gate.
/// </para>
/// </remarks>
public static class SpecialItemEventReader
{
    public static SpecialItemEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);

        // Read outside the storing/loading branch, between the base and the flags.
        var items = ReadSpecialObjectList(ar);

        int forceExit = 0;
        int waitForReturn = 0;
        if (version >= DesignVersion.V0830)
        {
            forceExit = ar.ReadInt32();
            waitForReturn = ar.ReadInt32();
        }

        return new SpecialItemEvent(baseEvent, items, forceExit, waitForReturn);
    }

    /// <summary>Reads a <c>SPECIAL_OBJECT_EVENT_LIST</c> (<c>GameEvent.cpp:496</c>).</summary>
    public static List<SpecialObjectEvent> ReadSpecialObjectList(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var list = new List<SpecialObjectEvent>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            list.Add(new SpecialObjectEvent(
                ar.ReadByte(), ar.ReadByte(), ar.ReadInt32(), ar.ReadInt32()));
        }
        return list;
    }
}
