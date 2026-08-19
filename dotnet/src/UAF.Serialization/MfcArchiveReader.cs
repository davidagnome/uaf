using System.Buffers.Binary;
using System.Text;

namespace UAF.Serialization;

/// <summary>
/// Reads the primitive encoding that MFC's <c>CArchive</c> produces.
/// </summary>
/// <remarks>
/// <para>
/// This is the bottom layer of the format. Above it sit the container prologue (magic + version,
/// or none) and, for designs at or after 0.573, the <c>CAR</c> LZW/string-interning wrapper.
/// Designs older than that are read with these primitives directly — see
/// <c>Level.cpp:2168</c> and docs/PORTING-PLAN.md section 3.2.
/// </para>
/// <para>
/// Everything is little-endian. Strings are length-prefixed using MFC's
/// <c>AfxWriteStringLength</c> scheme and carry <b>single-byte</b> characters: the legacy projects
/// build with <c>CharacterSet=MultiByte</c>, so <c>CString</c> is <c>CStringA</c>. The encoding is
/// therefore a Windows codepage, never UTF-8 — decoding with UTF-8 silently corrupts any design
/// containing non-ASCII text.
/// </para>
/// </remarks>
public sealed class MfcArchiveReader
{
    private readonly Stream _stream;
    private readonly Encoding _encoding;

    /// <summary>
    /// The default codepage for legacy design files: Windows-1252. Registering
    /// <see cref="CodePagesEncodingProvider"/> is done once in the static constructor, since
    /// .NET Core does not ship non-UTF encodings by default.
    /// </summary>
    public static Encoding DefaultEncoding { get; }

    static MfcArchiveReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DefaultEncoding = Encoding.GetEncoding(1252);
    }

    public MfcArchiveReader(Stream stream, Encoding? encoding = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _encoding = encoding ?? DefaultEncoding;
    }

    /// <summary>Byte offset into the underlying stream.</summary>
    public long Position => _stream.Position;

    /// <summary>
    /// Whether a read past the end of the file yields zeroes instead of throwing
    /// (<c>CArchive::Read</c>'s actual behaviour).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MFC's extraction operators do not check how much they read.</b> <c>ar &gt;&gt; n</c> is
    /// <c>Read(&amp;n, sizeof n)</c> with the return value discarded, so a file that ends mid-record
    /// leaves the destination holding whatever was there — in practice zero. The reference
    /// therefore <i>opens</i> a truncated design and treats the missing tail as absent, where this
    /// port would refuse it.
    /// </para>
    /// <para>
    /// <b>Off by default, and deliberately.</b> Silently zero-filling past EOF everywhere would
    /// turn genuine corruption — a mis-parse that runs off the end — into plausible data, which is
    /// the failure this reader exists to prevent. It is switched on only where a shipped file is
    /// known to be short and the reference is known to read it anyway. See
    /// <see cref="TruncatedAt"/> for telling the two apart afterwards.
    /// </para>
    /// </remarks>
    public bool ZeroFillPastEnd { get; set; }

    /// <summary>
    /// Where the file first ran out, when <see cref="ZeroFillPastEnd"/> let a read past it succeed.
    /// </summary>
    /// <remarks>
    /// <b>A short read is a fact about the file, not a detail to swallow.</b> A caller that
    /// tolerated the truncation still needs to be able to say so — and a value here on a file
    /// nobody expected to be short is a mis-parse, not a short file.
    /// </remarks>
    public long? TruncatedAt { get; private set; }

    private void ReadExactly(Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = _stream.Read(buffer[read..]);
            if (n == 0)
            {
                if (ZeroFillPastEnd)
                {
                    // CArchive's own behaviour: the bytes that were not there stay as they were,
                    // which for a freshly zeroed destination is zero. Recorded so a caller can
                    // still tell a short file from a clean one.
                    TruncatedAt ??= _stream.Position;
                    buffer[read..].Clear();
                    return;
                }

                throw new EndOfStreamException(
                    $"Expected {buffer.Length} bytes at offset {_stream.Position}, got {read}.");
            }
            read += n;
        }
    }

    public byte ReadByte()
    {
        Span<byte> b = stackalloc byte[1];
        ReadExactly(b);
        return b[0];
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();

    public bool ReadBool() => ReadByte() != 0;

    public short ReadInt16()
    {
        Span<byte> b = stackalloc byte[2];
        ReadExactly(b);
        return BinaryPrimitives.ReadInt16LittleEndian(b);
    }

    public ushort ReadUInt16()
    {
        Span<byte> b = stackalloc byte[2];
        ReadExactly(b);
        return BinaryPrimitives.ReadUInt16LittleEndian(b);
    }

    public int ReadInt32()
    {
        Span<byte> b = stackalloc byte[4];
        ReadExactly(b);
        return BinaryPrimitives.ReadInt32LittleEndian(b);
    }

    public uint ReadUInt32()
    {
        Span<byte> b = stackalloc byte[4];
        ReadExactly(b);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }

    public long ReadInt64()
    {
        Span<byte> b = stackalloc byte[8];
        ReadExactly(b);
        return BinaryPrimitives.ReadInt64LittleEndian(b);
    }

    public ulong ReadUInt64()
    {
        Span<byte> b = stackalloc byte[8];
        ReadExactly(b);
        return BinaryPrimitives.ReadUInt64LittleEndian(b);
    }

    public float ReadSingle()
    {
        Span<byte> b = stackalloc byte[4];
        ReadExactly(b);
        return BinaryPrimitives.ReadSingleLittleEndian(b);
    }

    public double ReadDouble()
    {
        Span<byte> b = stackalloc byte[8];
        ReadExactly(b);
        return BinaryPrimitives.ReadDoubleLittleEndian(b);
    }

    /// <summary>
    /// Reads a length prefix written by MFC's <c>AfxWriteStringLength</c>:
    /// <code>
    /// len &lt; 0xFF        -> BYTE len
    /// len &lt; 0xFFFE      -> BYTE 0xFF, WORD len
    /// otherwise          -> BYTE 0xFF, WORD 0xFFFF, DWORD len
    /// </code>
    /// The escape values are what make the boundary cases (254/255/65534/65535) worth testing
    /// explicitly — an off-by-one here silently shifts every subsequent field in the file.
    /// </summary>
    public uint ReadStringLength()
    {
        byte b = ReadByte();
        if (b < 0xFF)
        {
            return b;
        }

        ushort w = ReadUInt16();
        if (w < 0xFFFF)
        {
            return w;
        }

        return ReadUInt32();
    }

    /// <summary>
    /// Reads a length-prefixed single-byte string, decoded with the configured codepage.
    /// </summary>
    public string ReadString()
    {
        uint length = CheckedStringLength();
        if (length == 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[length];
        ReadExactly(bytes);
        return _encoding.GetString(bytes);
    }

    /// <summary>
    /// A string length the file could actually hold, or a diagnosable failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A length longer than the rest of the file is the loudest thing a mis-parse says</b>, and
    /// without this it says it in the worst way: <c>new byte[length]</c> throws
    /// <see cref="OverflowException"/> for a length past <see cref="int.MaxValue"/> and
    /// <c>OutOfMemoryException</c> for one merely enormous. Neither names a file or an offset, and
    /// neither is in any caller's catch list — so a reader that had quietly gone off the rails
    /// took the process down instead of being refused.
    /// </para>
    /// <para>
    /// Checked against the stream's own length rather than a constant, so a genuinely long string
    /// in a genuinely large file is still read. Streams that cannot report a length are left
    /// alone: there is nothing to compare against, and inventing a cap would refuse valid data.
    /// </para>
    /// </remarks>
    private uint CheckedStringLength()
    {
        long at = _stream.Position;
        uint length = ReadStringLength();

        if (!_stream.CanSeek)
        {
            return length;
        }

        long remaining = _stream.Length - _stream.Position;
        if (length > remaining)
        {
            throw new InvalidDataException(
                $"A string at offset {at} declares {length} bytes, but only {remaining} remain in "
                + "the file. The parse is not aligned with the data.");
        }

        return length;
    }

    /// <summary>
    /// Reads the raw bytes of a length-prefixed string without decoding. Use this when a value
    /// must round-trip byte-for-byte regardless of codepage.
    /// </summary>
    public byte[] ReadStringBytes()
    {
        uint length = CheckedStringLength();
        byte[] bytes = new byte[length];
        if (length > 0)
        {
            ReadExactly(bytes);
        }
        return bytes;
    }

    public byte[] ReadBytes(int count)
    {
        byte[] bytes = new byte[count];
        ReadExactly(bytes);
        return bytes;
    }

    public void Skip(long count) => _stream.Seek(count, SeekOrigin.Current);

    public void SeekTo(long offset) => _stream.Seek(offset, SeekOrigin.Begin);
}
