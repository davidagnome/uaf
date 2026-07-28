using System.Text;

namespace UAF.Serialization;

/// <summary>
/// The primitives shared by the plain and compressed archive readers.
/// </summary>
/// <remarks>
/// <para>
/// The C++ side writes each structure twice — once against <c>CArchive</c>, once against
/// <c>CAR</c> — and most ports of it here follow suit with a pair of overloads, because the two
/// paths genuinely diverge (<c>ASLENTRY</c> applies a key fixup on the compressed path only).
/// </para>
/// <para>
/// Where the two really are byte-identical, duplicating a long walk invites the halves to drift
/// apart silently. This interface exists for those cases; use it only after checking the C++
/// twins actually agree, since the divergences are not marked in any way.
/// </para>
/// </remarks>
public interface IArchiveCursor
{
    /// <summary>
    /// True for the <c>CAR</c> path. Callers need this where the C++ twins genuinely diverge —
    /// notably <c>ASLENTRY</c>, whose compressed overload applies a key fixup the plain one does
    /// not. Carrying it on the cursor keeps that fork explicit instead of silently picking one.
    /// </summary>
    bool IsCompressed { get; }

    int ReadInt32();

    uint ReadUInt32();

    ushort ReadUInt16();

    byte ReadByte();

    double ReadDouble();

    /// <summary>Reads raw bytes — used for struct blits such as <c>LOGFONT</c>.</summary>
    byte[] ReadBytes(int count);

    /// <summary>
    /// Reads a collection count in whichever encoding this archive uses.
    /// </summary>
    /// <remarks>
    /// <b>Not interchangeable with <see cref="ReadUInt32"/>.</b> <c>CAR::ReadCount</c>
    /// (<c>class.cpp:11707</c>) delegates to MFC's <c>CArchive::ReadCount</c> when
    /// <c>compressType</c> is 0 — a <c>WORD</c> that escapes to a <c>DWORD</c> on 0xFFFF — but
    /// reads a flat <c>DWORD</c> otherwise. So the same call site is 2 bytes in a tier-2 archive
    /// and 4 in a tier-3 one, for identical small counts.
    /// </remarks>
    uint ReadCount();

    /// <summary>Reads a counted string verbatim, without the <c>DAS</c> blank convention.</summary>
    string ReadString();
}

/// <summary>Adapts the two concrete readers to <see cref="IArchiveCursor"/>.</summary>
public static class ArchiveCursor
{
    public static IArchiveCursor For(MfcArchiveReader reader) => new PlainCursor(reader);

    public static IArchiveCursor For(CarArchiveReader reader) => new CarCursor(reader);

    private sealed class PlainCursor(MfcArchiveReader reader) : IArchiveCursor
    {
        public bool IsCompressed => false;

        public int ReadInt32() => reader.ReadInt32();

        public uint ReadUInt32() => reader.ReadUInt32();

        public ushort ReadUInt16() => reader.ReadUInt16();

        public byte ReadByte() => reader.ReadByte();

        public double ReadDouble() => reader.ReadDouble();

        public byte[] ReadBytes(int count) => reader.ReadBytes(count);

        public uint ReadCount() => ReadMfcCount(this);

        public string ReadString() => reader.ReadString();
    }

    private sealed class CarCursor(CarArchiveReader reader) : IArchiveCursor
    {
        public bool IsCompressed => true;

        public int ReadInt32() => reader.ReadInt32();

        public uint ReadUInt32() => reader.ReadUInt32();

        public ushort ReadUInt16() => reader.ReadUInt16();

        public byte ReadByte() => reader.ReadByte();

        public double ReadDouble() => reader.ReadDouble();

        public byte[] ReadBytes(int count) => reader.ReadBytes(count);

        // compressType 0 alone uses the MFC encoding; 1 and 2 both write a flat DWORD.
        public uint ReadCount() =>
            reader.CompressType == 0 ? ReadMfcCount(this) : reader.ReadUInt32();

        public string ReadString() => reader.ReadString();
    }

    /// <summary>MFC's <c>CArchive::ReadCount</c>: a <c>WORD</c>, escaping to a <c>DWORD</c>.</summary>
    private static uint ReadMfcCount(IArchiveCursor cursor)
    {
        ushort small = cursor.ReadUInt16();
        return small != 0xFFFF ? small : cursor.ReadUInt32();
    }
}
