using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks a level file as far as its event list, and cross-checks the declared event count against
/// the ASL markers actually present in the bytes.
/// </summary>
/// <remarks>
/// <para>
/// <c>eventData</c> comes before <c>zoneData</c> in <c>LEVEL::Serialize</c> (<c>Level.cpp:1224</c>),
/// so reaching the event list needs only the cell grid — no zones, walls or step events. That makes
/// this reachable well before a full <c>LEVEL</c> reader exists.
/// </para>
/// <para>
/// The check is worth having because the two numbers come from completely independent places: one
/// is a count read through the cell grid, the other is a substring tally over the raw file. They
/// can only agree if the header, the dimensions and every cell were read at the right width.
/// </para>
/// </remarks>
public class LevelEventListTests
{
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

    /// <summary>path, grid width, grid height, event count.</summary>
    public static TheoryData<string, int, int, int> Levels => new()
    {
        { "src/UAFWinEd/DefaultDesign.dsn/Data/Level000.lvl", 10, 10, 2 },
        { "reference/Case.dsn/Data/Level001.lvl", 21, 21, 575 },
    };

    [Theory]
    [MemberData(nameof(Levels))]
    public void Event_count_agrees_with_the_markers_in_the_raw_bytes(
        string rel, int width, int height, int expectedEvents)
    {
        string path = Path.Combine(RepoRoot().FullName, rel);
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        var ar = new MfcArchiveReader(fs);

        var (w, h) = LevelReader.ReadDimensions(ar);
        Assert.Equal(width, w);
        Assert.Equal(height, h);

        long gridStart = fs.Position;
        for (int i = 0; i < w * h; i++)
        {
            LevelReader.ReadCell(ar, header.Version);
        }

        // 15 bytes per cell, exactly. Any other width and the event count below would be noise.
        Assert.Equal(15, (fs.Position - gridStart) / (w * h));
        Assert.Equal(0, (fs.Position - gridStart) % (w * h));

        ar.ReadInt32();                                  // m_level
        int eventCount = ar.ReadInt32();

        Assert.Equal(expectedEvents, eventCount);

        // The independent tally: every event writes one EVENT_DATA_ATTR marker.
        Assert.Equal(eventCount, CountMarker(path, AslMaps.EventData));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void First_event_ordinal_is_a_type_the_dispatch_table_knows(
        string rel, int width, int height, int expectedEvents)
    {
        string path = Path.Combine(RepoRoot().FullName, rel);
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        var ar = new MfcArchiveReader(fs);

        var (w, h) = LevelReader.ReadDimensions(ar);
        for (int i = 0; i < w * h; i++) LevelReader.ReadCell(ar, header.Version);
        ar.ReadInt32();
        ar.ReadInt32();

        // Only the first ordinal is reachable: advancing past event 0 needs its subclass fields.
        var first = (EventType)ar.ReadInt32();

        Assert.True(Enum.IsDefined(first), $"ordinal {(int)first} is not a known event type");
        Assert.False(EventDispatch.ReadsNothing(first),
                     $"{first} would read nothing, which no real level should open with");

        _ = (width, height, expectedEvents);
    }

    [Fact]
    public void Level_files_are_not_compressed_even_in_compressed_designs()
    {
        // Case.dsn is 2.53 -- well past the 0.930 gate that compresses its databases -- yet its
        // level files read as plain. So the compression decision is per-file-kind, not per-design,
        // and a reader must not infer one from the other.
        string path = Path.Combine(RepoRoot().FullName, "reference/Case.dsn/Data/Level001.lvl");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
        Assert.Equal(2.53, header.Version.Value, 6);

        // Reading the grid plainly yields sane dimensions; against a compressed stream it would not.
        var (w, h) = LevelReader.ReadDimensions(new MfcArchiveReader(fs));
        Assert.InRange(w, 1, 128);
        Assert.InRange(h, 1, 128);
    }

    private static int CountMarker(string path, string marker)
    {
        byte[] data = File.ReadAllBytes(path);
        byte[] needle = System.Text.Encoding.ASCII.GetBytes(marker);
        int count = 0;
        for (int i = 0; i + needle.Length <= data.Length; i++)
        {
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle)) count++;
        }
        return count;
    }
}
