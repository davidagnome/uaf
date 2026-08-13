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
/// <param name="Global">
/// The design header, present only when a template supplied the parts FRUA has no equivalent for.
/// </param>
/// <param name="Events">The template's global event list, carried through unchanged.</param>
public sealed record FruaConvertedDesign(
    IReadOnlyDictionary<int, LevelFile> Levels,
    IReadOnlyList<MonsterRecord> Monsters,
    IReadOnlyList<CharacterRecord> Characters,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<string> AmmoTypes,
    GlobalStatsPrefix? Global = null,
    IReadOnlyList<(EventType Type, IGameEvent Body)>? Events = null)
{
    /// <summary>The global events, never null.</summary>
    public IReadOnlyList<(EventType Type, IGameEvent Body)> Events { get; init; } = Events ?? [];
}

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

    /// <summary>
    /// Converts a design, taking its header from a template.
    /// </summary>
    /// <param name="design">The FRUA design.</param>
    /// <param name="templateGameData">
    /// The template's <c>game.dat</c>, read in full. <b>Not through
    /// <c>GlobalStatsReader.ReadThroughCharacters</c></b>: that stops before <c>LEVEL_INFO</c>,
    /// and <see cref="GlobalStatsWriter.CanWrite"/> refuses a header without it — so a prefix read
    /// converts fine and then cannot be written.
    /// </param>
    public static FruaConvertedDesign Convert(FruaDesign design, string templateGameData)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(templateGameData);

        var (template, events) = ReadTemplate(templateGameData);
        var start = design.Levels.TryGetValue(design.Game.StartLevel + 1, out var level)
            ? level
            : null;

        return Convert(design) with
        {
            Global = FruaGameDataConverter.Apply(template, design.Game, start),
            Events = events,
        };
    }

    /// <summary>
    /// Reads a template's <c>game.dat</c> whole, including its global event list.
    /// </summary>
    /// <remarks>
    /// The events are carried through rather than converted: they are the template's, and FRUA has
    /// no global event list of its own — its events all belong to a level.
    /// </remarks>
    public static (GlobalStatsPrefix Global,
                   IReadOnlyList<(EventType Type, IGameEvent Body)> Events)
        ReadTemplate(string gameDataPath)
    {
        ArgumentNullException.ThrowIfNull(gameDataPath);

        using var stream = File.OpenRead(gameDataPath);
        var cursor = GameDataReader.Open(stream);

        var events = new List<(EventType, IGameEvent)>();

        var global = GlobalStatsReader.Read(
            cursor.Body, cursor.Version, ArchiveRole.Editor,
            (ar, type, version) =>
            {
                var body = EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor);

                if (body is not null)
                {
                    events.Add((type, body));
                }

                return body;
            });

        return (global, events);
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

        if (converted.Global is { } global)
        {
            written.Add(WriteGameData(Path.Combine(data, "game.dat"), global, converted.Events));
        }

        return written;
    }

    /// <summary>
    /// Writes <c>game.dat</c>: the magic, the version, then the record inside a <c>CAR</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The version appears twice</b>, once on the raw file and once as the record's own first
    /// field — which is not redundancy but how <c>GetDesignVersion</c> works:
    /// <see cref="UnstampedVersionSource.PayloadFirstField"/> says an unstamped <c>game.dat</c>
    /// takes its version from the payload, so the field has to be there whether or not the magic
    /// is. <see cref="GlobalStatsWriter.Write"/> emits the inner one itself.
    /// </para>
    /// <para>
    /// <b>Compressed, which is what real files are — <see cref="DesignFileKind.TierFor"/>
    /// disagrees and is not consulted.</b> Because <see cref="DesignFileKind.GameData"/> has no
    /// compression threshold, <c>TierFor</c> would call a modern <c>game.dat</c>
    /// <see cref="ArchiveTier.UncompressedCar"/> — but every <c>game.dat</c> in the corpus carries
    /// compression type <c>02</c> at offset 16, and <c>GameDataReader.Open</c> reaches for
    /// <see cref="CarArchiveReader"/> whenever the magic is present rather than asking what the
    /// version implies. Nothing branches on the game-data tier at all: the three places that
    /// branch on <see cref="DesignFileHeader.Tier"/> all read databases. So this writes the same
    /// framing real files use, and the classification is inert rather than wrong-and-live.
    /// </para>
    /// </remarks>
    private static string WriteGameData(string path, GlobalStatsPrefix global,
                                        IReadOnlyList<(EventType Type, IGameEvent Body)> events)
    {
        using var stream = File.Create(path);

        WriteMagic(new MfcArchiveWriter(stream));

        using var car = CarArchiveWriter.Open(stream);
        GlobalStatsWriter.Write(ArchiveWriteCursor.For(car), global, events);
        return path;
    }

    /// <summary>The eight-byte sentinel and the version, on the raw file.</summary>
    private static void WriteMagic(MfcArchiveWriter writer)
    {
        Span<byte> magic = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
            magic, DesignFileHeader.Magic);
        writer.WriteBytes(magic);
        writer.WriteDouble(WrittenVersion.Value);
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
        WriteMagic(new MfcArchiveWriter(stream));

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
