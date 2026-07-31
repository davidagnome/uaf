using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>PicDataType</c> — an art slot: a surface type and a filename.</summary>
public sealed record PicDataSlot(int PicType, string Name);

/// <summary>The design's global sounds and music queues.</summary>
public sealed record GlobalSounds(
    string CharHit, string CharMiss, string PartyBump, string PartyStep, string DeathMusic,
    IReadOnlyList<string> IntroMusic, IReadOnlyList<string> CreditsMusic,
    IReadOnlyList<string> CampMusic);

/// <summary>A special object or key (<c>GlobalData.cpp:1245</c>).</summary>
public sealed record SpecialObject(
    string Name, int Id, ushort Stage, uint ExamineEvent, string ExamineLabel,
    int CanBeDropped, IReadOnlyList<AslEntry> Attributes);

/// <summary>A quest (<c>GlobalData.cpp:1784</c>).</summary>
public sealed record Quest(
    string Name, int State, ushort Stage, int Id, IReadOnlyList<AslEntry> Attributes);

/// <summary>
/// Reads the structures that follow <c>GLOBAL_STATS</c>'s attribute list.
/// </summary>
public static class GlobalTailReaders
{
    /// <summary>Art slots written unconditionally after the ASL (<c>GlobalData.cpp:4492</c>).</summary>
    public static readonly string[] AlwaysPresentArt =
    [
        "HBarVPArt", "VBarVPArt", "FrameVPArt", "VBarCombArt", "HBarCombArt",
        "CombatWallArt", "CombatCursorArt", "CombatDeathIconArt",
    ];

    /// <summary>Reads a <c>PicDataType</c> (<c>PicSlot.cpp:900</c>): a type and a name.</summary>
    /// <remarks>
    /// Not to be confused with <c>PIC_DATA</c> — that is the ten-field animation record read by
    /// <see cref="PicDataReader"/>. This one is two fields.
    /// </remarks>
    public static PicDataSlot ReadPicSlot(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);
        return new PicDataSlot(ar.ReadInt32(), ReadDas(ar));
    }

    /// <summary>
    /// Reads the art block that follows the global ASL.
    /// </summary>
    /// <remarks>
    /// Eight unconditional slots, then three gated ones, then a full <c>PIC_DATA</c> cursor at
    /// 0.575 and above. Note <c>CharViewFrameVPArt</c>'s gate is <c>version &gt;= 5.26 ||
    /// IsStoring()</c> — the storing side is unconditional, so a design written by this code and
    /// read back below 5.26 loses alignment.
    /// </remarks>
    public static List<PicDataSlot> ReadArtBlock(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var art = new List<PicDataSlot>(AlwaysPresentArt.Length + 3);
        for (int i = 0; i < AlwaysPresentArt.Length; i++)
        {
            art.Add(ReadPicSlot(ar));
        }

        if (version >= DesignVersion.V526) art.Add(ReadPicSlot(ar));       // CharViewFrameVPArt
        if (version.Value >= 0.930204) art.Add(ReadPicSlot(ar));           // CombatPetrifiedIconArt
        art.Add(ReadPicSlot(ar));                                          // CombatDeathArt

        if (version >= DesignVersion.V0575)
        {
            PicDataReader.Read(ar, version, PicArchiveVariant.Car);        // CursorArt
        }

        return art;
    }

    /// <summary>
    /// Reads a <c>GLOBAL_SOUND_DATA</c> (<c>GlobalData.cpp:1025</c>).
    /// </summary>
    /// <remarks>
    /// The three music queues each arrived at a different version and are gated on the
    /// <b>global</b> <c>globalData.version</c> rather than the parameter: intro was a bare string
    /// below 0.710 and a queue above it, credits appear only from 5.25, and camp only from 0.910.
    /// </remarks>
    public static GlobalSounds ReadSounds(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string charHit = ReadDas(ar);
        string charMiss = ReadDas(ar);
        string partyBump = ReadDas(ar);
        string partyStep = ReadDas(ar);
        string deathMusic = ReadDas(ar);

        List<string> intro;
        if (version < DesignVersion.V0710)
        {
            intro = [ar.ReadString()];                   // a single name, not a queue
        }
        else
        {
            intro = CombatEventReader.ReadBackgroundSounds(ar);
        }

        var credits = version >= DesignVersion.V525
            ? CombatEventReader.ReadBackgroundSounds(ar)
            : [];

        var camp = version >= DesignVersion.V0910
            ? CombatEventReader.ReadBackgroundSounds(ar)
            : [];

        return new GlobalSounds(charHit, charMiss, partyBump, partyStep, deathMusic,
                                intro, credits, camp);
    }

    /// <summary>Reads a <c>SPECIAL_OBJECT_LIST</c>: a count then that many objects.</summary>
    public static List<SpecialObject> ReadSpecialObjects(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var objects = new List<SpecialObject>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            string name = ReadDas(ar);
            int id = ar.ReadInt32();
            ushort stage = ar.ReadUInt16();              // WORD, not int

            uint examineEvent = 0;
            string examineLabel = string.Empty;
            if (version >= DesignVersion.V0810)
            {
                examineEvent = ar.ReadUInt32();
                examineLabel = ReadDas(ar);
            }

            int canBeDropped = version >= DesignVersion.V0830 ? ar.ReadInt32() : 0;

            var attributes = AslReader.Read(ar, version, AslMaps.SpecialObjectData);
            objects.Add(new SpecialObject(name, id, stage, examineEvent, examineLabel,
                                          canBeDropped, attributes));
        }
        return objects;
    }

    /// <summary>Reads a <c>QUEST_LIST</c>: a count then that many quests.</summary>
    public static List<Quest> ReadQuests(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var quests = new List<Quest>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            string name = ReadDas(ar);
            int state = ar.ReadInt32();
            ushort stage = ar.ReadUInt16();              // WORD again
            int id = ar.ReadInt32();

            quests.Add(new Quest(name, state, stage, id,
                                 AslReader.Read(ar, version, AslMaps.QuestData)));
        }
        return quests;
    }

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}

/// <summary>One entry point on a level — where the party arrives from elsewhere.</summary>
public sealed record EntryPoint(int X, int Y);

/// <summary>Per-level metadata held globally rather than in the level file.</summary>
public sealed record LevelStats(
    byte Height, byte Width, int Used, int Overland, int AreaViewStyle, string Name,
    IReadOnlyList<EntryPoint> EntryPoints, string StepSound, string BumpSound,
    BackgroundSoundData? Sounds, WallOverrides? Overrides, CellLevelContents? Contents,
    IReadOnlyList<AslEntry> Attributes);

/// <summary>The design's level table.</summary>
public sealed record LevelInfo(int NumberOfLevels, IReadOnlyDictionary<uint, LevelStats> Levels);

/// <summary>Value range and display name for one class of gem or jewellery.</summary>
public sealed record GemConfig(int MinValue, int MaxValue, string Name);

/// <summary>One coin denomination: its exchange rate, whether it is the base, and its name.</summary>
public sealed record CoinType(double Rate, int IsBase, string Name);

/// <summary>Currency configuration: weights, exchange rates, valuables and denominations.</summary>
public sealed record MoneyData(
    int Weight, int HighestRate, int HighestRateType, int DefaultType,
    GemConfig? Gems, GemConfig? Jewelry, IReadOnlyList<CoinType> Coins);

/// <summary>One difficulty level's monster and experience modifiers.</summary>
public sealed record DifficultyLevel(
    string Name, int ModifyHitDice, int ModifyQuantity, int ModifyMonsterExp, int ModifyAllExp,
    sbyte HitDiceAmount, sbyte QuantityAmount, sbyte MonsterExpAmount, sbyte AllExpAmount);

/// <summary>The five difficulty levels and which is default.</summary>
public sealed record DifficultyData(byte DefaultLevel, IReadOnlyList<DifficultyLevel> Levels);

/// <summary>One journal entry the party has collected.</summary>
public sealed record JournalEntry(int Entry, int OriginalEntry, string Text);

/// <summary>
/// Reads the <c>GLOBAL_STATS</c> structures that follow the character list.
/// </summary>
public static class GlobalStatsTailReaders
{
    /// <summary>Entry points per level — a fixed table (<c>Externs.h:904</c>).</summary>
    public const int MaxEntryPoints = 8;

    /// <summary>Difficulty levels — fixed (<c>GlobalData.h:155</c>).</summary>
    public const int DifficultyLevels = 5;

    /// <summary>
    /// Bytes of the gem/jewellery name written to the archive (<c>Money.h:60</c>).
    /// </summary>
    /// <remarks>
    /// The member is <c>char name[MAX_NAME + 1]</c> — a raw C buffer, not a string and not an
    /// array of them — and the loop writes <c>MAX_NAME</c> <i>characters</i> one at a time. So this
    /// is ten single bytes, not ten counted strings. The extra slot is the NUL terminator, which is
    /// never serialized.
    /// </remarks>
    public const int GemNameLength = 10;

    /// <summary>
    /// Above this version, <c>LEVEL_STATS</c> carries wall overrides and cell contents.
    /// </summary>
    /// <remarks>
    /// <c>_CELL_CONTENTS_VERSION</c> is 5.0 (<c>Externs.h:191</c>). Both structures are now read —
    /// see <see cref="CellContentsReaders"/> — which is what lets a 5.x design be walked past this
    /// point at all.
    /// </remarks>
    public static DesignVersion CellContentsGate => new(5.0);

    /// <summary>Reads a <c>LEVEL_STATS</c> (<c>GlobalData.cpp:3183</c>).</summary>
    public static LevelStats ReadLevelStats(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        byte height = ar.ReadByte();                      // BYTE, as in the level files
        byte width = ar.ReadByte();
        int used = ar.ReadInt32();
        int overland = ar.ReadInt32();

        int areaViewStyle = version >= DesignVersion.V0576 ? ar.ReadInt32() : 0;
        string name = ReadDas(ar);

        // A fixed table of eight, always written. Each is a Win32 POINT: two LONGs.
        var entryPoints = new List<EntryPoint>(MaxEntryPoints);
        for (int i = 0; i < MaxEntryPoints; i++)
        {
            entryPoints.Add(new EntryPoint(ar.ReadInt32(), ar.ReadInt32()));
        }

        string stepSound = string.Empty;
        string bumpSound = string.Empty;
        if (version >= DesignVersion.V0640)
        {
            stepSound = ReadDas(ar);
            bumpSound = ReadDas(ar);
        }

        // Note this is spelled out inline in the reference rather than calling
        // BACKGROUND_SOUND_DATA::Serialize, but the layout is identical.
        BackgroundSoundData? sounds = null;
        if (version >= DesignVersion.V0710)
        {
            sounds = CombatEventReader.ReadBackgroundSoundData(ar);
        }

        WallOverrides? overrides = null;
        CellLevelContents? contents = null;
        if (version >= CellContentsGate)
        {
            overrides = CellContentsReaders.ReadWallOverrides(ar);
            contents = CellContentsReaders.ReadCellContents(ar);
        }

        return new LevelStats(height, width, used, overland, areaViewStyle, name,
                              entryPoints, stepSound, bumpSound, sounds, overrides, contents,
                              AslReader.Read(ar, version, AslMaps.LevelStats));
    }

    /// <summary>
    /// Reads a <c>LEVEL_INFO</c> (<c>GlobalData.cpp:3574</c>).
    /// </summary>
    /// <remarks>
    /// Sparse: a total level count, then a count of <i>populated</i> entries, each preceded by its
    /// own index. So the second number is not the first, and the indices need not be contiguous.
    /// </remarks>
    public static LevelInfo ReadLevelInfo(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int numberOfLevels = ar.ReadInt32();
        int count = ar.ReadInt32();

        var levels = new Dictionary<uint, LevelStats>();
        for (int i = 0; i < count; i++)
        {
            uint index = ar.ReadUInt32();
            levels[index] = ReadLevelStats(ar, version);
        }
        return new LevelInfo(numberOfLevels, levels);
    }

    /// <summary>Reads a <c>GEM_CONFIG</c> (<c>Money.cpp:349</c>).</summary>
    public static GemConfig ReadGemConfig(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int minValue = ar.ReadInt32();
        int maxValue = ar.ReadInt32();

        // Ten raw bytes, NUL-padded -- see GemNameLength.
        byte[] raw = ar.ReadBytes(GemNameLength);
        int length = Array.IndexOf(raw, (byte)0);
        string name = System.Text.Encoding.Latin1.GetString(
            raw, 0, length < 0 ? raw.Length : length);

        return new GemConfig(minValue, maxValue, name);
    }

    /// <summary>Reads a <c>MONEY_DATA_TYPE</c> (<c>Money.cpp:969</c>).</summary>
    public static MoneyData ReadMoneyData(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int weight = version >= DesignVersion.V0662 ? ar.ReadInt32() : 0;
        int highestRate = ar.ReadInt32();

        int highestRateType = 0, defaultType = 0;
        GemConfig? gems = null, jewelry = null;
        if (version >= DesignVersion.V0661)
        {
            highestRateType = ar.ReadInt32();
            defaultType = ar.ReadInt32();
            gems = ReadGemConfig(ar);
            jewelry = ReadGemConfig(ar);
        }

        // Ten COIN_TYPE records, read OUTSIDE the storing/loading branch (Money.cpp:998) and so
        // present at every version. Note these are full records here -- MONEY_SACK's Coins[] of
        // the same name is a plain int array.
        var coins = new List<CoinType>(MonsterLeafReaders.MaxCoinTypes);
        for (int i = 0; i < MonsterLeafReaders.MaxCoinTypes; i++)
        {
            coins.Add(ReadCoinType(ar));
        }

        return new MoneyData(weight, highestRate, highestRateType, defaultType,
                             gems, jewelry, coins);
    }

    /// <summary>Bytes of a coin's name written to the archive (<c>Money.h:32</c>).</summary>
    public const int CoinNameLength = 10;

    /// <summary>
    /// Reads a <c>COIN_TYPE</c> (<c>Money.cpp:185</c>).
    /// </summary>
    /// <remarks>
    /// <c>rate</c> is a <c>double</c>, and <c>Name</c> is a raw <c>char</c> buffer read one byte at
    /// a time — the same shape as <see cref="ReadGemConfig"/>'s name.
    /// </remarks>
    public static CoinType ReadCoinType(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        double rate = ar.ReadDouble();
        int isBase = ar.ReadInt32();

        byte[] raw = ar.ReadBytes(CoinNameLength);
        int length = Array.IndexOf(raw, (byte)0);
        string name = System.Text.Encoding.Latin1.GetString(
            raw, 0, length < 0 ? raw.Length : length);

        return new CoinType(rate, isBase, name);
    }

    /// <summary>
    /// Reads a <c>DIFFICULTY_LEVEL_DATA</c> (<c>GlobalData.cpp:849</c>).
    /// </summary>
    /// <remarks>
    /// The five per-level records are read <b>outside</b> the storing/loading branch, so only
    /// <c>m_defaultLvl</c> is inside it. Each level's four amount fields are <c>char</c>, not
    /// <c>int</c> — 4 bytes where a uniform reading would take 16.
    /// </remarks>
    public static DifficultyData ReadDifficulty(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        byte defaultLevel = ar.ReadByte();                // BYTE

        var levels = new List<DifficultyLevel>(DifficultyLevels);
        for (int i = 0; i < DifficultyLevels; i++)
        {
            levels.Add(new DifficultyLevel(
                ReadDas(ar),
                ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32(), ar.ReadInt32(),
                (sbyte)ar.ReadByte(), (sbyte)ar.ReadByte(),
                (sbyte)ar.ReadByte(), (sbyte)ar.ReadByte()));
        }
        return new DifficultyData(defaultLevel, levels);
    }

    /// <summary>Reads a <c>JOURNAL_DATA</c> (<c>Party.h:186</c>): a count then the entries.</summary>
    public static List<JournalEntry> ReadJournal(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var entries = new List<JournalEntry>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            entries.Add(new JournalEntry(ar.ReadInt32(), ar.ReadInt32(), ReadDas(ar)));
        }
        return entries;
    }

    private static string ReadDas(IArchiveCursor ar) =>
        ArchiveStringConventions.Decode(ar.ReadString());
}
