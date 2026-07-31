using UAF.Data;
using UAF.Media;

namespace UAFcore;

/// <summary>
/// The geometry of one viewport wall format: where each wall slot is cut from the wall art, where
/// it lands on screen, and the cell positions the viewport draws.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>VIEWPORT</c>'s config loading (<c>Shared/Viewport.cpp:660-694</c>). A design
/// declares several of these — <c>SomethingWild</c> has five — and the engine picks one according
/// to how much of the corridor is visible.
/// </para>
/// <para>
/// <b>The slot rectangles are source rects into the wall art, not screen rects.</b> Their widths
/// encode the fake-3D scale: in a 640×480 format, <c>E</c> is 112 pixels wide (the wall directly
/// ahead), <c>H</c> is 48 (one cell further), the <c>I</c>–<c>P</c> group is 16 (further still),
/// and the rest are 32-wide side walls. That matches the sizes <c>config640.txt</c> documents in
/// its own header — "WallPic 112 x 134, 48 x 58, 16 x 19". Every rect is the full 211-pixel art
/// height, because only the width varies with distance; the vertical foreshortening is baked into
/// the artwork.
/// </para>
/// <para>
/// <b>The viewport coordinate count is 13 or 15, decided by the data rather than declared.</b>
/// <c>Viewport.cpp:675</c> reads 15 when <c>DistantWallCount == 7</c> and 13 otherwise, and if any
/// coordinate in the run is missing it discards <i>all</i> of them and falls back to built-in
/// defaults rather than using a partial set. Reproduced, because a design with an incomplete run
/// would otherwise render with a mix of its own coordinates and ours.
/// </para>
/// </remarks>
public sealed record WallFormat(
    int Band,
    IReadOnlyList<SurfaceRect> SlotRects,
    IReadOnlyList<(int X, int Y)> SlotOffsets,
    IReadOnlyList<(int X, int Y)> ViewportCoords,
    bool UsedDefaultCoords)
{
    /// <summary><c>WALL_FORMAT_TYPE::MAX_SLOT_TYPES</c> (<c>Viewport.h:182</c>) — slots A to P.</summary>
    public const int MaxSlotTypes = 16;

    /// <summary>The slot letter for an index, as the config keys spell it.</summary>
    public static char SlotLetter(int index) => (char)('A' + index);
}

/// <summary>Loads every wall format a design's config declares.</summary>
public static class WallFormatReader
{
    /// <summary>
    /// The coordinate counts the original accepts, in the order it tries them
    /// (<c>Viewport.cpp:675</c>).
    /// </summary>
    private const int ShortCoordCount = 13;
    private const int LongCoordCount = 15;

    /// <summary>
    /// Reads formats until one is missing, which is how the original terminates its own loop.
    /// </summary>
    /// <param name="maxBands">
    /// A stop, not an expectation. The C++ loops over a fixed format count; here the run ends at
    /// the first band with no <c>A<i>n</i>_WALL_RECT</c>, so a design declaring three works as
    /// well as one declaring five.
    /// </param>
    public static List<WallFormat> ReadAll(DesignConfig config, int maxBands = 16)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Peeking rather than consuming: DesignConfig hands a token out once by default, and these
        // keys are read repeatedly while probing for the end of the run.
        var formats = new List<WallFormat>();
        for (int band = 1; band <= maxBands; band++)
        {
            var format = Read(config, band);
            if (format is null)
            {
                break;
            }

            formats.Add(format);
        }

        return formats;
    }

    /// <summary>Reads one band, or null when it is not declared.</summary>
    public static WallFormat? Read(DesignConfig config, int band)
    {
        ArgumentNullException.ThrowIfNull(config);

        var rects = new List<SurfaceRect>(WallFormat.MaxSlotTypes);
        var offsets = new List<(int, int)>(WallFormat.MaxSlotTypes);

        for (int slot = 0; slot < WallFormat.MaxSlotTypes; slot++)
        {
            char letter = WallFormat.SlotLetter(slot);

            if (!config.TryGetRect($"{letter}{band}_WALL_RECT", out int l, out int t,
                                   out int r, out int b, consume: false) ||
                !config.TryGetPoint($"{letter}{band}_OFF", out int ox, out int oy, consume: false))
            {
                // The original treats any missing slot as failure for the whole format and skips
                // it entirely, so a half-declared band is never used.
                return null;
            }

            rects.Add(new SurfaceRect(l, t, r, b));
            offsets.Add((ox, oy));
        }

        var coords = ReadCoords(config, band, LongCoordCount)
                     ?? ReadCoords(config, band, ShortCoordCount);

        return new WallFormat(band, rects, offsets, coords ?? [], coords is null);
    }

    /// <summary>
    /// Reads a full run of viewport coordinates, or null if any one of them is missing.
    /// </summary>
    /// <remarks>
    /// All or nothing on purpose: <c>Viewport.cpp:686-690</c> replaces the whole array with
    /// defaults when the run is incomplete, rather than filling the gaps.
    /// </remarks>
    private static List<(int X, int Y)>? ReadCoords(DesignConfig config, int band, int count)
    {
        var coords = new List<(int, int)>(count);
        for (int i = 0; i < count; i++)
        {
            if (!config.TryGetPoint($"VIEWPORT_COORD_{i}_{band}", out int x, out int y,
                                    consume: false))
            {
                return null;
            }

            coords.Add((x, y));
        }

        return coords;
    }
}
