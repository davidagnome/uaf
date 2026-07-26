using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// The prologue of a design data file: an optional magic + version stamp, from which the payload
/// offset, the format version, and the archive tier are all derived.
/// </summary>
/// <remarks>
/// Mirrors the read path at <c>Level.cpp:2151</c> and its per-type twins in <c>Items.cpp:3405</c>,
/// <c>Spell.cpp:5955</c>, <c>Monster.cpp</c>, and <c>Char.cpp:6939</c>. See
/// docs/PORTING-PLAN.md section 3.2.
/// </remarks>
public readonly record struct DesignFileHeader(
    DesignVersion Version,
    long PayloadOffset,
    bool HadMagic,
    ArchiveTier Tier)
{
    /// <summary>
    /// The 8-byte sentinel written before the version stamp, as <c>__int64 0xFABCDEFABCDEFABF</c>
    /// (little-endian on disk: <c>BF FA DE BC FA DE BC FA</c>).
    /// </summary>
    public const ulong Magic = 0xFABCDEFABCDEFABFUL;

    /// <summary>
    /// Reads the prologue from the start of <paramref name="stream"/> using
    /// <paramref name="kind"/>'s rules.
    /// </summary>
    /// <param name="unstampedFallbackOverride">
    /// Supplies the fallback when the magic is absent, for file types whose fallback is computed
    /// rather than constant — items use <c>min(globalData.version, 0.696)</c>. When null,
    /// <paramref name="kind"/>'s static fallback is used.
    /// </param>
    public static DesignFileHeader Read(
        Stream stream,
        DesignFileKind kind,
        DesignVersion? unstampedFallbackOverride = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(kind);

        stream.Seek(0, SeekOrigin.Begin);
        var reader = new MfcArchiveReader(stream);

        ulong header = reader.ReadUInt64();
        if (header == Magic)
        {
            DesignVersion stamped = new(reader.ReadDouble());
            return new DesignFileHeader(stamped, PayloadOffset: 16, HadMagic: true, kind.TierFor(stamped));
        }

        // No magic: rewind. How the version is then obtained differs per file type.
        stream.Seek(0, SeekOrigin.Begin);

        DesignVersion resolved = kind.UnstampedSource switch
        {
            // game.dat: GetDesignVersion seeks back to 0 and reads the double there regardless
            // (Globals.cpp:3460), so the payload's own first field *is* the container version.
            UnstampedVersionSource.PayloadFirstField => new DesignVersion(reader.ReadDouble()),

            // Databases: min(globalData.version, 0.696) -- caller must supply it.
            UnstampedVersionSource.CappedGlobalVersion =>
                unstampedFallbackOverride ?? kind.UnstampedFallback,

            // *.lvl: a literal constant.
            _ => unstampedFallbackOverride ?? kind.UnstampedFallback,
        };

        // Payload always starts at 0 when there is no magic -- including for game.dat, where the
        // version double is re-read as GLOBAL_STATS's first serialized field.
        stream.Seek(0, SeekOrigin.Begin);
        return new DesignFileHeader(resolved, PayloadOffset: 0, HadMagic: false, kind.TierFor(resolved));
    }

    /// <summary>
    /// True when this file's version falls in the range the reference editor itself warns it
    /// cannot load reliably — <c>[0.998101, 0.9988]</c> per <c>Level.cpp:3340</c>. Output derived
    /// from such a file is not trustworthy ground truth when diffing against the C++ oracle.
    /// </summary>
    public bool IsInUnreliableRange =>
        Version >= DesignVersion.SpellNames && Version.Value <= 0.9988;
}
