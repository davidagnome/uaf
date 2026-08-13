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
        FruaEventType.AddNpc,
        FruaEventType.RemoveNpc,
        FruaEventType.NpcSays,
        FruaEventType.SpecialItem,
        FruaEventType.Utilities,
        FruaEventType.Combat,
        FruaEventType.PickOneCombat,
        FruaEventType.GuidedTour,
        FruaEventType.QuestionButton,
        FruaEventType.QuestionList,
        FruaEventType.Shop,
        FruaEventType.Temple,
        FruaEventType.TrainingHall,
        FruaEventType.Tavern,
        FruaEventType.TavernTales,
        FruaEventType.SmallTown,
        FruaEventType.Encounter,
        FruaEventType.WhoTries,
        FruaEventType.WhoPays,
        FruaEventType.EnterPassword,
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

            FruaEventType.AddNpc => AddNpc(source, id, strings, design),
            FruaEventType.RemoveNpc => RemoveNpc(source, id, strings, design),
            FruaEventType.NpcSays => NpcSays(source, id, strings, design),
            FruaEventType.SpecialItem => SpecialItem(source, id, strings, design),
            FruaEventType.Utilities => Utilities(source, id, design),

            // PickOneCombat is an obsoleted type the engine folds into Combat; both read the same
            // payload, and the reference keeps the ordinals apart only so a design round-trips.
            FruaEventType.Combat => Combat(source, id, EventType.Combat, strings, design),
            FruaEventType.PickOneCombat =>
                Combat(source, id, EventType.PickOneCombat, strings, design),

            FruaEventType.GuidedTour => Tour(source, id, design),
            FruaEventType.QuestionButton =>
                Question(source, id, EventType.QuestionButton, strings, design),
            FruaEventType.QuestionList =>
                Question(source, id, EventType.QuestionList, strings, design),

            FruaEventType.Shop => Shop(source, id, design),
            FruaEventType.Temple => Temple(source, id, strings, design),
            FruaEventType.TrainingHall => TrainingHall(source, id, strings, design),
            FruaEventType.Tavern => Tavern(source, id, strings, design),
            FruaEventType.TavernTales => TavernTales(source, id, strings, design),
            FruaEventType.SmallTown => SmallTown(source, id, strings, design),
            FruaEventType.Encounter => Encounter(source, id, strings, design),
            FruaEventType.WhoTries => WhoTries(source, id, strings, design),
            FruaEventType.WhoPays => WhoPays(source, id, strings, design),
            FruaEventType.EnterPassword => Password(source, id, strings, design),

            _ => null,
        };
    }

    private static GameEventBase Base(FruaEvent source, EventType type, uint id,
                                      string text, FruaDesign? design,
                                      (byte Slot, bool Has)? picture = null) =>
        FruaEventControlConverter.Base(source, (int)type, id, text, design, picture);

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
            Base: Base(source, EventType.TextStatement, id, payload.Text, design,
                       (payload.PictureSlot, payload.PictureIsLarge)),
            WaitForReturn: payload.WaitForReturn ? 1 : 0,
            ForceBackup: payload.ForceBackup ? 1 : 0,

            // The engine's HighlightText is a whole-event flag; FRUA marks its highlights inline
            // with /h markers, which FruaTextEvent has already inserted into the text.
            HighlightText: 0,
            Distance: 0,
            Sound: string.Empty);
    }

    /// <summary>
    /// A set of item ordinals, resolved against the design's database.
    /// </summary>
    /// <remarks>
    /// <b>Empty slots are zeroes, not absences.</b> A treasure, a shop and a creature all carry
    /// fixed-length ordinal arrays with unused entries left at zero, and
    /// <see cref="FruaItemConverter.Instance"/> rejects those along with any ordinal the design
    /// has no item for — so filtering is what it returns, not what the array looks like.
    /// </remarks>
    private static ItemInstance[] Items(IEnumerable<byte> ordinals, FruaDesign? design,
                                        bool identified = false,
                                        IReadOnlyList<byte>? quantities = null) =>
        ordinals
            .Select((ordinal, i) => FruaItemConverter.Instance(
                ordinal, design?.Items,
                quantities is not null && i < quantities.Count ? quantities[i] : 0,
                identified))
            .OfType<ItemInstance>()
            .ToArray();

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

            Items: new ItemList(Items(payload.ItemSlots, design, payload.ItemsAreIdentified),
                                ReadyItems.Empty),
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
            Base: Base(source, EventType.Damage, id, Text(strings, payload.TextSlot), design,
                       (payload.PictureSlot, payload.PictureIsLarge)),
            NbrAttacks: payload.Attacks,
            ChancePerAttack: payload.ChancePerAttack,
            DmgDice: payload.DiceSides,
            DmgDiceQty: payload.DiceCount,
            DmgBonus: payload.DamageBonus,
            SaveBonus: payload.SaveBonus,
            AttackThac0: payload.Thac0,
            // None of these four may be cast: three of the reader's enums disagree with the
            // engine's numbering -- see FruaEventEnums.
            EventSave: FruaEventEnums.SaveEffect(payload.Save),
            SpellSave: FruaEventEnums.SpellSaveVersus(payload.SpellSave),
            Who: FruaEventEnums.PartyAffect(payload.Target),
            Distance: FruaEventEnums.Distance(payload.Distance));
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
            Base: Base(source, EventType.QuestStage, id, Text(strings, payload.TextSlot), design,
                       (payload.PictureSlot, payload.PictureIsLarge)),
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

            // Not a bare 0/1: the engine's list opens with NoPartyMember, so the whole party is
            // 1 and the active character 2.
            Who: FruaEventEnums.PartyAffect(payload.ActiveCharacterOnly));
    }

    private static IGameEvent YesNo(FruaEvent source, uint id, FruaStringTable? strings,
                                    FruaDesign? design)
    {
        var payload = FruaQuestionYesNoEvent.Read(source);

        return new YesNoEvent(
            Base: Base(source, EventType.QuestionYesNo, id,
                       Text(strings, payload.TextSlot), design),
            YesChainAction: FruaEventEnums.PostChainAction(payload.OnYes),
            NoChainAction: FruaEventEnums.PostChainAction(payload.OnNo),

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
            Base: Base(source, EventType.Vault, id, Text(strings, payload.TextSlot), design,
                       (payload.PictureSlot, payload.PictureIsLarge)),
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

    /// <summary>
    /// The name of the NPC an event refers to, resolved through the design.
    /// </summary>
    /// <remarks>
    /// <b>This is one of the reference's nine disabled lookups.</b> Its
    /// <c>data->charAdded = GetMonsterKey(...)</c> and <c>data->charRemoved = ...</c> are both
    /// commented out behind <c>NotImplemented</c> markers, so a design imported by the reference
    /// gets add- and remove-NPC events that name nobody. <see cref="FruaDesign.NpcIn"/> resolves
    /// them; without a design there is nothing to resolve against and the name stays empty.
    /// </remarks>
    private static string NpcName(FruaNpcEvent payload, FruaDesign? design) =>
        design?.NpcIn(payload)?.Name ?? string.Empty;

    private static IGameEvent AddNpc(FruaEvent source, uint id, FruaStringTable? strings,
                                     FruaDesign? design)
    {
        var payload = FruaNpcEvent.Read(source);

        return new AddNpcEvent(
            Base: Base(source, EventType.AddNpc, id, Text(strings, payload.TextSlot), design),

            // The engine's operation selects add, remove or replace; a FRUA add-NPC event only
            // ever adds, which is what its own event type already says.
            Operation: 0,
            CharacterId: NpcName(payload, design),
            HitPointMod: payload.HitPointModifier,

            // FRUA carries no edited copy of the NPC, so the design's own record is the one used.
            UseOriginal: 1);
    }

    private static IGameEvent RemoveNpc(FruaEvent source, uint id, FruaStringTable? strings,
                                        FruaDesign? design)
    {
        var payload = FruaNpcEvent.Read(source);

        return new RemoveNpcEvent(
            Base: Base(source, EventType.RemoveNPCEvent, id,
                       Text(strings, payload.TextSlot), design),
            Distance: FruaEventEnums.Distance(payload.Distance),
            CharacterId: NpcName(payload, design));
    }

    private static IGameEvent NpcSays(FruaEvent source, uint id, FruaStringTable? strings,
                                      FruaDesign? design)
    {
        var payload = FruaNpcEvent.Read(source);

        return new NpcSaysEvent(
            Base: Base(source, EventType.NPCSays, id, Text(strings, payload.TextSlot), design),
            CharacterId: NpcName(payload, design),
            Distance: FruaEventEnums.Distance(payload.Distance),
            Sound: string.Empty,
            MustHitReturn: 1,

            // FRUA highlights inline with /h markers rather than flagging the whole event.
            Highlight: 0);
    }

    /// <summary>
    /// A special-item event, which gives or takes exactly one object.
    /// </summary>
    /// <remarks>
    /// <b>The engine's event holds a list; FRUA's holds one.</b> The engine can give several
    /// objects at once, so the single FRUA object becomes a one-element list rather than the
    /// converter inventing companions for it.
    /// </remarks>
    private static IGameEvent SpecialItem(FruaEvent source, uint id, FruaStringTable? strings,
                                          FruaDesign? design)
    {
        var payload = FruaSpecialItemEvent.Read(source);

        return new SpecialItemEvent(
            Base: Base(source, EventType.SpecialItem, id, Text(strings, payload.TextSlot), design),
            Items: [Object(payload.ObjectKind, payload.ObjectIndex, (byte)payload.Operation)],
            ForceExit: 0,
            WaitForReturn: 1);
    }

    /// <summary>
    /// One key, special item or quest, as the engine's <c>SPECIAL_OBJECT_EVENT</c>.
    /// </summary>
    /// <remarks>
    /// The <c>ItemType</c> byte is the engine's own discriminator, and its order is the same one
    /// FRUA's single numbering is carved into: keys, then items, then quests.
    /// </remarks>
    private static SpecialObjectEvent Object(FruaObjectKind kind, int index, byte operation) =>
        new(ItemType: (byte)kind, Operation: operation, Index: index, Id: index + 1);

    private static IGameEvent Utilities(FruaEvent source, uint id, FruaDesign? design)
    {
        var payload = FruaUtilitiesEvent.Read(source);

        // The four checked objects share the trigger byte's numbering, and an unset slot is zero
        // rather than absent -- so the empty ones are dropped rather than tested for key zero.
        var checks = payload.CheckedObjects
            .Where(o => o > 0)
            .Select(o => Object(FruaEvent.ObjectKind(o), FruaEvent.ObjectIndex(o), 0))
            .ToArray();

        return new UtilitiesEvent(
            Base: Base(source, EventType.Utilities, id, string.Empty, design),
            EndPlay: payload.EndPlay ? 1 : 0,
            Operation: (int)payload.Operation,
            ItemCheck: (int)payload.ItemCheck,
            MathItemType: (byte)payload.MathObjectKind,

            // FRUA's arithmetic writes its result back into the object it read, so the two
            // halves of the engine's operation name the same thing.
            ResultItemType: (byte)payload.MathObjectKind,
            MathAmount: payload.MathAmount,
            MathItemIndex: payload.MathObjectIndex,
            ResultItemIndex: payload.MathObjectIndex,
            Items: checks);
    }

    private static IGameEvent Combat(FruaEvent source, uint id, EventType type,
                                     FruaStringTable? strings, FruaDesign? design)
    {
        var payload = FruaCombatEvent.Read(source);

        return new CombatEvent(
            Base: Base(source, type, id, Text(strings, payload.TextSlot), design),
            DeathSound: string.Empty,
            MoveSound: string.Empty,
            TurnUndeadSound: string.Empty,
            Distance: FruaEventEnums.Distance(payload.Distance),

            // FRUA gives no facing for a combat; the engine's zero is its own default.
            Direction: 0,
            Surprise: FruaEventEnums.Surprise(payload.Surprise),
            AutoApproach: payload.AutoApproach ? 1 : 0,
            Outdoors: payload.Outdoors ? 1 : 0,
            NoMonsterTreasure: payload.NoMonsterTreasure ? 1 : 0,
            PartyNeverDies: payload.PartyNeverDies ? 1 : 0,
            NoMagic: payload.NoMagic ? 1 : 0,
            MonsterMorale: payload.MonsterMorale,
            TurningMod: 0,
            RandomMonster: 0,
            PartyNoExperience: 0,
            BackgroundSounds: new BackgroundSoundData([], [], 0, 0, 0),
            Monsters: Monsters(payload, design));
    }

    /// <summary>
    /// The monsters a combat fields, resolved against the design.
    /// </summary>
    /// <remarks>
    /// <b>The reference imports none of these.</b> Its monster assignment is the largest of the
    /// nine disabled <c>GetMonsterKey</c> lookups, so a combat imported by the reference has
    /// quantities and no monsters. Without a design there is nothing to resolve against and the
    /// list is empty — which is the reference's own outcome, reached honestly.
    /// </remarks>
    private static MonsterEvent[] Monsters(FruaCombatEvent payload, FruaDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        return design.MonstersIn(payload)
            .Select(m => new MonsterEvent(
                Quantity: m.Quantity,

                // The engine's MONSTER_TYPE and NPC_TYPE, which is the same split
                // ImportMonsterToUAF makes when it decides where the record goes.
                Type: m.Monster.IsNpc ? FruaCharacterConverter.NpcType : MonsterType,
                MonsterId: m.Monster.IsNpc ? string.Empty : m.Monster.Name,
                CharacterId: m.Monster.IsNpc ? m.Monster.Name : string.Empty,

                // FRUA's encounters are hostile; friendly monsters are an engine feature.
                Friendly: 0,
                MoraleAdjustment: 0,

                // A fixed quantity, not a rolled one -- FRUA states the number outright.
                QtyDiceSides: 0,
                QtyDiceQty: 0,
                QtyBonus: 0,
                UseQty: 1,
                Money: null,
                Items: new ItemList(
                    m.Monster.Record is { } record
                        ? Items(record.ItemsCarried, design, quantities: record.ItemQuantities)
                        : [],
                    ReadyItems.Empty)))
            .ToArray();
    }

    /// <summary><c>MONSTER_TYPE</c> — the counterpart to <c>NPC_TYPE</c>.</summary>
    public const int MonsterType = 1;

    private static IGameEvent Tour(FruaEvent source, uint id, FruaDesign? design)
    {
        var payload = FruaGuidedTourEvent.Read(source);

        // The engine's step carries a label the editor shows; FRUA's is a bare direction, so the
        // enum's own name is the only text there is to give it.
        var steps = payload.Steps
            .Select(s => new TourStep(s.ToString(), (int)s))
            .ToArray();

        return new GuidedTour(
            Base: Base(source, EventType.GuidedTour, id, string.Empty, design),
            TourX: payload.StartX,
            TourY: payload.StartY,
            Facing: payload.Facing == FruaFacing.Unknown ? 0 : (int)payload.Facing,
            UseStartLocation: payload.UseStartLocation ? 1 : 0,
            ExecuteEvent: payload.ExecuteEvent ? 1 : 0,
            Steps: steps);
    }

    /// <summary>
    /// A question event, whose five buttons are always present.
    /// </summary>
    /// <remarks>
    /// <b>All five buttons exist whether or not the design uses them.</b> The reference sets
    /// <c>numListButtons = 5</c> and marks every one present before reading anything, so an unused
    /// button is one with an empty label rather than one that is absent.
    /// </remarks>
    private static IGameEvent Question(FruaEvent source, uint id, EventType type,
                                       FruaStringTable? strings, FruaDesign? design)
    {
        var payload = FruaQuestionButtonEvent.Read(source);

        var options = payload.ButtonActions
            .Select(a => new QuestionOption(
                Label: string.Empty,
                Present: 1,
                PostChainAction: FruaEventEnums.PostChainAction(a),
                Chain: 0))
            .ToArray();

        return new QuestionEvent(
            Base: Base(source, type, id, Text(strings, payload.TextSlot), design),
            Title: Text(strings, payload.LabelSlot),
            NumButtons: options.Length,
            Options: options);
    }


    private static IGameEvent Shop(FruaEvent source, uint id, FruaDesign? design)
    {
        var payload = FruaShopEvent.Read(source);

        return new ShopEvent(
            Base: Base(source, EventType.ShopEvent, id, string.Empty, design),
            ForceExit: payload.ForceExit ? 1 : 0,
            CostFactor: FruaEventEnums.CostFactor(payload.CostFactor),

            // FRUA prices identification and buyback by the same cost factor rather than
            // separately, so the engine's independent knobs take its defaults.
            CostToIdentify: 0,
            BuybackPercentage: 0,
            CanIdentify: 0,
            CanAppraiseGems: 0,
            CanAppraiseJewels: 0,
            BuyItemsSoldOnly: 0,

            // A shop's stock is identified: the party can see what it is buying.
            ItemsAvailable: new ItemList(Items(payload.ItemsAvailable, design, identified: true),
                                         ReadyItems.Empty));
    }

    private static IGameEvent Temple(FruaEvent source, uint id, FruaStringTable? strings,
                                     FruaDesign? design)
    {
        var payload = FruaTempleEvent.Read(source);

        return new TempleEvent(
            Base: Base(source, EventType.TempleEvent, id, Text(strings, payload.TextSlot), design),
            ForceExit: payload.ForceExit ? 1 : 0,
            AllowDonations: payload.AllowDonations ? 1 : 0,
            CostFactor: FruaEventEnums.CostFactor(payload.CostFactor),

            // FRUA's temples heal without a level ceiling.
            MaxLevel: 0,
            DonationTrigger: (int)payload.DonationTrigger,
            DonationChain: 0,

            // The spells a temple casts are the engine's own list, chosen in its editor; FRUA
            // has no equivalent field, so an imported temple starts with none rather than with
            // a guess at what a temple ought to offer.
            TempleSpells: new SpellBook(0, []),
            TotalDonation: 0);
    }

    /// <summary>
    /// A training hall, and the six baseclasses its flag byte can name.
    /// </summary>
    /// <remarks>
    /// <b>These are five of the reference's fourteen <c>NotImplemented</c> markers.</b> Its six
    /// <c>TrainMagicUser</c>-style assignments are commented out, so a hall imported by the
    /// reference trains nobody. <see cref="FruaTrainingHallEvent.Trains"/> decodes the byte.
    /// </remarks>
    private static IGameEvent TrainingHall(FruaEvent source, uint id, FruaStringTable? strings,
                                           FruaDesign? design)
    {
        var payload = FruaTrainingHallEvent.Read(source);

        return new TrainingHallEvent(
            Base: Base(source, EventType.TrainingHallEvent, id,
                       Text(strings, payload.TextSlot), design),
            ForceExit: payload.ForceExit ? 1 : 0,
            Trainable: Trainable(payload.Trains),

            // The reference applies the cost factor to a fixed base price rather than storing a
            // multiplier, because the engine's hall charges an amount.
            Cost: FruaTrainingHallEvent.BaseCost);
    }

    /// <summary>The baseclasses a set of FRUA training flags names.</summary>
    private static TrainableBaseclass[] Trainable(FruaTrainedClasses classes)
    {
        (FruaTrainedClasses Flag, string Name)[] all =
        [
            (FruaTrainedClasses.MagicUser, "MagicUser"),
            (FruaTrainedClasses.Cleric, "Cleric"),
            (FruaTrainedClasses.Thief, "Thief"),
            (FruaTrainedClasses.Fighter, "Fighter"),
            (FruaTrainedClasses.Paladin, "Paladin"),
            (FruaTrainedClasses.Ranger, "Ranger"),
        ];

        return all.Where(c => classes.HasFlag(c.Flag))

                  // FRUA names no level range, so the hall trains every level it is asked for.
                  .Select(c => new TrainableBaseclass(c.Name, 0, 0, string.Empty))
                  .ToArray();
    }

    private static IGameEvent Tavern(FruaEvent source, uint id, FruaStringTable? strings,
                                     FruaDesign? design)
    {
        var payload = FruaTavernEvent.Read(source);

        var tales = payload.TaleSlots
            .Select(slot => Text(strings, slot))
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => new Tale(t, 0))
            .ToArray();

        return new TavernEvent(
            Base: Base(source, EventType.TavernEvent, id, Text(strings, payload.TextSlot), design),
            ForceExit: 0,

            // FRUA prices drinks by the design's own economy rather than a tavern multiplier.
            Inflation: 0,
            Barkeep: 0,
            AllowFights: payload.AllowFights ? 1 : 0,
            AllowDrinks: payload.AllowDrinks ? 1 : 0,
            FightChain: 0,
            DrinkChain: 0,
            DrinkPointTrigger: 0,
            TaleOrder: payload.TalesInRandomOrder ? 1 : 0,
            EachTaleOnceOnly: 0,
            Tales: tales,

            // FRUA's taverns serve no named drinks, but the engine's record holds a fixed five
            // that are never counted on the wire -- so a shorter list truncates the event and the
            // writer refuses it. Five blanks are what the reference's own defaults write.
            Drinks: Enumerable.Repeat(new Drink(string.Empty, 0), MoreEventReaders.MaxDrinks)
                              .ToArray());
    }

    /// <summary>
    /// A tavern-tales event, which FRUA stores as a single rumour.
    /// </summary>
    /// <remarks>
    /// The engine's own comment marks <c>TavernTales</c> obsolete and folded into
    /// <c>TavernEvent</c>, but the ordinal is still read and written, so a design that uses it
    /// converts to the same type rather than being promoted.
    /// </remarks>
    private static IGameEvent TavernTales(FruaEvent source, uint id, FruaStringTable? strings,
                                          FruaDesign? design)
    {
        var payload = FruaTavernEvent.Read(source);

        var tales = payload.TaleSlots
            .Select(slot => Text(strings, slot))
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => new TavernTale(t, 0, []))
            .ToArray();

        return new TavernTalesEvent(
            Base: Base(source, EventType.TavernTales, id, Text(strings, payload.TextSlot), design),
            Flags: 0,
            Tales: tales,
            Attributes: []);
    }

    /// <summary>
    /// A small town, which is a hub chaining to the services its flags select.
    /// </summary>
    /// <remarks>
    /// <b>FRUA generates the children; the engine only chains to them.</b> The flag byte spawns up
    /// to six child events with hard-coded English prompts, which
    /// <see cref="FruaSmallTownEvent"/> holds as named constants. Those children are separate
    /// events that a writer has to emit and chain — so the hub converts here with its chains left
    /// at zero, and filling them in belongs with whatever assigns the children their keys.
    /// </remarks>
    private static IGameEvent SmallTown(FruaEvent source, uint id, FruaStringTable? strings,
                                        FruaDesign? design)
    {
        var payload = FruaSmallTownEvent.Read(source);

        return new SmallTownEvent(
            Base: Base(source, EventType.SmallTown, id, Text(strings, payload.TextSlot), design),

            // A long the reference reads and writes and never uses.
            Unused: 0,
            TempleChain: 0,
            TrainingHallChain: 0,
            ShopChain: 0,
            InnChain: 0,
            TavernChain: 0,
            VaultChain: 0);
    }

    private static IGameEvent Encounter(FruaEvent source, uint id, FruaStringTable? strings,
                                        FruaDesign? design)
    {
        var payload = FruaEncounterEvent.Read(source);

        var options = payload.Buttons
            .Select((b, i) => new EncounterOption(
                Label: FruaEncounterEvent.Labels[i],
                Present: b.Present ? 1 : 0,
                AllowedUpClose: b.AllowedUpClose ? 1 : 0,
                OptionResult: FruaEventEnums.EncounterResult(b.Result),
                Chain: 0,
                OnlyUpClose: b.OnlyUpClose ? 1 : 0))
            .ToArray();

        return new EncounterEvent(
            Base: Base(source, EventType.EncounterEvent, id,
                       Text(strings, payload.TextSlot), design),
            Distance: FruaEventEnums.Distance(payload.Distance),
            MonsterSpeed: payload.MonsterSpeed,

            // What happens when the monsters arrive with nothing chosen. FRUA has no such field,
            // and its own behaviour is to fight, which is the engine's CombatNoSurprise.
            ZeroRangeResult: FruaEventEnums.EncounterResult(
                FruaEncounterResult.CombatNoSurprise),
            CombatChain: 0,
            TalkChain: 0,
            EscapeChain: 0,
            NumButtons: options.Length,
            Options: options);
    }

    /// <summary>
    /// A who-tries event: one ability or thief-skill check against a target.
    /// </summary>
    /// <remarks>
    /// <b>FRUA checks one thing; the engine checks any set.</b> Its stored value is a single
    /// selector stepping by four, so the engine's two check lists get exactly one entry between
    /// them — or none at all, when the selector is one of the two that always succeed or fail.
    /// </remarks>
    private static IGameEvent WhoTries(FruaEvent source, uint id, FruaStringTable? strings,
                                       FruaDesign? design)
    {
        var payload = FruaWhoTriesEvent.Read(source);

        // The six abilities run 8..28 and the eight thief skills 32..60, both stepping by four.
        int selector = (int)payload.Check;
        int[] abilities = selector is >= 8 and <= 28 ? [(selector - 8) / 4] : [];
        int[] skills = selector >= 32 ? [(selector - 32) / 4] : [];

        return new WhoTriesEvent(
            Base: Base(source, EventType.WhoTries, id, Text(strings, payload.TextSlot), design),
            AlwaysSucceeds: payload.Check == FruaWhoTriesCheck.AlwaysSucceeds ? 1 : 0,
            AlwaysFails: payload.Check == FruaWhoTriesCheck.AlwaysFails ? 1 : 0,
            AbilityChecks: abilities,
            ThiefSkillChecks: skills,
            StrengthBonus: 0,
            CompareToDie: payload.CompareToDie ? 1 : 0,
            CompareDie: 0,

            // FRUA gives one attempt and no branch targets beyond the chain every event has.
            NbrTries: 1,
            SuccessChain: 0,
            SuccessAction: 0,
            FailAction: 0,
            FailChain: 0,
            SuccessTransfer: NoTransfer,
            FailTransfer: NoTransfer);
    }

    /// <summary>A transfer that goes nowhere, for the branches FRUA does not fill in.</summary>
    private static TransferData NoTransfer { get; } = new(0, 0, 0, 0, 0, 0);


    /// <summary>
    /// The transfer a trial event's success or failure sends the party to.
    /// </summary>
    /// <remarks>
    /// <b>The entry point is <c>-1</c>, not a slot.</b> The reference sets both transfers'
    /// <c>destEP</c> to that, meaning the destination is given by coordinates rather than by one
    /// of the level's named entry points — which FRUA does not supply for these events, so the
    /// coordinates stay at the origin and the facing is the only part that carries across.
    /// </remarks>
    private static TransferData TrialTransfer(bool executeEvent, FruaFacing facing) =>
        new(ExecuteEvent: executeEvent ? 1 : 0,
            DestEntryPoint: FruaTrialActions.NoEntryPoint,
            DestLevel: 0,
            DestX: 0,
            DestY: 0,
            Facing: facing == FruaFacing.Unknown ? 0 : (int)facing);

    private static IGameEvent WhoPays(FruaEvent source, uint id, FruaStringTable? strings,
                                      FruaDesign? design)
    {
        var payload = FruaWhoPaysEvent.Read(source);
        var transfer = TrialTransfer(payload.ExecuteDestinationEvent, payload.Facing);

        // FRUA names one currency; the engine has a field per currency, so the others are zero.
        return new WhoPaysEvent(
            Base: Base(source, EventType.WhoPays, id, Text(strings, payload.TextSlot), design),
            Impossible: payload.Payment == FruaPaymentKind.Impossible ? 1 : 0,
            Gems: payload.Payment == FruaPaymentKind.Gems ? payload.Amount : 0,
            Jewels: payload.Payment == FruaPaymentKind.Jewels ? payload.Amount : 0,
            Platinum: payload.Payment == FruaPaymentKind.Platinum ? payload.Amount : 0,
            SuccessChain: 0,
            SuccessAction: (int)payload.SuccessAction,
            FailAction: (int)payload.FailAction,
            FailChain: 0,

            // Platinum is the design's default coin type.
            MoneyType: 0,
            SuccessTransfer: transfer,
            FailTransfer: transfer);
    }

    private static IGameEvent Password(FruaEvent source, uint id, FruaStringTable? strings,
                                       FruaDesign? design)
    {
        var payload = FruaPasswordEvent.Read(source);
        var transfer = TrialTransfer(payload.ExecuteDestinationEvent, payload.Facing);

        return new PasswordEvent(
            Base: Base(source, EventType.EnterPassword, id,
                       Text(strings, payload.TextSlot), design),

            // FRUA gives one attempt; the engine counts them.
            NbrTries: 1,
            SuccessChain: 0,
            FailChain: 0,
            SuccessAction: (int)payload.SuccessAction,
            FailAction: (int)payload.FailAction,

            // The answer is a string-table entry like any other text.
            Password: Text(strings, payload.PasswordSlot),
            SuccessTransfer: transfer,
            FailTransfer: transfer);
    }
}
