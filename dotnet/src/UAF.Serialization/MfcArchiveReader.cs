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

    private void ReadExactly(Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = _stream.Read(buffer[read..]);
            if (n == 0)
            {
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
        uint length = ReadStringLength();
        if (length == 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[length];
        ReadExactly(bytes);
        return _encoding.GetString(bytes);
    }

    /// <summary>
    /// Reads the raw bytes of a length-prefixed string without decoding. Use this when a value
    /// must round-trip byte-for-byte regardless of codepage.
    /// </summary>
    public byte[] ReadStringBytes()
    {
        uint length = ReadStringLength();
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
