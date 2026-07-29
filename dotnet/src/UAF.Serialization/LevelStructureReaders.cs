using UAF.Common;

namespace UAF.Serialization;

/// <summary>Resting rules for a zone (<c>GameEvent.cpp:5800</c>).</summary>
public sealed record RestEvent(
    int AllowResting, uint Event, int Chance, int EveryMinutes, int PreviousMinuteChecked);

/// <summary>One of a level's sixteen zones (<c>Level.cpp:231</c>).</summary>
public sealed record Zone(
    string SummonedMonster, int AddedTurningDifficulty, int AllowMap,
    int AllowMagic, int AllowAutoDarken,
    string Message, string Name, string IndoorCombatArt, string OutdoorCombatArt,
    BackgroundSoundData Sounds, PicRecord CampArt, PicRecord TreasurePicture,
    RestEvent Rest, IReadOnlyList<AslEntry> Attributes);

/// <summary>A level's zone table plus its area-view art (<c>Level.cpp:568</c>).</summary>
public sealed record ZoneData(IReadOnlyList<Zone> Zones, string AreaViewArt);

/// <summary>A step event — fires when the party walks onto a marked square.</summary>
public sealed record StepEvent(
    int StepCount, uint Event, int ZoneMask, string Name, IReadOnlyList<AslEntry> Attributes);

/// <summary>One wall-set slot: the art and behaviour of a wall type.</summary>
public sealed record WallSetSlot(
    string WallFile, string DoorFile, string OverlayFile, string SoundFile, string AreaViewFile,
    int Used, int DoorFirst, int DrawAreaView, string UnlockSpellId,
    int BlendOverlay, int BlendAmount);

/// <summary>One background slot: the backdrop art and its day/night behaviour.</summary>
public sealed record BackgroundSlot(
    string BackgroundFile, string BackgroundFileAlt, string SoundFile, int SuppressStepSound,
    int Used, int StartTime, int EndTime, int UseAltBackground,
    int UseAlphaBlend, int AlphaBlendPercent, int UseTransparency);

/// <summary>
/// Reads the structures that make up the rest of a level, after its event list.
/// </summary>
public static class LevelStructureReaders
{
    /// <summary>Zones per level — a fixed table, not a design choice.</summary>
    public const int ZonesPerLevel = 16;

    /// <summary>Step-event slots at 1.0210 and above; below it the level writes 8.</summary>
    public const int MaxStepEvents = 255;

    /// <summary>Legacy step-event slot count.</summary>
    public const int LegacyStepEvents = 8;

    /// <summary>Wall and background sets before their counts were written (0.600 / 0.660).</summary>
    public const int LegacySetCount = 16;

    /// <summary>Reads a <c>REST_EVENT</c> (<c>GameEvent.cpp:5800</c>).</summary>
    public static RestEvent ReadRest(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);
        return new RestEvent(ar.ReadInt32(), ar.ReadUInt32(), ar.ReadInt32(),
                             ar.ReadInt32(), ar.ReadInt32());
    }

    /// <summary>Reads one <c>ZONE</c> (<c>Level.cpp:231</c>).</summary>
    /// <remarks>
    /// <c>bgSounds</c> here is a <c>BACKGROUND_SOUND_DATA</c>, the same two-queue type used by
    /// combat events — verified in the class body rather than assumed from the member name.
    /// </remarks>
    public static Zone ReadZone(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string summonedMonster;
        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            int key = ar.ReadInt32();
            summonedMonster = key <= 0 ? string.Empty : key.ToString();
        }
        else
        {
            summonedMonster = ar.ReadString();
        }

        int addedTurningDifficulty = ar.ReadInt32();
        int allowMap = ar.ReadInt32();

        int allowMagic = version >= DesignVersion.V0660 ? ar.ReadInt32() : 0;
        int allowAutoDarken = version >= DesignVersion.V0730 ? ar.ReadInt32() : 0;

        string message = ReadDas(ar);
        string name = ReadDas(ar);
        string indoorCombatArt = ReadDas(ar);
        string outdoorCombatArt = ReadDas(ar);

        var sounds = version >= DesignVersion.V0720
            ? CombatEventReader.ReadBackgroundSoundData(ar)
            : new BackgroundSoundData([], [], 0, 0, 0);

        var campArt = PicDataReader.Read(ar, version, PicArchiveVariant.Car);
        var treasurePicture = PicDataReader.Read(ar, version, PicArchiveVariant.Car);
        var rest = ReadRest(ar);
        var attributes = AslReader.Read(ar, version, AslMaps.Zone);

        return new Zone(summonedMonster, addedTurningDifficulty, allowMap, allowMagic,
                        allowAutoDarken, message, name, indoorCombatArt, outdoorCombatArt,
                        sounds, campArt, treasurePicture, rest, attributes);
    }

    /// <summary>Reads a <c>ZONE_DATA</c> (<c>Level.cpp:568</c>): a count, the zones, then art.</summary>
    public static ZoneData ReadZoneData(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var zones = new List<Zone>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            zones.Add(ReadZone(ar, version, role));
        }

        string areaViewArt = version >= DesignVersion.V0731 ? ReadDas(ar) : string.Empty;
        return new ZoneData(zones, areaViewArt);
    }

    /// <summary>
    /// Reads a <c>STEP_EVENT_DATA</c> (<c>GameEvent.cpp:6016</c>).
    /// </summary>
    /// <remarks>
    /// Despite the name this is <b>not</b> a <c>GameEvent</c> — it has no shared base and its own
    /// ASL name (<c>STEPEVENT_ATTR</c>). Its shape changes completely at 1.0210: below that, a
    /// chained event id followed by one <c>BOOL</c> per zone; above it, four plain fields.
    /// </remarks>
    public static StepEvent ReadStepEvent(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int stepCount = 0, zoneMask = 0;
        uint stepEvent;
        string name = string.Empty;

        if (version.Value < 1.0210)
        {
            stepEvent = ar.ReadUInt32();

            // One BOOL per zone, packed into a mask after reading. The zone count itself was
            // written from 0.5661; before that it was a fixed eight.
            int zoneCount = version >= DesignVersion.V05661 ? ar.ReadInt32() : LegacyStepEvents;
            for (int i = 0; i < zoneCount; i++)
            {
                if (ar.ReadInt32() != 0) zoneMask |= 1 << i;
            }
        }
        else
        {
            stepCount = ar.ReadInt32();
            stepEvent = ar.ReadUInt32();
            zoneMask = ar.ReadInt32();
            name = ar.ReadString();
        }

        var attributes = version >= DesignVersion.V0566
            ? AslReader.Read(ar, version, AslMaps.StepEvent)
            : [];

        return new StepEvent(stepCount, stepEvent, zoneMask, name, attributes);
    }

    /// <summary>Reads a <c>WallSetSlotMemType</c> (<c>PicSlot.cpp:503</c>).</summary>
    public static WallSetSlot ReadWallSet(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string wallFile = ReadDas(ar);
        string doorFile = ReadDas(ar);
        string overlayFile = ReadDas(ar);

        string soundFile = version >= DesignVersion.V05771 ? ReadDas(ar) : string.Empty;
        string areaViewFile = version >= DesignVersion.V0698 ? ReadDas(ar) : string.Empty;

        int used = ar.ReadInt32();
        int doorFirst = version >= DesignVersion.V0694 ? ar.ReadInt32() : 0;
        int drawAreaView = version >= DesignVersion.V0698 ? ar.ReadInt32() : 0;

        string unlockSpellId;
        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            int key = ar.ReadInt32();
            unlockSpellId = key < 0 ? string.Empty : key.ToString();

            // Two retired longs follow, only on this path.
            ar.ReadInt32();
            ar.ReadInt32();
        }
        else
        {
            unlockSpellId = ar.ReadString();
        }

        int blendOverlay = 0, blendAmount = 0;
        if (version >= DesignVersion.V0620)
        {
            blendOverlay = ar.ReadInt32();
            blendAmount = ar.ReadInt32();
        }

        return new WallSetSlot(wallFile, doorFile, overlayFile, soundFile, areaViewFile,
                               used, doorFirst, drawAreaView, unlockSpellId,
                               blendOverlay, blendAmount);
    }

    /// <summary>Reads a <c>BackgroundSlotMemType</c> (<c>PicSlot.cpp:750</c>).</summary>
    public static BackgroundSlot ReadBackgroundSet(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string backgroundFile = ReadDas(ar);
        string backgroundFileAlt = ReadDas(ar);

        string soundFile = string.Empty;
        int suppressStepSound = 0;
        if (version >= DesignVersion.V0640)
        {
            soundFile = ReadDas(ar);
            suppressStepSound = ar.ReadInt32();
        }

        int used = ar.ReadInt32();
        int startTime = ar.ReadInt32();
        int endTime = ar.ReadInt32();
        int useAltBackground = ar.ReadInt32();

        int useAlphaBlend = 0, alphaBlendPercent = 0;
        if (version >= DesignVersion.V0620)
        {
            useAlphaBlend = ar.ReadInt32();
            alphaBlendPercent = ar.ReadInt32();
        }

        int useTransparency = version >= DesignVersion.V0630 ? ar.ReadInt32() : 0;

        return new BackgroundSlot(backgroundFile, backgroundFileAlt, soundFile, suppressStepSound,
                                  used, startTime, endTime, useAltBackground,
                                  useAlphaBlend, alphaBlendPercent, useTransparency);
    }

    /// <summary>Reads a <c>BLOCKAGE_KEYS</c> (<c>Level.cpp:1023</c>): a count then that many ints.</summary>
    public static List<int> ReadBlockageKeys(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var keys = new List<int>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            keys.Add(ar.ReadInt32());
        }
        return keys;
    }

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}

/// <summary>A complete level file.</summary>
public sealed record LevelFile(
    DesignVersion Version, byte Width, byte Height,
    int EventCount, ZoneData Zones, IReadOnlyList<AslEntry> Attributes,
    IReadOnlyList<StepEvent> StepEvents,
    IReadOnlyList<WallSetSlot> WallSets, IReadOnlyList<BackgroundSlot> BackgroundSets,
    IReadOnlyList<int> BlockageKeys);

/// <summary>
/// Reads a whole <c>.lvl</c> file (<c>LEVEL::Serialize</c>, <c>Level.cpp:1224</c>).
/// </summary>
/// <remarks>
/// <para>
/// Order: dimensions, the cell grid, the event list, zone data, the level ASL, step events, wall
/// sets, background sets, and blockage keys.
/// </para>
/// <para>
/// Level files are <b>never compressed</b>, even in designs whose databases are — the compression
/// decision is per file kind.
/// </para>
/// </remarks>
public static class LevelFileReader
{
    /// <summary>
    /// Reads a level, using <paramref name="readEvent"/> to consume each event body.
    /// </summary>
    /// <param name="readEvent">
    /// Returns false for an event type the caller cannot read, which aborts the walk. Injected
    /// rather than referenced directly so this reader does not depend on every event subclass.
    /// </param>
    public static LevelFile Read(Stream stream, ArchiveRole role,
                                 Func<IArchiveCursor, EventType, DesignVersion, bool> readEvent)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(readEvent);

        var header = DesignFileHeader.Read(stream, DesignFileKind.LevelData);
        var plain = new MfcArchiveReader(stream);
        var version = header.Version;

        var (width, height) = LevelReader.ReadDimensions(plain);
        for (int i = 0; i < width * height; i++)
        {
            LevelReader.ReadCell(plain, version);
        }

        var ar = ArchiveCursor.For(plain);

        ar.ReadInt32();                                  // m_level
        int eventCount = ar.ReadInt32();
        for (int i = 0; i < eventCount; i++)
        {
            var type = (EventType)ar.ReadInt32();
            if (EventDispatch.ReadsNothing(type)) continue;

            if (!readEvent(ar, type, version))
            {
                throw new NotSupportedException(
                    $"Event {i} of {eventCount} has type {type}, which the caller cannot read.");
            }
        }

        var zones = LevelStructureReaders.ReadZoneData(ar, version, role);
        var attributes = AslReader.Read(ar, version, AslMaps.Level);

        // Eight slots below 1.0210, otherwise the full table -- always written in full, with
        // unused slots carrying defaults.
        int stepSlots = version.Value < 1.0210
            ? LevelStructureReaders.LegacyStepEvents
            : LevelStructureReaders.MaxStepEvents;

        var stepEvents = new List<StepEvent>(stepSlots);
        for (int i = 0; i < stepSlots; i++)
        {
            stepEvents.Add(LevelStructureReaders.ReadStepEvent(ar, version));
        }

        // Wall and background sets gained explicit counts at different versions -- 0.600 and
        // 0.660 -- so a design between them has a counted wall table and a fixed background one.
        var wallSets = new List<WallSetSlot>();
        int wallCount = version >= DesignVersion.V0600
            ? ar.ReadInt32()
            : LevelStructureReaders.LegacySetCount;
        for (int i = 0; i < wallCount; i++)
        {
            wallSets.Add(LevelStructureReaders.ReadWallSet(ar, version, role));
        }

        var backgroundSets = new List<BackgroundSlot>();
        int backgroundCount = version >= DesignVersion.V0660
            ? ar.ReadInt32()
            : LevelStructureReaders.LegacySetCount;
        for (int i = 0; i < backgroundCount; i++)
        {
            backgroundSets.Add(LevelStructureReaders.ReadBackgroundSet(ar, version));
        }

        var blockageKeys = version >= DesignVersion.V0842
            ? LevelStructureReaders.ReadBlockageKeys(ar)
            : [];

        return new LevelFile(version, width, height, eventCount, zones, attributes,
                             stepEvents, wallSets, backgroundSets, blockageKeys);
    }
}
