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
/// <b>Returning false is not "skip".</b> There is no way to skip a body of unknown length, so a
/// false answer must abort the walk — a caller that treated it as "ignore and continue" would read
/// the next event's fields out of the middle of this one.
/// </para>
/// </remarks>
public static class EventBodyReader
{
    /// <summary>
    /// Reads one event body, or returns false when its type has no reader.
    /// </summary>
    /// <param name="role">
    /// Engine and editor builds serialize several events differently, so this cannot be assumed.
    /// The walk tests use <see cref="ArchiveRole.Editor"/> because that is what the shipped level
    /// files were written by.
    /// </param>
    public static bool TryRead(IArchiveCursor ar, EventType type, DesignVersion version,
                               ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        switch (type)
        {
            case EventType.Combat:
            case EventType.PickOneCombat:
                CombatEventReader.Read(ar, version, role);
                return true;
            case EventType.TextStatement:
                TextEventReader.Read(ar, version, role);
                return true;
            case EventType.GuidedTour:
                GuidedTourReader.Read(ar, version, role);
                return true;
            case EventType.SpecialItem:
                SpecialItemEventReader.Read(ar, version, role);
                return true;
            case EventType.QuestStage:
                QuestEventReader.Read(ar, version, role);
                return true;
            case EventType.Utilities:
                UtilitiesEventReader.Read(ar, version, role);
                return true;
            case EventType.ChainEventType:
                SimpleEventReaders.ReadChain(ar, version, role);
                return true;
            case EventType.QuestionList:
                SimpleEventReaders.ReadQuestionList(ar, version, role);
                return true;
            case EventType.QuestionButton:
                SimpleEventReaders.ReadQuestionButton(ar, version, role);
                return true;
            case EventType.QuestionYesNo:
                SimpleEventReaders.ReadYesNo(ar, version, role);
                return true;
            case EventType.PassTime:
                SimpleEventReaders.ReadPassTime(ar, version, role);
                return true;
            case EventType.Stairs:
            case EventType.Teleporter:
            case EventType.TransferModule:
                SimpleEventReaders.ReadTransfer(ar, version, role);
                return true;
            case EventType.LogicBlock:
                LogicBlockEventReader.Read(ar, version, role);
                return true;
            case EventType.NPCSays:
                MoreEventReaders.ReadNpcSays(ar, version, role);
                return true;
            case EventType.TavernEvent:
                MoreEventReaders.ReadTavern(ar, version, role);
                return true;
            case EventType.TempleEvent:
                MoreEventReaders.ReadTemple(ar, version, role);
                return true;
            case EventType.ShopEvent:
                MoreEventReaders.ReadShop(ar, version, role);
                return true;
            case EventType.RemoveNPCEvent:
                MoreEventReaders.ReadRemoveNpc(ar, version, role);
                return true;
            case EventType.Camp:
                MoreEventReaders.ReadCamp(ar, version, role);
                return true;
            case EventType.TrainingHallEvent:
                MoreEventReaders.ReadTrainingHall(ar, version, role);
                return true;
            case EventType.Sounds:
                MoreEventReaders.ReadSound(ar, version, role);
                return true;
            case EventType.GainExperience:
                MoreEventReaders.ReadGainExperience(ar, version, role);
                return true;
            case EventType.FlowControl:
                MoreEventReaders.ReadFlowControl(ar, version, role);
                return true;
            case EventType.WhoPays:
                MoreEventReaders.ReadWhoPays(ar, version, role);
                return true;
            case EventType.RandomEvent:
                SimpleEventReaders.ReadRandom(ar, version, role);
                return true;
            case EventType.AddNpc:
                SimpleEventReaders.ReadAddNpc(ar, version, role);
                return true;
            case EventType.GiveTreasure:
                TreasureEventReaders.ReadGiveTreasure(ar, version, role);
                return true;
            case EventType.CombatTreasure:
                TreasureEventReaders.ReadCombatTreasure(ar, version, role);
                return true;
            default:
                return false;
        }
    }
}
