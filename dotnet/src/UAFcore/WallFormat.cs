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
/// <b>The slot rectangles are source rects into the wall art, not screen rects</b>, and their
/// sizes encode the fake-3D scale. The default format's are the ones <c>config640.txt</c>
/// documents in its own header — "WallPic 112 x 134, 48 x 58, 16 x 19": <c>E</c> is 112×134 (the
/// wall directly ahead), <c>H</c> is 48×58 one cell further, <c>I</c> is 16 wide further still,
/// and the side walls are 32×211.
/// </para>
/// <para>
/// Height varies with distance in the default format but <i>not</i> in the alternates, whose rects
/// are all 211 tall — those wall packs bake the foreshortening into the artwork instead. So a
/// renderer cannot assume either, and must take both dimensions from the rectangle rather than
/// deriving the height.
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

/// <summary>Loads a design's wall formats: the default, then the alternates.</summary>
/// <remarks>
/// <b>There are two kinds of format and only one of them is numbered.</b> The default is built
/// from <i>unsuffixed</i> keys — <c>A_WALL_RECT</c>, <c>VIEWPORT_COORD_0</c> — and occupies index
/// 0 (<c>Viewport.cpp:584-596</c>). The alternates come from suffixed keys, <c>A1_WALL_RECT</c>
/// and friends, and are appended after it. <c>GetFormat</c> starts its search at index 1 with the
/// comment "skip first format which is the default layout" (<c>Viewport.h:207</c>) and falls back
/// to 0, so the default is what renders whenever the wall art matches no alternate.
/// <para>
/// The alternates matter in practice. <c>SomethingWild</c>'s wall art is 1500×375 and its five
/// bands declare 480×360, 500×375, 168×352, 1000×375 and 1500×375 — so its walls render through
/// <b>band 5</b>, the last one. Treating band 1 as format 0, which an earlier revision of this
/// file did, cuts every wall from the wrong rectangles and draws nothing at all; the two mistakes
/// compound, because a wrong index and a wrong search both end at the same blank viewport.
/// </para>
/// </remarks>
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

        int count = 0;
        if (config.TryGetValue("MAX_ALTERNATE_WALL_FORMATS", out string raw, consume: false))
        {
            _ = int.TryParse(raw.Split('/')[0].Trim(), out count);
        }

        // Peeking rather than consuming: DesignConfig hands a token out once by default, and a
        // caller may well read these more than once.
        // Index 0 is the default, which every alternate is appended after -- GetFormat's search
        // starts at 1 and falls back to 0, so the ordering is not cosmetic.
        var formats = new List<WallFormat>(Math.Max(count, 0) + 1);
        var fallback = ReadDefault(config);
        if (fallback is not null)
        {
            formats.Add(fallback);
        }

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

    /// <summary>
    /// Reads the default format from the unsuffixed keys — index 0, and the fallback for any wall
    /// sheet that matches no alternate.
    /// </summary>
    /// <remarks>
    /// It declares no <c>ImageWidth</c>/<c>ImageHeight</c>, which is consistent: nothing ever
    /// matches against it, it is only ever fallen back to. <see cref="WallFormat.Matches"/>
    /// therefore never returns true for it.
    /// </remarks>
    public static WallFormat? ReadDefault(DesignConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Read(config, band: null);
    }

    /// <summary>
    /// Picks the format for a wall sheet of the given size, as <c>GetFormat</c> does.
    /// </summary>
    /// <remarks>
    /// The search skips index 0 and falls back to it, so a sheet matching nothing still draws.
    /// </remarks>
    public static WallFormat? SelectFor(IReadOnlyList<WallFormat> formats, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(formats);

        for (int i = 1; i < formats.Count; i++)
        {
            if (formats[i].Matches(width, height))
            {
                return formats[i];
            }
        }

        return formats.Count > 0 ? formats[0] : null;
    }

    /// <summary>Reads one band, or the default when <paramref name="band"/> is null.</summary>
    public static WallFormat? Read(DesignConfig config, int? band)
    {
        ArgumentNullException.ThrowIfNull(config);

        // The default band has no suffix on any of its keys, and declares no sheet dimensions --
        // it is only ever fallen back to, never matched.
        string suffix = band is null ? string.Empty : band.Value.ToString();
        int imageWidth = 0, imageHeight = 0;

        if (band is not null)
        {
            // The sheet dimensions are what GetFormat matches on, so an alternate without them can
            // never be selected and the original treats it as a failure.
            if (!config.TryGetValue($"WIDTH_WALL_FORMAT_{suffix}", out string widthText, consume: false) ||
                !config.TryGetValue($"HEIGHT_WALL_FORMAT_{suffix}", out string heightText, consume: false) ||
                !int.TryParse(widthText.Split('/')[0].Trim(), out imageWidth) ||
                !int.TryParse(heightText.Split('/')[0].Trim(), out imageHeight))
            {
                return null;
            }
        }

        int distantWalls = WallFormat.DefaultDistantWallCount;
        if (config.TryGetValue($"NUM_DISTANT_WALLS_{suffix}", out string distantText, consume: false)
            && int.TryParse(distantText.Split('/')[0].Trim(), out int parsedDistant))
        {
            distantWalls = parsedDistant;
        }

        var rects = new List<SurfaceRect>(WallFormat.MaxSlotTypes);
        var offsets = new List<(int, int)>(WallFormat.MaxSlotTypes);

        for (int slot = 0; slot < WallFormat.MaxSlotTypes; slot++)
        {
            char letter = WallFormat.SlotLetter(slot);

            if (!config.TryGetRect($"{letter}{suffix}_WALL_RECT", out int l, out int t,
                                   out int r, out int b, consume: false) ||
                !config.TryGetPoint($"{letter}{suffix}_OFF", out int ox, out int oy, consume: false))
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
        var coords = ReadCoords(config, suffix, expected);

        return new WallFormat(band ?? 0, imageWidth, imageHeight, distantWalls,
                              rects, offsets, coords ?? [], coords is null);
    }

    /// <summary>
    /// Reads a full run of viewport coordinates, or null if any one of them is missing.
    /// </summary>
    /// <remarks>
    /// All or nothing on purpose: <c>Viewport.cpp:686-690</c> replaces the whole array with
    /// defaults when the run is incomplete, rather than filling the gaps.
    /// </remarks>
    private static List<(int X, int Y)>? ReadCoords(DesignConfig config, string suffix, int count)
    {
        var coords = new List<(int, int)>(count);
        for (int i = 0; i < count; i++)
        {
            string key = suffix.Length == 0
                ? $"VIEWPORT_COORD_{i}"
                : $"VIEWPORT_COORD_{i}_{suffix}";

            if (!config.TryGetPoint(key, out int x, out int y, consume: false))
            {
                return null;
            }

            coords.Add((x, y));
        }

        return coords;
    }
}
