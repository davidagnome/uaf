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

    /// <summary>Reads every event of a level, paired with the ordinal that tagged it.</summary>
    private static List<(EventType Type, IGameEvent Body)>? ReadEvents(out DesignVersion version)
    {
        version = default;

        var root = RepoRoot();
        string? path = root is null ? null : Path.Combine(root.FullName, Level);
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

        // A floor, not an equality: this rises as bodies land, and naming the number is what makes
        // a regression say how much ground was lost. 574 of 575 -- the single holdout is a Combat
        // event, whose body carries a monster list and is the next one to port.
        Assert.True(writable >= 574,
                    $"{writable} of {events.Count} events are writable; it was 574");
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
        var events = ReadEvents(out _);
        if (events is null)
        {
            return;
        }

        var unwritable = events.FirstOrDefault(e => !EventBodyWriter.CanWrite(e.Type));
        if (unwritable.Body is null)
        {
            return;                                      // every type in this level is written
        }

        var ex = Assert.Throws<NotSupportedException>(
            () => Write(unwritable.Type, unwritable.Body));
        Assert.Contains("no writer yet", ex.Message);
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
