using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks a real level's event list as far as the ported subclasses allow.
/// </summary>
/// <remarks>
/// <para>
/// Case.dsn's Level001 holds 575 events across many types. Only some subclasses are ported, so this
/// walks until it meets an unported one and stops — deliberately, rather than guessing at a layout.
/// The assertion is on how far it gets, so the test tightens by itself as more subclasses land
/// instead of needing to be rewritten.
/// </para>
/// <para>
/// What makes it a real check rather than a smoke test: an event's fields are only reachable
/// through the previous event's, so reading the Nth event at all means the preceding N-1 were read
/// at exactly the right length.
/// </para>
/// </remarks>
public class EventWalkTests
{
    /// <summary>How far the walk reached when this test was last updated.</summary>
    /// <summary>
    /// The walk now covers this level completely. Kept as a floor rather than an equality so a
    /// regression names the event it broke on.
    /// </summary>
    private const int KnownReach = 575;

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>Reads one event of a ported type, or returns false for anything else.</summary>
    private static bool TryReadEvent(IArchiveCursor ar, EventType type, DesignVersion version)
    {
        switch (type)
        {
            case EventType.Combat:
            case EventType.PickOneCombat:
                CombatEventReader.Read(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.TextStatement:
                TextEventReader.Read(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.GuidedTour:
                GuidedTourReader.Read(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.SpecialItem:
                SpecialItemEventReader.Read(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.QuestStage:
                QuestEventReader.Read(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.Utilities:
                UtilitiesEventReader.Read(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.ChainEventType:
                SimpleEventReaders.ReadChain(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.QuestionList:
                SimpleEventReaders.ReadQuestionList(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.QuestionButton:
                SimpleEventReaders.ReadQuestionButton(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.QuestionYesNo:
                SimpleEventReaders.ReadYesNo(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.PassTime:
                SimpleEventReaders.ReadPassTime(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.Stairs:
            case EventType.Teleporter:
            case EventType.TransferModule:
                SimpleEventReaders.ReadTransfer(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.LogicBlock:
                LogicBlockEventReader.Read(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.NPCSays:
                MoreEventReaders.ReadNpcSays(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.TavernEvent:
                MoreEventReaders.ReadTavern(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.TempleEvent:
                MoreEventReaders.ReadTemple(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.ShopEvent:
                MoreEventReaders.ReadShop(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.RemoveNPCEvent:
                MoreEventReaders.ReadRemoveNpc(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.Camp:
                MoreEventReaders.ReadCamp(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.TrainingHallEvent:
                MoreEventReaders.ReadTrainingHall(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.Sounds:
                MoreEventReaders.ReadSound(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.GainExperience:
                MoreEventReaders.ReadGainExperience(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.FlowControl:
                MoreEventReaders.ReadFlowControl(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.WhoPays:
                MoreEventReaders.ReadWhoPays(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.RandomEvent:
                SimpleEventReaders.ReadRandom(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.AddNpc:
                SimpleEventReaders.ReadAddNpc(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.GiveTreasure:
                TreasureEventReaders.ReadGiveTreasure(ar, version, ArchiveRole.Editor);
                return true;
            case EventType.CombatTreasure:
                TreasureEventReaders.ReadCombatTreasure(ar, version, ArchiveRole.Editor);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Counts <c>EVENT_DATA_ATTR</c> markers occurring before a byte offset.</summary>
    private static int MarkersBefore(string rel, long offset)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(RepoRoot().FullName, rel));
        byte[] needle = System.Text.Encoding.ASCII.GetBytes(AslMaps.EventData);
        int count = 0;
        for (int i = 0; i + needle.Length <= data.Length && i < offset; i++)
        {
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle)) count++;
        }
        return count;
    }

    private static (int Reached, Dictionary<EventType, int> Seen, int Declared, int Handled,
                    long EndPosition) Walk(string rel)
    {
        int handled = 0;
        using var fs = File.OpenRead(Path.Combine(RepoRoot().FullName, rel));
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        var plain = new MfcArchiveReader(fs);

        var (w, h) = LevelReader.ReadDimensions(plain);
        for (int i = 0; i < w * h; i++)
        {
            LevelReader.ReadCell(plain, header.Version);
        }

        var ar = ArchiveCursor.For(plain);
        ar.ReadInt32();                                  // m_level
        int declared = ar.ReadInt32();

        var seen = new Dictionary<EventType, int>();
        int reached = 0;
        for (int i = 0; i < declared; i++)
        {
            var type = (EventType)ar.ReadInt32();
            seen[type] = seen.GetValueOrDefault(type) + 1;

            // Mirror CreateNewEvent (GameEvent.cpp:3833): ANY ordinal it does not recognise --
            // not just NoEvent -- yields a null object, so nothing further is read and the entry
            // is just its four bytes. Case.dsn's Level001 really does contain two such ordinals
            // (600 and 1800).
            //
            // Skipping them is therefore correct, but it also hides drift, since a desynchronised
            // stream produces unrecognised ordinals too. Marker counting below is what actually
            // detects that.
            if (EventDispatch.ReadsNothing(type) || TryReadEvent(ar, type, header.Version))
            {
                if (!EventDispatch.ReadsNothing(type)) handled++;
                reached = i + 1;
                continue;
            }
            break;                                       // an unported type; stop cleanly
        }
        return (reached, seen, declared, handled, fs.Position);
    }

    [Fact]
    public void Walk_reaches_at_least_as_far_as_it_did_before()
    {
        string path = Path.Combine(RepoRoot().FullName, "reference/Case.dsn/Data/Level001.lvl");
        if (!File.Exists(path)) return;

        var (reached, seen, declared, handled, endPosition) = Walk("reference/Case.dsn/Data/Level001.lvl");

        Assert.Equal(575, declared);
        Assert.True(reached >= KnownReach,
                    $"walk regressed: reached {reached}, previously {KnownReach}");

        // Every event in a 575-event level, read end to end. Each one's fields are reachable only
        // through the previous event's, so this is 575 consecutive exact-length reads.
        Assert.Equal(declared, reached);

        // THE drift detector. Every event that actually reads a body writes exactly one
        // EVENT_DATA_ATTR marker, so the number of bodies read must equal the number of markers
        // lying before where the walk stopped. Skipping unrecognised ordinals cannot fake this:
        // a desynchronised stream drifts away from the marker positions immediately.
        Assert.Equal(MarkersBefore("reference/Case.dsn/Data/Level001.lvl", endPosition), handled);
    }

    [Fact]
    public void Ported_types_appear_repeatedly_rather_than_once()
    {
        string path = Path.Combine(RepoRoot().FullName, "reference/Case.dsn/Data/Level001.lvl");
        if (!File.Exists(path)) return;

        var (_, seen, _, _, _) = Walk("reference/Case.dsn/Data/Level001.lvl");

        // Reading one event of a type could be luck; reading dozens in sequence could not, since
        // each depends on the previous ending in exactly the right place.
        Assert.True(seen.GetValueOrDefault(EventType.TextStatement) > 400,
                    "expected many text events");

        // Every ordinal must now be a real event type. Before BACKGROUND_SOUND_DATA was modelled
        // correctly, this level appeared to contain ordinals 600 and 1800 -- they were actually a
        // combat event's EndTime and StartTime, read as event headers after a 16-byte shortfall.
        Assert.All(seen.Keys, k => Assert.True(Enum.IsDefined(k), $"ordinal {(int)k} is not a type"));

        // ...and no NoEvent entries, which the drift also faked.
        Assert.False(seen.ContainsKey(EventType.NoEvent));
        Assert.True(seen.GetValueOrDefault(EventType.QuestStage) >= 16);
        Assert.True(seen.GetValueOrDefault(EventType.Utilities) >= 14);
        Assert.True(seen.GetValueOrDefault(EventType.GuidedTour) >= 9);
        Assert.True(seen.GetValueOrDefault(EventType.SpecialItem) >= 6);
        Assert.True(seen.GetValueOrDefault(EventType.ChainEventType) >= 4);
    }

    /// <summary>Walks every level in a design, returning how many walked to completion.</summary>
    private static (int Complete, int Total, List<string> Details) WalkDesign(string rel)
    {
        var dir = new DirectoryInfo(Path.Combine(RepoRoot().FullName, rel));
        if (!dir.Exists) return (0, 0, []);

        int complete = 0, total = 0;
        var details = new List<string>();
        foreach (var file in dir.GetFiles("*.lvl").OrderBy(f => f.Name))
        {
            total++;
            var (reached, _, declared, _, _) = Walk(Path.Combine(rel, file.Name));
            if (reached == declared) complete++;
            else details.Add($"{file.Name}: {reached}/{declared}");
        }
        return (complete, total, details);
    }

    /// <summary>design folder, level count, total events.</summary>
    public static TheoryData<string, int, int> Designs => new()
    {
        { "reference/Case.dsn/Data", 10, 4244 },
        { "reference/Ambassador's_Letter/Data", 3, 1529 },
        { "reference/SomethingWild.dsn/Data", 8, 461 },
        { "reference/dc-default/data-files", 1, 0 },
    };

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_level_of_every_design_walks_completely(
        string rel, int expectedLevels, int expectedEvents)
    {
        var (complete, total, details) = WalkDesign(rel);
        if (total == 0) return;                          // gitignored fixture absent

        // Every event's fields are reachable only through the previous event's, so a single wrong
        // field width anywhere in any of these records would stop a walk short. Across the four
        // designs this is 6,234 consecutive exact-length reads spanning versions 2.53 to 5.28.
        Assert.Equal(expectedLevels, total);
        Assert.Equal(total, complete);
        Assert.Empty(details);
        Assert.Equal(expectedEvents, TotalEvents(rel));
    }

    /// <summary>Sums the events read across every level of a design.</summary>
    private static int TotalEvents(string rel)
    {
        var dir = new DirectoryInfo(Path.Combine(RepoRoot().FullName, rel));
        int events = 0;
        foreach (var file in dir.GetFiles("*.lvl"))
        {
            events += Walk(Path.Combine(rel, file.Name)).Reached;
        }
        return events;
    }

    [Fact]
    public void DefaultDesign_level_walks_completely()
    {
        var (reached, seen, declared, _, _) = Walk("src/UAFWinEd/DefaultDesign.dsn/Data/Level000.lvl");

        // Both of this level's entries are combats, and both are ported, so the walk finishes.
        // (It formerly read as Combat + NoEvent; that was the BACKGROUND_SOUND_DATA shortfall.)
        Assert.Equal(2, declared);
        Assert.Equal(2, reached);
        Assert.Equal(2, seen[EventType.Combat]);
        Assert.False(seen.ContainsKey(EventType.NoEvent));
    }
}
