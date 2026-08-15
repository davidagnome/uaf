using System.Buffers.Binary;
using UAF.Common;
using UAF.Serialization;

namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// One global event, as a record rather than the tuple the reader and writer trade in.
/// </summary>
/// <remarks>
/// <c>GlobalStatsReader</c> hands events to a callback and <c>GlobalStatsWriter</c> takes them
/// back as <c>(EventType, IGameEvent)</c>. A <c>ValueTuple</c> has fields and no properties, so
/// <see cref="StructuralDiff"/> would find nothing to walk into and fall back on reference
/// equality — which is never true for two separately-read events. Wrapping the pair in a record
/// is what lets the diff reach the event body.
/// </remarks>
public sealed record GlobalEvent(EventType Type, IGameEvent Body);

/// <summary>The whole of <c>game.dat</c>: the record, and the global event list beside it.</summary>
public sealed record GameDataModel(GlobalStatsPrefix Global, IReadOnlyList<GlobalEvent> Events);

/// <summary>
/// A design file the port can both read and write, as a pair of functions over a stream.
/// </summary>
/// <param name="Read">Decodes a whole file — prologue included — into the port's model.</param>
/// <param name="Write">Encodes that model back into a whole file, prologue included.</param>
/// <remarks>
/// Split this way so the same codec drives all three checks: writing what was read
/// (byte identity), reading what was written and writing it again (the fixpoint), and comparing
/// the two decoded models (what actually survived).
/// </remarks>
public sealed record DesignFileCodec(string Name, Func<Stream, object> Read, Func<object, byte[]> Write);

/// <summary>
/// Reads and writes whole design files, prologue and all.
/// </summary>
/// <remarks>
/// <para>
/// <b>No writer in the port emits a database's file prologue, so this has to.</b>
/// <c>ItemRecordWriter.WriteDatabase</c> and its two siblings write a payload;
/// <c>DesignFileHeader</c> has a <c>Read</c> and no <c>Write</c>. The framing assembled here is
/// the one the only in-tree producer uses (<c>UAF.Import.Frua/FruaDesignConverter.cs:290</c>):
/// eight bytes of magic and a <c>double</c> version on the raw file, then the compressed
/// <c>CAR</c>. It is not optional — <c>DesignFileKind.Database</c> puts the compression threshold
/// at 0.930, so any magic-stamped database resolves to
/// <see cref="ArchiveTier.CompressedCar"/> and a plain payload behind that header decompresses
/// into noise.
/// </para>
/// <para>
/// <b>Every file is read as <see cref="ArchiveRole.Editor"/>.</b> The two roles differ only in
/// what they will accept below 0.998101, and the editor is the legacy-capable one — the engine
/// refuses such a design outright (<c>Level.cpp:3365</c>). An editor harness that read as
/// <see cref="ArchiveRole.Engine"/> could not open the one design that is committed to the
/// repository.
/// </para>
/// </remarks>
public static class DesignFiles
{
    /// <summary>
    /// The version <c>game.dat</c> declares, which the databases need when they carry no magic of
    /// their own — an unstamped database is <c>min(globalData.version, 0.696)</c>
    /// (<c>Items.cpp:3405</c>).
    /// </summary>
    public static DesignVersion GlobalVersion(CorpusDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);

        using var stream = File.OpenRead(Path.Combine(design.DataDirectory, "game.dat"));
        return GameDataReader.Open(stream).Version;
    }

    /// <summary>
    /// The codec for one file, or null when the port has no writer for that kind.
    /// </summary>
    public static DesignFileCodec? CodecFor(string path, DesignVersion globalVersion)
    {
        ArgumentNullException.ThrowIfNull(path);

        string name = Path.GetFileName(path);

        if (name.Equals("game.dat", StringComparison.OrdinalIgnoreCase))
        {
            return new DesignFileCodec(name, ReadGameData, WriteGameData);
        }

        if (name.Equals("items.dat", StringComparison.OrdinalIgnoreCase))
        {
            return new DesignFileCodec(
                name,
                s => ReadDatabase(s, globalVersion, ItemRecordReader.ReadDatabase),
                m => WholeFile(ItemRecordWriter.WrittenVersion, ar =>
                {
                    var db = (ItemDatabase)m;
                    ItemRecordWriter.WriteDatabase(ar, db.Items, db.AmmoTypes);
                }));
        }

        if (name.Equals("monsters.dat", StringComparison.OrdinalIgnoreCase))
        {
            return new DesignFileCodec(
                name,
                s => ReadDatabase(s, globalVersion, MonsterRecordReader.ReadDatabase),
                m => WholeFile(MonsterRecordWriter.WrittenVersion,
                               ar => MonsterRecordWriter.WriteDatabase(ar, (List<MonsterRecord>)m)));
        }

        if (name.Equals("spells.dat", StringComparison.OrdinalIgnoreCase))
        {
            return new DesignFileCodec(
                name,
                s => ReadDatabase(s, globalVersion, SpellRecordReader.ReadDatabase),
                m => WholeFile(SpellRecordWriter.WrittenVersion,
                               ar => SpellRecordWriter.WriteDatabase(ar, (List<SpellRecord>)m)));
        }

        if (name.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase))
        {
            return new DesignFileCodec(name, ReadLevel, WriteLevel);
        }

        if (IsCharacterFile(name))
        {
            return UnsupportedReason(path) is null
                ? new DesignFileCodec(name, ReadCharacter, WriteCharacter)
                : null;
        }

        return null;
    }

    /// <summary>
    /// Why a file the corpus turned up has no codec, or null when it has one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There are two character formats and the port only has one of them.</b>
    /// <c>SomethingWild</c> ships <c>Data/Uril Kabo.CHAR</c>, which opens
    /// <c>{"charVersion":"-2147483647"</c> — it is <i>JSON</i>, written by
    /// <c>CHARACTER::Export(JWriter&amp;)</c> (<c>Shared/Char.cpp:3128</c>) and read by
    /// <c>CHARACTER::Import(JReader&amp;)</c> (<c>:3303</c>). The port has neither.
    /// </para>
    /// <para>
    /// This is reported rather than skipped because the two failure modes look identical from
    /// outside and are not the same thing. <see cref="CharacterFileReader"/> rejects the file with
    /// "declares version 0.563, below the 0.93 floor" — the branch its own remarks call reachable
    /// only for a file no build can load — which reads like a broken file. It is not broken: it is
    /// a format the reference reads happily and the port cannot see. A design containing one
    /// cannot be saved without losing it.
    /// </para>
    /// </remarks>
    public static string? UnsupportedReason(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!IsCharacterFile(Path.GetFileName(path)))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        Span<byte> prologue = stackalloc byte[8];
        if (stream.ReadAtLeast(prologue, 8, throwOnEndOfStream: false) < 8
            || BinaryPrimitives.ReadUInt64LittleEndian(prologue) != CharacterFileReader.Magic)
        {
            return "a JSON character file (CHARACTER::Export, Shared/Char.cpp:3128), not the " +
                   "binary format -- the port has no reader or writer for it";
        }

        return null;
    }

    private static bool IsCharacterFile(string name) =>
        name.EndsWith(".chr", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".CHAR", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a file from disk through its codec.</summary>
    public static object ReadFile(DesignFileCodec codec, string path)
    {
        ArgumentNullException.ThrowIfNull(codec);

        using var stream = File.OpenRead(path);
        return codec.Read(stream);
    }

    /// <summary>Reads a file already in memory through its codec.</summary>
    public static object ReadBytes(DesignFileCodec codec, byte[] file)
    {
        ArgumentNullException.ThrowIfNull(codec);

        using var stream = new MemoryStream(file, writable: false);
        return codec.Read(stream);
    }

    // -- game.dat ----------------------------------------------------------------------------

    private static object ReadGameData(Stream source)
    {
        var cursor = GameDataReader.Open(source);

        // The reader reports the events but hands the bodies to a callback rather than keeping
        // them, so they are collected on the way past — the writer needs them back.
        var events = new List<GlobalEvent>();
        var global = GlobalStatsReader.Read(
            cursor.Body, cursor.Version, ArchiveRole.Editor,
            (ar, type, version) =>
            {
                var body = EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor);
                if (body is not null)
                {
                    events.Add(new GlobalEvent(type, body));
                }

                return body;
            });

        return new GameDataModel(global, events);
    }

    private static byte[] WriteGameData(object model)
    {
        var game = (GameDataModel)model;
        return WholeFile(
            GlobalStatsWriter.WrittenVersion,
            ar => GlobalStatsWriter.Write(
                ar, game.Global,
                [.. game.Events.Select(e => (e.Type, e.Body))]));
    }

    // -- databases ---------------------------------------------------------------------------

    private static object ReadDatabase<T>(
        Stream source, DesignVersion globalVersion,
        Func<IArchiveCursor, DesignVersion, ArchiveRole, T> read)
        where T : notnull
    {
        var header = DesignFileHeader.Read(source, DesignFileKind.Database,
                                           DesignFileKind.ItemsFallback(globalVersion));
        source.Seek(header.PayloadOffset, SeekOrigin.Begin);

        var cursor = header.Tier == ArchiveTier.CompressedCar
            ? ArchiveCursor.For(CarArchiveReader.Open(source))
            : ArchiveCursor.For(new MfcArchiveReader(source));

        return read(cursor, header.Version, ArchiveRole.Editor);
    }

    // -- levels ------------------------------------------------------------------------------

    private static object ReadLevel(Stream source) =>
        LevelFileReader.Read(
            source, ArchiveRole.Editor,
            (ar, type, version) => EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor));

    private static byte[] WriteLevel(object model)
    {
        var output = new MemoryStream();
        LevelFileWriter.WriteFile(output, (LevelFile)model);
        return output.ToArray();
    }

    // -- characters --------------------------------------------------------------------------

    private static object ReadCharacter(Stream source) =>
        CharacterFileReader.Read(source, ArchiveRole.Editor);

    private static byte[] WriteCharacter(object model)
    {
        var output = new MemoryStream();
        CharacterFileWriter.Write(output, (CharacterFile)model);
        return output.ToArray();
    }

    // -- framing -----------------------------------------------------------------------------

    /// <summary>
    /// The magic, a version stamp, then a compressed <c>CAR</c> payload — the framing every
    /// database and <c>game.dat</c> use on disk.
    /// </summary>
    /// <remarks>
    /// The stamp is the writer's own <c>WrittenVersion</c>, never the version the file was read
    /// at. That is not a shortcut: the payload always goes out in the modern shape, so a header
    /// claiming an older version is the one combination nothing can read — the point
    /// <c>CharacterFileWriter</c> makes at length, and the reason no shipped design can come back
    /// byte-identical.
    /// </remarks>
    private static byte[] WholeFile(DesignVersion stamp, Action<IArchiveWriteCursor> write)
    {
        var output = new MemoryStream();
        var plain = new MfcArchiveWriter(output);

        Span<byte> magic = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(magic, DesignFileHeader.Magic);
        plain.WriteBytes(magic);
        plain.WriteDouble(stamp.Value);

        using (var car = CarArchiveWriter.Open(output))
        {
            write(ArchiveWriteCursor.For(car));
        }

        return output.ToArray();
    }
}
