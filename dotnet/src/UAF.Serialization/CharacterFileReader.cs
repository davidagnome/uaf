using UAF.Common;

namespace UAF.Serialization;

/// <summary>A standalone <c>.chr</c> file: its declared version and the character in it.</summary>
public sealed record CharacterFile(DesignVersion Version, bool HadHeader, CharacterRecord Character);

/// <summary>
/// Reads a saved character (<c>.chr</c>), written by <c>CHARACTER::SaveCharacter</c>
/// (<c>Shared/Char.cpp:6994</c>).
/// </summary>
/// <remarks>
/// <para>
/// A fifth container framing, distinct from the four in <c>docs/SERIALIZATION.md</c>: an 8-byte
/// magic, an 8-byte <c>double</c> version, then a <c>CHARACTER</c> through a <see cref="CarArchiveReader"/>.
/// </para>
/// <para>
/// <b>The header is written through the archive but read around it.</b> Saving does
/// <c>CAR ar(&amp;myFile, store)</c> and then <c>ar.Serialize((char*)&amp;hdr, 8)</c>; loading does
/// <c>myFile.Read(&amp;hdr, 8)</c> on the raw file and only then constructs
/// <c>CAR ar(&amp;myFile, load)</c> (<c>Char.cpp:6939,7018</c>). The two agree solely because a
/// <c>CAR</c> emits nothing at construction — the magic really is at offset 0 in the file. Worth
/// stating because it is the sort of asymmetry that makes a reader written from the storing branch
/// come out 16 bytes wrong, and because it means the intern table starts empty at the character
/// record rather than having seen the header.
/// </para>
/// <para>
/// <b>The archive is a <c>CAR</c>, but reading it as one is wrong.</b> <c>CAR::CAR</c> sets
/// <c>m_compressType = 0</c> (<c>class.cpp:11602</c>) and nothing on this path changes it, and at
/// type 0 a <c>CAR</c> is a pass-through: <c>CAR::ReadString</c> delegates straight to
/// <c>ar.ReadString(str)</c> on the underlying <c>CArchive</c>, and every other type reaches a
/// <c>die("Not Needed?")</c> (<c>class.h:492-503</c>). So the payload is plain MFC — ordinary
/// counted strings, no interning, no LZW — and it is read with an <see cref="MfcArchiveReader"/>.
/// </para>
/// <para>
/// This was found the hard way: reading through the CAR cursor desynchronised all six shipped
/// files within the first few fields, because that cursor applies string interning unconditionally.
/// The lesson generalises — <b>"the writer constructed a CAR" does not mean "the bytes are CAR
/// bytes"</b>, and the compression type decides which. It is the same distinction already recorded
/// for the three archive tiers, arriving from a new direction.
/// </para>
/// <para>
/// <b>A missing magic is not an error.</b> Files written before the version was recorded have the
/// character record at offset 0; the engine rewinds and assumes <c>_VERSION_0563_</c>
/// (<c>Char.cpp:6944-6949</c>). Reproduced, because those files are still loadable in the original.
/// </para>
/// </remarks>
public static class CharacterFileReader
{
    /// <summary>The signal value at the head of a versioned character file.</summary>
    public const ulong Magic = 0xFABCDEFABCDEFABF;

    /// <summary>
    /// The version assumed when no magic is present — the last build that did not record one.
    /// </summary>
    public static DesignVersion AssumedVersion => DesignVersion.V0563;

    /// <summary>
    /// The engine refuses anything below this, since a character predating special abilities has a
    /// different record shape (<c>Char.cpp:6952</c>).
    /// </summary>
    public static DesignVersion MinimumVersion => DesignVersion.SpecialAbilities;

    public static CharacterFile Read(Stream stream, ArchiveRole role = ArchiveRole.Engine)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Seek(0, SeekOrigin.Begin);

        var probe = new MfcArchiveReader(stream);
        ulong magic = probe.ReadUInt64();

        DesignVersion version;
        bool hadHeader = magic == Magic;

        if (hadHeader)
        {
            version = new DesignVersion(probe.ReadDouble());
        }
        else
        {
            // Not a version: those bytes are the start of the record. Rewind entirely.
            stream.Seek(0, SeekOrigin.Begin);
            version = AssumedVersion;
        }

        if (version < MinimumVersion)
        {
            throw new NotSupportedException(
                $"character file declares version {version.Value}, below the " +
                $"{MinimumVersion.Value} floor the engine enforces (Char.cpp:6952)");
        }

        var cursor = ArchiveCursor.For(new MfcArchiveReader(stream));
        return new CharacterFile(version, hadHeader,
                                 CharacterReader.Read(cursor, version, role));
    }

    public static CharacterFile Read(string path, ArchiveRole role = ArchiveRole.Engine)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Read(stream, role);
    }
}
