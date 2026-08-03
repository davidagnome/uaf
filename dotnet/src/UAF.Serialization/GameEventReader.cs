using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// The trigger and gating conditions shared by every event (<c>GameEvent.cpp:1532</c>).
/// </summary>
/// <param name="LegacyIds">
/// <b>Not a wire field.</b> True when the four database references above were read as
/// pre-0.998101 numeric keys and rendered as their digits, so <c>"12"</c> means "item 12" rather
/// than an item named <c>"12"</c>. Nothing distinguishes the two once read, which is why the
/// provenance is recorded — see <see cref="GameEventWriter.CanWrite"/>.
/// </param>
public sealed record EventControl(
    int EventStatusUnused, int EventResultUnused, int OnceOnly,
    int ChainTrigger, int EventTrigger,
    string ItemId, int Quest, int Chance, int Facing, string RaceId,
    string ClassOrBaseclassId, string CharacterId,
    IReadOnlyList<AslEntry> Attributes,
    string GpdlData, int GpdlIsBinary,
    int PartyX, int PartyY,
    string MemorizedSpellId, uint MemorizedSpellClass, uint MemorizedSpellLevel,
    bool LegacyIds = false);

/// <summary>The fields every event carries, regardless of its concrete type.</summary>
public sealed record GameEventBase(
    EventControl Control, PicRecord Pic, PicRecord Pic2,
    int EventType, uint Id, int X, int Y,
    int ChainEventHappen, int ChainEventNotHappen,
    string Text, string Text2, string Text3,
    IReadOnlyList<AslEntry> Attributes);

/// <summary>
/// Reads the <c>GameEvent</c> base and its <c>EVENT_CONTROL</c> (<c>GameEvent.cpp:2419</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every one of the ~68 <c>*_EVENT_DATA</c> classes opens with <c>GameEvent::Serialize</c> and then
/// reads its own fields, so this is the shared preamble for all of them.
/// </para>
/// <para>
/// <b>Two version sources.</b> These functions gate on the <c>version</c> parameter in some places
/// and on the <b>global</b> <c>LoadingVersion</c> in others — sometimes within a few lines of each
/// other (<c>GameEvent.cpp:1641</c> vs <c>:1644</c>). They normally agree when loading a design, so
/// this reader takes one version and uses it for both; the distinction is recorded here because a
/// case where they diverge would be invisible until a field shifted.
/// </para>
/// <para>
/// <b>The event ASL is not named <c>…_ATTRIBUTES</c>.</b> It is <c>EVENT_DATA_ATTR</c>, and the
/// control block's is <c>EVENTCONT_ATTR</c>. See <see cref="AslMaps"/>.
/// </para>
/// </remarks>
public static class GameEventReader
{
    /// <summary>Below this the event carries no attribute list.</summary>
    public static DesignVersion EventAslGate => DesignVersion.V0564;

    /// <summary>Below this the control block carries no attribute list.</summary>
    public static DesignVersion ControlAslGate => DesignVersion.V0566;

    public static GameEventBase Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var control = ReadControl(ar, version, role);

        // Two PIC_DATA before any of the event's own fields.
        var pic = PicDataReader.Read(ar, version, PicArchiveVariant.Car);
        var pic2 = PicDataReader.Read(ar, version, PicArchiveVariant.Car);

        int eventType = ar.ReadInt32();
        uint id = ar.ReadUInt32();
        int x = ar.ReadInt32();
        int y = ar.ReadInt32();
        int chainEventHappen = ar.ReadInt32();
        int chainEventNotHappen = ar.ReadInt32();

        string text = ReadDas(ar);
        string text2 = ReadDas(ar);
        string text3 = ReadDas(ar);

        var attributes = version >= EventAslGate
            ? AslReader.Read(ar, version, AslMaps.EventData)
            : [];

        return new GameEventBase(control, pic, pic2, eventType, id, x, y,
                                 chainEventHappen, chainEventNotHappen,
                                 text, text2, text3, attributes);
    }

    /// <summary>Reads an <c>EVENT_CONTROL</c> (<c>GameEvent.cpp:1567</c>).</summary>
    public static EventControl ReadControl(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        bool legacyIds = role == ArchiveRole.Editor && version < DesignVersion.SpellNames;

        int eventStatusUnused = ar.ReadInt32();
        int eventResultUnused = ar.ReadInt32();
        int onceOnly = ar.ReadInt32();
        int chainTrigger = ar.ReadInt32();
        int eventTrigger = ar.ReadInt32();

        // Legacy designs stored numeric database keys where later ones store names. Every one of
        // these is an int in the old form and a string in the new, so the widths differ too.
        string itemId = legacyIds ? LegacyKey(ar) : ar.ReadString();

        int quest = ar.ReadInt32();
        int chance = ar.ReadInt32();
        int facing = ar.ReadInt32();

        string raceId = legacyIds ? LegacyKey(ar) : ar.ReadString();

        // CLASS_BASECLASS_ID reads EITHER classID or baseclassID depending on the trigger
        // (GameEvent.h:826) -- but both derive from CString, so it is one string either way and
        // only the destination differs.
        string classOrBaseclassId = legacyIds ? LegacyKey(ar) : ar.ReadString();

        string characterId = string.Empty;
        if (version >= DesignVersion.V0820)
        {
            characterId = legacyIds ? LegacyKey(ar) : ar.ReadString();
        }

        var attributes = version >= ControlAslGate
            ? AslReader.Read(ar, version, AslMaps.EventControl)
            : [];

        string gpdlData = string.Empty;
        int gpdlIsBinary = 0;
        if (version >= DesignVersion.V0880)
        {
            gpdlData = ReadDas(ar);
            gpdlIsBinary = ar.ReadInt32();
        }

        int partyX = 0;
        int partyY = 0;
        string memorizedSpellId = string.Empty;
        uint memorizedSpellClass = 0;
        uint memorizedSpellLevel = 0;
        if (version >= DesignVersion.V0911)
        {
            partyX = ar.ReadInt32();
            partyY = ar.ReadInt32();
            memorizedSpellId = legacyIds ? LegacyKey(ar) : ar.ReadString();
            memorizedSpellClass = ar.ReadUInt32();
            memorizedSpellLevel = ar.ReadUInt32();
        }

        return new EventControl(
            eventStatusUnused, eventResultUnused, onceOnly, chainTrigger, eventTrigger,
            itemId, quest, chance, facing, raceId, classOrBaseclassId, characterId,
            attributes, gpdlData, gpdlIsBinary, partyX, partyY,
            memorizedSpellId, memorizedSpellClass, memorizedSpellLevel, legacyIds);
    }

    /// <summary>
    /// Consumes a pre-<c>VersionSpellNames</c> numeric database key and renders it as text.
    /// </summary>
    /// <remarks>
    /// The reference resolves these against the loaded databases to recover a name. That needs the
    /// databases in hand, which a standalone reader does not have, so the raw key is preserved
    /// instead — enough to keep the stream aligned and to resolve later.
    /// </remarks>
    private static string LegacyKey(IArchiveCursor ar)
    {
        int key = ar.ReadInt32();
        return key <= 0 ? string.Empty : key.ToString();
    }

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}
