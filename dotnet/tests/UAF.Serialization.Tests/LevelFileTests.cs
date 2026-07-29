using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads whole <c>.lvl</c> files — grid, events, zones, step events, wall and background sets,
/// blockage keys — and requires each to land exactly on EOF.
/// </summary>
/// <remarks>
/// <para>
/// This is the strongest assertion available for level files. A level is one long chain: the cell
/// grid sizes itself from the dimensions, the event list from a count, each event from its own
/// layout, and everything after it sits at whatever offset those leave behind. Landing on the final
/// byte means every one of those reads consumed exactly the right number of bytes.
/// </para>
/// <para>
/// Fixtures under <c>reference/</c> are gitignored; those tests return early when absent.
/// </para>
/// </remarks>
public class LevelFileTests
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

    /// <summary>design folder, level count, total events across those levels.</summary>
    public static TheoryData<string, int, int> Designs => new()
    {
        { "src/UAFWinEd/DefaultDesign.dsn/Data", 1, 2 },
        { "reference/Case.dsn/Data", 10, 4244 },
        { "reference/Ambassador's_Letter/Data", 3, 1529 },
        { "reference/SomethingWild.dsn/Data", 8, 461 },
    };

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_level_reads_to_exactly_its_last_byte(
        string rel, int expectedLevels, int expectedEvents)
    {
        var dir = new DirectoryInfo(Path.Combine(RepoRoot().FullName, rel));
        if (!dir.Exists) return;

        int levels = 0, events = 0;
        foreach (var file in dir.GetFiles("*.lvl").OrderBy(f => f.Name))
        {
            levels++;
            using var fs = file.OpenRead();
            var level = LevelFileReader.Read(fs, ArchiveRole.Editor, EventWalkTests.TryPublic);
            events += level.EventCount;

            Assert.Equal(fs.Length, fs.Position);

            // Fixed tables, present in full regardless of how many entries a design uses.
            Assert.Equal(LevelStructureReaders.ZonesPerLevel, level.Zones.Zones.Count);
            Assert.Equal(8, level.BlockageKeys.Count);
        }

        Assert.Equal(expectedLevels, levels);
        Assert.Equal(expectedEvents, events);
    }

    [Fact]
    public void Step_event_table_size_changes_at_1_0210()
    {
        // Below 1.0210 a level writes 8 step-event slots; at and above it, 255. DefaultDesign is
        // 0.915 and the reference designs are 2.53+, so both branches are covered by fixtures.
        string defaultDesign = Path.Combine(RepoRoot().FullName,
            "src/UAFWinEd/DefaultDesign.dsn/Data/Level000.lvl");

        using (var fs = File.OpenRead(defaultDesign))
        {
            var level = LevelFileReader.Read(fs, ArchiveRole.Editor, EventWalkTests.TryPublic);
            Assert.True(level.Version.Value < 1.0210);
            Assert.Equal(LevelStructureReaders.LegacyStepEvents, level.StepEvents.Count);
        }

        string modern = Path.Combine(RepoRoot().FullName, "reference/Case.dsn/Data/Level001.lvl");
        if (!File.Exists(modern)) return;

        using (var fs = File.OpenRead(modern))
        {
            var level = LevelFileReader.Read(fs, ArchiveRole.Editor, EventWalkTests.TryPublic);
            Assert.True(level.Version.Value >= 1.0210);
            Assert.Equal(LevelStructureReaders.MaxStepEvents, level.StepEvents.Count);
        }
    }

    [Fact]
    public void Zones_carry_real_names_and_art()
    {
        string path = Path.Combine(RepoRoot().FullName, "reference/Case.dsn/Data/Level001.lvl");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var level = LevelFileReader.Read(fs, ArchiveRole.Editor, EventWalkTests.TryPublic);

        // Printable content this deep into the file is only reachable if the whole event list
        // before it was consumed correctly.
        Assert.All(level.Zones.Zones, z =>
        {
            Assert.All(z.Name, ch => Assert.InRange(ch, ' ', '~'));
            Assert.All(z.IndoorCombatArt, ch => Assert.InRange(ch, ' ', '~'));
        });

        Assert.Contains(level.Zones.Zones, z => z.Name.Length > 0);
        Assert.All(level.WallSets, w => Assert.All(w.WallFile, ch => Assert.InRange(ch, ' ', '~')));
    }
}
