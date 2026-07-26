using UAF.Common;

namespace UAF.Serialization;

/// <summary>Which archive layer the payload of a design file uses.</summary>
public enum ArchiveTier
{
    /// <summary>Plain <c>CArchive</c> — no <c>CAR</c> wrapper, no LZW, no string interning.</summary>
    PlainArchive,

    /// <summary><c>CAR</c>, but <c>Compress(true)</c> was never called — no LZW.</summary>
    UncompressedCar,

    /// <summary><c>CAR</c> with the 13-bit LZW layer.</summary>
    CompressedCar,
}

/// <summary>
/// Per-file-type container rules: the version assumed when no magic stamp is present, and the
/// thresholds at which the archive layer changes.
/// </summary>
/// <remarks>
/// <para>
/// These constants are <b>not</b> shared between file types and must be transcribed from each
/// type's own loader. Generalising one file's thresholds to another silently mis-parses every
/// design in the gap — for example items use 0.697 where level data uses 0.573.
/// </para>
/// <para>
/// See docs/PORTING-PLAN.md section 3.2.
/// </para>
/// </remarks>
public sealed record DesignFileKind(
    string Name,
    DesignVersion UnstampedFallback,
    DesignVersion CarThreshold,
    DesignVersion? CompressionThreshold)
{
    /// <summary>
    /// Level and game data (<c>game.dat</c>, <c>*.lvl</c>) — <c>Level.cpp:2151</c>.
    /// The unstamped fallback is the literal 0.572 and the archive switches at 0.573.
    /// </summary>
    public static DesignFileKind LevelData { get; } = new(
        "LevelData",
        UnstampedFallback: DesignVersion.V0572,
        CarThreshold: DesignVersion.V0573,
        CompressionThreshold: null);

    /// <summary>
    /// Item database (<c>items.dat</c>) — <c>Items.cpp:3405</c>. Note the unstamped fallback here
    /// is <c>min(globalData.version, 0.696)</c>, so it depends on already-loaded global state;
    /// use <see cref="ItemsFallback"/> rather than the static value.
    /// </summary>
    public static DesignFileKind Items { get; } = new(
        "Items",
        UnstampedFallback: DesignVersion.V0696,
        CarThreshold: DesignVersion.V0697,
        CompressionThreshold: DesignVersion.SpecialAbilities);   // 0.930

    /// <summary>
    /// The items fallback is <c>min(globalData.version, _VERSION_0696_)</c> (<c>Items.cpp:3418</c>),
    /// not a constant — which means <c>game.dat</c> must be loaded before the databases or they
    /// receive the wrong version.
    /// </summary>
    public static DesignVersion ItemsFallback(DesignVersion globalDataVersion) =>
        globalDataVersion < DesignVersion.V0696 ? globalDataVersion : DesignVersion.V0696;

    /// <summary>The archive tier implied by a version for this file type.</summary>
    public ArchiveTier TierFor(DesignVersion version)
    {
        if (version < CarThreshold)
        {
            return ArchiveTier.PlainArchive;
        }
        if (CompressionThreshold is { } threshold && version >= threshold)
        {
            return ArchiveTier.CompressedCar;
        }
        return ArchiveTier.UncompressedCar;
    }
}
