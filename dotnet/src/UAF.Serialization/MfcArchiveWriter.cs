using System.Buffers.Binary;
using System.Text;

namespace UAF.Serialization;

/// <summary>
/// Writes the plain <c>CArchive</c> stream that <see cref="MfcArchiveReader"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first half of the writer Phase 1 has been missing.</b> Everything above this — records,
/// levels, save games — has a reader and no counterpart, which is why the round-trip exit criterion
/// is unmet and why the editor cannot start. This is the byte layer; the record writers sit on top.
/// </para>
/// <para>
/// Every method here is the exact inverse of the reader's, and the two are tested against each
/// other rather than against a description. The encodings that matter are the two variable-width
/// ones — string lengths and collection counts — which are <b>different schemes</b> despite both
/// escaping on <c>0xFFFF</c>.
/// </para>
/// </remarks>
public sealed class MfcArchiveWriter
{
    private readonly Stream _stream;
    private readonly Encoding _encoding;

    public MfcArchiveWriter(Stream stream, Encoding? encoding = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _encoding = encoding ?? MfcArchiveReader.DefaultEncoding;
    }

    /// <summary>Byte offset into the underlying stream.</summary>
    public long Position => _stream.Position;

    public void WriteByte(byte value) => _stream.WriteByte(value);

    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteDouble(double value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    /// <summary>A 4-byte float. Distinct from <see cref="WriteDouble"/>; both occur in records.</summary>
    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteBytes(ReadOnlySpan<byte> value) => _stream.Write(value);

    /// <summary>
    /// Writes a string's length in MFC's escaping scheme.
    /// </summary>
    /// <remarks>
    /// <b>Three tiers, each escaping into the next with an all-ones marker.</b> Under 255 is one
    /// byte; under 65535 is <c>0xFF</c> then a word; anything larger is <c>0xFF</c>, <c>0xFFFF</c>,
    /// then a dword. The boundaries are <i>exclusive</i> — a length of exactly 255 does not fit in
    /// the byte tier, because 255 is the escape.
    /// <para>
    /// <b>This is not the same scheme as <see cref="WriteCount"/></b>, which has no byte tier at
    /// all. Using one for the other writes a stream that reads back plausibly for small values and
    /// desynchronises for large ones.
    /// </para>
    /// </remarks>
    public void WriteStringLength(uint length)
    {
        if (length < 0xFF)
        {
            WriteByte((byte)length);
            return;
        }

        WriteByte(0xFF);

        if (length < 0xFFFF)
        {
            WriteUInt16((ushort)length);
            return;
        }

        WriteUInt16(0xFFFF);
        WriteUInt32(length);
    }

    /// <summary>Writes a length-prefixed string in the configured codepage.</summary>
    /// <remarks>
    /// <b>The length is in bytes, not characters.</b> Windows-1252 makes those the same for
    /// everything it can encode, but a character it cannot becomes a single <c>?</c> — so the count
    /// must come from the encoded bytes rather than from the string.
    /// </remarks>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        byte[] bytes = _encoding.GetBytes(value);
        WriteStringLength((uint)bytes.Length);
        WriteBytes(bytes);
    }

    /// <summary>Writes already-encoded string bytes, for values that must round-trip exactly.</summary>
    public void WriteStringBytes(ReadOnlySpan<byte> value)
    {
        WriteStringLength((uint)value.Length);
        WriteBytes(value);
    }

    /// <summary>
    /// Writes a collection count in MFC's scheme (<c>CArchive::WriteCount</c>).
    /// </summary>
    /// <remarks>
    /// <b>Two tiers, not three: a word escaping to a dword on <c>0xFFFF</c>.</b> There is no
    /// single-byte form, so a count of 3 costs two bytes where a string length of 3 costs one.
    /// </remarks>
    public void WriteCount(uint count)
    {
        if (count < 0xFFFF)
        {
            WriteUInt16((ushort)count);
            return;
        }

        WriteUInt16(0xFFFF);
        WriteUInt32(count);
    }
}
