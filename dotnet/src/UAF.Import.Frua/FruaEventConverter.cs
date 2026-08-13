using UAF.Serialization;

namespace UAF.Import.Frua;

/// <summary>
/// Turns a DOS FRUA event into the engine's own event record.
/// </summary>
/// <remarks>
/// <para>
/// The twenty-byte record names a type; that type decides what its sixteen payload bytes mean and
/// which of the engine's forty-five event classes it becomes. The base and control block are the
/// same for all of them — see <see cref="FruaEventControlConverter"/> — so what is left here is
/// one mapping per type.
/// </para>
/// <para>
/// <b>Text lives in the level's string table, not in the event.</b> Almost every payload carries a
/// slot number rather than a string, so a converter without the table produces events with empty
/// text. That is why <see cref="Convert"/> takes one, and why passing null is a legitimate call
/// rather than an error — the structure is still right.
/// </para>
/// <para>
/// <b>Not every type is mapped yet.</b> <see cref="Convert"/> returns null for one it does not
/// handle, and <see cref="Converts"/> answers which is which without building anything, so a
/// caller can report coverage rather than discovering gaps as silence.
/// </para>
/// </remarks>
public static class FruaEventConverter
{
    /// <summary>The FRUA event types this converter maps.</summary>
    /// <remarks>
    /// The list is what <see cref="Convert"/> switches on; the two are tested against each other
    /// so neither can drift from the other.
    /// </remarks>
    public static IReadOnlySet<FruaEventType> Converts { get; } = new HashSet<FruaEventType>
    {
        FruaEventType.TextStatement,
        FruaEventType.GiveTreasure,
        FruaEventType.CombatTreasure,
        FruaEventType.Damage,
        FruaEventType.Sounds,
        FruaEventType.QuestStage,
        FruaEventType.GainExperience,
        FruaEventType.QuestionYesNo,
        FruaEventType.Vault,
        FruaEventType.PassTime,
        FruaEventType.ChainEvent,
        FruaEventType.Camp,
        FruaEventType.Stairs,
        FruaEventType.Teleporter,
        FruaEventType.TransferModule,
    };

    /// <summary>
    /// Converts one event, or returns null for a type not yet mapped.
    /// </summary>
    /// <param name="source">The twenty-byte record.</param>
    /// <param name="id">The key the produced event is stored under.</param>
    /// <param name="strings">The level's string table, for the slots payloads name.</param>
    /// <param name="design">Resolves the objects a trigger names.</param>
    public static IGameEvent? Convert(FruaEvent source, uint id,
                                      FruaStringTable? strings = null, FruaDesign? design = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Type switch
        {
            FruaEventType.TextStatement => Text(source, id, strings, design),

            // Both FRUA treasure types produce the engine's GiveTreasure; the difference is only
            // which of its two event ordinals the design used.
            FruaEventType.GiveTreasure => Treasure(source, id, EventType.GiveTreasure, design),
            FruaEventType.CombatTreasure => Treasure(source, id, EventType.CombatTreasure, design),

            FruaEventType.Damage => Damage(source, id, strings, design),
            FruaEventType.Sounds => Sounds(source, id, design),
            FruaEventType.QuestStage => Quest(source, id, strings, design),
            FruaEventType.GainExperience => Experience(source, id, strings, design),
            FruaEventType.QuestionYesNo => YesNo(source, id, strings, design),
            FruaEventType.Vault => Vault(source, id, strings, design),
            FruaEventType.PassTime => PassTime(source, id, strings, design),
            FruaEventType.ChainEvent => Chain(source, id, design),
            FruaEventType.Camp => Camp(source, id, design),

            // One FRUA payload, three engine ordinals -- the reference's own comment says
            // "Stairs=Teleporter=TransferModule".
            FruaEventType.Stairs => Transfer(source, id, EventType.Stairs, strings, design),
            FruaEventType.Teleporter => Transfer(source, id, EventType.Teleporter, strings, design),
            FruaEventType.TransferModule =>
                Transfer(source, id, EventType.TransferModule, strings, design),

            _ => null,
        };
    }

    private static GameEventBase Base(FruaEvent source, EventType type, uint id,
                                      string text, FruaDesign? design) =>
        FruaEventControlConverter.Base(source, (int)type, id, text, design);

    /// <summary>Resolves a string slot, giving empty for a missing table or an absent slot.</summary>
    /// <remarks>
    /// <b>Slot zero means "no text", not "the first string".</b> The payload readers keep the raw
    /// slot, so the zero test belongs here rather than in each caller.
    /// </remarks>
    private static string Text(FruaStringTable? strings, int slot) =>
        slot > 0 ? strings?.Get(slot) ?? string.Empty : string.Empty;

    /// <summary>
    /// A text event, whose text is the only one assembled by its own payload reader.
    /// </summary>
    /// <remarks>
    /// <b>Its text is five slots joined, not one</b>, with highlight markers inserted between —
    /// which is why <see cref="FruaTextEvent.Read"/> needs the table rather than a slot number,
    /// and why this is the one case that cannot fall back to an empty string by resolving zero.
    /// </remarks>
    private static IGameEvent Text(FruaEvent source, uint id, FruaStringTable? strings,
                                   FruaDesign? design)
    {
        var payload = FruaTextEvent.Read(source, strings);

        return new TextEvent(
            Base: Base(source, EventType.TextStatement, id, payload.Text, design),
            WaitForReturn: payload.WaitForReturn ? 1 : 0,
            ForceBackup: payload.ForceBackup ? 1 : 0,

            // The engine's HighlightText is a whole-event flag; FRUA marks its highlights inline
            // with /h markers, which FruaTextEvent has already inserted into the text.
            HighlightText: 0,
            Distance: 0,
            Sound: string.Empty);
    }

    private static IGameEvent Treasure(FruaEvent source, uint id, EventType type,
                                       FruaDesign? design)
    {
        var payload = FruaTreasureEvent.Read(source);

        return new TreasureEvent(
            Base: Base(source, type, id, string.Empty, design),

            // Platinum is the default coin type, so it goes in the first slot and the rest are
            // zero; gems and jewellery are counts of unnamed pieces until the item pass can name
            // them.
            Money: new MoneySack(Coins(payload.Platinum), [], []),

            // The eight item slots are ordinals into the design's item database -- see the class
            // remarks on FruaCharacterConverter for why that pass has to come first.
            Items: new ItemList([], ReadyItems.Empty),
            SilentGiveToActiveChar: 0);
    }

    /// <summary>The ten coin slots, with an amount in the first.</summary>
    private static int[] Coins(int platinum)
    {
        var coins = new int[MonsterLeafReaders.MaxCoinTypes];
        coins[0] = platinum;
        return coins;
    }

    private static IGameEvent Damage(FruaEvent source, uint id, FruaStringTable? strings,
                                     FruaDesign? design)
    {
        var payload = FruaDamageEvent.Read(source);

        return new DamageEvent(
            Base: Base(source, EventType.Damage, id, Text(strings, payload.TextSlot), design),
            NbrAttacks: payload.Attacks,
            ChancePerAttack: payload.ChancePerAttack,
            DmgDice: payload.DiceSides,
            DmgDiceQty: payload.DiceCount,
            DmgBonus: payload.DamageBonus,
            SaveBonus: payload.SaveBonus,
            AttackThac0: payload.Thac0,
            EventSave: (int)payload.Save,
            SpellSave: (int)payload.SpellSave,
            Who: (int)payload.Target,
            Distance: (int)payload.Distance);
    }

    private static IGameEvent Sounds(FruaEvent source, uint id, FruaDesign? design)
    {
        var payload = FruaSoundEvent.Read(source);

        // The slots name entries in the design's sound table, which the art pass resolves; the
        // count is what matters structurally, so empty slots are dropped rather than named.
        var sounds = payload.SoundSlots.Where(s => s > 0)
                                       .Select(_ => string.Empty)
                                       .ToArray();

        return new SoundEvent(Base(source, EventType.Sounds, id, string.Empty, design), sounds);
    }

    private static IGameEvent Quest(FruaEvent source, uint id, FruaStringTable? strings,
                                    FruaDesign? design)
    {
        var payload = FruaQuestEvent.Read(source);

        return new QuestEvent(
            Base: Base(source, EventType.QuestStage, id, Text(strings, payload.TextSlot), design),
            Operation: (int)payload.Accept,
            CompleteOnAccept: payload.CompleteOnAccept ? 1 : 0,
            FailOnRejection: payload.FailOnRejection ? 1 : 0,
            Quest: payload.QuestIndex,
            Stage: (ushort)payload.Stage,
            AcceptChain: 0,
            RejectChain: 0);
    }

    private static IGameEvent Experience(FruaEvent source, uint id, FruaStringTable? strings,
                                         FruaDesign? design)
    {
        var payload = FruaGainExperienceEvent.Read(source);

        return new GainExperienceEvent(
            Base: Base(source, EventType.GainExperience, id,
                       Text(strings, payload.TextSlot), design),
            Experience: (int)payload.Experience,
            Sound: string.Empty,

            // Hard-coded by the reference rather than read -- see FruaGainExperienceEvent.
            Chance: FruaGainExperienceEvent.Chance,

            // The engine's eventPartyAffectType: 0 is the whole party, 1 the active character.
            Who: payload.ActiveCharacterOnly ? 1 : 0);
    }

    private static IGameEvent YesNo(FruaEvent source, uint id, FruaStringTable? strings,
                                    FruaDesign? design)
    {
        var payload = FruaQuestionYesNoEvent.Read(source);

        return new YesNoEvent(
            Base: Base(source, EventType.QuestionYesNo, id,
                       Text(strings, payload.TextSlot), design),
            YesChainAction: (int)payload.OnYes,
            NoChainAction: (int)payload.OnNo,

            // FRUA's yes and no branches name text to show, not events to chain to; the single
            // chain byte every event has is the only jump it can express.
            YesChain: 0,
            NoChain: 0);
    }

    private static IGameEvent Vault(FruaEvent source, uint id, FruaStringTable? strings,
                                    FruaDesign? design)
    {
        var payload = FruaVaultEvent.Read(source);

        return new VaultEvent(
            Base: Base(source, EventType.Vault, id, Text(strings, payload.TextSlot), design),
            ForceBackup: payload.ForceBackup ? 1 : 0,

            // FRUA has one vault; the engine numbers them from zero.
            WhichVault: 0);
    }

    private static IGameEvent PassTime(FruaEvent source, uint id, FruaStringTable? strings,
                                       FruaDesign? design)
    {
        var payload = FruaPassTimeEvent.Read(source);

        return new PassTimeEvent(
            Base: Base(source, EventType.PassTime, id, Text(strings, payload.TextSlot), design),
            Days: payload.Days,
            Hours: payload.Hours,
            Minutes: payload.Minutes,

            // FRUA passes a duration silently and unconditionally: no interrupt, no absolute set.
            AllowStop: 0,
            SetTime: 0,
            PassSilent: 1);
    }

    private static IGameEvent Chain(FruaEvent source, uint id, FruaDesign? design) =>
        new ChainEvent(
            Base: Base(source, EventType.ChainEventType, id, string.Empty, design),
            Chain: source.ChainEvent);

    private static IGameEvent Camp(FruaEvent source, uint id, FruaDesign? design) =>
        new CampEvent(
            Base: Base(source, EventType.Camp, id, string.Empty, design),
            ForceExit: 0);

    private static IGameEvent Transfer(FruaEvent source, uint id, EventType type,
                                       FruaStringTable? strings, FruaDesign? design)
    {
        var payload = FruaTransferEvent.Read(source);

        return new TransferEvent(
            Base: Base(source, type, id, Text(strings, payload.TextSlot), design),
            AskYesNo: payload.AskYesNo ? 1 : 0,
            TransferOnYes: payload.TransferOnYes ? 1 : 0,

            // A Drow-specific engine option with no FRUA source.
            DestroyDrow: 0,
            ActivateBeforeEntry: payload.ExecuteDestinationEvent ? 1 : 0,
            Destination: new TransferData(
                ExecuteEvent: payload.ExecuteDestinationEvent ? 1 : 0,
                DestEntryPoint: payload.DestinationEntryPoint,

                // The destination level is not in the payload -- FRUA transfers within the level
                // unless the entry point says otherwise, and a module transfer names a design.
                DestLevel: 0,
                DestX: payload.DestinationX,
                DestY: payload.DestinationY,
                Facing: (int)payload.Facing));
    }
}
