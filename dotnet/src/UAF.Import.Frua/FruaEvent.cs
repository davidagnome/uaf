namespace UAF.Import.Frua;

/// <summary>
/// What an event does (<c>ConvertEventType</c>, <c>UAFWinEd/UAImport.cpp:4317</c>).
/// </summary>
/// <remarks>
/// The values are FRUA's stored bytes, and the names are the engine's <c>eventType</c>. <b>28, 30
/// and 31 are gaps</b> — the reference's switch has no case for them and they fall to
/// <see cref="None"/> along with any other unknown byte.
/// </remarks>
public enum FruaEventType
{
    None = 0,
    Combat = 1,
    TextStatement = 2,
    GiveTreasure = 3,
    Damage = 4,
    Stairs = 5,
    TrainingHall = 6,
    Tavern = 7,
    Shop = 8,
    Temple = 9,
    QuestionButton = 10,
    TransferModule = 11,
    GuidedTour = 12,
    AddNpc = 13,
    NpcSays = 14,
    Encounter = 15,
    Utilities = 16,
    Sounds = 17,
    WhoTries = 18,
    WhoPays = 19,
    EnterPassword = 20,
    QuestionList = 21,
    SmallTown = 22,
    ChainEvent = 23,
    Vault = 24,
    CombatTreasure = 25,
    GainExperience = 26,
    PassTime = 27,
    Camp = 29,
    RemoveNpc = 32,
    PickOneCombat = 33,
    Teleporter = 34,
    QuestStage = 35,
    QuestionYesNo = 36,
    TavernTales = 37,
    SpecialItem = 38,
}

/// <summary>
/// What must be true for an event to fire (<c>ConvertEventControl</c>,
/// <c>UAFWinEd/UAImport.cpp:4133</c>).
/// </summary>
/// <remarks>
/// The values are the stored byte's top five bits, which step by eight. <b>There is no case 0</b>:
/// the reference's switch leaves the trigger at its default, which is this port's
/// <see cref="Always"/>.
/// </remarks>
public enum FruaTrigger
{
    Always = 0,
    PartyHaveItem = 8,
    PartyNotHaveItem = 16,
    Daytime = 24,
    Nighttime = 32,
    RandomChance = 40,
    PartySearching = 48,
    PartyNotSearching = 56,
    FacingDirection = 64,
    QuestComplete = 72,
    QuestFailed = 80,
    QuestInProgress = 88,
    PartyDetectingTraps = 96,
    PartyNotDetectingTraps = 104,
    PartySeeInvisible = 112,
    PartyNotSeeInvisible = 120,
    ClassInParty = 128,
    RaceInParty = 136,
}

/// <summary>How a chained event decides whether to run.</summary>
public enum FruaChainTrigger
{
    Always = 0,
    IfEventHappened = 2,
    IfEventDidNotHappen = 4,
}

/// <summary>What a trigger's data byte is naming.</summary>
/// <remarks>
/// One numbering covers all three (<c>GetObjectKeyType</c>, <c>UAImport.cpp:1737</c>): 0–7 are the
/// eight special keys, 8–19 the twelve special items, and 20–63 the forty-four quests. So a single
/// byte addresses any of them and the range decides which.
/// </remarks>
public enum FruaObjectKind
{
    Key,
    Item,
    Quest,
}

/// <summary>
/// One of a level's hundred event records (<c>UAImportEvent</c>,
/// <c>UAFWinEd/UAImport.cpp:1768</c>).
/// </summary>
/// <remarks>
/// <para>
/// Twenty bytes: type, trigger, trigger data, chain event, then sixteen bytes whose meaning is
/// decided by the type.
/// </para>
/// <para>
/// <b>The reference addresses those sixteen bytes by a one-based offset over the whole record.</b>
/// <c>EventByte</c> reads <c>pData[FileOffset - 5]</c> where <c>pData</c> is the sixteen-byte tail,
/// so offset 5 is its first byte: one-based, less the four header bytes, less one. Every offset
/// quoted in the reference's per-type readers is in that scheme, which is why
/// <see cref="Byte"/>, <see cref="Word"/> and <see cref="Dword"/> take it directly rather than
/// making each call site subtract.
/// </para>
/// <para>
/// <b>And <c>FileOffset</c> is a <c>BYTE</c></b>, so no event field can be addressed past 255 —
/// which is academic at sixteen bytes of payload, but it is why the reference never grew a larger
/// record.
/// </para>
/// </remarks>
public sealed record FruaEvent(
    FruaEventType Type,
    byte RawType,
    bool OnceOnly,
    FruaChainTrigger ChainTrigger,
    FruaTrigger Trigger,
    byte TriggerData,
    byte ChainEvent,
    IReadOnlyList<byte> Data)
{
    /// <summary>Bytes per event record on disk.</summary>
    public const int Length = 20;

    /// <summary>How many records a level file stores.</summary>
    public const int PerLevel = 100;

    /// <summary>Where the records begin, right after the ENCR marker.</summary>
    public const int At = 3786;

    /// <summary>The first offset that addresses the payload rather than the header.</summary>
    private const int PayloadBase = 5;

    /// <summary>Reads one record.</summary>
    public static FruaEvent Read(ReadOnlySpan<byte> bytes)
    {
        byte trigger = bytes[1];

        return new FruaEvent(
            Type: TypeOf(bytes[0]),
            RawType: bytes[0],
            OnceOnly: (trigger & 0x01) != 0,
            ChainTrigger: (trigger & 0x06) switch
            {
                2 => FruaChainTrigger.IfEventHappened,
                4 => FruaChainTrigger.IfEventDidNotHappen,
                _ => FruaChainTrigger.Always,
            },
            Trigger: Enum.IsDefined((FruaTrigger)(trigger & 0xF8))
                ? (FruaTrigger)(trigger & 0xF8)
                : FruaTrigger.Always,
            TriggerData: bytes[2],
            ChainEvent: bytes[3],
            Data: bytes.Slice(4, 16).ToArray());
    }

    /// <summary>Reads a level's hundred records.</summary>
    public static IReadOnlyList<FruaEvent> ReadAll(ReadOnlySpan<byte> level)
    {
        var events = new FruaEvent[PerLevel];

        for (int i = 0; i < PerLevel; i++)
        {
            events[i] = Read(level.Slice(At + (i * Length), Length));
        }

        return events;
    }

    /// <summary>A payload byte at the reference's own one-based record offset.</summary>
    public byte Byte(int fileOffset) => Data[fileOffset - PayloadBase];

    /// <summary>A little-endian word at the reference's own record offset.</summary>
    public ushort Word(int fileOffset) =>
        (ushort)(Byte(fileOffset) | (Byte(fileOffset + 1) << 8));

    /// <summary>
    /// A little-endian dword at the reference's own record offset.
    /// </summary>
    /// <remarks>
    /// The reference assembles it as two words rather than four bytes, which comes to the same
    /// thing — but it is worth knowing that its <c>GetDWord</c> reads
    /// <c>(temp2 &lt;&lt; 16) | temp1</c> and so is plain little-endian, unlike the zone-message
    /// words in the level header, which are byte-swapped on read.
    /// </remarks>
    public uint Dword(int fileOffset) =>
        (uint)(Word(fileOffset) | (Word(fileOffset + 2) << 16));

    /// <summary>Whether this record does anything at all.</summary>
    public bool IsEmpty => Type == FruaEventType.None;

    /// <summary>
    /// What <see cref="TriggerData"/> names, when the trigger is one that names something.
    /// </summary>
    public static FruaObjectKind ObjectKind(byte data) => data switch
    {
        < 8 => FruaObjectKind.Key,
        < 20 => FruaObjectKind.Item,
        _ => FruaObjectKind.Quest,
    };

    /// <summary>
    /// The zero-based index within whatever <see cref="ObjectKind"/> the byte names.
    /// </summary>
    public static int ObjectIndex(byte data) => ObjectKind(data) switch
    {
        FruaObjectKind.Key => data,
        FruaObjectKind.Item => data - 8,
        _ => data - 20,
    };

    /// <summary>
    /// The party facings a <see cref="FruaTrigger.FacingDirection"/> event fires on.
    /// </summary>
    /// <remarks>
    /// <b>A four-bit mask, not an ordinal</b> — N=1, E=2, S=4, W=8 — so 15 means any facing and 5
    /// means north or south. The reference enumerates all fifteen combinations by name; a mask is
    /// the same thing said once.
    /// </remarks>
    public IEnumerable<FruaFacing> Facings()
    {
        if ((TriggerData & 1) != 0) { yield return FruaFacing.North; }
        if ((TriggerData & 2) != 0) { yield return FruaFacing.East; }
        if ((TriggerData & 4) != 0) { yield return FruaFacing.South; }
        if ((TriggerData & 8) != 0) { yield return FruaFacing.West; }
    }

    /// <summary>
    /// The class a <see cref="FruaTrigger.ClassInParty"/> event wants, or null.
    /// </summary>
    /// <remarks>
    /// <b>There is no case 1, and the reference reads uninitialised memory for it.</b> Its switch
    /// covers 0, 2, 3, 4, 5 and 6, leaving a stack <c>CLASS_ID</c> unassigned for 1 and for
    /// anything above 6 — then stores it. Refused here rather than reproduced: a null says the
    /// design asked for a class this table cannot name, where copying the bug would invent a
    /// different wrong answer on every run.
    /// </remarks>
    public string? ClassWanted() => TriggerData switch
    {
        0 => "Cleric",
        2 => "Fighter",
        3 => "Paladin",
        4 => "Ranger",
        5 => "Magic User",
        6 => "Thief",
        _ => null,
    };

    /// <summary>The race a <see cref="FruaTrigger.RaceInParty"/> event wants, or null.</summary>
    public string? RaceWanted() => TriggerData switch
    {
        0 => "Elf",
        1 => "HalfElf",
        2 => "Dwarf",
        3 => "Gnome",
        4 => "Halfling",
        5 => "Human",
        _ => null,
    };

    private static FruaEventType TypeOf(byte stored) =>
        Enum.IsDefined((FruaEventType)stored) ? (FruaEventType)stored : FruaEventType.None;
}
