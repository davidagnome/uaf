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

/// <summary>How a file's version is determined when it carries no magic prologue.</summary>
public enum UnstampedVersionSource
{
    /// <summary>Assume a fixed constant (e.g. <c>.lvl</c> assumes 0.572).</summary>
    FixedFallback,

    /// <summary>
    /// Read the version <c>double</c> from offset 0 — the payload's own first field doubles as
    /// the container version. This is what <c>GetDesignVersion</c> does for <c>game.dat</c>.
    /// </summary>
    PayloadFirstField,

    /// <summary>
    /// <c>min(globalData.version, cap)</c> — depends on already-loaded state, so
    /// <c>game.dat</c> must be read first.
    /// </summary>
    CappedGlobalVersion,
}

/// <summary>
/// Per-file-type container rules: how an unstamped version is resolved, and the thresholds at
/// which the archive layer changes.
/// </summary>
/// <remarks>
/// <para>
/// These constants are <b>not</b> shared between file types and must be transcribed from each
/// type's own loader. Three distinct loaders exist and they disagree in every parameter:
/// </para>
/// <list type="table">
///   <item><term><c>game.dat</c></term><description><c>loadDesign(LPCSTR)</c> — version from
///     <c>GetDesignVersion</c> (<c>Globals.cpp:3447</c>), archive gate at
///     <b>0.998101</b> (<c>Level.cpp:3392</c>)</description></item>
///   <item><term><c>*.lvl</c></term><description><c>LoadLevel</c> — fallback <b>0.572</b>,
///     archive gate at <b>0.573</b> (<c>Level.cpp:2163</c>)</description></item>
///   <item><term>databases</term><description><c>loadData</c> — fallback
///     <c>min(globalData.version, 0.696)</c>, archive gate <b>0.697</b>, compression gate
///     <b>0.930</b> (<c>Items.cpp:3418</c>)</description></item>
/// </list>
/// <para>
/// <c>game.dat</c> and <c>*.lvl</c> agree only by coincidence at 0.915 — both say
/// <see cref="ArchiveTier.PlainArchive"/>. Across <c>[0.573, 0.998101)</c> they diverge, so
/// conflating them silently mis-parses every design in that range.
/// </para>
/// </remarks>
public sealed record DesignFileKind(
    string Name,
    UnstampedVersionSource UnstampedSource,
    DesignVersion UnstampedFallback,
    DesignVersion CarThreshold,
    DesignVersion? CompressionThreshold)
{
    /// <summary>
    /// <c>game.dat</c> — <c>loadDesign(LPCSTR)</c>, <c>Level.cpp:3341</c>.
    /// </summary>
    /// <remarks>
    /// Has no fallback constant at all: <c>GetDesignVersion</c> reads the magic, and if absent
    /// seeks back to 0 and reads the <c>double</c> there anyway (<c>Globals.cpp:3460</c>). So the
    /// version is always taken from the file — for an unstamped file it is literally the
    /// payload's first field, read twice (once to pick the archive, once by
    /// <c>GLOBAL_STATS::Serialize</c>).
    /// </remarks>
    public static DesignFileKind GameData { get; } = new(
        "GameData",
        UnstampedSource: UnstampedVersionSource.PayloadFirstField,
        UnstampedFallback: default,
        CarThreshold: DesignVersion.SpellNames,      // 0.998101
        CompressionThreshold: null);

    /// <summary>Level files (<c>LevelNNN.lvl</c>) — <c>LoadLevel</c>, <c>Level.cpp:2151</c>.</summary>
    public static DesignFileKind LevelData { get; } = new(
        "LevelData",
        UnstampedSource: UnstampedVersionSource.FixedFallback,
        UnstampedFallback: DesignVersion.V0572,
        CarThreshold: DesignVersion.V0573,
        CompressionThreshold: null);

    /// <summary>
    /// Item / monster / spell databases — <c>loadData</c>, <c>Items.cpp:3405</c> and twins.
    /// </summary>
    public static DesignFileKind Database { get; } = new(
        "Database",
        UnstampedSource: UnstampedVersionSource.CappedGlobalVersion,
        UnstampedFallback: DesignVersion.V0696,
        CarThreshold: DesignVersion.V0697,
        CompressionThreshold: DesignVersion.SpecialAbilities);   // 0.930

    /// <summary>Retained for source compatibility; prefer <see cref="Database"/>.</summary>
    public static DesignFileKind Items => Database;

    /// <summary>
    /// The database fallback is <c>min(globalData.version, 0.696)</c> (<c>Items.cpp:3418</c>),
    /// not a constant — so <c>game.dat</c> must be loaded before the databases or they receive
    /// the wrong version.
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
