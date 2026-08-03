using System.Buffers.Binary;
using System.Text;

namespace UAF.Serialization;

/// <summary>
/// Writes the compressed <c>CAR</c> stream <see cref="CarArchiveReader"/> reads
/// (<c>class.cpp:11821</c> onward).
/// </summary>
/// <remarks>
/// <para>
/// <b>The second half of the last unexplored part of the format.</b> With
/// <see cref="CarLzwCompressor"/> beneath it, a compressed design can be produced for the first
/// time — which is what byte-identity with a shipped file and the whole editor phase were waiting
/// on.
/// </para>
/// <para>
/// <b>Compressed <c>CAR</c> is a different encoding, not LZW bolted onto the plain one.</b> Beyond
/// compression it <i>interns strings</i>: each is written as a <c>uint</c> index, where 0 means
/// "new, and here it is" and anything else is a back-reference. That is why a compressed structure
/// cannot be read by seeking to it, and why this writer must see every string in the same order
/// the reader will.
/// </para>
/// </remarks>
public sealed class CarArchiveWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly Encoding _encoding;
    private readonly CarLzwCompressor _lzw;
    private readonly Dictionary<string, uint> _interned = [];
    private uint _nextIndex = 1;                     // slot 0 is the "new string" marker
    private bool _closed;

    /// <summary>
    /// Opens a compressed archive, writing the compression-type byte in the clear.
    /// </summary>
    /// <remarks>
    /// <b>The type byte is written uncompressed and everything after it is not.</b>
    /// <c>CAR::Compress</c> emits it through the archive before switching the flag on
    /// (<c>class.cpp:11670</c>), so a reader consumes one plain byte and then starts decoding.
    /// </remarks>
    public static CarArchiveWriter Open(Stream stream, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.WriteByte(CompressType);
        return new CarArchiveWriter(stream, encoding);
    }

    /// <summary>The only compression type this writes. <c>CAR::Compress</c> always emits 2.</summary>
    /// <remarks>
    /// Every tagged database on disk carries <b>1</b> instead, which no code path here produces —
    /// so those files were written by something else, or by a build that differed. Reading honours
    /// both; writing has only ever produced 2.
    /// </remarks>
    public const byte CompressType = 2;

    private CarArchiveWriter(Stream stream, Encoding? encoding)
    {
        _stream = stream;
        _encoding = encoding ?? MfcArchiveReader.DefaultEncoding;
        _lzw = new CarLzwCompressor(stream);
    }

    public void WriteByte(byte value) => _lzw.WriteByte(value);

    public void WriteBytes(ReadOnlySpan<byte> value) => _lzw.Write(value);

    public void WriteInt16(short value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        _lzw.Write(buffer);
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _lzw.Write(buffer);
    }

    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _lzw.Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _lzw.Write(buffer);
    }

    public void WriteDouble(double value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        _lzw.Write(buffer);
    }

    /// <summary>A 4-byte float. Distinct from <see cref="WriteDouble"/>; both occur in records.</summary>
    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        _lzw.Write(buffer);
    }

    /// <summary>
    /// Writes a collection count.
    /// </summary>
    /// <remarks>
    /// <b>A flat <c>DWORD</c>, not MFC's escaping scheme.</b> <c>CAR::WriteCount</c> delegates to
    /// the underlying archive only when <c>compressType</c> is 0; this writer is always type 2, so
    /// the count is four bytes for every value. Using the plain writer's two-tier form here is the
    /// mistake the reader's own remarks warn about, from the other side.
    /// </remarks>
    public void WriteCount(uint count) => WriteUInt32(count);

    /// <summary>
    /// Writes an interned string (<c>class.cpp:11899</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A string already seen is written as its index alone — four bytes, however long it is. A new
    /// one is written as index 0, then its length, then its bytes, and takes the next slot.
    /// </para>
    /// <para>
    /// <b>A string containing an embedded NUL is written but never interned.</b> The reference
    /// tests <c>GetLength() == strlen()</c> and takes a separate path that skips the table
    /// entirely (<c>class.cpp:11927</c>). Interning it would shift every later index by one and
    /// desynchronise the reader's table against the writer's — and the reader has the matching
    /// exclusion, so the two only agree if both skip it.
    /// </para>
    /// <para>
    /// <b>The length is in bytes, not characters</b>, for the same reason as the plain writer: a
    /// character the codepage cannot encode becomes a single <c>?</c>.
    /// </para>
    /// </remarks>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        byte[] bytes = _encoding.GetBytes(value);
        bool hasEmbeddedNul = Array.IndexOf(bytes, (byte)0) >= 0;

        if (!hasEmbeddedNul && _interned.TryGetValue(value, out uint index))
        {
            WriteUInt32(index);
            return;
        }

        WriteUInt32(0);
        WriteInt32(bytes.Length);
        _lzw.Write(bytes);

        if (!hasEmbeddedNul)
        {
            _interned[value] = _nextIndex++;
        }
    }

    /// <summary>How many strings have been interned, matching the reader's count.</summary>
    public int InternedStringCount => _interned.Count;

    /// <summary>
    /// Ends the stream (<c>CAR::Flush</c>).
    /// </summary>
    /// <remarks>
    /// <b>Required.</b> Without it the final partial block is never written and the terminator
    /// never appears, so the reader stops early on a short read and silently returns whatever it
    /// had — which looks like a truncated design rather than an unflushed writer.
    /// </remarks>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _lzw.Flush();
    }

    public void Dispose() => Close();
}
