namespace UAF.Serialization;

/// <summary>
/// Dispatches an event body to the writer for its type — the inverse of
/// <see cref="EventBodyReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the shared blocker for the two record types still unwritten.</b>
/// <c>GLOBAL_STATS</c>'s storing branch ends with the design's global event list
/// (<c>GlobalData.cpp:4556</c>) and a <c>LEVEL</c> is mostly events, so neither can be written
/// until the bodies can. The dispatch exists first, with the types that have no writer throwing a
/// citation, so that a caller finds out which type stopped it rather than producing a truncated
/// file.
/// </para>
/// <para>
/// <b>The type tag is written by the list, not by the body.</b> A level's event list is a chain of
/// type-tagged bodies with no length prefixes: the tag comes first, then the body. This writes only
/// the body, exactly as <see cref="EventBodyReader.TryRead"/> reads only the body — the caller owns
/// the tag, because the caller is what knows the chain.
/// </para>
/// </remarks>
public static class EventBodyWriter
{
    /// <summary>
    /// Whether a body of this type can be written at all.
    /// </summary>
    /// <remarks>
    /// Lets a caller check a whole list before starting a file, the way every database writer
    /// checks its records — a level that stops half way through its event chain has left a file
    /// nothing can read.
    /// </remarks>
    public static bool CanWrite(EventType type) => type switch
    {
        EventType.ChainEventType or EventType.QuestionList or EventType.QuestionButton or
        EventType.QuestionYesNo or EventType.PassTime or EventType.RandomEvent or
        EventType.AddNpc or EventType.Stairs or EventType.Teleporter or
        EventType.TransferModule or EventType.TextStatement or EventType.QuestStage or
        EventType.GuidedTour or EventType.SpecialItem or EventType.Utilities or
        EventType.GiveTreasure or EventType.CombatTreasure or EventType.LogicBlock or
        EventType.Combat or EventType.PickOneCombat or
        EventType.Sounds or EventType.GainExperience or EventType.Camp or
        EventType.RemoveNPCEvent or EventType.NPCSays or EventType.TrainingHallEvent or
        EventType.TempleEvent or EventType.ShopEvent or EventType.TavernEvent or
        EventType.WhoPays or EventType.Damage or EventType.Vault => true,
        _ => false,
    };

    /// <summary>Writes one event body.</summary>
    /// <exception cref="NotSupportedException">
    /// When the type has no writer yet, or has none at all — see <see cref="CanWrite"/>.
    /// </exception>
    public static void Write(IArchiveWriteCursor ar, EventType type, IGameEvent body)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(body);

        switch (type)
        {
            case EventType.Damage:
                PartyEffectEventWriters.WriteDamage(ar, Expect<DamageEvent>(body, type));
                return;
            case EventType.Vault:
                PartyEffectEventWriters.WriteVault(ar, Expect<VaultEvent>(body, type));
                return;

            case EventType.ChainEventType:
                SimpleEventWriters.WriteChain(ar, Expect<ChainEvent>(body, type));
                return;
            case EventType.QuestionList:
                SimpleEventWriters.WriteQuestionList(ar, Expect<QuestionEvent>(body, type));
                return;
            case EventType.QuestionButton:
                SimpleEventWriters.WriteQuestionButton(ar, Expect<QuestionEvent>(body, type));
                return;
            case EventType.QuestionYesNo:
                SimpleEventWriters.WriteYesNo(ar, Expect<YesNoEvent>(body, type));
                return;
            case EventType.PassTime:
                SimpleEventWriters.WritePassTime(ar, Expect<PassTimeEvent>(body, type));
                return;
            case EventType.RandomEvent:
                SimpleEventWriters.WriteRandom(ar, Expect<RandomEvent>(body, type));
                return;
            case EventType.AddNpc:
                SimpleEventWriters.WriteAddNpc(ar, Expect<AddNpcEvent>(body, type));
                return;
            case EventType.Stairs:
            case EventType.Teleporter:
            case EventType.TransferModule:
                SimpleEventWriters.WriteTransfer(ar, Expect<TransferEvent>(body, type));
                return;

            case EventType.TextStatement:
                ContentEventWriters.WriteText(ar, Expect<TextEvent>(body, type));
                return;
            case EventType.QuestStage:
                ContentEventWriters.WriteQuest(ar, Expect<QuestEvent>(body, type));
                return;
            case EventType.GuidedTour:
                ContentEventWriters.WriteGuidedTour(ar, Expect<GuidedTour>(body, type));
                return;
            case EventType.SpecialItem:
                ContentEventWriters.WriteSpecialItem(ar, Expect<SpecialItemEvent>(body, type));
                return;
            case EventType.Utilities:
                ContentEventWriters.WriteUtilities(ar, Expect<UtilitiesEvent>(body, type));
                return;
            case EventType.GiveTreasure:
                ContentEventWriters.WriteGiveTreasure(ar, Expect<TreasureEvent>(body, type));
                return;
            case EventType.CombatTreasure:
                ContentEventWriters.WriteCombatTreasure(ar, Expect<TreasureEvent>(body, type));
                return;
            case EventType.LogicBlock:
                ContentEventWriters.WriteLogicBlock(ar, Expect<LogicBlockEvent>(body, type));
                return;

            case EventType.Combat:
            case EventType.PickOneCombat:
                CombatEventWriter.Write(ar, Expect<CombatEvent>(body, type));
                return;

            case EventType.Sounds:
                TownEventWriters.WriteSound(ar, Expect<SoundEvent>(body, type));
                return;
            case EventType.GainExperience:
                TownEventWriters.WriteGainExperience(ar, Expect<GainExperienceEvent>(body, type));
                return;
            case EventType.Camp:
                TownEventWriters.WriteCamp(ar, Expect<CampEvent>(body, type));
                return;
            case EventType.RemoveNPCEvent:
                TownEventWriters.WriteRemoveNpc(ar, Expect<RemoveNpcEvent>(body, type));
                return;
            case EventType.NPCSays:
                TownEventWriters.WriteNpcSays(ar, Expect<NpcSaysEvent>(body, type));
                return;
            case EventType.TrainingHallEvent:
                TownEventWriters.WriteTrainingHall(ar, Expect<TrainingHallEvent>(body, type));
                return;
            case EventType.TempleEvent:
                TownEventWriters.WriteTemple(ar, Expect<TempleEvent>(body, type));
                return;
            case EventType.ShopEvent:
                TownEventWriters.WriteShop(ar, Expect<ShopEvent>(body, type));
                return;
            case EventType.TavernEvent:
                TownEventWriters.WriteTavern(ar, Expect<TavernEvent>(body, type));
                return;
            case EventType.WhoPays:
                TownEventWriters.WriteWhoPays(ar, Expect<WhoPaysEvent>(body, type));
                return;

            // Unreachable in any design the reference could load: CreateNewEvent reaches
            // die(0xab51a) for the first and does not list the second at all
            // (GameEvent.cpp:3888). EventBodyReader returns null for the same pair.
            case EventType.InnEvent:
            case EventType.GPDLEvent:
                throw new NotSupportedException(
                    $"{type} has no serialized shape: CreateNewEvent (GameEvent.cpp:3888) cannot " +
                    "construct one, so no design the reference loads can contain it.");

            default:
                throw new NotSupportedException(
                    $"{type} has a reader but no writer yet. The bodies are being ported in " +
                    "corpus-frequency order; see EventBodyReader.TryRead for the shape and " +
                    "GameEvent.cpp for the storing branch.");
        }
    }

    /// <summary>
    /// Casts a body to the record its type is read into, naming both when it is not.
    /// </summary>
    /// <remarks>
    /// The dispatch is by ordinal and the payload by type, and the two can disagree — several
    /// ordinals share a record (the three transfer forms, and both question forms). An
    /// <c>InvalidCastException</c> here would say only that a cast failed; this says which event
    /// type was being written and what it was handed.
    /// </remarks>
    private static T Expect<T>(IGameEvent body, EventType type) where T : class, IGameEvent =>
        body as T ?? throw new ArgumentException(
            $"event type {type} is written from {typeof(T).Name}, not {body.GetType().Name}.",
            nameof(body));
}
