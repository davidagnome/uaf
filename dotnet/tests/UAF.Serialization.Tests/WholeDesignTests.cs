using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// End-to-end reads over every available design, exercising the same path <c>uaf-fileprobe</c>
/// takes.
/// </summary>
/// <remarks>
/// <para>
/// Per-type tests all passed while <c>game.dat</c>'s mid-stream compression was mis-modelled: the
/// databases in the same folders read perfectly, and the defect lived in the interaction. These
/// tests read a <b>whole design</b> for that reason — the remaining format surprises are
/// concentrated in combinations, not in any single record type.
/// </para>
/// <para>
/// Fixtures under <c>reference/</c> are gitignored, so each case returns early when absent rather
/// than failing. <c>DefaultDesign</c> is in the repo and always runs.
/// </para>
/// </remarks>
public class WholeDesignTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Every design we can read, with its expected version and name.</summary>
    public static TheoryData<string, double, string> Designs => new()
    {
        { Path.Combine("src", "UAFWinEd", "DefaultDesign.dsn", "Data"), 0.915025, "DefaultDesign" },
        { Path.Combine("reference", "ci-tier3", "Data"), 5.29, "DefaultDesign" },
        { Path.Combine("reference", "Case.dsn", "Data"), 2.53, "Case of Masterpiece" },
        { Path.Combine("reference", "SomethingWild.dsn", "Data"), 3.55, "Something Wild" },
        { Path.Combine("reference", "dc-default", "data-files"), 5.28, "November 19, 2018" },
    };

    private static int ReadDatabaseCount(string dataDir, string file, DesignVersion globalVersion)
    {
        using var fs = File.OpenRead(Path.Combine(dataDir, file));
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database,
                                           DesignFileKind.ItemsFallback(globalVersion));
        if (header.Tier == ArchiveTier.CompressedCar)
        {
            fs.Seek(16, SeekOrigin.Begin);
            return CarArchiveReader.Open(fs).ReadInt32();
        }
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        return new MfcArchiveReader(fs).ReadInt32();
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Design_reads_end_to_end(string relativeDataDir, double version, string designName)
    {
        string dataDir = Path.Combine(RepoRoot(), relativeDataDir);
        if (!File.Exists(Path.Combine(dataDir, "game.dat"))) { return; }   // fixture absent

        // game.dat -- both framings, whichever this design uses.
        DesignVersion designVersion;
        using (var fs = File.OpenRead(Path.Combine(dataDir, "game.dat")))
        {
            var cursor = GameDataReader.Open(fs);
            designVersion = cursor.Version;
            Assert.Equal(version, cursor.Version.Value, precision: 6);
            Assert.Equal(designName, cursor.ReadString());
        }

        // Databases -- tier 2 or tier 3 depending on version, handled transparently.
        foreach (string file in new[] { "items.dat", "monsters.dat", "spells.dat" })
        {
            int count = ReadDatabaseCount(dataDir, file, designVersion);
            Assert.InRange(count, 1, 20_000);
        }

        // Level map -- never compressed, at any version.
        string? level = Directory.EnumerateFiles(dataDir, "Level*.lvl").OrderBy(p => p).FirstOrDefault();
        Assert.NotNull(level);
        using (var fs = File.OpenRead(level!))
        {
            var header = DesignFileHeader.Read(fs, DesignFileKind.LevelData);
            Assert.Equal(ArchiveTier.UncompressedCar, header.Tier);
            fs.Seek(header.PayloadOffset, SeekOrigin.Begin);

            var (w, h, cells) = LevelReader.ReadAreaMap(new MfcArchiveReader(fs), header.Version);
            Assert.InRange(w, 1, 255);
            Assert.InRange(h, 1, 255);
            Assert.Equal(w * h, cells.Length);
            Assert.All(cells, c => Assert.InRange(c.Background, 0, 63));
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Framing_matches_the_version(string relativeDataDir, double version, string designName)
    {
        _ = designName;
        string path = Path.Combine(RepoRoot(), relativeDataDir, "game.dat");
        if (!File.Exists(path)) { return; }

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);

        // The fixtures divide cleanly: only the pre-0.998101 DefaultDesign lacks the magic and so
        // uses the plain framing. Everything the editor has written since is magic-stamped and
        // compresses mid-stream.
        var expected = version < 0.998101
            ? GameDataFraming.Plain
            : GameDataFraming.CompressedMidStream;
        Assert.Equal(expected, cursor.Framing);
    }
}
