using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads <c>game.dat</c> from end to end and requires the stream to be exhausted exactly.
/// </summary>
/// <remarks>
/// <para>
/// <c>GLOBAL_STATS</c> is the largest single record in the format — the design's entire global
/// state. Reaching the end means every structure in it was sized correctly: the prefix, the
/// attribute list, art slots, sound queues, keys, special items, quests, the character list, the
/// level table, currency, difficulty levels, the global event list, the journal, and the fix-up
/// spellbook.
/// </para>
/// <para>
/// A 5.x design stops short by design: <c>LEVEL_STATS</c> gains wall-override and cell-content
/// tables at 5.0 which are not ported. That boundary is asserted rather than left implicit.
/// </para>
/// </remarks>
public class GameDataWholeFileTests
{
    private static string? GameDat(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        string path = Path.Combine(dir!.FullName, "reference", rel, "game.dat");
        return File.Exists(path) ? path : null;
    }

    /// <summary>folder, level count, global event count.</summary>
    public static TheoryData<string, int, int> Designs => new()
    {
        { "Case.dsn/Data", 10, 136 },
        { "SomethingWild.dsn/Data", 8, 27 },
    };

    [Theory]
    [MemberData(nameof(Designs))]
    public void Whole_record_reads_and_exhausts_the_stream(
        string rel, int expectedLevels, int expectedGlobalEvents)
    {
        string? path = GameDat(rel);
        if (path is null) return;

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        var g = GlobalStatsReader.Read(cursor.Body, cursor.Version, ArchiveRole.Editor,
                                      EventWalkTests.TryPublic);

        Assert.Equal(expectedLevels, g.Levels!.Levels.Count);
        Assert.Equal(expectedGlobalEvents, g.GlobalEventCount);

        // The whole-file assertion. Any wrong field width anywhere in this record leaves bytes
        // over or runs off the end.
        Assert.Throws<EndOfStreamException>(() => cursor.Body.ReadByte());
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Level_table_matches_the_level_files_on_disk(
        string rel, int expectedLevels, int expectedGlobalEvents)
    {
        string? path = GameDat(rel);
        if (path is null) return;

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        var g = GlobalStatsReader.Read(cursor.Body, cursor.Version, ArchiveRole.Editor,
                                      EventWalkTests.TryPublic);

        // Two entirely independent sources: a count read from deep inside game.dat, and the number
        // of .lvl files in the folder. Their agreement is a strong cross-check on the level table.
        int filesOnDisk = new DirectoryInfo(Path.GetDirectoryName(path)!).GetFiles("*.lvl").Length;
        Assert.Equal(filesOnDisk, g.Levels!.Levels.Count);

        _ = (expectedLevels, expectedGlobalEvents);
    }

    [Fact]
    public void Currency_decodes_to_the_standard_denominations()
    {
        string? path = GameDat("Case.dsn/Data");
        if (path is null) return;

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        var g = GlobalStatsReader.Read(cursor.Body, cursor.Version, ArchiveRole.Editor,
                                      EventWalkTests.TryPublic);

        // COIN_TYPE names are raw NUL-padded char buffers, not counted strings, and its rate is a
        // double. Getting either wrong yields garbage names -- so real denominations are the proof.
        string[] named = [.. g.Money!.Coins.Where(c => c.Name.Length > 0).Select(c => c.Name)];
        Assert.Equal(["Platinum", "Electrum", "Gold", "Silver", "Copper"], named);

        // Ten slots regardless of how many are used.
        Assert.Equal(MonsterLeafReaders.MaxCoinTypes, g.Money.Coins.Count);
        Assert.All(g.Money.Coins, c => Assert.InRange(c.Rate, 0, 100000));

        // Five difficulty levels, always.
        Assert.Equal(GlobalStatsTailReaders.DifficultyLevels, g.Difficulty!.Levels.Count);
    }

    [Fact]
    public void A_5x_design_stops_at_the_documented_cell_contents_boundary()
    {
        string? path = GameDat("dc-default/data-files");
        if (path is null) return;

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);

        // Rather than reading LEVEL_STATS wrongly, the reader refuses and says why. Asserted so the
        // gap stays visible and cannot be mistaken for coverage.
        var ex = Assert.Throws<NotSupportedException>(
            () => GlobalStatsReader.Read(cursor.Body, cursor.Version, ArchiveRole.Editor,
                                        EventWalkTests.TryPublic));
        Assert.Contains("m_cellContents", ex.Message);
        Assert.True(cursor.Version >= GlobalStatsTailReaders.CellContentsGate);
    }
}
