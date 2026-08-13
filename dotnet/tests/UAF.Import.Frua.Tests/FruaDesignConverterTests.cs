using UAF.Import.Frua;
using UAF.Serialization;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Converting a whole FRUA design and writing it out as a UAF design directory.
/// </summary>
public class FruaDesignConverterTests : IDisposable
{
    private readonly string scratch =
        Path.Combine(Path.GetTempPath(), "uaf-frua-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static DirectoryInfo? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        return dir;
    }

    private static string? Design(params string[] parts)
    {
        if (Root() is not { } root)
        {
            return null;
        }

        string path = Path.Combine([root.FullName, "reference", .. parts]);
        return Directory.Exists(path) ? path : null;
    }

    private static string? Heirs() =>
        Design("Unlimited Adventures -ENG", "DESIGNS", "UA", "HEIRS.DSN");

    /// <summary>Runelord is the only fixture with an item database of its own.</summary>
    private static string? Runelord() => Design("RUNELORD.DSN");

    [Fact]
    public void A_whole_design_converts()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var converted = FruaDesignConverter.Convert(FruaDesign.Open(path));

        Assert.NotEmpty(converted.Levels);
        Assert.All(converted.Levels.Values, level => Assert.True(level.Width > 0));

        // Heirs ships four creatures: three monsters and one NPC.
        Assert.Equal(3, converted.Monsters.Count);
        Assert.Single(converted.Characters);

        // And no item database, so nothing to convert from it.
        Assert.Empty(converted.Items);
    }

    /// <summary>A design with items converts them, and names its ammunition kinds.</summary>
    [Fact]
    public void A_design_with_items_converts_them()
    {
        if (Runelord() is not { } path)
        {
            return;
        }

        var converted = FruaDesignConverter.Convert(
            FruaDesign.Open(path, Path.GetDirectoryName(path)));

        Assert.NotEmpty(converted.Items);

        // The ammunition list is derived from the items, not read -- it is the distinct set of
        // kinds they name, and a design with bows has at least one.
        Assert.All(converted.AmmoTypes, a => Assert.False(string.IsNullOrEmpty(a)));
        Assert.Equal(converted.AmmoTypes.Distinct().Count(), converted.AmmoTypes.Count);
    }

    /// <summary>
    /// A written design's levels read back through the port's own reader.
    /// </summary>
    /// <remarks>
    /// <b>This is the framing check.</b> Every file is the eight-byte magic, the version as a
    /// <c>double</c>, then the payload; nothing else verifies that the writer and
    /// <see cref="DesignFileHeader"/> agree about it.
    /// </remarks>
    [Fact]
    public void A_written_designs_levels_read_back()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var converted = FruaDesignConverter.Convert(FruaDesign.Open(path));
        var written = FruaDesignConverter.Write(converted, scratch);

        Assert.NotEmpty(written);
        Assert.All(written, f => Assert.True(File.Exists(f), f));

        foreach (var (number, level) in converted.Levels)
        {
            string file = Path.Combine(scratch, FruaDesignConverter.DataDirectory,
                                       FruaDesignConverter.LevelFileName(number));

            Assert.True(File.Exists(file), file);

            using var stream = File.OpenRead(file);
            var reread = LevelFileReader.Read(
                stream, ArchiveRole.Editor,
                (ar, type, ver) => EventBodyReader.TryRead(ar, type, ver, ArchiveRole.Editor));

            Assert.Equal(level.Width, reread.Width);
            Assert.Equal(level.Height, reread.Height);
            Assert.Equal(level.EventCount, reread.EventCount);
            Assert.Equal(level.Cells.Count, reread.Cells.Count);
        }
    }

    /// <summary>
    /// A written <c>monsters.dat</c> reads back through the port's own reader.
    /// </summary>
    /// <remarks>
    /// <b>A database is a compressed <c>CAR</c>, and the tier is what decides that rather than the
    /// writer.</b> <c>DesignFileKind.Database</c> puts the <c>CAR</c> threshold at 0.697 and the
    /// compression threshold at 0.930, both far below what an import writes, so the reader feeds
    /// the payload to the LZW decompressor whatever was put there — a plain payload behind this
    /// header reads as garbage rather than as a short file. This is the check that settled it.
    /// </remarks>
    [Fact]
    public void A_written_monster_database_reads_back()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var converted = FruaDesignConverter.Convert(FruaDesign.Open(path));
        FruaDesignConverter.Write(converted, scratch);

        string file = Path.Combine(scratch, FruaDesignConverter.DataDirectory, "monsters.dat");
        Assert.True(File.Exists(file), file);

        using var stream = File.OpenRead(file);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database);

        Assert.True(header.HadMagic);
        Assert.Equal(FruaDesignConverter.WrittenVersion.Value, header.Version.Value);
        Assert.Equal(ArchiveTier.CompressedCar, header.Tier);

        // The compressed stream starts after the magic and the version, at 16.
        stream.Seek(16, SeekOrigin.Begin);
        var reread = MonsterRecordReader.ReadDatabase(
            CarArchiveReader.Open(stream), header.Version, ArchiveRole.Editor);

        Assert.Equal(converted.Monsters.Count, reread.Count);
        Assert.Equal(converted.Monsters.Select(m => m.Name), reread.Select(m => m.Name));
        Assert.Equal(converted.Monsters.Select(m => m.ArmorClass),
                     reread.Select(m => m.ArmorClass));
    }

    /// <summary>
    /// A template's rules databases are copied, and its levels are not.
    /// </summary>
    /// <remarks>
    /// <b>Level files are skipped by pattern.</b> A template with more levels than the imported
    /// design would otherwise leave its own behind for the party to walk into.
    /// </remarks>
    [Fact]
    public void A_template_supplies_what_frua_has_no_equivalent_for()
    {
        if (Heirs() is not { } path || Design("Case.dsn") is not { } template)
        {
            return;
        }

        var converted = FruaDesignConverter.Convert(FruaDesign.Open(path));
        FruaDesignConverter.Write(converted, scratch, template);

        string data = Path.Combine(scratch, FruaDesignConverter.DataDirectory);

        // The rules FRUA has no equivalent for come from the template.
        foreach (string rules in new[] { "spells.dat", "classes.dat", "races.dat" })
        {
            string file = Path.Combine(data, rules);

            if (File.Exists(Path.Combine(template, "Data", rules)))
            {
                Assert.True(File.Exists(file), $"{rules} was not copied");
            }
        }

        // The template's own levels are not.
        var levels = Directory.EnumerateFiles(data, "Level*.lvl")
                              .Select(Path.GetFileName)
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (int number in converted.Levels.Keys)
        {
            Assert.Contains(FruaDesignConverter.LevelFileName(number), levels);
        }

        Assert.Equal(converted.Levels.Count, levels.Count);
    }

    /// <summary>Converting the same design twice gives the same thing.</summary>
    /// <remarks>
    /// Dictionary ordering is not, so the creature branch sorts by index before converting; this
    /// is what would catch that slipping.
    /// </remarks>
    [Fact]
    public void Conversion_is_repeatable()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var design = FruaDesign.Open(path);
        var first = FruaDesignConverter.Convert(design);
        var second = FruaDesignConverter.Convert(design);

        Assert.Equal(first.Monsters.Select(m => m.Name), second.Monsters.Select(m => m.Name));
        Assert.Equal(first.Characters.Select(c => c.Name), second.Characters.Select(c => c.Name));
        Assert.Equal(first.Levels.Keys.Order(), second.Levels.Keys.Order());
    }


    /// <summary>
    /// A written <c>game.dat</c> carries the FRUA design's identity and reads back.
    /// </summary>
    /// <remarks>
    /// <b>The template has to be read in full, not as a prefix.</b>
    /// <c>GlobalStatsReader.ReadThroughCharacters</c> stops before <c>LEVEL_INFO</c> and
    /// <c>GlobalStatsWriter.CanWrite</c> refuses a header without it — so a prefix read converts
    /// fine and then cannot be written, which is what blocked this until now.
    /// </remarks>
    [Fact]
    public void A_written_game_data_carries_the_frua_header()
    {
        if (Heirs() is not { } path || Design("Case.dsn") is not { } template)
        {
            return;
        }

        string templateGameData = Path.Combine(template, "Data", "game.dat");

        if (!File.Exists(templateGameData))
        {
            return;
        }

        var design = FruaDesign.Open(path);
        var converted = FruaDesignConverter.Convert(design, templateGameData);

        Assert.NotNull(converted.Global);
        Assert.Equal(design.Game.DesignName, converted.Global.DesignName);

        FruaDesignConverter.Write(converted, scratch, template);

        string file = Path.Combine(scratch, FruaDesignConverter.DataDirectory, "game.dat");
        Assert.True(File.Exists(file), file);

        using var stream = File.OpenRead(file);
        var cursor = GameDataReader.Open(stream);
        var reread = GlobalStatsReader.Read(
            cursor.Body, cursor.Version, ArchiveRole.Editor,
            (ar, type, version) => EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor));

        // The design's own identity came from FRUA.
        Assert.Equal(design.Game.DesignName, reread.DesignName);
        Assert.Equal((int)design.Game.StartExperience, reread.StartExp);
        Assert.Equal((int)design.Game.StartPlatinum, reread.StartPlatinum);

        // And the tables FRUA has no equivalent for came from the template.
        Assert.NotNull(reread.Money);
        Assert.NotNull(reread.Difficulty);
        Assert.NotNull(reread.Levels);
        // Heirs: "Heirs to skull crag", 50,000 experience, 100 platinum -- the same values
        // FruaGameDataConverterTests reads straight out of game001.dat.
        Assert.Equal("Heirs to skull crag", reread.DesignName);
        Assert.Equal(50_000, reread.StartExp);
        Assert.Equal(100, reread.StartPlatinum);
    }

    /// <summary>
    /// Without a template there is no header to write, and the rest still writes.
    /// </summary>
    /// <remarks>
    /// A design directory with no <c>game.dat</c> is not one the engine can open — this pins that
    /// the converter says so by omission rather than by writing an unusable header.
    /// </remarks>
    [Fact]
    public void Without_a_template_no_game_data_is_written()
    {
        if (Heirs() is not { } path)
        {
            return;
        }

        var converted = FruaDesignConverter.Convert(FruaDesign.Open(path));

        Assert.Null(converted.Global);
        Assert.Empty(converted.Events);

        var written = FruaDesignConverter.Write(converted, scratch);

        Assert.NotEmpty(written);
        Assert.DoesNotContain(written, f => Path.GetFileName(f) == "game.dat");
    }
}
