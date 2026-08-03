using System.Buffers.Binary;
using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes a saved character (<c>.chr</c>), the inverse of <see cref="CharacterFileReader"/>
/// (<c>CHARACTER::SaveCharacter</c>, <c>Shared/Char.cpp:6994</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The first whole file the port can write that is not a database.</b> The framing is eight
/// bytes of magic, a <c>double</c> version, then a <c>CHARACTER</c> — and the payload is <i>plain
/// MFC</i> despite travelling through a <c>CAR</c>, because <c>CAR::CAR</c> leaves
/// <c>m_compressType</c> at 0 and nothing on this path changes it. See
/// <see cref="CharacterFileReader"/>, where that cost real time to establish: constructing a
/// <c>CAR</c> does not mean the bytes are <c>CAR</c> bytes.
/// </para>
/// <para>
/// <b>The header goes through the archive rather than around it</b>, which is what makes the two
/// halves agree: the reference stores with <c>ar.Serialize((char*)&amp;hdr, 8)</c> but loads with
/// <c>myFile.Read(&amp;hdr, 8)</c> on the raw file. A <c>CAR</c> emits nothing at construction, so
/// the magic really is at offset 0.
/// </para>
/// <para>
/// <b>A headerless file cannot be produced, and could not be read back either.</b> The reference
/// has always written the magic; a file without one is assumed to be
/// <see cref="CharacterFileReader.AssumedVersion"/> — 0.563 — which is below the
/// <see cref="CharacterFileReader.MinimumVersion"/> floor the engine enforces, so
/// <see cref="CharacterFileReader.Read(Stream, ArchiveRole)"/> rejects it. That branch is reachable
/// only for a file no build can load.
/// </para>
/// </remarks>
public static class CharacterFileWriter
{
    /// <summary>
    /// The lowest version that can be declared: the one whose reader reads exactly what
    /// <see cref="CharacterRecordWriter"/> writes.
    /// </summary>
    public static DesignVersion MinimumWritableVersion => CharacterRecordWriter.WrittenVersion;

    /// <summary>
    /// Writes one character file.
    /// </summary>
    /// <param name="stream">Written from its current position; the magic lands wherever that is.</param>
    /// <param name="character">The record.</param>
    /// <param name="version">
    /// The version to declare. Defaults to <see cref="MinimumWritableVersion"/> and cannot be below
    /// it — see the remarks.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Declaring a version below <see cref="MinimumWritableVersion"/> is refused rather than
    /// honoured.</b> The record always goes out in the modern shape, so a file stamped 3.64 with a
    /// 5.24 record in it is the one combination nothing can read — the reader would stop four
    /// bytes short at the icon's <c>RestartFrame</c> and desynchronise from there. The reference
    /// has the same asymmetry and resolves it the same way: it stamps the file with
    /// <c>ENGINE_VER</c>, not with whatever the character was loaded as.
    /// </para>
    /// <para>
    /// The consequence for the corpus is worth stating plainly: <b>the six shipped <c>.chr</c>
    /// files declare 3.64 and cannot be reproduced byte for byte.</b> Writing one back upgrades it,
    /// exactly as the reference upgrades an old design when it saves — and as
    /// <c>SomethingWild</c>'s <c>monsters.dat</c> already does for the same reason.
    /// </para>
    /// </remarks>
    public static void Write(Stream stream, CharacterRecord character, DesignVersion? version = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(character);

        var declared = version ?? MinimumWritableVersion;
        if (declared < MinimumWritableVersion)
        {
            throw new NotSupportedException(
                $"a .chr file cannot declare version {declared.Value}: the record is always " +
                $"written in the {MinimumWritableVersion.Value} shape, so the two would " +
                "disagree at the icon's RestartFrame and every byte after it. Write at " +
                $"{MinimumWritableVersion.Value} or above -- the reference stamps ENGINE_VER for " +
                "the same reason.");
        }

        var writer = new MfcArchiveWriter(stream);

        Span<byte> magic = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(magic, CharacterFileReader.Magic);
        writer.WriteBytes(magic);

        writer.WriteDouble(declared.Value);

        CharacterRecordWriter.Write(ArchiveWriteCursor.For(writer), character);
    }

    /// <summary>Writes one character file to <paramref name="path"/>, replacing it if present.</summary>
    public static void Write(string path, CharacterRecord character, DesignVersion? version = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var stream = File.Create(path);
        Write(stream, character, version);
    }

    /// <summary>Writes a file back out, keeping whatever version it declared where that is legal.</summary>
    /// <remarks>
    /// A convenience for the round trip. A file below <see cref="MinimumWritableVersion"/> is
    /// upgraded rather than refused, because refusing would make every shipped <c>.chr</c>
    /// unwritable — the upgrade is what the reference does too.
    /// </remarks>
    public static void Write(Stream stream, CharacterFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Write(stream, file.Character,
              file.Version < MinimumWritableVersion ? MinimumWritableVersion : file.Version);
    }
}
