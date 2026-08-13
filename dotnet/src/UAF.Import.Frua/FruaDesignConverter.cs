using UAF.Common;
using UAF.Serialization;

namespace UAF.Import.Frua;

/// <summary>
/// A whole FRUA design, converted but not yet written.
/// </summary>
/// <param name="Levels">Converted levels, keyed by their one-based number.</param>
/// <param name="Monsters">Creatures that took the monster branch.</param>
/// <param name="Characters">Creatures that took the NPC branch.</param>
/// <param name="Items">The design's item database, empty when it ships none.</param>
/// <param name="AmmoTypes">The ammunition kinds the items between them name.</param>
public sealed record FruaConvertedDesign(
    IReadOnlyDictionary<int, LevelFile> Levels,
    IReadOnlyList<MonsterRecord> Monsters,
    IReadOnlyList<CharacterRecord> Characters,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<string> AmmoTypes);

/// <summary>
/// Converts a whole DOS FRUA design and writes it out as a UAF design directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>An import mutates a design rather than building one</b>, which is why writing takes a
/// template directory as well as an output one. A design directory holds rules databases FRUA has
/// no equivalent for — abilities, baseclasses, classes, races, spells, special abilities — plus
/// the Forth AI script and the sound configuration. Those are copied; only the files an import
/// actually produces are written over.
/// </para>
/// <para>
/// <b>The framing is not the same for every file.</b> All of them open with the eight-byte magic
/// and the version as a <c>double</c>, but what follows depends on the file's kind: a level is a
/// plain payload, and a database is a <b>compressed <c>CAR</c></b>, because
/// <see cref="DesignFileKind.Database"/> puts its compression threshold at 0.930 and an import
/// writes far past that. The tier decides, not the writer — a plain payload behind a database
/// header is fed to the LZW decompressor and read as garbage. The round trips in
/// <c>FruaDesignConverterTests</c> are what establish this rather than an assumption; the
/// database one was written expecting a plain archive and was wrong.
/// </para>
/// </remarks>
public static class FruaDesignConverter
{
    /// <summary>The version every written file is stamped with.</summary>
    public static DesignVersion WrittenVersion => LevelFileWriter.WrittenVersion;

    /// <summary>Converts every part of a design that has a converter.</summary>
    public static FruaConvertedDesign Convert(FruaDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);

        var levels = design.Levels.ToDictionary(
            level => level.Key,
            level => FruaLevelConverter.Convert(level.Value, level.Key, design));

        var monsters = new List<MonsterRecord>();
        var characters = new List<CharacterRecord>();

        // The same two-way branch ImportMonsterToUAF makes, in index order so a design converts
        // the same way twice.
        foreach (var creature in design.Monsters.OrderBy(m => m.Key).Select(m => m.Value))
        {
            if (creature.IsMonster)
            {
                monsters.Add(FruaCharacterConverter.ToMonster(creature, design.Items));
            }
            else
            {
                characters.Add(FruaCharacterConverter.ToCharacter(creature, design.Items));
            }
        }

        var items = new List<ItemRecord>();

        if (design.Items is { } database)
        {
            foreach (var item in database.Items)
            {
                items.Add(FruaItemConverter.Convert(item, database.Classes[item.ClassIndex]));
            }
        }

        // Every distinct kind a converted item names, which is what the database's trailing list
        // holds -- it sits after the records rather than inside them.
        var ammo = items.Select(i => i.Scalars.AmmoType)
                        .Where(a => !string.IsNullOrEmpty(a))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();

        return new FruaConvertedDesign(levels, monsters, characters, items, ammo);
    }

    /// <summary>The subdirectory of a design that holds its data files.</summary>
    public const string DataDirectory = "Data";

    /// <summary>The name a level file is written under.</summary>
    /// <remarks>Three digits, zero-padded — <c>Level001.lvl</c> through <c>Level255.lvl</c>.</remarks>
    public static string LevelFileName(int number) => $"Level{number:D3}.lvl";

    /// <summary>
    /// Writes a converted design into <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="converted">The converted design.</param>
    /// <param name="outputDirectory">Where to write. Created if it does not exist.</param>
    /// <param name="templateDirectory">
    /// An existing design whose rules databases and scripts are copied first. Null writes only the
    /// files the import produces, which is not a design the engine can open — see the class
    /// remarks.
    /// </param>
    /// <returns>The files written, in the order they were written.</returns>
    public static IReadOnlyList<string> Write(FruaConvertedDesign converted,
                                              string outputDirectory,
                                              string? templateDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(converted);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        string data = Path.Combine(outputDirectory, DataDirectory);
        Directory.CreateDirectory(data);

        if (templateDirectory is not null)
        {
            CopyTemplate(templateDirectory, outputDirectory);
        }

        var written = new List<string>();

        foreach (var (number, level) in converted.Levels.OrderBy(l => l.Key))
        {
            string path = Path.Combine(data, LevelFileName(number));

            using var stream = File.Create(path);
            LevelFileWriter.WriteFile(stream, level);
            written.Add(path);
        }

        if (converted.Monsters.Count > 0)
        {
            written.Add(WriteDatabase(
                Path.Combine(data, "monsters.dat"),
                ar => MonsterRecordWriter.WriteDatabase(ar, converted.Monsters)));
        }

        if (converted.Items.Count > 0)
        {
            written.Add(WriteDatabase(
                Path.Combine(data, "items.dat"),
                ar => ItemRecordWriter.WriteDatabase(ar, converted.Items, converted.AmmoTypes)));
        }

        return written;
    }

    /// <summary>
    /// Writes one database file: the magic, the version, then the records.
    /// </summary>
    /// <remarks>
    /// <b>A database is a compressed <c>CAR</c>, not a plain archive — and it is the tier that
    /// makes it one, not a choice.</b> <see cref="DesignFileKind.Database"/> puts the <c>CAR</c>
    /// threshold at 0.697 and the compression threshold at 0.930, and
    /// <see cref="WrittenVersion"/> is far past both, so <see cref="DesignFileHeader.Read"/>
    /// resolves a magic-stamped database to <see cref="ArchiveTier.CompressedCar"/> and will feed
    /// the bytes to the LZW decompressor whatever was actually written. A plain payload behind
    /// that header reads as garbage rather than as a short file.
    /// </remarks>
    private static string WriteDatabase(string path, Action<IArchiveWriteCursor> write)
    {
        using var stream = File.Create(path);

        // The magic and version are on the raw file, ahead of the compressed stream.
        var header = new MfcArchiveWriter(stream);

        Span<byte> magic = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
            magic, DesignFileHeader.Magic);
        header.WriteBytes(magic);
        header.WriteDouble(WrittenVersion.Value);

        using var car = CarArchiveWriter.Open(stream);
        write(ArchiveWriteCursor.For(car));
        return path;
    }

    /// <summary>
    /// Copies a template design, skipping the files an import replaces.
    /// </summary>
    /// <remarks>
    /// <b>The level files are skipped by pattern, not by name.</b> A template with more levels
    /// than the imported design would otherwise leave its own behind, and the party would walk
    /// into somebody else's dungeon.
    /// </remarks>
    private static void CopyTemplate(string templateDirectory, string outputDirectory)
    {
        foreach (string source in Directory.EnumerateFiles(templateDirectory, "*",
                                                           SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(templateDirectory, source);
            string name = Path.GetFileName(source);

            if (name.StartsWith("Level", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Replaced.Contains(name))
            {
                continue;
            }

            string target = Path.Combine(outputDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    /// <summary>The template files an import writes its own version of.</summary>
    private static readonly HashSet<string> Replaced =
        new(StringComparer.OrdinalIgnoreCase) { "monsters.dat", "items.dat" };
}
