using System.Text.Json;
using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Diffs this port's parsing against the C++ reference implementation's JSON dump.
/// </summary>
/// <remarks>
/// <para>
/// The golden file is produced by <c>UAFWinEd.exe "-config &lt;design&gt;" "-dumpjson &lt;out&gt;"</c>
/// in the Oracle workflow and committed to <c>oracle/golden/</c>. This is the validation
/// backbone described in docs/PORTING-PLAN.md section 8: a field-by-field comparison is the only
/// thing that catches a mis-parse which produces plausible-looking values rather than an error.
/// </para>
/// <para>
/// These tests <b>return early</b> rather than fail when the golden file is absent, so the suite
/// stays green before the first dump lands and on checkouts that do not carry it. xUnit 2.9 has
/// no <c>Assert.Skip</c>, so an early return is the only dependency-free option — which means a
/// missing golden file looks identical to a passing comparison from the test summary alone.
/// The .NET workflow therefore checks for the file separately and emits a warning when it is
/// missing, so the gap cannot go unnoticed. Once
/// <c>oracle/golden/DefaultDesign.json</c> exists, these are the tests that matter most.
/// </para>
/// </remarks>
public class OracleDiffTests
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

    private static string GoldenPath =>
        Path.Combine(RepoRoot(), "oracle", "golden", "DefaultDesign.json");

    private static string DataFile(string name) =>
        Path.Combine(RepoRoot(), "src", "UAFWinEd", "DefaultDesign.dsn", "Data", name);

    /// <summary>Loads the golden dump, or null when it has not been produced yet.</summary>
    private static JsonElement? Golden()
    {
        if (!File.Exists(GoldenPath))
        {
            return null;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(GoldenPath));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Golden_dump_is_usable_ground_truth()
    {
        if (Golden() is not { } root) { return; }   // no golden dump yet -- see class remarks

        var meta = root.GetProperty("_meta");

        // A dump written after a failed load carries default state, not design data.
        Assert.True(meta.GetProperty("ok").GetBoolean(),
            "golden dump has _meta.ok = false and is not ground truth");

        // The editor warns it cannot reliably load [0.998101, 0.9988] (Level.cpp:3340), so a
        // fixture in that window would not settle a C#/C++ disagreement either way.
        double version = meta.GetProperty("designVersion").GetDouble();
        Assert.False(version >= 0.998101 && version <= 0.9988,
            $"golden fixture version {version} is inside the editor's unreliable range");

        // Compared fields must not carry machine-specific absolute paths, or the golden file
        // stops being reproducible off the machine that generated it. Only _meta.diagnostics is
        // allowed to (it is informational and excluded from comparison).
        foreach (var property in meta.EnumerateObject())
        {
            if (property.Name == "diagnostics" || property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            Assert.DoesNotContain(":\\", property.Value.GetString());
        }
    }

    [Fact]
    public void Database_record_counts_agree_with_the_reference()
    {
        if (Golden() is not { } root) { return; }   // no golden dump yet -- see class remarks

        var counts = root.GetProperty("counts");

        foreach (var (file, key) in new[]
                 {
                     ("items.dat", "items"),
                     ("monsters.dat", "monsters"),
                     ("spells.dat", "spells"),
                 })
        {
            using var fs = File.OpenRead(DataFile(file));
            var header = DesignFileHeader.Read(fs, DesignFileKind.Items);
            fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
            int ours = new MfcArchiveReader(fs).ReadInt32();

            Assert.Equal(counts.GetProperty(key).GetInt32(), ours);
        }
    }

    [Fact]
    public void GlobalData_scalars_agree_with_the_reference()
    {
        if (Golden() is not { } root) { return; }   // no golden dump yet -- see class remarks

        var g = root.GetProperty("globalData");

        using var fs = File.OpenRead(DataFile("game.dat"));
        var header = DesignFileHeader.Read(fs, DesignFileKind.GameData);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        double version = ar.ReadDouble();
        string designName = ar.ReadString();
        int startLevel = ar.ReadInt32();
        byte startX = ar.ReadByte();
        byte startY = ar.ReadByte();
        byte startFacing = ar.ReadByte();
        int startTime = ar.ReadInt32();
        int startExp = ar.ReadInt32();
        int startExpType = ar.ReadInt32();
        ar.ReadInt32();                      // retired startEquip slot
        int startPlatinum = ar.ReadInt32();
        int startGem = ar.ReadInt32();
        int startJewelry = ar.ReadInt32();
        int dungeonTimeDelta = ar.ReadInt32();
        int dungeonSearchTimeDelta = ar.ReadInt32();
        int wildernessTimeDelta = ar.ReadInt32();
        int wildernessSearchTimeDelta = ar.ReadInt32();
        int autoDarkenViewport = ar.ReadInt32();
        int autoDarkenAmount = ar.ReadInt32();
        int startDarken = ar.ReadInt32();
        int endDarken = ar.ReadInt32();
        int minPCs = ar.ReadInt32();
        ar.ReadInt32();                      // maxParty_maxPCs (packed; compared via accessors)
        int flags = ar.ReadInt32();

        Assert.Equal(g.GetProperty("version").GetDouble(), version, precision: 10);
        Assert.Equal(g.GetProperty("designName").GetString(), designName);
        Assert.Equal(g.GetProperty("startLevel").GetInt32(), startLevel);
        Assert.Equal(g.GetProperty("startX").GetInt32(), startX);
        Assert.Equal(g.GetProperty("startY").GetInt32(), startY);
        Assert.Equal(g.GetProperty("startFacing").GetInt32(), startFacing);
        Assert.Equal(g.GetProperty("startTime").GetInt32(), startTime);
        Assert.Equal(g.GetProperty("startExp").GetInt32(), startExp);
        Assert.Equal(g.GetProperty("startExpType").GetInt32(), startExpType);
        Assert.Equal(g.GetProperty("startPlatinum").GetInt32(), startPlatinum);
        Assert.Equal(g.GetProperty("startGem").GetInt32(), startGem);
        Assert.Equal(g.GetProperty("startJewelry").GetInt32(), startJewelry);
        Assert.Equal(g.GetProperty("dungeonTimeDelta").GetInt32(), dungeonTimeDelta);
        Assert.Equal(g.GetProperty("dungeonSearchTimeDelta").GetInt32(), dungeonSearchTimeDelta);
        Assert.Equal(g.GetProperty("wildernessTimeDelta").GetInt32(), wildernessTimeDelta);
        Assert.Equal(g.GetProperty("wildernessSearchTimeDelta").GetInt32(), wildernessSearchTimeDelta);
        Assert.Equal(g.GetProperty("startDarken").GetInt32(), startDarken);
        Assert.Equal(g.GetProperty("endDarken").GetInt32(), endDarken);
        Assert.Equal(g.GetProperty("minPCs").GetInt32(), minPCs);
        Assert.Equal(g.GetProperty("flags").GetInt32(), flags);

        // These are the BOOL-typed fields. The dumper emits them as integers precisely because
        // AutoDarkenAmount holds 256; if either side ever coerces to a JSON boolean this fails.
        Assert.Equal(g.GetProperty("autoDarkenViewport").GetInt32(), autoDarkenViewport);
        Assert.Equal(g.GetProperty("autoDarkenAmount").GetInt32(), autoDarkenAmount);
    }

    [Fact]
    public void Art_path_strings_agree_including_the_empty_string_sentinel()
    {
        if (Golden() is not { } root) { return; }   // no golden dump yet -- see class remarks

        var g = root.GetProperty("globalData");

        using var fs = File.OpenRead(DataFile("game.dat"));
        var header = DesignFileHeader.Read(fs, DesignFileKind.GameData);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var ar = new MfcArchiveReader(fs);

        ar.ReadDouble(); ar.ReadString(); ar.ReadInt32();
        ar.Skip(3);
        for (int i = 0; i < 7; i++) ar.ReadInt32();
        for (int i = 0; i < 8; i++) ar.ReadInt32();
        for (int i = 0; i < 3; i++) ar.ReadInt32();

        string mapArt = ArchiveStringConventions.Decode(ar.ReadString());
        ar.Skip(60);                                                   // LOGFONTA blob
        string iconBgArt = ArchiveStringConventions.Decode(ar.ReadString());
        string backgroundArt = ArchiveStringConventions.Decode(ar.ReadString());

        Assert.Equal(g.GetProperty("mapArt").GetString(), mapArt);
        Assert.Equal(g.GetProperty("iconBgArt").GetString(), iconBgArt);

        // BackgroundArt is stored as the "*" sentinel and must decode to empty on both sides.
        Assert.Equal(g.GetProperty("backgroundArt").GetString(), backgroundArt);
        Assert.Equal(string.Empty, backgroundArt);
    }
}
