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

        public string ReadString() => reader.ReadString();
    }
}
