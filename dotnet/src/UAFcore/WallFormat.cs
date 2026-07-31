using UAF.Data;
using UAF.Media;

namespace UAFcore;

/// <summary>
/// The geometry of one viewport wall format: where each wall slot is cut from the wall art, where
/// it lands on screen, and the cell positions the viewport draws.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>VIEWPORT</c>'s config loading (<c>Shared/Viewport.cpp:640-694</c>). A design
/// declares <c>MAX_ALTERNATE_WALL_FORMATS</c> of them — five, in every shipped config.
/// </para>
/// <para>
/// <b>A format is selected by the wall art's own dimensions</b>, not by anything about the party
/// or the corridor. <c>WallFormatMgr::GetFormat(width, height)</c> (<c>Viewport.h:203</c>) walks
/// the formats looking for one whose <see cref="ImageWidth"/> and <see cref="ImageHeight"/> match
/// the sheet that was loaded, and falls back to format 0 — which it skips while searching, because
/// it is the built-in default layout. The config's own comments give this away: the bands are
/// labelled "from Kevin's Tavern demo" and "from Kevin's New Walls", third-party wall packs at
/// 480×360 and 500×375. So this is the mechanism that lets a design drop in community art with a
/// different sheet layout and have the engine cut it up correctly.
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
    int ImageWidth, int ImageHeight, int DistantWallCount,
    IReadOnlyList<SurfaceRect> SlotRects,
    IReadOnlyList<(int X, int Y)> SlotOffsets,
    IReadOnlyList<(int X, int Y)> ViewportCoords,
    bool UsedDefaultCoords)
{
    /// <summary><c>WALL_FORMAT_TYPE::MAX_SLOT_TYPES</c> (<c>Viewport.h:182</c>) — slots A to P.</summary>
    public const int MaxSlotTypes = 16;

    /// <summary>The slot letter for an index, as the config keys spell it.</summary>
    public static char SlotLetter(int index) => (char)('A' + index);

    /// <summary>
    /// The default when <c>NUM_DISTANT_WALLS_<i>n</i></c> is absent (<c>Viewport.cpp:658</c>).
    /// </summary>
    public const int DefaultDistantWallCount = 5;

    /// <summary>Whether this format matches a wall sheet of the given size.</summary>
    public bool Matches(int width, int height) =>
        ImageWidth == width && ImageHeight == height;
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
    /// Reads every format the config declares, as <c>VIEWPORT::LoadAlternateWallFormats</c> does.
    /// </summary>
    /// <remarks>
    /// The count comes from <c>MAX_ALTERNATE_WALL_FORMATS</c> rather than from probing until a
    /// band is missing (<c>Viewport.cpp:640</c>). A band that fails to load is skipped rather than
    /// ending the run, so a config with a gap still yields the bands after it — which matters,
    /// because the band index is not the array index once one has been skipped.
    /// </remarks>
    public static List<WallFormat> ReadAll(DesignConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.TryGetValue("MAX_ALTERNATE_WALL_FORMATS", out string raw, consume: false) ||
            !int.TryParse(raw.Split('/')[0].Trim(), out int count))
        {
            return [];
        }

        // Peeking rather than consuming: DesignConfig hands a token out once by default, and a
        // caller may well read these more than once.
        var formats = new List<WallFormat>(Math.Max(count, 0));
        for (int band = 1; band <= count; band++)
        {
            var format = Read(config, band);
            if (format is not null)
            {
                formats.Add(format);
            }
        }

        return formats;
    }

    /// <summary>Reads one band, or null when it is not declared.</summary>
    public static WallFormat? Read(DesignConfig config, int band)
    {
        ArgumentNullException.ThrowIfNull(config);

        // The sheet dimensions are what GetFormat matches on, so a band without them can never
        // be selected and the original treats it as a failure.
        if (!config.TryGetValue($"WIDTH_WALL_FORMAT_{band}", out string widthText, consume: false) ||
            !config.TryGetValue($"HEIGHT_WALL_FORMAT_{band}", out string heightText, consume: false) ||
            !int.TryParse(widthText.Split('/')[0].Trim(), out int imageWidth) ||
            !int.TryParse(heightText.Split('/')[0].Trim(), out int imageHeight))
        {
            return null;
        }

        int distantWalls = WallFormat.DefaultDistantWallCount;
        if (config.TryGetValue($"NUM_DISTANT_WALLS_{band}", out string distantText, consume: false)
            && int.TryParse(distantText.Split('/')[0].Trim(), out int parsedDistant))
        {
            distantWalls = parsedDistant;
        }

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

        // 15 coordinates when there are seven distant walls, 13 otherwise -- decided by the data
        // rather than declared (Viewport.cpp:675).
        int expected = distantWalls == 7 ? LongCoordCount : ShortCoordCount;
        var coords = ReadCoords(config, band, expected);

        return new WallFormat(band, imageWidth, imageHeight, distantWalls,
                              rects, offsets, coords ?? [], coords is null);
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
