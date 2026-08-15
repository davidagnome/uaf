using UAF.Serialization;

namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// Locates and names the first byte on which two versions of a design file disagree.
/// </summary>
/// <remarks>
/// <para>
/// A raw offset is only half an answer. Every design file the port writes begins with the same
/// sixteen-byte prologue — eight bytes of <c>0xFABCDEFABCDEFABF</c> and a <c>double</c> version
/// (<c>DesignFileHeader.Read</c>, and the store side at <c>Char.cpp:6994</c>) — so an offset
/// inside it names a field directly, and an offset past it is a payload offset worth quoting as
/// such rather than as a file offset.
/// </para>
/// <para>
/// <b>Past the prologue a database is LZW, so a file offset means nothing.</b> One changed field
/// early in a compressed stream moves every byte after it, and the first differing file offset
/// then points at wherever the two dictionaries first diverged rather than at the field that
/// caused it. <see cref="Decompressed"/> exists for that: it runs the payload back through
/// <see cref="CarLzwDecompressor"/> so the offset is an offset into the record stream, which
/// <see cref="StructuralDiff"/> can then put a name to.
/// </para>
/// </remarks>
public static class ByteDiff
{
    /// <summary>The index of the first byte that differs, or null when the two agree.</summary>
    /// <remarks>
    /// A shorter file that is otherwise a prefix of the longer one differs at its own length —
    /// which is the truthful answer, and the one a naive loop over the shorter length misses.
    /// </remarks>
    public static int? FirstDifference(byte[] left, byte[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        int shared = Math.Min(left.Length, right.Length);
        for (int i = 0; i < shared; i++)
        {
            if (left[i] != right[i])
            {
                return i;
            }
        }

        return left.Length == right.Length ? null : shared;
    }

    /// <summary>
    /// A one-line verdict on two whole design files, naming the field when the difference is in
    /// the prologue.
    /// </summary>
    public static string Describe(byte[] original, byte[] written)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(written);

        if (FirstDifference(original, written) is not { } at)
        {
            return $"identical ({original.Length} bytes)";
        }

        string sizes = $"original {original.Length} bytes, written {written.Length} bytes";

        // The magic: eight bytes, and the only way they differ is a writer that emitted a
        // different sentinel or no sentinel at all.
        if (at < 8)
        {
            return $"differs at byte {at}, inside the 8-byte magic — " +
                   $"original {Hex(original, 0, 8)}, written {Hex(written, 0, 8)} ({sizes})";
        }

        // The version stamp. This is the expected difference for every shipped design: the
        // writers emit their own WrittenVersion, never the version the file was read at.
        if (at < 16 && original.Length >= 16 && written.Length >= 16)
        {
            double before = BitConverter.ToDouble(original, 8);
            double after = BitConverter.ToDouble(written, 8);
            return $"differs at byte {at}, inside the version stamp — the file declares " +
                   $"{before} and the writer stamped {after} ({sizes})";
        }

        return $"differs at byte {at} (payload byte {at - 16}) — " +
               $"original {Hex(original, at, 8)}, written {Hex(written, at, 8)} ({sizes})";
    }

    /// <summary>
    /// The decompressed payload of a <c>CAR</c> design file, so a difference can be located in
    /// record-stream coordinates instead of in the compressed stream.
    /// </summary>
    /// <param name="file">A whole design file, prologue included.</param>
    /// <returns>
    /// The decompressed bytes, or null when the file is not a compressed <c>CAR</c> — level files
    /// never are (<c>Level.cpp:2186</c> leaves <c>ar.Compress(true)</c> commented out) and neither
    /// is a <c>.chr</c>, whose payload is plain MFC despite travelling through a <c>CAR</c>.
    /// </returns>
    /// <remarks>
    /// Draining is the only way to learn the length: LZW carries no size, and the reference stops
    /// on code 8191 or on a short block read rather than reporting an end
    /// (<see cref="CarLzwDecompressor.Ended"/>).
    /// </remarks>
    public static byte[]? Decompressed(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length < 17
            || BitConverter.ToUInt64(file, 0) != DesignFileHeader.Magic
            || file[16] != CarArchiveWriter.CompressType)
        {
            return null;
        }

        var stream = new MemoryStream(file, index: 17, count: file.Length - 17, writable: false);
        var lzw = new CarLzwDecompressor(stream);
        var payload = new MemoryStream();

        const int Chunk = 64 * 1024;
        while (!lzw.Ended)
        {
            byte[] block = lzw.ReadBytes(Chunk);
            payload.Write(block, 0, block.Length);
            if (block.Length < Chunk)
            {
                break;
            }
        }

        return payload.ToArray();
    }

    private static string Hex(byte[] bytes, int from, int count)
    {
        int end = Math.Min(from + count, bytes.Length);
        return from >= bytes.Length
            ? "<past end of file>"
            : Convert.ToHexString(bytes, from, end - from);
    }
}
