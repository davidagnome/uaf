using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes a whole <c>.lvl</c> file (<c>LEVEL::Serialize</c>, <c>Level.cpp:1224</c>) — the sixth and
/// last record type, and the one an editor actually edits.
/// </summary>
/// <remarks>
/// <para>
/// Order: dimensions, the cell grid, the event chain, zone data, the level ASL, step events, wall
/// sets, background sets and blockage keys.
/// </para>
/// <para>
/// <b>Level files are never compressed</b>, even in a design whose databases are.
/// <c>LoadLevel</c> constructs a <c>CAR</c> and leaves <c>ar.Compress(true)</c> commented out
/// (<c>Level.cpp:2186</c>), so the payload is plain archive primitives at every version — the same
/// "constructed a CAR does not mean CAR bytes" distinction a <c>.chr</c> file turns on. The
/// compression decision is per file kind, not per design.
/// </para>
/// </remarks>
public static class LevelFileWriter
{
    /// <inheritdoc cref="MonsterRecordWriter.WrittenVersion"/>
    /// <remarks>
    /// 5.24, set by the two <c>PIC_DATA</c> inside every zone. The level's own highest gate is
    /// <b>1.0210</b>, where the step-event table changes shape completely — from a chained id and
    /// one <c>BOOL</c> per zone to four plain fields, and from 8 slots to 255.
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.V524;

    /// <summary>
    /// Whether a level can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// A level is mostly events, so most refusals come from them — a type with no writer, or a
    /// body carrying pre-0.998101 numeric ids. Two are the level's own, and both are the fixed
    /// tables it writes without a count: the step-event slots and the sixteen zones.
    /// </remarks>
    public static bool CanWrite(LevelFile level, out string reason)
    {
        ArgumentNullException.ThrowIfNull(level);

        if (level.Cells.Count != level.Width * level.Height)
        {
            reason = $"the grid holds {level.Cells.Count} cells for a " +
                     $"{level.Width}×{level.Height} level, which needs " +
                     $"{level.Width * level.Height}.";
            return false;
        }

        if (level.StepEvents.Count != LevelStructureReaders.MaxStepEvents)
        {
            reason = $"a level writes exactly {LevelStructureReaders.MaxStepEvents} step-event " +
                     $"slots at {WrittenVersion.Value}, not {level.StepEvents.Count}. A design " +
                     "below 1.0210 has 8, and the missing ones have no default the reference " +
                     "would recognise -- its own table is a fixed array of the full size.";
            return false;
        }

        foreach (var entry in level.Entries)
        {
            if (entry.Body is null)
            {
                continue;                                // a bare tag; four bytes and no body
            }

            if (!EventBodyWriter.CanWrite(entry.Type))
            {
                reason = $"the level holds a {entry.Type}, which has no writer. A body has no " +
                         "length prefix, so a level written without it would have every later " +
                         "event read out of the middle of this one.";
                return false;
            }

            if (!GameEventWriter.CanWrite(entry.Body.Base, out string eventReason))
            {
                reason = eventReason;
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Writes a whole <c>.lvl</c> file: the magic, the version stamp, then the payload.
    /// </summary>
    /// <remarks>
    /// The stamp is <see cref="WrittenVersion"/>, not whatever the level was read at — the payload
    /// is always the modern shape, so a header claiming otherwise is the one combination nothing
    /// can read. <see cref="CharacterFileWriter"/> makes the same choice for the same reason.
    /// </remarks>
    public static void WriteFile(Stream stream, LevelFile level)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(level);

        var writer = new MfcArchiveWriter(stream);

        Span<byte> magic = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
            magic, DesignFileHeader.Magic);
        writer.WriteBytes(magic);
        writer.WriteDouble(WrittenVersion.Value);

        Write(writer, level);
    }

    /// <summary>Writes the level payload, without the file header.</summary>
    /// <exception cref="NotSupportedException">
    /// When the level holds a shape that cannot go out — see <see cref="CanWrite"/>.
    /// </exception>
    /// <remarks>
    /// <b>The dimensions go out width-then-height while being declared height-then-width</b>
    /// (<c>Level.h:58</c>), and both are <c>BYTE</c>. Writing them in declaration order transposes
    /// every non-square level silently — the grid still reads back, with the wrong shape.
    /// </remarks>
    public static void Write(MfcArchiveWriter writer, LevelFile level)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(level);

        if (!CanWrite(level, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        writer.WriteByte(level.Width);               // width first -- see the remarks
        writer.WriteByte(level.Height);

        foreach (var cell in level.Cells)
        {
            WriteCell(writer, cell);
        }

        var ar = ArchiveWriteCursor.For(writer);

        ar.WriteInt32(level.Level);                  // m_level
        ar.WriteInt32(level.Entries.Count);

        foreach (var entry in level.Entries)
        {
            ar.WriteInt32((int)entry.Type);
            if (entry.Body is not null)
            {
                EventBodyWriter.Write(ar, entry.Type, entry.Body);
            }
        }

        WriteZoneData(ar, level.Zones);
        AslWriter.Write(ar, WrittenVersion, AslMaps.Level, level.Attributes);

        foreach (var step in level.StepEvents)
        {
            WriteStepEvent(ar, step);
        }

        ar.WriteInt32(level.WallSets.Count);
        foreach (var wall in level.WallSets)
        {
            WriteWallSet(ar, wall);
        }

        ar.WriteInt32(level.BackgroundSets.Count);
        foreach (var background in level.BackgroundSets)
        {
            WriteBackgroundSet(ar, background);
        }

        ar.WriteInt32(level.BlockageKeys.Count);
        foreach (int key in level.BlockageKeys)
        {
            ar.WriteInt32(key);
        }
    }

    /// <summary>Writes one <c>AREA_MAP_DATA</c> cell (<c>Level.cpp:694</c>).</summary>
    /// <remarks>
    /// <para>
    /// Fifteen bytes, every field a single <c>BYTE</c> even where the C++ member is wider —
    /// <c>eventExists</c> is declared <c>BOOL</c> and serialized as one byte.
    /// </para>
    /// <para>
    /// <b>The two display flags are folded back into the background byte's top bits.</b> The reader
    /// strips them so a caller sees the real 0‥63 index; writing the index alone would lose the
    /// distant-background behaviour of every cell that had it.
    /// </para>
    /// <para>
    /// The wall and blockage arrays go out in <b>declaration</b> order — north, south, east, west —
    /// which is not compass order. <see cref="AreaMapCell.WallAt"/> is the permutation a consumer
    /// wants; the wire wants the array as it stands.
    /// </para>
    /// </remarks>
    public static void WriteCell(MfcArchiveWriter writer, AreaMapCell cell)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(cell);

        if (cell.Walls.Length != 4 || cell.Blockage.Length != 4)
        {
            throw new ArgumentException(
                "a cell has four walls and four blockage bytes; the counts are compile-time in " +
                "the reference and never written.", nameof(cell));
        }

        byte raw = (byte)(cell.Background & 0x3F);
        if (cell.ShowDistantBackground) raw |= 0x80;
        if (cell.DistantBackgroundInBands) raw |= 0x40;
        writer.WriteByte(raw);

        writer.WriteByte(cell.NorthBg);
        writer.WriteByte(cell.EastBg);
        writer.WriteByte(cell.SouthBg);
        writer.WriteByte(cell.WestBg);

        writer.WriteByte(cell.Zone);
        writer.WriteByte((byte)(cell.EventExists ? 1 : 0));

        writer.WriteBytes(cell.Walls);
        writer.WriteBytes(cell.Blockage);
    }

    /// <summary>Writes a <c>ZONE_DATA</c> (<c>Level.cpp:568</c>): a count, the zones, then art.</summary>
    public static void WriteZoneData(IArchiveWriteCursor ar, ZoneData zones)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(zones);

        ar.WriteInt32(zones.Zones.Count);
        foreach (var zone in zones.Zones)
        {
            WriteZone(ar, zone);
        }

        WriteDas(ar, zones.AreaViewArt);
    }

    /// <summary>Writes one <c>ZONE</c> (<c>Level.cpp:231</c>).</summary>
    /// <remarks>
    /// <c>bgSounds</c> is a <c>BACKGROUND_SOUND_DATA</c> — the two-queue type, not the bare list.
    /// </remarks>
    public static void WriteZone(IArchiveWriteCursor ar, Zone zone)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(zone);

        ar.WriteString(zone.SummonedMonster);        // verbatim: a MONSTER_ID
        ar.WriteInt32(zone.AddedTurningDifficulty);
        ar.WriteInt32(zone.AllowMap);
        ar.WriteInt32(zone.AllowMagic);
        ar.WriteInt32(zone.AllowAutoDarken);

        WriteDas(ar, zone.Message);
        WriteDas(ar, zone.Name);
        WriteDas(ar, zone.IndoorCombatArt);
        WriteDas(ar, zone.OutdoorCombatArt);

        CombatEventWriter.WriteBackgroundSoundData(ar, zone.Sounds);

        PicDataWriter.Write(ar, zone.CampArt, PicArchiveVariant.Car);
        PicDataWriter.Write(ar, zone.TreasurePicture, PicArchiveVariant.Car);

        WriteRest(ar, zone.Rest);
        AslWriter.Write(ar, WrittenVersion, AslMaps.Zone, zone.Attributes);
    }

    /// <summary>Writes a <c>REST_EVENT</c> (<c>GameEvent.cpp:5800</c>).</summary>
    public static void WriteRest(IArchiveWriteCursor ar, RestEvent rest)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(rest);

        ar.WriteInt32(rest.AllowResting);
        ar.WriteUInt32(rest.Event);
        ar.WriteInt32(rest.Chance);
        ar.WriteInt32(rest.EveryMinutes);
        ar.WriteInt32(rest.PreviousMinuteChecked);
    }

    /// <summary>Writes a <c>STEP_EVENT_DATA</c> (<c>GameEvent.cpp:6016</c>).</summary>
    /// <remarks>
    /// <b>Not a <c>GameEvent</c> despite the name</b> — no shared base, and its own ASL name
    /// (<c>STEPEVENT_ATTR</c>). The modern shape is four plain fields; the pre-1.0210 one is a
    /// chained id and one <c>BOOL</c> per zone, and cannot be produced.
    /// </remarks>
    public static void WriteStepEvent(IArchiveWriteCursor ar, StepEvent step)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(step);

        ar.WriteInt32(step.StepCount);
        ar.WriteUInt32(step.Event);
        ar.WriteInt32(step.ZoneMask);
        ar.WriteString(step.Name);                   // verbatim, not through the blank convention

        AslWriter.Write(ar, WrittenVersion, AslMaps.StepEvent, step.Attributes);
    }

    /// <summary>Writes a <c>WallSetSlotMemType</c> (<c>PicSlot.cpp:503</c>).</summary>
    public static void WriteWallSet(IArchiveWriteCursor ar, WallSetSlot wall)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(wall);

        WriteDas(ar, wall.WallFile);
        WriteDas(ar, wall.DoorFile);
        WriteDas(ar, wall.OverlayFile);
        WriteDas(ar, wall.SoundFile);
        WriteDas(ar, wall.AreaViewFile);

        ar.WriteInt32(wall.Used);
        ar.WriteInt32(wall.DoorFirst);
        ar.WriteInt32(wall.DrawAreaView);

        // The legacy path's two trailing longs are read-only -- they follow the numeric key form,
        // which this writer never produces.
        ar.WriteString(wall.UnlockSpellId);          // verbatim: a SPELL_ID

        ar.WriteInt32(wall.BlendOverlay);
        ar.WriteInt32(wall.BlendAmount);
    }

    /// <summary>Writes a <c>BackgroundSlotMemType</c> (<c>PicSlot.cpp:750</c>).</summary>
    public static void WriteBackgroundSet(IArchiveWriteCursor ar, BackgroundSlot background)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(background);

        WriteDas(ar, background.BackgroundFile);
        WriteDas(ar, background.BackgroundFileAlt);
        WriteDas(ar, background.SoundFile);
        ar.WriteInt32(background.SuppressStepSound);

        ar.WriteInt32(background.Used);
        ar.WriteInt32(background.StartTime);
        ar.WriteInt32(background.EndTime);
        ar.WriteInt32(background.UseAltBackground);

        ar.WriteInt32(background.UseAlphaBlend);
        ar.WriteInt32(background.AlphaBlendPercent);
        ar.WriteInt32(background.UseTransparency);
    }

    private static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
