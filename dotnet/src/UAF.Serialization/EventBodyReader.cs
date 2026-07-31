using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Dispatches an event body to the reader for its type.
/// </summary>
/// <remarks>
/// <para>
/// A level's event list is a chain of type-tagged bodies with no length prefixes, so consuming one
/// requires knowing its exact shape. That makes this switch the gate on everything downstream of
/// the event list in a <c>.lvl</c> file — the wall sets, background sets and blockage keys all sit
/// after it.
/// </para>
/// <para>
/// It lived in the serialization tests until now, which meant the engine could only read a level's
/// cell grid (<see cref="LevelFileReader.ReadAreaMapOnly"/>) and never its wall art. Moving it here
/// is what unblocks that; the tests now call this rather than keeping their own copy, so there is
/// one dispatcher to keep correct rather than two.
/// </para>
/// <para>
/// <b>Returning null is not "skip".</b> There is no way to step over a body of unknown length, so
/// a null answer must abort the walk — a caller that treated it as "ignore and continue" would read
/// the next event's fields out of the middle of this one.
/// </para>
/// </remarks>
public static class EventBodyReader
{
    /// <summary>
    /// Reads one event body, or returns null when its type has no reader.
    /// </summary>
    /// <param name="role">
    /// Engine and editor builds serialize several events differently, so this cannot be assumed.
    /// The walk tests use <see cref="ArchiveRole.Editor"/> because that is what the shipped level
    /// files were written by.
    /// </param>
    public static IGameEvent? TryRead(IArchiveCursor ar, EventType type, DesignVersion version,
                                      ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        switch (type)
        {
            case EventType.Combat:
            case EventType.PickOneCombat:
                return CombatEventReader.Read(ar, version, role);
            case EventType.TextStatement:
                return TextEventReader.Read(ar, version, role);
            case EventType.GuidedTour:
                return GuidedTourReader.Read(ar, version, role);
            case EventType.SpecialItem:
                return SpecialItemEventReader.Read(ar, version, role);
            case EventType.QuestStage:
                return QuestEventReader.Read(ar, version, role);
            case EventType.Utilities:
                return UtilitiesEventReader.Read(ar, version, role);
            case EventType.ChainEventType:
                return SimpleEventReaders.ReadChain(ar, version, role);
            case EventType.QuestionList:
                return SimpleEventReaders.ReadQuestionList(ar, version, role);
            case EventType.QuestionButton:
                return SimpleEventReaders.ReadQuestionButton(ar, version, role);
            case EventType.QuestionYesNo:
                return SimpleEventReaders.ReadYesNo(ar, version, role);
            case EventType.PassTime:
                return SimpleEventReaders.ReadPassTime(ar, version, role);
            case EventType.Stairs:
            case EventType.Teleporter:
            case EventType.TransferModule:
                return SimpleEventReaders.ReadTransfer(ar, version, role);
            case EventType.LogicBlock:
                return LogicBlockEventReader.Read(ar, version, role);
            case EventType.NPCSays:
                return MoreEventReaders.ReadNpcSays(ar, version, role);
            case EventType.TavernEvent:
                return MoreEventReaders.ReadTavern(ar, version, role);
            case EventType.TempleEvent:
                return MoreEventReaders.ReadTemple(ar, version, role);
            case EventType.ShopEvent:
                return MoreEventReaders.ReadShop(ar, version, role);
            case EventType.RemoveNPCEvent:
                return MoreEventReaders.ReadRemoveNpc(ar, version, role);
            case EventType.Camp:
                return MoreEventReaders.ReadCamp(ar, version, role);
            case EventType.TrainingHallEvent:
                return MoreEventReaders.ReadTrainingHall(ar, version, role);
            case EventType.Sounds:
                return MoreEventReaders.ReadSound(ar, version, role);
            case EventType.GainExperience:
                return MoreEventReaders.ReadGainExperience(ar, version, role);
            case EventType.FlowControl:
                return MoreEventReaders.ReadFlowControl(ar, version, role);
            case EventType.WhoPays:
                return MoreEventReaders.ReadWhoPays(ar, version, role);
            case EventType.RandomEvent:
                return SimpleEventReaders.ReadRandom(ar, version, role);
            case EventType.AddNpc:
                return SimpleEventReaders.ReadAddNpc(ar, version, role);
            case EventType.GiveTreasure:
                return TreasureEventReaders.ReadGiveTreasure(ar, version, role);
            case EventType.CombatTreasure:
                return TreasureEventReaders.ReadCombatTreasure(ar, version, role);
            default:
                return null;
        }
    }
}
