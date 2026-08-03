using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips real events pulled out of a shipped level's event list.
/// </summary>
/// <remarks>
/// <para>
/// The events are the shared blocker for the two record types still unwritten — a
/// <c>GLOBAL_STATS</c> ends with the global event list and a <c>LEVEL</c> is mostly events — so
/// these are ported in corpus-frequency order and this test grows with them. It asserts what
/// fraction of a real level is writable so far, as a floor, which means it tightens by itself
/// rather than needing rewriting each time a body lands.
/// </para>
/// <para>
/// Reading is done through the shipped file rather than a fixture for the reason the whole port
/// keeps rediscovering: a synthetic event can only pin a convention, never discover one.
/// </para>
/// </remarks>
public class EventWriterCorpusTests
{
    private static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    private const string Level = "reference/Case.dsn/Data/Level001.lvl";

    /// <summary>Every level file the two design directories that ship them hold.</summary>
    private static List<string> AllLevels()
    {
        var root = RepoRoot();
        if (root is null)
        {
            return [];
        }

        var paths = new List<string>();
        foreach (string design in (string[])["Case.dsn/Data", "SomethingWild.dsn/Data"])
        {
            string dir = Path.Combine(root.FullName, "reference",
                                      Path.Combine(design.Split('/')));
            if (Directory.Exists(dir))
            {
                paths.AddRange(Directory.EnumerateFiles(dir, "*.lvl"));
            }
        }
        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    /// <summary>Reads every event of a level, paired with the ordinal that tagged it.</summary>
    private static List<(EventType Type, IGameEvent Body)>? ReadEvents(out DesignVersion version)
    {
        var root = RepoRoot();
        string? path = root is null ? null : Path.Combine(root.FullName, Level);
        return ReadEvents(path, out version);
    }

    private static List<(EventType Type, IGameEvent Body)>? ReadEvents(
        string? path, out DesignVersion version)
    {
        version = default;

        if (path is null || !File.Exists(path))
        {
            return null;
        }

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        version = header.Version;

        var plain = new MfcArchiveReader(fs);
        var (w, h) = LevelReader.ReadDimensions(plain);
        for (int i = 0; i < w * h; i++)
        {
            LevelReader.ReadCell(plain, header.Version);
        }

        var ar = ArchiveCursor.For(plain);
        ar.ReadInt32();                                  // m_level
        int declared = ar.ReadInt32();

        var events = new List<(EventType, IGameEvent)>(declared);
        for (int i = 0; i < declared; i++)
        {
            var type = (EventType)ar.ReadInt32();
            if (EventDispatch.ReadsNothing(type))
            {
                continue;                                // four bytes and no body
            }

            var body = EventBodyReader.TryRead(ar, type, header.Version, ArchiveRole.Editor);
            Assert.NotNull(body);
            events.Add((type, body!));
        }
        return events;
    }

    private static byte[] Write(EventType type, IGameEvent body)
    {
        var stream = new MemoryStream();
        EventBodyWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)), type, body);
        return stream.ToArray();
    }

    private static IGameEvent ReadBack(byte[] bytes, EventType type)
    {
        var stream = new MemoryStream(bytes);
        var cursor = ArchiveCursor.For(new MfcArchiveReader(stream));
        var body = EventBodyReader.TryRead(cursor, type, GameEventWriter.WrittenVersion,
                                           ArchiveRole.Editor);
        Assert.NotNull(body);

        // Exact exhaustion. An event body has no length prefix, so in a real level a writer that
        // is a few bytes off corrupts every event after it and nothing before -- which is exactly
        // the failure this catches at the single-event level, where it is still diagnosable.
        Assert.Equal(stream.Length, stream.Position);
        return body!;
    }

    [Fact]
    public void Every_writable_event_in_a_real_level_round_trips()
    {
        var events = ReadEvents(out _);
        if (events is null)
        {
            return;
        }

        var writable = events.Where(e => EventBodyWriter.CanWrite(e.Type)).ToList();
        Assert.NotEmpty(writable);

        foreach (var (type, body) in writable)
        {
            byte[] first = Write(type, body);
            var read = ReadBack(first, type);

            AssertSameBase(body.Base, read.Base, type);

            // Write-read-write identity: a byte the writer omits is one the reader takes from
            // somewhere else, so the second pass diverges.
            Assert.Equal(first, Write(type, read));
        }
    }

    [Fact]
    public void The_writable_share_of_a_real_level_does_not_regress()
    {
        var events = ReadEvents(out _);
        if (events is null)
        {
            return;
        }

        // All 575 declared entries carry a body: the level has no ordinal that CreateNewEvent
        // would refuse, so none is skipped on the way in.
        Assert.Equal(575, events.Count);

        int writable = events.Count(e => EventBodyWriter.CanWrite(e.Type));

        // Every event in a real 575-event level now writes. Kept as an equality rather than a
        // floor now that it is complete: from here a regression is a lost type, not lost ground.
        Assert.Equal(events.Count, writable);
    }

    [Fact]
    public void Every_writable_event_in_every_shipped_level_round_trips()
    {
        // The whole corpus rather than one level: 18 files across two designs. A type that only
        // ever appears once -- a shop, a tavern, an NPC conversation -- is invisible to the
        // single-level test above, and those are exactly the ones with no second example to
        // check a guess against.
        var levels = AllLevels();
        if (levels.Count == 0)
        {
            return;
        }

        Assert.Equal(18, levels.Count);

        int total = 0, written = 0;
        var unwritten = new SortedDictionary<EventType, int>();

        foreach (string path in levels)
        {
            var events = ReadEvents(path, out _);
            Assert.NotNull(events);

            foreach (var (type, body) in events!)
            {
                total++;
                if (!EventBodyWriter.CanWrite(type))
                {
                    unwritten[type] = unwritten.GetValueOrDefault(type) + 1;
                    continue;
                }

                byte[] first = Write(type, body);
                AssertSameBase(body.Base, ReadBack(first, type).Base, type);
                Assert.Equal(first, Write(type, ReadBack(first, type)));
                written++;
            }
        }

        Assert.Equal(4705, total);

        // What is left is the town-service tail, and naming the types rather than just the count
        // is what makes this say which work remains.
        Assert.Equal(4679, written);
        Assert.Equal(
            "Camp, GainExperience, NPCSays, RemoveNPCEvent, ShopEvent, Sounds, TavernEvent, " +
            "TempleEvent, TrainingHallEvent, WhoPays",
            string.Join(", ", unwritten.Keys));
    }

    [Fact]
    public void Every_type_the_dispatcher_claims_it_can_write_really_appears()
    {
        // Guards the reverse of the floor above: a CanWrite that listed a type nothing tests would
        // make the count look better without any evidence behind it.
        var events = ReadEvents(out _);
        if (events is null)
        {
            return;
        }

        var present = events.Select(e => e.Type).Distinct().ToHashSet();
        var claimed = Enum.GetValues<EventType>().Where(EventBodyWriter.CanWrite).ToList();

        Assert.NotEmpty(claimed);
        Assert.Contains(claimed, present.Contains);
    }

    [Fact]
    public void A_type_with_no_writer_yet_says_so_rather_than_writing_nothing()
    {
        // No longer reachable through this level -- every type in it writes -- so the throw is
        // exercised directly. It has to stay exercised: a body of unknown length cannot be
        // stepped over, so a dispatcher that silently wrote nothing would leave a level whose
        // every later event is read out of the middle of this one.
        var events = ReadEvents(out _);
        if (events is null)
        {
            return;
        }

        var body = events[0].Body;

        Assert.False(EventBodyWriter.CanWrite(EventType.ShopEvent));
        var ex = Assert.Throws<NotSupportedException>(
            () => Write(EventType.ShopEvent, body));
        Assert.Contains("no writer yet", ex.Message);
    }

    [Fact]
    public void A_type_that_has_no_shape_at_all_says_something_different()
    {
        // InnEvent and GPDLEvent are not "not ported": CreateNewEvent cannot construct either
        // (GameEvent.cpp:3888), so no design the reference loads can contain one. Saying "no
        // writer yet" would imply there is something to port.
        var events = ReadEvents(out _);
        if (events is null)
        {
            return;
        }

        foreach (var type in (EventType[])[EventType.InnEvent, EventType.GPDLEvent])
        {
            Assert.False(EventBodyWriter.CanWrite(type));
            var ex = Assert.Throws<NotSupportedException>(
                () => Write(type, events[0].Body));
            Assert.Contains("no serialized shape", ex.Message);
        }
    }

    [Fact]
    public void The_corpus_events_carry_no_legacy_numeric_ids()
    {
        // Case.dsn is 2.53, past the 0.998101 that replaced numeric database keys with names --
        // so the refusal is real but nothing here trips it, and the round trip above is not
        // quietly skipping most of the level.
        var events = ReadEvents(out var version);
        if (events is null)
        {
            return;
        }

        Assert.True(version > DesignVersion.SpellNames);
        Assert.All(events, e => Assert.False(e.Body.Base.Control.LegacyIds));
        Assert.All(events,
                   e => Assert.True(GameEventWriter.CanWrite(e.Body.Base, out string r), r));
    }

    private static void AssertSameBase(GameEventBase expected, GameEventBase actual, EventType type)
    {
        Assert.Equal(expected.EventType, actual.EventType);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.ChainEventHappen, actual.ChainEventHappen);
        Assert.Equal(expected.ChainEventNotHappen, actual.ChainEventNotHappen);
        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal(expected.Text2, actual.Text2);
        Assert.Equal(expected.Text3, actual.Text3);
        Assert.Equal(expected.Attributes, actual.Attributes);
        Assert.Equal(expected.Pic, actual.Pic);
        Assert.Equal(expected.Pic2, actual.Pic2);

        var e = expected.Control;
        var a = actual.Control;
        Assert.Equal(e.EventStatusUnused, a.EventStatusUnused);
        Assert.Equal(e.EventResultUnused, a.EventResultUnused);
        Assert.Equal(e.OnceOnly, a.OnceOnly);
        Assert.Equal(e.ChainTrigger, a.ChainTrigger);
        Assert.Equal(e.EventTrigger, a.EventTrigger);
        Assert.Equal(e.ItemId, a.ItemId);
        Assert.Equal(e.Quest, a.Quest);
        Assert.Equal(e.Chance, a.Chance);
        Assert.Equal(e.Facing, a.Facing);
        Assert.Equal(e.RaceId, a.RaceId);
        Assert.Equal(e.ClassOrBaseclassId, a.ClassOrBaseclassId);
        Assert.Equal(e.CharacterId, a.CharacterId);
        Assert.Equal(e.Attributes, a.Attributes);
        Assert.Equal(e.GpdlData, a.GpdlData);
        Assert.Equal(e.GpdlIsBinary, a.GpdlIsBinary);
        Assert.Equal(e.PartyX, a.PartyX);
        Assert.Equal(e.PartyY, a.PartyY);
        Assert.Equal(e.MemorizedSpellId, a.MemorizedSpellId);
        Assert.Equal(e.MemorizedSpellClass, a.MemorizedSpellClass);
        Assert.Equal(e.MemorizedSpellLevel, a.MemorizedSpellLevel);

        _ = type;
    }
}
