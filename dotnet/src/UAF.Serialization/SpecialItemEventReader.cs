using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>SPECIAL_ITEM_KEY_EVENT_DATA</c> — a gate opened by carrying certain items.</summary>
public sealed record SpecialItemEvent(
    GameEventBase Base, ItemList Items, int ForceExit, int WaitForReturn);

/// <summary>
/// Reads <c>SPECIAL_ITEM_KEY_EVENT_DATA</c> (<c>GameEvent.cpp:9243</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>items</c> is read between the base and the flags, outside the storing/loading branch.
/// </para>
/// <para>
/// <b>The branch is asymmetric.</b> Storing always writes <c>forceExit</c> and
/// <c>WaitForReturn</c>; loading reads them only at 0.830 and above. So a design written by this
/// code and then read back at an older version would lose eight bytes of alignment — the same
/// write-new/read-old shape as <c>Specab</c>'s 0.920 gate.
/// </para>
/// </remarks>
public static class SpecialItemEventReader
{
    public static SpecialItemEvent Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var baseEvent = GameEventReader.Read(ar, version, role);
        var items = MonsterLeafReaders.ReadItemList(ar, version, role);

        int forceExit = 0;
        int waitForReturn = 0;
        if (version >= DesignVersion.V0830)
        {
            forceExit = ar.ReadInt32();
            waitForReturn = ar.ReadInt32();
        }

        return new SpecialItemEvent(baseEvent, items, forceExit, waitForReturn);
    }
}
