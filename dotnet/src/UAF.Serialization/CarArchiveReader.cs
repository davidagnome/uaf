using System.Text;

namespace UAF.Serialization;

/// <summary>
/// Reads a <c>CAR</c> stream in <b>compressed</b> mode (<c>m_compressType != 0</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is a genuinely different encoding from plain <c>CArchive</c>, not just the same bytes
/// with LZW on top. Two things change once <c>Compress(true)</c> has run
/// (<c>class.cpp:11938</c>):
/// </para>
/// <list type="number">
///   <item>Every scalar is pulled through the LZW decoder rather than read directly.</item>
///   <item>Strings are <b>interned</b>. Each is preceded by a <c>uint</c> index:
///     <c>0</c> means "a new string follows" — a <b>4-byte</b> length then the characters —
///     while any other value is a back-reference into the table of strings already seen.
///     MFC's 1-byte counted-string prefix does not appear at all.</item>
/// </list>
/// <para>
/// So <see cref="MfcArchiveReader"/> cannot read a compressed stream even after decompression:
/// it would take the 4-byte length as a 1-byte count and three more fields. That mistake reads
/// as "decompression produced noise" when the decompressor is in fact perfect.
/// </para>
/// <para>
/// The table is <b>1-based</b>: <c>m_nextIndex</c> starts at 1 (<c>class.cpp:11603</c>) and
/// lookups are direct (<c>m_stringArray[index]</c>, <c>class.cpp:11994</c>), so slot 0 is never
/// occupied and is free to act as the sentinel.
/// </para>
/// </remarks>
public sealed class CarArchiveReader
{
    private readonly CarLzwDecompressor _lzw;
    private readonly Encoding _encoding;

    /// <summary>Interned strings, 1-based; slot 0 is the "new string" sentinel and stays unused.</summary>
    private readonly List<string> _stringTable = [string.Empty];

    /// <summary>The compression type byte read from the head of the stream (2 in practice).</summary>
    public byte CompressType { get; }

    private CarArchiveReader(byte compressType, CarLzwDecompressor lzw, Encoding encoding)
    {
        CompressType = compressType;
        _lzw = lzw;
        _encoding = encoding;
    }

    /// <summary>
    /// Opens a compressed <c>CAR</c> stream positioned at the compression-type byte — i.e. just
    /// past a 16-byte magic+version prologue.
    /// </summary>
    /// <remarks>
    /// The type byte is written through <c>CAR</c> while compression is still off
    /// (<c>class.cpp:11670</c>), so it is plain and must be consumed before the LZW layer starts.
    /// </remarks>
    public static CarArchiveReader Open(Stream stream, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        int type = stream.ReadByte();
        if (type < 0)
        {
            throw new EndOfStreamException("stream ended before the CAR compression-type byte");
        }
        return new CarArchiveReader((byte)type, new CarLzwDecompressor(stream),
                                    encoding ?? MfcArchiveReader.DefaultEncoding);
    }

    private byte[] Raw(int count)
    {
        byte[] buffer = _lzw.ReadBytes(count);
        if (buffer.Length != count)
        {
            throw new EndOfStreamException(
                $"compressed stream ended: wanted {count} bytes, got {buffer.Length}");
        }
        return buffer;
    }

    public int ReadInt32() => BitConverter.ToInt32(Raw(4), 0);

    public uint ReadUInt32() => BitConverter.ToUInt32(Raw(4), 0);

    public short ReadInt16() => BitConverter.ToInt16(Raw(2), 0);

    public ushort ReadUInt16() => BitConverter.ToUInt16(Raw(2), 0);

    public byte ReadByte() => Raw(1)[0];

    public double ReadDouble() => BitConverter.ToDouble(Raw(8), 0);

    public float ReadSingle() => BitConverter.ToSingle(Raw(4), 0);

    public byte[] ReadBytes(int count) => Raw(count);

    /// <summary>
    /// Reads an interned string (<c>class.cpp:11938</c>).
    /// </summary>
    public string ReadString()
    {
        uint index = ReadUInt32();
        if (index != 0)
        {
            if (index >= (uint)_stringTable.Count)
            {
                // The C++ throws 0x23 here (class.cpp:11990); a forward reference means the
                // stream is out of sync, not that the table is merely short.
                throw new InvalidDataException(
                    $"CAR string index {index} is beyond the table ({_stringTable.Count} entries)");
            }
            return _stringTable[(int)index];
        }

        int length = ReadInt32();
        if (length > 1_000_000)
        {
            throw new InvalidDataException($"implausible CAR string length {length}");
        }

        string value = length == 0 ? string.Empty : _encoding.GetString(Raw(length));

        // Strings containing an embedded NUL are NOT interned: with compressType > 1 the C++
        // bails out before SetAtGrow when `len != strlen(temp)` (class.cpp:11975). Interning it
        // anyway would shift every later index by one and desynchronise the whole table.
        bool hasEmbeddedNul = length > 0 && value.IndexOf('\0') >= 0;
        if (CompressType <= 1 || !hasEmbeddedNul)
        {
            _stringTable.Add(value);
        }

        return value;
    }

    /// <summary>Number of interned strings so far, excluding the unused slot 0.</summary>
    public int InternedStringCount => _stringTable.Count - 1;
}
