using UAF.Serialization;

namespace UAF.Import.Frua;

/// <summary>
/// Builds the <see cref="GameEventBase"/> and <see cref="EventControl"/> every imported event
/// shares (<c>UAImportEvent::ConvertEventControl</c>, <c>UAImport.cpp:4133</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One FRUA trigger byte becomes four separate engine fields.</b> Its low bit is
/// once-only, the next two are the chain trigger, and the top five are the trigger itself —
/// which then decides what the *separate* trigger-data byte means. Depending on the trigger it is
/// a special key, a special item, a quest, a percentage, a facing bitmask, a class or a race.
/// </para>
/// </remarks>
public static class FruaEventControlConverter
{
    /// <summary>
    /// The engine's <c>eventTriggerType</c> ordinal for a FRUA trigger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Divide-by-eight is right fifteen times and wrong on the last one.</b> FRUA's triggers
    /// step by eight and the engine's are consecutive, so 8→1, 16→2 and so on all the way to
    /// 128→16. But the engine interleaves <c>ClassNotInParty</c> at 17, which FRUA has no
    /// equivalent for, so FRUA's 136 is the engine's <b>18</b> (<c>RaceInParty</c>) and not its 17.
    /// </para>
    /// <para>
    /// A shortcut that is correct for every value a spot-check would try is exactly the kind that
    /// survives review, which is why the table is written out.
    /// </para>
    /// </remarks>
    public static int TriggerOrdinal(FruaTrigger trigger) => trigger switch
    {
        FruaTrigger.Always => 0,
        FruaTrigger.PartyHaveItem => 1,
        FruaTrigger.PartyNotHaveItem => 2,
        FruaTrigger.Daytime => 3,
        FruaTrigger.Nighttime => 4,
        FruaTrigger.RandomChance => 5,
        FruaTrigger.PartySearching => 6,
        FruaTrigger.PartyNotSearching => 7,
        FruaTrigger.FacingDirection => 8,
        FruaTrigger.QuestComplete => 9,
        FruaTrigger.QuestFailed => 10,
        FruaTrigger.QuestInProgress => 11,
        FruaTrigger.PartyDetectingTraps => 12,
        FruaTrigger.PartyNotDetectingTraps => 13,
        FruaTrigger.PartySeeInvisible => 14,
        FruaTrigger.PartyNotSeeInvisible => 15,
        FruaTrigger.ClassInParty => 16,

        // Not 17. The engine's 17 is ClassNotInParty, which FRUA cannot express.
        FruaTrigger.RaceInParty => 18,

        _ => 0,
    };

    /// <summary>The engine's <c>chainTriggerType</c> ordinal, which FRUA's values match.</summary>
    public static int ChainTriggerOrdinal(FruaChainTrigger trigger) => trigger switch
    {
        FruaChainTrigger.IfEventHappened => 1,
        FruaChainTrigger.IfEventDidNotHappen => 2,
        _ => 0,
    };

    /// <summary>
    /// The six classes a <see cref="FruaTrigger.ClassInParty"/> can name.
    /// </summary>
    /// <remarks>
    /// <b>There is no case 1.</b> FRUA's class 1 is the unused Knight, and the reference's switch
    /// simply has no branch for it — leaving <c>classID</c> at whatever the uninitialised
    /// <c>CLASS_ID</c> held. This port yields empty instead, which is a refusal rather than a fix:
    /// the reference's value is not reproducible.
    /// </remarks>
    public static string TriggerClassName(byte data) => data switch
    {
        0 => "Cleric",
        2 => "Fighter",
        3 => "Paladin",
        4 => "Ranger",
        5 => "Magic User",
        6 => "Thief",
        _ => string.Empty,
    };

    /// <summary>The six races a <see cref="FruaTrigger.RaceInParty"/> can name.</summary>
    public static string TriggerRaceName(byte data) => data switch
    {
        0 => "Elf",
        1 => "HalfElf",
        2 => "Dwarf",
        3 => "Gnome",
        4 => "Halfling",
        5 => "Human",
        _ => string.Empty,
    };

    /// <summary>
    /// The control block for one event.
    /// </summary>
    /// <param name="source">The twenty-byte record.</param>
    /// <param name="design">
    /// Resolves a special key, item or quest index to its name. Null leaves those empty, which is
    /// what an event whose trigger names none of them gets anyway.
    /// </param>
    public static EventControl Control(FruaEvent source, FruaDesign? design = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        string itemId = string.Empty;
        int quest = 0;
        int chance = 0;
        int facing = 0;
        string raceId = string.Empty;
        string classId = string.Empty;

        switch (source.Trigger)
        {
            // The five triggers whose data byte addresses a key, an item or a quest by one
            // shared numbering -- see FruaEvent.ObjectKind.
            case FruaTrigger.PartyHaveItem:
            case FruaTrigger.PartyNotHaveItem:
            case FruaTrigger.QuestComplete:
            case FruaTrigger.QuestFailed:
            case FruaTrigger.QuestInProgress:
                (itemId, quest) = Object(source.TriggerData, design);
                break;

            case FruaTrigger.RandomChance:
                chance = source.TriggerData;
                break;

            case FruaTrigger.FacingDirection:
                facing = source.TriggerData;
                break;

            case FruaTrigger.ClassInParty:
                classId = TriggerClassName(source.TriggerData);
                break;

            case FruaTrigger.RaceInParty:
                raceId = TriggerRaceName(source.TriggerData);
                break;
        }

        return new EventControl(
            EventStatusUnused: 0,
            EventResultUnused: 0,
            OnceOnly: source.OnceOnly ? 1 : 0,
            ChainTrigger: ChainTriggerOrdinal(source.ChainTrigger),
            EventTrigger: TriggerOrdinal(source.Trigger),
            ItemId: itemId,
            Quest: quest,
            Chance: chance,
            Facing: facing,
            RaceId: raceId,
            ClassOrBaseclassId: classId,
            CharacterId: string.Empty,
            Attributes: [],
            GpdlData: string.Empty,
            GpdlIsBinary: 0,
            PartyX: 0,
            PartyY: 0,
            MemorizedSpellId: string.Empty,
            MemorizedSpellClass: 0,
            MemorizedSpellLevel: 0);
    }

    /// <summary>
    /// Resolves a trigger's data byte into either a named object or a quest number.
    /// </summary>
    /// <remarks>
    /// The engine keeps keys and special items in one <c>specialItem</c>-shaped field and quests
    /// in a separate numeric one, so the two come back as a pair with only one of them filled.
    /// </remarks>
    private static (string ItemId, int Quest) Object(byte data, FruaDesign? design)
    {
        int index = FruaEvent.ObjectIndex(data);

        return FruaEvent.ObjectKind(data) switch
        {
            FruaObjectKind.Key => (design?.KeyName(index) ?? string.Empty, 0),
            FruaObjectKind.Item => (design?.SpecialItemName(index) ?? string.Empty, 0),
            _ => (string.Empty, index),
        };
    }

    /// <summary>
    /// The shared base for one event.
    /// </summary>
    /// <param name="source">The twenty-byte record.</param>
    /// <param name="eventType">The engine's <c>eventType</c> ordinal for the produced event.</param>
    /// <param name="id">The key this event is stored under.</param>
    /// <param name="text">The event's text, which most types leave empty.</param>
    /// <param name="design">Resolves trigger objects; see <see cref="Control"/>.</param>
    /// <remarks>
    /// <b>The chain target is a plain event number, not a key.</b> FRUA stores one byte naming
    /// another event in the same level, and which of the engine's two chain slots it lands in
    /// depends on the chain trigger — happened, not-happened, or both.
    /// </remarks>
    /// <param name="picture">
    /// The event's art slot and whether it names anything. The reference calls <c>AssignPic</c>
    /// with both, and the flag rather than the slot is what decides — see
    /// <see cref="FruaArtConverter.Picture"/>.
    /// </param>
    public static GameEventBase Base(FruaEvent source, int eventType, uint id,
                                     string text = "", FruaDesign? design = null,
                                     (byte Slot, bool Has)? picture = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var pic = picture is { } p
            ? FruaArtConverter.Picture(p.Slot, p.Has) ?? EmptyPicture
            : EmptyPicture;

        return new GameEventBase(
            Control: Control(source, design),
            Pic: pic,

            // FRUA names one picture per event; the engine's second slot has no source.
            Pic2: EmptyPicture,
            EventType: eventType,
            Id: id,
            X: 0,
            Y: 0,
            ChainEventHappen: source.ChainTrigger != FruaChainTrigger.IfEventDidNotHappen
                ? source.ChainEvent
                : 0,
            ChainEventNotHappen: source.ChainTrigger != FruaChainTrigger.IfEventHappened
                ? source.ChainEvent
                : 0,
            Text: text,
            Text2: string.Empty,
            Text3: string.Empty,
            Attributes: []);
    }

    internal static PicRecord EmptyPicture { get; } =
        new(PicType: 0, FileName: string.Empty, TimeDelay: 0, NumFrames: 0,
            FrameWidth: 0, FrameHeight: 0, Flags: 0, MaxLoops: 0,
            Style: 0, UseAlpha: 0, AlphaValue: 0, RestartFrame: 0);
}
