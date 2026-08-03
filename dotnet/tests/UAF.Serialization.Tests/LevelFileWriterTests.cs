using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole shipped level files.
/// </summary>
/// <remarks>
/// The sixth and last record type. A level is mostly its event chain, so this is also the widest
/// test the event writers get: all 18 files of the two designs that ship levels, 4,705 events, each
/// written in place in a chain with no length prefixes anywhere.
/// </remarks>
public class LevelFileWriterTests
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
            string dir = Path.Combine(root.FullName, "reference", Path.Combine(design.Split('/')));
            if (Directory.Exists(dir))
            {
                paths.AddRange(Directory.EnumerateFiles(dir, "*.lvl"));
            }
        }
        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    private static LevelFile Read(Stream stream) =>
        LevelFileReader.Read(stream, ArchiveRole.Editor,
                             (ar, type, version) =>
                                 EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor));

    private static LevelFile ReadFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    private static byte[] Write(LevelFile level)
    {
        var stream = new MemoryStream();
        LevelFileWriter.WriteFile(stream, level);
        return stream.ToArray();
    }

    private static LevelFile ReadBack(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return Read(stream);
    }

    [Fact]
    public void Every_shipped_level_round_trips()
    {
        var levels = AllLevels();
        if (levels.Count == 0)
        {
            return;
        }

        Assert.Equal(18, levels.Count);

        foreach (string path in levels)
        {
            var level = ReadFile(path);
            Assert.True(LevelFileWriter.CanWrite(level, out string reason),
                        $"{Path.GetFileName(path)}: {reason}");

            var read = ReadBack(Write(level));
            AssertSame(level, read, path);
        }
    }

    [Fact]
    public void Writing_what_was_read_gives_the_same_bytes_the_second_time()
    {
        // In a level this is the assertion that matters most: the event chain has no length
        // prefixes anywhere, so a writer a few bytes off in one event corrupts every event after
        // it and nothing before -- and the second pass is what makes that visible.
        foreach (string path in AllLevels())
        {
            byte[] first = Write(ReadFile(path));
            byte[] second = Write(ReadBack(first));

            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void The_grid_survives_its_packed_flags_and_its_declaration_order()
    {
        var levels = AllLevels();
        if (levels.Count == 0)
        {
            return;
        }

        // Width and height go out in the opposite order to their declaration, so only a
        // non-square level catches getting it wrong -- and most of the corpus is square, so the
        // one that is not has to be sought out rather than assumed.
        var oblong = levels.Select(ReadFile).First(l => l.Width != l.Height);
        var readOblong = ReadBack(Write(oblong));

        Assert.Equal(oblong.Width, readOblong.Width);
        Assert.Equal(oblong.Height, readOblong.Height);
        AssertSameCells(oblong.Cells, readOblong.Cells);

        // What the corpus does NOT reach: not one cell in any of the 18 levels sets either
        // display flag, so the packing has no real example to check against and the round trip
        // above proves nothing about it. Said here rather than left implied -- the fixture below
        // pins the convention, it cannot discover it.
        Assert.DoesNotContain(
            levels.Select(ReadFile).SelectMany(l => l.Cells),
            c => c.ShowDistantBackground || c.DistantBackgroundInBands);
    }

    [Fact]
    public void The_background_byte_carries_two_flags_in_its_top_bits()
    {
        // Read from Level.cpp:698: bit 7 is showDistant, bit 6 is inBands, and the index is the
        // low six bits. A writer that emitted the index alone would drop both silently -- and no
        // shipped level sets either, so this fixture is their only coverage.
        var cell = new AreaMapCell(
            Background: 0x3F, ShowDistantBackground: true, DistantBackgroundInBands: true,
            NorthBg: 1, EastBg: 2, SouthBg: 3, WestBg: 4,
            Zone: 5, EventExists: true, Walls: [6, 7, 8, 9], Blockage: [10, 11, 12, 13]);

        var stream = new MemoryStream();
        LevelFileWriter.WriteCell(new MfcArchiveWriter(stream), cell);
        stream.Position = 0;

        Assert.Equal(15, stream.Length);          // every field one byte

        var read = LevelReader.ReadCell(new MfcArchiveReader(stream), LevelFileWriter.WrittenVersion);

        Assert.Equal(cell with { Walls = [], Blockage = [] },
                     read with { Walls = [], Blockage = [] });
        Assert.Equal(cell.Walls, read.Walls);
        Assert.Equal(cell.Blockage, read.Blockage);
    }

    [Fact]
    public void The_event_chain_keeps_its_bodyless_tags_in_place()
    {
        // An unrecognised ordinal is four bytes and no body. Dropping one shortens the chain and
        // every later event's position with it -- so the entries list keeps them where they were.
        var levels = AllLevels();
        if (levels.Count == 0)
        {
            return;
        }

        var withBodyless = levels
            .Select(ReadFile)
            .FirstOrDefault(l => l.Entries.Any(e => e.Body is null));

        if (withBodyless is null)
        {
            // Nothing in the corpus has one; the count identity below is then trivially true and
            // this is worth saying rather than leaving the test looking like it proved something.
            Assert.All(levels.Select(ReadFile),
                       l => Assert.Equal(l.Entries.Count, l.Events.Count));
            return;
        }

        Assert.Equal(withBodyless.EventCount, withBodyless.Entries.Count);
        Assert.True(withBodyless.Events.Count < withBodyless.Entries.Count);
    }

    [Fact]
    public void A_level_file_is_never_compressed()
    {
        // Even in a design whose databases are. The decision is per file kind, and a writer that
        // assumed otherwise would produce something the reference cannot open.
        var levels = AllLevels();
        if (levels.Count == 0)
        {
            return;
        }

        using var fs = File.OpenRead(levels[0]);
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);

        Assert.NotEqual(ArchiveTier.CompressedCar, header.Tier);
    }

    /// <summary>
    /// Compares cells field by field: <c>AreaMapCell</c> holds two <c>byte[]</c>, which a record
    /// compares by reference.
    /// </summary>
    private static void AssertSameCells(IReadOnlyList<AreaMapCell> expected,
                                        IReadOnlyList<AreaMapCell> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i] with { Walls = [], Blockage = [] },
                         actual[i] with { Walls = [], Blockage = [] });
            Assert.Equal(expected[i].Walls, actual[i].Walls);
            Assert.Equal(expected[i].Blockage, actual[i].Blockage);
        }
    }

    private static void AssertSame(LevelFile expected, LevelFile actual, string path)
    {
        string what = Path.GetFileName(path);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Level, actual.Level);
        AssertSameCells(expected.Cells, actual.Cells);
        Assert.Equal(expected.EventCount, actual.EventCount);
        Assert.Equal(expected.Entries.Count, actual.Entries.Count);
        Assert.Equal(expected.Entries.Select(e => e.Type), actual.Entries.Select(e => e.Type));
        Assert.Equal(expected.Attributes, actual.Attributes);
        Assert.Equal(expected.BlockageKeys, actual.BlockageKeys);
        Assert.Equal(expected.WallSets, actual.WallSets);
        Assert.Equal(expected.BackgroundSets, actual.BackgroundSets);

        Assert.Equal(expected.Zones.AreaViewArt, actual.Zones.AreaViewArt);
        Assert.Equal(expected.Zones.Zones.Count, actual.Zones.Zones.Count);
        for (int i = 0; i < expected.Zones.Zones.Count; i++)
        {
            var e = expected.Zones.Zones[i];
            var a = actual.Zones.Zones[i];

            // Zone holds lists, which a record compares by reference.
            Assert.Equal(e with { Sounds = a.Sounds, Attributes = a.Attributes },
                         a with { Sounds = a.Sounds, Attributes = a.Attributes });
            Assert.Equal(e.Attributes, a.Attributes);
            Assert.Equal(e.Sounds.Day, a.Sounds.Day);
            Assert.Equal(e.Sounds.Night, a.Sounds.Night);
            Assert.Equal(e.Sounds.UseNightMusic, a.Sounds.UseNightMusic);
        }

        Assert.Equal(expected.StepEvents.Count, actual.StepEvents.Count);
        for (int i = 0; i < expected.StepEvents.Count; i++)
        {
            Assert.Equal(expected.StepEvents[i] with { Attributes = actual.StepEvents[i].Attributes },
                         actual.StepEvents[i]);
            Assert.Equal(expected.StepEvents[i].Attributes, actual.StepEvents[i].Attributes);
        }

        _ = what;
    }
}
