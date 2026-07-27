using System.Text.Json;
using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Covers both <c>game.dat</c> framings, including the mid-stream compression switch.
/// </summary>
/// <remarks>
/// The compressed framing is diffed against the oracle's own reading of the same file
/// (<c>reference/ci-tier3</c>, generated and dumped by the same CI run), which is the only
/// check that would have caught the original bug: reading a magic-stamped file as a plain
/// container yields a design name of binary noise, never an exception.
/// </remarks>
public class GameDataReaderTests
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

    [Fact]
    public void Unstamped_game_dat_uses_the_plain_framing()
    {
        string path = Path.Combine(RepoRoot(), "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "game.dat");
        using var fs = File.OpenRead(path);

        var cursor = GameDataReader.Open(fs);

        Assert.Equal(GameDataFraming.Plain, cursor.Framing);
        Assert.Equal(0.915025, cursor.Version.Value, precision: 10);
        Assert.Equal("DefaultDesign", cursor.ReadString());
    }

    [Fact]
    public void Magic_stamped_game_dat_switches_to_compression_mid_stream()
    {
        string dir = Path.Combine(RepoRoot(), "reference", "ci-tier3");
        string path = Path.Combine(dir, "Data", "game.dat");
        string dump = Path.Combine(dir, "Tier3Design.json");
        if (!File.Exists(path) || !File.Exists(dump)) { return; }   // fixture not present

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);

        Assert.Equal(GameDataFraming.CompressedMidStream, cursor.Framing);

        string designName = cursor.ReadString();

        // Diff against the reference's reading of these exact bytes.
        using var doc = JsonDocument.Parse(File.ReadAllText(dump));
        var oracle = doc.RootElement.GetProperty("globalData");
        Assert.Equal(oracle.GetProperty("designName").GetString(), designName);
        Assert.Equal(oracle.GetProperty("version").GetDouble(), cursor.Version.Value, precision: 10);
    }

    [Theory]
    [InlineData("Case.dsn", 2.53, "Case of Masterpiece")]
    [InlineData("SomethingWild.dsn", 3.55, "Something Wild")]
    [InlineData("dc-default/data-files", 5.28, "November 19, 2018")]
    public void Community_designs_read_their_names(string relative, double version, string expected)
    {
        string path = Path.Combine(RepoRoot(), "reference",
                                   Path.Combine(relative.Split('/')), "game.dat");
        if (!File.Exists(path))
        {
            path = Path.Combine(RepoRoot(), "reference", relative.Replace('/', Path.DirectorySeparatorChar),
                                "Data", "game.dat");
            if (!File.Exists(path)) { return; }
        }

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);

        Assert.Equal(GameDataFraming.CompressedMidStream, cursor.Framing);
        Assert.Equal(version, cursor.Version.Value, precision: 6);

        // A real, readable design name is the evidence. The failure this guards against produced
        // strings like "P@\x05\x00\x02" -- printable-ish binary, never an exception.
        Assert.Equal(expected, cursor.ReadString());
    }

    [Fact]
    public void Plain_framing_reads_the_version_from_offset_zero()
    {
        // With no magic the leading 8 bytes serve double duty: container version AND
        // GLOBAL_STATS's first serialized field. The cursor must not consume them twice.
        string path = Path.Combine(RepoRoot(), "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "game.dat");
        using var fs = File.OpenRead(path);

        var cursor = GameDataReader.Open(fs);
        Assert.Equal(8, fs.Position);   // exactly the version consumed, nothing more
        Assert.Equal("DefaultDesign", cursor.ReadString());
    }
}
