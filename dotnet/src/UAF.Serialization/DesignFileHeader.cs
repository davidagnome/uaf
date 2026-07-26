using UAF.Common;

namespace UAF.Serialization;

/// <summary>Which archive layer the payload of a design file uses.</summary>
public enum ArchiveKind
{
    /// <summary>
    /// Plain <c>CArchive</c> — no <c>CAR</c> wrapper, no LZW, no string interning.
    /// Used for designs older than <see cref="DesignVersion.V0573"/>.
    /// </summary>
    PlainArchive,

    /// <summary>
    /// <c>CAR</c> — the LZW/string-interning wrapper around <c>CArchive</c>.
    /// Used for <see cref="DesignVersion.V0573"/> and later.
    /// </summary>
    CompressedArchive,
}

/// <summary>
/// The prologue of a design data file: an optional magic + version stamp, from which the payload
/// offset, the format version, and the archive layer are all derived.
/// </summary>
/// <remarks>
/// Mirrors the read path at <c>Level.cpp:2151</c> (and its twins in <c>Items.cpp:3407</c>,
/// <c>Spell.cpp:5955</c>, <c>Monster.cpp</c>, <c>Char.cpp:6939</c>). See
/// docs/PORTING-PLAN.md section 3.2.
/// </remarks>
public readonly record struct DesignFileHeader(
    DesignVersion Version,
    long PayloadOffset,
    bool HadMagic,
    ArchiveKind Archive)
{
    /// <summary>
    /// The 8-byte sentinel written before the version stamp, as <c>__int64 0xFABCDEFABCDEFABF</c>
    /// (little-endian on disk: <c>BF FA DE BC FA DE BC FA</c>).
    /// </summary>
    public const ulong Magic = 0xFABCDEFABCDEFABFUL;

    /// <summary>
    /// Reads the prologue from the start of <paramref name="stream"/>.
    /// </summary>
    /// <param name="unstampedFallback">
    /// The version to assume when the magic is absent. This <b>differs by file type</b>: level and
    /// game data use <see cref="DesignVersion.V0572"/> (<c>Level.cpp:2163</c>), character files use
    /// <see cref="DesignVersion.V0563"/> (<c>Char.cpp:6948</c>). There is deliberately no default —
    /// picking the wrong one mis-parses every unstamped file of that type, and silently.
    /// </param>
    public static DesignFileHeader Read(Stream stream, DesignVersion unstampedFallback)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Seek(0, SeekOrigin.Begin);
        var reader = new MfcArchiveReader(stream);

        ulong header = reader.ReadUInt64();
        if (header == Magic)
        {
            DesignVersion version = new(reader.ReadDouble());
            return new DesignFileHeader(version, PayloadOffset: 16, HadMagic: true, ArchiveFor(version));
        }

        // No magic: rewind and treat the whole file as payload, at the fallback version.
        stream.Seek(0, SeekOrigin.Begin);
        return new DesignFileHeader(
            unstampedFallback, PayloadOffset: 0, HadMagic: false, ArchiveFor(unstampedFallback));
    }

    /// <summary>
    /// The archive layer implied by a version. The switch is at
    /// <see cref="DesignVersion.V0573"/> (<c>Level.cpp:2168</c>).
    /// </summary>
    public static ArchiveKind ArchiveFor(DesignVersion version) =>
        version < DesignVersion.V0573 ? ArchiveKind.PlainArchive : ArchiveKind.CompressedArchive;

    /// <summary>
    /// True when this file's version falls in the range the reference editor itself warns it
    /// cannot load reliably — <c>[0.998101, 0.9988]</c> per <c>Level.cpp:3340</c>. Output derived
    /// from such a file is not trustworthy ground truth when diffing against the C++ oracle.
    /// </summary>
    public bool IsInUnreliableRange =>
        Version >= DesignVersion.SpellNames && Version.Value <= 0.9988;
}
