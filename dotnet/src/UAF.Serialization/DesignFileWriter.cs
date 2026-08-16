using System.Buffers.Binary;
using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes the prologue a design data file carries, and the compressed payload behind it.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="DesignFileHeader.Read"/>, which had no writer: every record
/// writer in the port emits a <i>payload</i>, and the eight bytes of magic and the version
/// <c>double</c> in front of it were assembled by each caller. There was exactly one in-tree
/// producer (<c>UAF.Import.Frua/FruaDesignConverter.cs</c>) and one in a test harness, which is one
/// too many for something an editor has to get right on every save.
/// </para>
/// <para>
/// <b>The framing is not optional and getting it wrong is silent.</b>
/// <see cref="DesignFileKind.Database"/> puts the compression threshold at 0.930, so any
/// magic-stamped database resolves to <see cref="ArchiveTier.CompressedCar"/> — a plain payload
/// written behind that header decompresses into noise rather than failing to open.
/// </para>
/// <para>
/// <b>The stamp is the writer's own <c>WrittenVersion</c>, never the version the file was read
/// at.</b> The payload always goes out in the modern shape, so a header claiming an older version
/// is the one combination nothing can read. This is why no shipped design comes back
/// byte-identical — see docs/PORTING-PLAN.md section 12.
/// </para>
/// </remarks>
public static class DesignFileWriter
{
    /// <summary>
    /// Writes magic, a version stamp, and a compressed <c>CAR</c> payload to a stream.
    /// </summary>
    /// <param name="stream">Written from its current position.</param>
    /// <param name="stamp">The version to declare — the writer's <c>WrittenVersion</c>.</param>
    /// <param name="payload">Emits the file's records into the compressed archive.</param>
    public static void Write(Stream stream, DesignVersion stamp,
                             Action<IArchiveWriteCursor> payload)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);

        var plain = new MfcArchiveWriter(stream);

        Span<byte> magic = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(magic, DesignFileHeader.Magic);
        plain.WriteBytes(magic);
        plain.WriteDouble(stamp.Value);

        // Disposing the CAR writer is what flushes its string table and the compressed body, so
        // the scope has to close before the caller sees the stream.
        using var car = CarArchiveWriter.Open(stream);
        payload(ArchiveWriteCursor.For(car));
    }

    /// <summary>The same file, as bytes.</summary>
    public static byte[] ToBytes(DesignVersion stamp, Action<IArchiveWriteCursor> payload)
    {
        var output = new MemoryStream();
        Write(output, stamp, payload);
        return output.ToArray();
    }
}
