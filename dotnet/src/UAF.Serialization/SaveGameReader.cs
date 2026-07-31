using UAF.Common;

namespace UAF.Serialization;

/// <summary>One suspended task's saved state (<c>TASK_STATE_SAVE</c>, <c>Party.h:421</c>).</summary>
/// <remarks>
/// <c>datacount</c> is an <c>unsigned char</c> between two <c>unsigned int</c>s, and
/// <c>data[MAX_TASK_STATE_SAVE_BYTES]</c> is an array of <b>uints</b> despite the constant's name
/// saying bytes.
/// </remarks>
public sealed record TaskState(uint Id, uint Flags, IReadOnlyList<uint> Data);

/// <summary>
/// A <c>PARTY</c> record's scalars — everything before its nested structures
/// (<c>Party.cpp:953</c>).
/// </summary>
public sealed record PartyState(
    IReadOnlyList<TaskState> TaskStack,
    int Days, int Hours, int Minutes, int DrinkPoints, string Name,
    int Adventuring, int AreaView, int Searching,
    byte Level, byte Speed, int PosX, int PosY, int PrevPosX, int PrevPosY,
    byte Facing, byte ActiveCharacter, byte ActiveItem,
    byte TradeItem, byte TradeGiver, int TradeQuantity,
    byte SkillLevel, byte CharacterCount, int MoneyPooled);

/// <summary>One level's event-trigger flags (<c>LEVEL_FLAG_DATA</c>, <c>Party.cpp:4400</c>).</summary>
/// <remarks>
/// <c>StepCounts</c> is a raw blit of <c>STEP_COUNTER</c> — 16 <c>unsigned long</c>s, one per zone
/// (<c>Externs.h:858</c>) — not a serialized field list, so it is 64 bytes regardless of content.
/// </remarks>
public sealed record LevelFlags(uint[] StepCounts, IReadOnlyDictionary<uint, int> EventResults);

/// <summary>A <c>.pty</c> savegame header, its party state, and a cursor at the rest.</summary>
public sealed record SaveGame(
    DesignVersion Version, PartyState Party, IReadOnlyList<LevelFlags> EventFlags,
    IArchiveCursor Body);

/// <summary>
/// Reads a saved game (<c>.pty</c>), written by <c>serializeGame</c>
/// (<c>UAFWin/Dgngame.cpp:95</c>).
/// </summary>
/// <remarks>
/// <para>
/// The sixth container framing: an 8-byte <c>double</c> version read straight off the file, then a
/// <b>compressed</b> CAR — <c>CAR car(&amp;myFile, load); car.Compress(true);</c>
/// (<c>Dgngame.cpp:184-186</c>). The compression-type byte sits at offset 8 and reads 0x02 in both
/// shipped files, so this is tier 3, the same LZW layer as a compressed <c>game.dat</c>.
/// </para>
/// <para>
/// <b>Only the framing is ported. The body is not.</b> A savegame continues with
/// <c>PARTY::Serialize(CAR&amp;)</c> (<c>Party.cpp:953</c>) and then
/// <c>QUEST_LIST</c>, two <c>SPECIAL_OBJECT_LIST</c>s, the global vaults, an
/// <c>QUEST_LIST</c>, two <c>SPECIAL_OBJECT_LIST</c>s, the global vaults, an
/// <c>ACTIVE_SPELL_LIST</c>, and seven <c>Restore</c> calls covering spells, globals, level info,
/// keys, special items, items and monsters (<c>Dgngame.cpp:188-236</c>) — a different verb from
/// <c>Serialize</c>, and one not yet examined.
/// </para>
/// <para>
/// <b>Two traps in <c>PARTY</c>, both of which produced confident nonsense before being found.</b>
/// The record does not begin at its clock fields — a task state stack comes first
/// (<c>Party.cpp:996</c>), so transcribing from <c>days</c>, which is where a search lands, eats
/// that stack as the time of day. And field widths are not guessable from neighbours:
/// <c>adventuring</c>, <c>areaView</c>, <c>searching</c> and <c>moneyPooled</c> are 4-byte
/// <c>BOOL</c>s, while <c>level</c>, <c>speed</c>, <c>facing</c>, <c>activeCharacter</c>,
/// <c>activeItem</c>, <c>tradeItem</c>, <c>tradeGiver</c>, <c>skillLevel</c> and
/// <c>numCharacters</c> are <c>BYTE</c>s interleaved among them (<c>Party.h:599-613</c>) — yet
/// <c>tradeQty</c>, which reads like a sibling of <c>tradeItem</c>, is an <c>int</c> declared
/// further down. Reading them all as ints yields a plausible-looking party standing at map
/// position (196608, 131072).
/// </para>
/// <para>
/// <b>The <c>VISIT_DATA</c> tag is what makes this verifiable.</b> This format cannot use the
/// read-to-exact-EOF assertion that carried every other one here, because <c>PARTY</c> is only the
/// first of roughly a dozen structures and nothing downstream would notice a drift. But
/// <c>VISIT_DATA::Serialize</c> writes its own name as a marker and the engine asserts on it, with
/// the comment "make sure we are located at the correct offset in the data file"
/// (<c>Party.cpp:4631-4633</c>). <see cref="Read(Stream, ArchiveRole)"/> checks it, so every field
/// width above is a checked claim. It lands on both shipped saves.
/// </para>
/// </remarks>
public static class SaveGameReader
{
    /// <summary>
    /// Below this the engine refuses outright — the event system changed shape
    /// (<c>Dgngame.cpp:157</c>).
    /// </summary>
    public static DesignVersion MinimumVersion => DesignVersion.V0573;

    /// <summary>
    /// The engine also refuses anything below <c>VersionSpellNames</c>, and that same threshold
    /// selects the compressed path (<c>Dgngame.cpp:164,180</c>) — so every loadable save is
    /// compressed and the plain-<c>CArchive</c> branch below it is unreachable in practice.
    /// </summary>
    public static DesignVersion CompressedFrom => DesignVersion.SpellNames;

    /// <summary>
    /// The sanity tag <c>VISIT_DATA::Serialize</c> writes, and the engine asserts on
    /// (<c>Party.cpp:4632</c>).
    /// </summary>
    public const string VisitDataTag = "VISIT_DATA";

    /// <summary><c>MAX_ZONES</c> (<c>Externs.h:858</c>), the length of a <c>STEP_COUNTER</c>.</summary>
    private const int MaxZones = 16;

    public static SaveGame Read(Stream stream, ArchiveRole role = ArchiveRole.Engine)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Seek(0, SeekOrigin.Begin);

        // Read off the raw file, not through any archive.
        var header = new MfcArchiveReader(stream);
        var version = new DesignVersion(header.ReadDouble());

        if (version < MinimumVersion)
        {
            throw new NotSupportedException(
                $"save game version {version.Value} pre-dates the event conversion; the engine " +
                "refuses it too (Dgngame.cpp:157)");
        }

        if (version < CompressedFrom)
        {
            throw new NotSupportedException(
                $"save game version {version.Value} is below VersionSpellNames " +
                $"({CompressedFrom.Value}); the engine refuses it (Dgngame.cpp:164)");
        }

        var cursor = ArchiveCursor.For(CarArchiveReader.Open(stream));
        var party = ReadPartyState(cursor, version);
        var eventFlags = ReadEventTriggerData(cursor);

        // The engine's own alignment check: VISIT_DATA::Serialize writes its name as a tag and the
        // loading branch asserts on it, with the comment "make sure we are located at the correct
        // offset in the data file" (Party.cpp:4631-4633). Verifying it here turns every field
        // width above into a checked claim rather than a hopeful one.
        string tag = cursor.ReadString();
        if (tag != VisitDataTag)
        {
            throw new InvalidDataException(
                $"expected the '{VisitDataTag}' tag after the party's event flags, found " +
                $"'{tag}'. The stream is misaligned somewhere in PARTY.");
        }

        return new SaveGame(version, party, eventFlags, cursor);
    }

    public static SaveGame Read(string path, ArchiveRole role = ArchiveRole.Engine)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Read(stream, role);
    }


    /// <summary>
    /// Reads <c>PARTY::Serialize(CAR&amp;)</c>'s scalars (<c>Party.cpp:996-1050</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two traps here, and both cost a rewrite. The record does <b>not</b> begin at the clock
    /// fields: a task state stack comes first, so transcribing from <c>days</c> — which is where a
    /// search lands — consumes that stack as the time of day.
    /// </para>
    /// <para>
    /// And the width of a field is not guessable from its neighbours. <c>adventuring</c>,
    /// <c>areaView</c>, <c>searching</c> and <c>moneyPooled</c> are 4-byte <c>BOOL</c>s, but
    /// <c>level</c>, <c>speed</c>, <c>facing</c>, <c>activeCharacter</c>, <c>activeItem</c>,
    /// <c>tradeItem</c>, <c>tradeGiver</c>, <c>skillLevel</c> and <c>numCharacters</c> are
    /// <c>BYTE</c>s interleaved among them (<c>Party.h:599-613</c>) — while <c>tradeQty</c>, which
    /// reads like a sibling of <c>tradeItem</c>, is an <c>int</c> declared further down. Reading
    /// them all as ints yields a plausible-looking party at an impossible map position.
    /// </para>
    /// </remarks>
    public static PartyState ReadPartyState(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int taskCount = ar.ReadInt32();
        var tasks = new List<TaskState>(Math.Max(taskCount, 0));
        for (int i = 0; i < taskCount; i++)
        {
            uint id = ar.ReadUInt32();
            uint flags = ar.ReadUInt32();
            int dataCount = ar.ReadByte();

            var data = new uint[dataCount];
            for (int d = 0; d < dataCount; d++)
            {
                data[d] = ar.ReadUInt32();
            }

            tasks.Add(new TaskState(id, flags, data));
        }

        int days = ar.ReadInt32();
        int hours = ar.ReadInt32();
        int minutes = ar.ReadInt32();
        int drinkPoints = ar.ReadInt32();
        string name = ArchiveStringConventions.Decode(ar.ReadString());

        int adventuring = ar.ReadInt32();
        int areaView = ar.ReadInt32();
        int searching = ar.ReadInt32();

        // Three BOOLs for detecting traps, invisibility and magic, dropped at 0.850.
        if (version < DesignVersion.V0850)
        {
            ar.ReadInt32();
            ar.ReadInt32();
            ar.ReadInt32();
        }

        byte level = ar.ReadByte();
        byte speed = ar.ReadByte();
        int posX = ar.ReadInt32();
        int posY = ar.ReadInt32();

        int prevPosX = 0;
        int prevPosY = 0;
        if (version >= DesignVersion.V0575)
        {
            prevPosX = ar.ReadInt32();
            prevPosY = ar.ReadInt32();
        }

        byte facing = ar.ReadByte();
        byte activeCharacter = ar.ReadByte();
        byte activeItem = ar.ReadByte();
        byte tradeItem = ar.ReadByte();
        byte tradeGiver = ar.ReadByte();
        int tradeQuantity = ar.ReadInt32();
        byte skillLevel = ar.ReadByte();
        byte characterCount = ar.ReadByte();
        int moneyPooled = ar.ReadInt32();

        return new PartyState(
            tasks, days, hours, minutes, drinkPoints, name,
            adventuring, areaView, searching,
            level, speed, posX, posY, prevPosX, prevPosY,
            facing, activeCharacter, activeItem,
            tradeItem, tradeGiver, tradeQuantity,
            skillLevel, characterCount, moneyPooled);
    }

    /// <summary>
    /// Reads <c>EVENT_TRIGGER_DATA::Serialize(CAR&amp;)</c> (<c>Party.cpp:614</c>): a count, then
    /// that many <c>LEVEL_FLAG_DATA</c>.
    /// </summary>
    public static List<LevelFlags> ReadEventTriggerData(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var levels = new List<LevelFlags>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            // STEP_COUNTER goes through car.Serialize((char*)&stepData, sizeof(stepData)) -- a raw
            // struct blit, so its 64 bytes are read whole rather than field by field.
            var raw = ar.ReadBytes(MaxZones * sizeof(uint));
            var stepCounts = new uint[MaxZones];
            for (int zone = 0; zone < MaxZones; zone++)
            {
                stepCounts[zone] = BitConverter.ToUInt32(raw, zone * sizeof(uint));
            }

            int flagCount = ar.ReadInt32();
            var results = new Dictionary<uint, int>(Math.Max(flagCount, 0));
            for (int f = 0; f < flagCount; f++)
            {
                uint key = ar.ReadUInt32();

                // TRIGGER_FLAGS is two ints, the first of which the engine itself calls
                // eventStatusUnused (Party.cpp:TRIGGER_FLAGS::Serialize).
                ar.ReadInt32();
                results[key] = ar.ReadInt32();
            }

            levels.Add(new LevelFlags(stepCounts, results));
        }

        return levels;
    }
}
