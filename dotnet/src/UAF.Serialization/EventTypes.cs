namespace UAF.Serialization;

/// <summary>
/// The <c>eventType</c> ordinals (<c>GameEvent.h:97</c>).
/// </summary>
/// <remarks>
/// <para>
/// These are <b>positional</b> — the C++ enum assigns no explicit values below 1000, so an ordinal
/// is only meaningful as an index into this exact sequence. Inserting a member anywhere but the end
/// renumbers everything after it and silently reinterprets every event in every existing level.
/// </para>
/// <para>
/// The <c>CONTROL_*</c> values from 1000 up are runtime-generated screens rather than design data.
/// The header notes they "are in save files and must not change"; they are listed here only so the
/// gap in numbering is explicit.
/// </para>
/// </remarks>
public enum EventType
{
    NoEvent = 0,
    AddNpc,
    Camp,
    ChainEventType,
    Combat,
    CombatTreasure,
    Damage,
    EncounterEvent,
    EnterPassword,
    GainExperience,
    GiveTreasure,
    GuidedTour,

    /// <summary>Obsolete — superseded by <see cref="WhoPays"/> plus <see cref="Camp"/>.</summary>
    InnEvent,

    NPCSays,
    PassTime,

    /// <summary>Obsolete — an option on <see cref="Combat"/> now.</summary>
    PickOneCombat,

    QuestStage,
    QuestionButton,
    QuestionList,
    QuestionYesNo,
    RemoveNPCEvent,
    ShopEvent,
    SmallTown,
    Sounds,
    SpecialItem,
    Stairs,
    TavernEvent,

    /// <summary>Obsolete — folded into <see cref="TavernEvent"/>.</summary>
    TavernTales,

    Teleporter,
    TempleEvent,
    TextStatement,
    TrainingHallEvent,
    TransferModule,
    Utilities,
    Vault,
    WhoPays,
    WhoTries,
    TakePartyItems,
    HealParty,
    LogicBlock,
    GPDLEvent,
    RandomEvent,
    PlayMovieEvent,
    JournalEvent,
    FlowControl,

    /// <summary>First of the runtime-only control screens. Not design data.</summary>
    ControlSplash = 1000,
}

/// <summary>
/// Maps an <see cref="EventType"/> to the concrete C++ class that reads it
/// (<c>GameEventList::CreateNewEvent</c>, <c>GameEvent.cpp:3833</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the dispatch table for the whole event system: a level's event list stores a type
/// ordinal, and the reader must pick the matching field layout. Several ordinals share a class —
/// <see cref="EventType.Stairs"/>, <see cref="EventType.Teleporter"/> and
/// <see cref="EventType.TransferModule"/> all read as <c>TRANSFER_EVENT_DATA</c>, and
/// <see cref="EventType.PickOneCombat"/> reads as <c>COMBAT_EVENT_DATA</c>.
/// </para>
/// <para>
/// <b><see cref="EventType.NoEvent"/> consumes nothing beyond its ordinal.</b>
/// <c>CreateNewEvent</c> returns null for it and the caller skips <c>Serialize</c> entirely
/// (<c>GameEvent.cpp:3634</c>), so such an entry is exactly four bytes. A reader that treats every
/// counted entry as a full event desynchronises on the first one.
/// </para>
/// <para>
/// <b>The ordinal appears twice.</b> The list reads it to choose the class, and then
/// <c>GameEvent::Serialize</c> reads it again into the event's own <c>event</c> field — after the
/// control block and two <c>PIC_DATA</c>, not immediately. Both reads are real bytes.
/// </para>
/// </remarks>
public static class EventDispatch
{
    /// <summary>The reference class name for each ordinal that maps to one.</summary>
    public static readonly IReadOnlyDictionary<EventType, string> ClassNames =
        new Dictionary<EventType, string>
        {
            [EventType.AddNpc] = "ADD_NPC_DATA",
            [EventType.Camp] = "CAMP_EVENT_DATA",
            [EventType.Combat] = "COMBAT_EVENT_DATA",
            [EventType.PickOneCombat] = "COMBAT_EVENT_DATA",
            [EventType.Damage] = "GIVE_DAMAGE_DATA",
            [EventType.EncounterEvent] = "ENCOUNTER_DATA",
            [EventType.EnterPassword] = "PASSWORD_DATA",
            [EventType.GainExperience] = "GAIN_EXP_DATA",
            [EventType.CombatTreasure] = "COMBAT_TREASURE",
            [EventType.GiveTreasure] = "GIVE_TREASURE_DATA",
            [EventType.GuidedTour] = "GUIDED_TOUR",
            [EventType.NPCSays] = "NPC_SAYS_DATA",
            [EventType.QuestionList] = "QUESTION_LIST_DATA",
            [EventType.QuestionButton] = "QUESTION_BUTTON_DATA",
            [EventType.PassTime] = "PASS_TIME_EVENT_DATA",
            [EventType.QuestionYesNo] = "QUESTION_YES_NO",
            [EventType.RemoveNPCEvent] = "REMOVE_NPC_DATA",
            [EventType.ShopEvent] = "SHOP",
            [EventType.TempleEvent] = "TEMPLE",
            [EventType.TavernTales] = "TAVERN_TALES",
            [EventType.TavernEvent] = "TAVERN",
            [EventType.TextStatement] = "TEXT_EVENT_DATA",
            [EventType.Stairs] = "TRANSFER_EVENT_DATA",
            [EventType.Teleporter] = "TRANSFER_EVENT_DATA",
            [EventType.TransferModule] = "TRANSFER_EVENT_DATA",
            [EventType.WhoPays] = "WHO_PAYS_EVENT_DATA",
            [EventType.WhoTries] = "WHO_TRIES_EVENT_DATA",
            [EventType.SpecialItem] = "SPECIAL_ITEM_KEY_EVENT_DATA",
            [EventType.Vault] = "VAULT_EVENT_DATA",
            [EventType.TrainingHallEvent] = "TRAININGHALL",
            [EventType.SmallTown] = "SMALL_TOWN_DATA",
            [EventType.RandomEvent] = "RANDOM_EVENT_DATA",
            [EventType.ChainEventType] = "CHAIN_EVENT",
            [EventType.QuestStage] = "QUEST_EVENT_DATA",
            [EventType.Utilities] = "UTILITIES_EVENT_DATA",
            [EventType.Sounds] = "SOUND_EVENT",
            [EventType.TakePartyItems] = "TAKE_PARTY_ITEMS_DATA",
            [EventType.HealParty] = "HEAL_PARTY_DATA",
            [EventType.LogicBlock] = "LOGIC_BLOCK_DATA",
            [EventType.PlayMovieEvent] = "PLAY_MOVIE_DATA",
            [EventType.JournalEvent] = "JOURNAL_EVENT",
            [EventType.FlowControl] = "FLOW_CONTROL_EVENT_DATA",
        };

    /// <summary>
    /// True when this ordinal produces no object, and therefore consumes no bytes beyond itself.
    /// </summary>
    public static bool ReadsNothing(EventType type) => !ClassNames.ContainsKey(type);
}
