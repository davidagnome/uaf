using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes the <c>GameEvent</c> base and its <c>EVENT_CONTROL</c> (<c>GameEvent.cpp:2419</c> and
/// <c>:1532</c>) — the preamble every one of the event types begins with.
/// </summary>
/// <remarks>
/// <para>
/// Both storing branches are flat runs with <b>no version gates</b>, the rule that has held for
/// every record type so far — see <see cref="MonsterRecordWriter"/>. The loading halves gate the
/// character id at 0.820, the two ASL blocks at 0.564 and 0.566, the GPDL pair at 0.880 and the
/// party/memorised-spell block at 0.911; none of that is mirrored here.
/// </para>
/// <para>
/// <b>The two ASL blocks have names that break the convention.</b> The event's is
/// <c>EVENT_DATA_ATTR</c> and the control's <c>EVENTCONT_ATTR</c>, not <c>…_ATTRIBUTES</c> —
/// see <see cref="AslMaps"/>. Writing the wrong one produces a file that reads back with the
/// attributes attached to the wrong object.
/// </para>
/// <para>
/// <b><c>CLASS_BASECLASS_ID</c> is one string on the wire whichever half it came from.</b> The
/// reference picks between <c>classID</c> and <c>baseclassID</c> by the event's trigger
/// (<c>GameEvent.h:826</c>), but both derive from <c>CString</c> and only one is ever written, so
/// the destination differs and the bytes do not.
/// </para>
/// </remarks>
public static class GameEventWriter
{
    /// <inheritdoc cref="MonsterRecordWriter.WrittenVersion"/>
    /// <remarks>
    /// 5.24 as everywhere else, set by the two embedded <c>PIC_DATA</c>. The base's own highest
    /// gate is 0.911.
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.V524;

    /// <summary>
    /// Whether an event's shared header can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// The one refusal is <see cref="EventControl.LegacyIds"/>: below 0.998101 an editor-role
    /// design stores <c>itemID</c>, <c>raceID</c>, the class reference and the memorised spell as
    /// <b>numeric database keys</b>, which this port renders as their digits because resolving them
    /// needs the databases in hand. Writing <c>"12"</c> into a modern file would name an item called
    /// <c>"12"</c> rather than item 12 — a file that reads back cleanly and refers to nothing.
    /// The reference resolves as it loads (<c>FindPreVersionSpellNamesItemID</c> and its siblings);
    /// where that conversion is unported there is no honest modern form, which is the same
    /// conclusion <see cref="MonsterRecordWriter"/> reaches about a legacy item id.
    /// </remarks>
    public static bool CanWrite(GameEventBase baseEvent, out string reason)
    {
        ArgumentNullException.ThrowIfNull(baseEvent);

        if (baseEvent.Control.LegacyIds)
        {
            reason = $"Event {baseEvent.Id} (type {baseEvent.EventType}) was read from a design " +
                     "below 0.998101, where its item, race, class and spell references are " +
                     "numeric database keys rather than names. Resolving them needs the databases " +
                     "the reference has loaded; writing the digits would name objects that do not " +
                     "exist.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes the shared header: the control block, two <c>PIC_DATA</c>, then the fields.</summary>
    /// <exception cref="NotSupportedException">
    /// When the event carries legacy numeric ids — see <see cref="CanWrite"/>.
    /// </exception>
    public static void Write(IArchiveWriteCursor ar, GameEventBase baseEvent)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(baseEvent);

        if (!CanWrite(baseEvent, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        WriteControl(ar, baseEvent.Control);

        PicDataWriter.Write(ar, baseEvent.Pic, PicArchiveVariant.Car);
        PicDataWriter.Write(ar, baseEvent.Pic2, PicArchiveVariant.Car);

        ar.WriteInt32(baseEvent.EventType);
        ar.WriteUInt32(baseEvent.Id);
        ar.WriteInt32(baseEvent.X);
        ar.WriteInt32(baseEvent.Y);
        ar.WriteInt32(baseEvent.ChainEventHappen);
        ar.WriteInt32(baseEvent.ChainEventNotHappen);

        WriteDas(ar, baseEvent.Text);
        WriteDas(ar, baseEvent.Text2);
        WriteDas(ar, baseEvent.Text3);

        AslWriter.Write(ar, WrittenVersion, AslMaps.EventData, baseEvent.Attributes);
    }

    /// <summary>Writes an <c>EVENT_CONTROL</c> (<c>GameEvent.cpp:1532</c>).</summary>
    /// <remarks>
    /// <b>Only <c>gpdlData</c> goes through the blank convention.</b> The four id strings and the
    /// memorised spell id are written verbatim — they are <c>CString</c>-derived ids, and an empty
    /// one stays empty rather than becoming the sentinel. The reference marks the difference only
    /// by which macro it used at the call site.
    /// </remarks>
    public static void WriteControl(IArchiveWriteCursor ar, EventControl control)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(control);

        ar.WriteInt32(control.EventStatusUnused);
        ar.WriteInt32(control.EventResultUnused);
        ar.WriteInt32(control.OnceOnly);
        ar.WriteInt32(control.ChainTrigger);
        ar.WriteInt32(control.EventTrigger);

        ar.WriteString(control.ItemId);                  // verbatim: an ITEM_ID
        ar.WriteInt32(control.Quest);
        ar.WriteInt32(control.Chance);
        ar.WriteInt32(control.Facing);
        ar.WriteString(control.RaceId);                  // verbatim: a RACE_ID
        ar.WriteString(control.ClassOrBaseclassId);      // one string either way
        ar.WriteString(control.CharacterId);

        AslWriter.Write(ar, WrittenVersion, AslMaps.EventControl, control.Attributes);

        WriteDas(ar, control.GpdlData);
        ar.WriteInt32(control.GpdlIsBinary);
        ar.WriteInt32(control.PartyX);
        ar.WriteInt32(control.PartyY);
        ar.WriteString(control.MemorizedSpellId);        // verbatim: a SPELL_ID
        ar.WriteUInt32(control.MemorizedSpellClass);
        ar.WriteUInt32(control.MemorizedSpellLevel);
    }

    internal static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
