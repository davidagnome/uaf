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
    private const int KnownReach = 552;

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
