namespace UAF.Serialization;

/// <summary>
/// The writing counterpart of <see cref="IArchiveCursor"/> — the primitives shared by the plain
/// and compressed archive writers.
/// </summary>
/// <remarks>
/// <para>
/// Record writers targeted <see cref="MfcArchiveWriter"/> concretely while their readers went
/// through <see cref="IArchiveCursor"/>, so nothing above the byte layer could produce a
/// compressed archive however complete the encoder was. This is what closes that gap.
/// </para>
/// <para>
/// <b>Three of these methods are genuinely different between the two encodings, not merely
/// dispatched.</b> A string is length-prefixed in the plain form and <i>interned</i> in the
/// compressed one; a count uses MFC's escaping scheme in one and a flat <c>DWORD</c> in the other;
/// and there is no compressed equivalent of writing raw string bytes. Everything else is the same
/// bytes down a different pipe.
/// </para>
/// </remarks>
public interface IArchiveWriteCursor
{
    /// <summary>
    /// True for the <c>CAR</c> path. Callers need it where the two genuinely diverge — notably
    /// <c>ASLENTRY</c>, whose compressed form applies a key fixup that is not invertible.
    /// </summary>
    bool IsCompressed { get; }

    void WriteByte(byte value);

    void WriteUInt16(ushort value);

    void WriteInt32(int value);

    void WriteUInt32(uint value);

    void WriteDouble(double value);

    /// <summary>A 4-byte float. Distinct from <see cref="WriteDouble"/>; both occur in records.</summary>
    void WriteSingle(float value);

    /// <summary>Writes raw bytes — used for struct blits such as <c>LOGFONT</c>.</summary>
    void WriteBytes(ReadOnlySpan<byte> value);

    /// <summary>
    /// Writes a collection count in whichever encoding this archive uses.
    /// </summary>
    /// <remarks>
    /// <b>Not interchangeable with <see cref="WriteUInt32"/>.</b> The plain form is a <c>WORD</c>
    /// escaping to a <c>DWORD</c> on 0xFFFF; the compressed form is a flat <c>DWORD</c>. Two bytes
    /// against four for every small count.
    /// </remarks>
    void WriteCount(uint count);

    /// <summary>Writes a string in whichever encoding this archive uses.</summary>
    /// <remarks>
    /// <b>The two are not the same shape at all.</b> Plain is a length prefix and the bytes;
    /// compressed is an index, and the bytes only the first time. A record writer does not need to
    /// know which — but it does need to write its strings in the same order the reader will read
    /// them, because the compressed table is built as it goes.
    /// </remarks>
    void WriteString(string value);
}

/// <summary>Adapts the two concrete writers to <see cref="IArchiveWriteCursor"/>.</summary>
public static class ArchiveWriteCursor
{
    public static IArchiveWriteCursor For(MfcArchiveWriter writer) => new PlainCursor(writer);

    public static IArchiveWriteCursor For(CarArchiveWriter writer) => new CarCursor(writer);

    private sealed class PlainCursor(MfcArchiveWriter writer) : IArchiveWriteCursor
    {
        public bool IsCompressed => false;

        public void WriteByte(byte value) => writer.WriteByte(value);

        public void WriteUInt16(ushort value) => writer.WriteUInt16(value);

        public void WriteInt32(int value) => writer.WriteInt32(value);

        public void WriteUInt32(uint value) => writer.WriteUInt32(value);

        public void WriteDouble(double value) => writer.WriteDouble(value);

        public void WriteSingle(float value) => writer.WriteSingle(value);

        public void WriteBytes(ReadOnlySpan<byte> value) => writer.WriteBytes(value);

        public void WriteCount(uint count) => writer.WriteCount(count);

        public void WriteString(string value) => writer.WriteString(value);
    }

    private sealed class CarCursor(CarArchiveWriter writer) : IArchiveWriteCursor
    {
        public bool IsCompressed => true;

        public void WriteByte(byte value) => writer.WriteByte(value);

        public void WriteUInt16(ushort value) => writer.WriteUInt16(value);

        public void WriteInt32(int value) => writer.WriteInt32(value);

        public void WriteUInt32(uint value) => writer.WriteUInt32(value);

        public void WriteDouble(double value) => writer.WriteDouble(value);

        public void WriteSingle(float value) => writer.WriteSingle(value);

        public void WriteBytes(ReadOnlySpan<byte> value) => writer.WriteBytes(value);

        public void WriteCount(uint count) => writer.WriteCount(count);

        public void WriteString(string value) => writer.WriteString(value);
    }
}
