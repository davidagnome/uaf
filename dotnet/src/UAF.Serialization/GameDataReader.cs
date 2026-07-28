using UAF.Common;

namespace UAF.Serialization;

/// <summary>How a <c>game.dat</c> payload is framed.</summary>
public enum GameDataFraming
{
    /// <summary>
    /// No magic. The leading <c>double</c> is both the container version and
    /// <c>GLOBAL_STATS</c>'s first field, and the whole payload is plain archive primitives.
    /// </summary>
    Plain,

    /// <summary>
    /// Magic present. Compression is enabled <b>mid-stream</b>, after an uncompressed version, and
    /// the version is then re-read from inside the compressed stream.
    /// </summary>
    CompressedMidStream,
}

/// <summary>
/// Opens a <c>game.dat</c> payload, handling both framings.
/// </summary>
/// <remarks>
/// <para>
/// <c>game.dat</c> does <b>not</b> use the container model the databases do. Its prologue is read
/// by the payload reader itself (<c>GLOBAL_STATS::Serialize</c>, <c>GlobalData.cpp:4336</c>),
/// which is why <c>loadDesign</c> never seeks past the magic:
/// </para>
/// <code>
///   car.Serialize((char*)&amp;temp, sizeof(temp));   // GLOBAL_STATS reads the magic ITSELF
///   if (temp == 0xFABCDEFABCDEFABF) {
///       car &gt;&gt; version;                           // uncompressed
///       car.Compress(true);                       // compression starts HERE
///       car &gt;&gt; version;                           // the SAME version again, compressed
///   }
///   else version = (double)temp;                  // no magic: those 8 bytes ARE the version
///   DAS(car, designName);
/// </code>
/// <para>
/// Reading a magic-stamped file as though the prologue were a container header parses the design
/// name as binary noise, with no error — the failure mode that <c>uaf-fileprobe</c> exposed by
/// reading whole designs rather than one type at a time.
/// </para>
/// </remarks>
public static class GameDataReader
{
    /// <summary>
    /// A cursor over <c>game.dat</c>'s payload, positioned immediately before
    /// <c>designName</c> and abstracting over the two framings.
    /// </summary>
    public sealed class Cursor(GameDataFraming framing, DesignVersion version,
                               CarArchiveReader? car, MfcArchiveReader? plain)
    {
        public GameDataFraming Framing { get; } = framing;

        /// <summary>The version as read from the payload.</summary>
        public DesignVersion Version { get; } = version;

        /// <summary>
        /// The underlying archive as raw primitives, for readers that need to apply the
        /// <c>DAS</c> convention selectively rather than to every string.
        /// </summary>
        /// <remarks>
        /// Deliberately a separate surface from this class's own <c>Read*</c> methods: those
        /// decode every string, which is right for the fields immediately after the prologue but
        /// wrong in general — <c>A_CStringPAIR_L</c>, for one, reads strings verbatim.
        /// </remarks>
        public IArchiveCursor Body { get; } =
            car is not null ? ArchiveCursor.For(car) : ArchiveCursor.For(plain!);

        public int ReadInt32() => car?.ReadInt32() ?? plain!.ReadInt32();

        public byte ReadByte() => car?.ReadByte() ?? plain!.ReadByte();

        public double ReadDouble() => car?.ReadDouble() ?? plain!.ReadDouble();

        /// <summary>Reads a string, applying the <c>ArchiveBlank</c> sentinel convention.</summary>
        public string ReadString() =>
            ArchiveStringConventions.Decode(car?.ReadString() ?? plain!.ReadString());
    }

    /// <summary>
    /// Opens the payload and consumes the version prologue, leaving the cursor at
    /// <c>designName</c>.
    /// </summary>
    public static Cursor Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Seek(0, SeekOrigin.Begin);

        var probe = new MfcArchiveReader(stream);
        ulong magic = probe.ReadUInt64();

        if (magic != DesignFileHeader.Magic)
        {
            // Those same 8 bytes are the version; rewind so the payload reader sees them.
            stream.Seek(0, SeekOrigin.Begin);
            var plain = new MfcArchiveReader(stream);
            return new Cursor(GameDataFraming.Plain, new DesignVersion(plain.ReadDouble()),
                              null, plain);
        }

        // Magic present: an uncompressed version, then the compression-type byte, then the same
        // version again from inside the LZW stream.
        probe.ReadDouble();                                  // uncompressed version (discarded)
        var car = CarArchiveReader.Open(stream);             // consumes the compressType byte
        return new Cursor(GameDataFraming.CompressedMidStream,
                          new DesignVersion(car.ReadDouble()), car, null);
    }
}
