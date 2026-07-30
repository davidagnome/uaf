namespace UAF.Media;

/// <summary>
/// The software blitter — the port of DirectDraw plus CDX's per-pixel loops
/// (<c>src/cdx/cdxsurface.cpp</c>), reached from the engine through <c>Graphics::Blit*</c>
/// (<c>Shared/Graphics.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the layer's whole reason for existing as managed code. The original is a software
/// blitter with source colour keys, integer alpha and per-pixel reads, so a GPU abstraction would
/// have to be fought rather than used (docs/PORTING-PLAN.md section 6). Keeping it here makes
/// rendering unit-testable with no window, and lets the editor and the game share one blitter
/// instead of growing two.
/// </para>
/// <para>
/// <b>The alpha argument runs backwards.</b> CDX blends with
/// <c>out = ((A * (dst - src)) >> 8) + src</c> where A is 0..256, so A is the weight of the
/// <i>destination</i>: A = 0 draws the source opaquely and A = 256 leaves the destination
/// untouched. Every caller in the C++ tree passes a value in those terms — including
/// <c>PIC_DATA::AlphaValue</c> and <c>WallSetSlotMemType::BlendAmount</c> straight out of the
/// design file — so the port keeps the same convention rather than inverting it and having to
/// remember to invert every stored value too. See <see cref="DestinationWeightMax"/>.
/// </para>
/// <para>
/// <b>Colour keys compare 24 bits.</b> CDX masks the top byte off before comparing
/// (<c>cdxsurface.cpp:3663</c>, <c>1957</c>), so a key matches on RGB alone. Kept, because art
/// loaded from a source with a stray alpha byte would otherwise stop being transparent.
/// </para>
/// <para>
/// <b>Every write is opaque.</b> CDX wrote 24 bits and left the top byte at zero, which DirectDraw
/// ignored. A surface here holds ARGB8888 that gets uploaded to an SDL texture and may be dumped
/// as an image in a test, so the blitter forces alpha to 0xFF on write. The colour bits are
/// bit-exact with the original; only the byte that was never displayed differs.
/// </para>
/// </remarks>
public static class Blitter
{
    /// <summary>
    /// The value of the destination weight that leaves the destination unchanged. 256, not 255 —
    /// CDX clamps to it and shifts by 8 (<c>cdxsurface.cpp:3021</c>).
    /// </summary>
    public const int DestinationWeightMax = 256;

    private const uint OpaqueAlpha = 0xFF000000;
    private const uint RgbMask = 0x00FFFFFF;

    /// <summary>
    /// Copies <paramref name="srcRect"/> to (<paramref name="dstX"/>, <paramref name="dstY"/>),
    /// honouring the source's colour key when its kind says to. This is
    /// <c>Graphics::BlitImage</c>, which chooses keyed or opaque from the surface <i>kind</i>
    /// rather than from an argument (<c>Shared/Graphics.cpp:2885</c>).
    /// </summary>
    /// <returns>False when clipping left nothing to draw.</returns>
    public static bool Blit(Surface dst, int dstX, int dstY, Surface src, SurfaceRect? srcRect = null)
        => src.IsKeyed
            ? BlitTransparent(dst, dstX, dstY, src, srcRect)
            : BlitOpaque(dst, dstX, dstY, src, srcRect);

    /// <summary>Copies pixels with no colour key, whatever the source's kind — <c>DrawBlk</c>.</summary>
    public static bool BlitOpaque(Surface dst, int dstX, int dstY, Surface src,
                                  SurfaceRect? srcRect = null)
    {
        if (!TryValidate(dst, ref dstX, ref dstY, src, srcRect, out var source))
        {
            return false;
        }

        for (int y = 0; y < source.Height; y++)
        {
            var from = src.Row(source.Top + y).Slice(source.Left, source.Width);
            var to = dst.Row(dstY + y).Slice(dstX, source.Width);
            for (int x = 0; x < from.Length; x++)
            {
                to[x] = from[x] | OpaqueAlpha;
            }
        }

        return true;
    }

    /// <summary>Copies pixels, skipping those equal to the source's colour key — <c>DrawTrans</c>.</summary>
    public static bool BlitTransparent(Surface dst, int dstX, int dstY, Surface src,
                                       SurfaceRect? srcRect = null)
    {
        if (!TryValidate(dst, ref dstX, ref dstY, src, srcRect, out var source))
        {
            return false;
        }

        // No key set means nothing to skip; DirectDraw would have blitted every pixel.
        uint key = (src.ColorKey ?? 0) & RgbMask;
        bool keyed = src.ColorKey.HasValue;

        for (int y = 0; y < source.Height; y++)
        {
            var from = src.Row(source.Top + y).Slice(source.Left, source.Width);
            var to = dst.Row(dstY + y).Slice(dstX, source.Width);
            for (int x = 0; x < from.Length; x++)
            {
                uint rgb = from[x] & RgbMask;
                if (!keyed || rgb != key)
                {
                    to[x] = rgb | OpaqueAlpha;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Alpha-blends the source over the destination with no colour key — <c>DrawBlkAlpha</c>
    /// (<c>cdxsurface.cpp:2960</c>). Reached from <c>Graphics::BlitAlphaImage</c> for opaque
    /// surface kinds, and from the alt-backdrop blend
    /// (<c>BackgroundSlotMemType::AlphaBlendPcnt</c>).
    /// </summary>
    /// <param name="destinationWeight">
    /// 0..<see cref="DestinationWeightMax"/>; 0 draws the source opaquely. Clamped, as CDX does.
    /// </param>
    public static bool BlitAlpha(Surface dst, int dstX, int dstY, Surface src,
                                 int destinationWeight, SurfaceRect? srcRect = null)
        => BlendCore(dst, dstX, dstY, src, destinationWeight, srcRect, useColorKey: false);

    /// <summary>
    /// Alpha-blends the source, skipping colour-keyed pixels — <c>DrawTransAlpha</c>
    /// (<c>cdxsurface.cpp:3327</c>). This is the path a sprite with
    /// <c>PIC_DATA::UseAlpha</c> takes.
    /// </summary>
    public static bool BlitTransparentAlpha(Surface dst, int dstX, int dstY, Surface src,
                                            int destinationWeight, SurfaceRect? srcRect = null)
        => BlendCore(dst, dstX, dstY, src, destinationWeight, srcRect, useColorKey: true);

    /// <summary>
    /// Scales the destination rectangle's pixels towards black — <c>DrawBlkShadow</c>
    /// (<c>cdxsurface.cpp:3720</c>), reached as <c>Graphics::DarkenDestSurface</c>.
    /// </summary>
    /// <remarks>
    /// The source surface is not read at all in the original; only its rectangle's size mattered,
    /// and <c>DarkenDestSurface</c> passes the destination as both. So the port takes no source.
    /// </remarks>
    /// <param name="shadow">
    /// 0..<see cref="DestinationWeightMax"/>: 0 blacks the rectangle out, 256 leaves it alone.
    /// </param>
    public static bool Darken(Surface dst, SurfaceRect rect, int shadow)
    {
        shadow = Math.Clamp(shadow, 0, DestinationWeightMax);

        if (!rect.TryClipTo(dst.ClipRect, out var target))
        {
            return false;
        }

        for (int y = target.Top; y < target.Bottom; y++)
        {
            var row = dst.Row(y);
            for (int x = target.Left; x < target.Right; x++)
            {
                uint d = row[x];
                uint b = (uint)((shadow * (int)(d & 0xFF)) >> 8);
                uint g = (uint)((shadow * (int)((d >> 8) & 0xFF)) >> 8);
                uint r = (uint)((shadow * (int)((d >> 16) & 0xFF)) >> 8);
                row[x] = OpaqueAlpha | (r << 16) | (g << 8) | b;
            }
        }

        return true;
    }

    /// <summary>
    /// Copies mirrored left-to-right — <c>DrawTransHFlip</c>/<c>DrawBlkHFlip</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine's parameter for this is called <c>flipY</c> (<c>Graphics::BlitImage</c>) but the
    /// call it makes is <c>DrawTransHFlip</c>, which asks DirectDraw for
    /// <c>DDBLTFX_MIRRORLEFTRIGHT</c> — a horizontal mirror. Only the engine uses it, to pose
    /// combat icons facing the other way. Named for what it does here.
    /// </para>
    /// <para>
    /// CDX's software fallback for this blit had an off-by-one that wrote one pixel past the right
    /// edge of the destination rectangle (<c>cdxsurface.cpp:2074</c>); it only ran when the
    /// hardware blit failed. This follows the DirectDraw path, which is what shipped.
    /// </para>
    /// </remarks>
    public static bool BlitMirrored(Surface dst, int dstX, int dstY, Surface src,
                                    SurfaceRect? srcRect = null)
    {
        if (!TryValidate(dst, ref dstX, ref dstY, src, srcRect, out var source))
        {
            return false;
        }

        uint key = (src.ColorKey ?? 0) & RgbMask;
        bool keyed = src.IsKeyed;

        for (int y = 0; y < source.Height; y++)
        {
            var from = src.Row(source.Top + y).Slice(source.Left, source.Width);
            var to = dst.Row(dstY + y).Slice(dstX, source.Width);
            for (int x = 0; x < from.Length; x++)
            {
                uint rgb = from[from.Length - 1 - x] & RgbMask;
                if (!keyed || rgb != key)
                {
                    to[x] = rgb | OpaqueAlpha;
                }
            }
        }

        return true;
    }

    private static bool BlendCore(Surface dst, int dstX, int dstY, Surface src,
                                  int destinationWeight, SurfaceRect? srcRect, bool useColorKey)
    {
        destinationWeight = Math.Clamp(destinationWeight, 0, DestinationWeightMax);

        if (!TryValidate(dst, ref dstX, ref dstY, src, srcRect, out var source))
        {
            return false;
        }

        uint key = (src.ColorKey ?? 0) & RgbMask;
        bool keyed = useColorKey && src.ColorKey.HasValue;

        for (int y = 0; y < source.Height; y++)
        {
            var from = src.Row(source.Top + y).Slice(source.Left, source.Width);
            var to = dst.Row(dstY + y).Slice(dstX, source.Width);
            for (int x = 0; x < from.Length; x++)
            {
                uint s = from[x];
                if (keyed && (s & RgbMask) == key)
                {
                    continue;
                }

                to[x] = Blend(s, to[x], destinationWeight);
            }
        }

        return true;
    }

    /// <summary>
    /// One pixel of CDX's blend: <c>((A * (dst - src)) >> 8) + src</c> per channel, with A the
    /// destination's weight. Exposed because it is the unit the golden-framebuffer tests pin.
    /// </summary>
    /// <remarks>
    /// The arithmetic shift on a negative difference is what makes this not a plain lerp: C++'s
    /// <c>>></c> on a negative int rounds towards negative infinity, and C#'s does too, so the
    /// transcription is exact only as long as the intermediate stays <c>int</c>.
    /// </remarks>
    public static uint Blend(uint src, uint dst, int destinationWeight)
    {
        int sb = (int)(src & 0xFF), db = (int)(dst & 0xFF);
        int sg = (int)((src >> 8) & 0xFF), dg = (int)((dst >> 8) & 0xFF);
        int sr = (int)((src >> 16) & 0xFF), dr = (int)((dst >> 16) & 0xFF);

        uint b = (uint)(((destinationWeight * (db - sb)) >> 8) + sb);
        uint g = (uint)(((destinationWeight * (dg - sg)) >> 8) + sg);
        uint r = (uint)(((destinationWeight * (dr - sr)) >> 8) + sr);

        return OpaqueAlpha | ((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF);
    }

    /// <summary>
    /// Clips the source rectangle to the source's clip rect, derives the destination rectangle,
    /// clips that to the destination's clip rect, and pushes the trimmed edges back onto the
    /// source — <c>CDXSurface::ValidateBlt</c> (<c>cdxsurface.cpp:1387</c>).
    /// </summary>
    /// <remarks>
    /// Doing it in this order is what lets the engine blit a tile that hangs off the left edge of
    /// the viewport: the destination clip trims the left edge, and the same amount is added to the
    /// source's left so the visible columns still line up.
    /// </remarks>
    private static bool TryValidate(Surface dst, ref int dstX, ref int dstY, Surface src,
                                    SurfaceRect? srcRect, out SurfaceRect source)
    {
        source = default;

        var requested = srcRect ?? src.Bounds;
        if (!requested.TryClipTo(src.ClipRect, out var clippedSource))
        {
            return false;
        }

        var wanted = SurfaceRect.FromBounds(dstX, dstY, clippedSource.Width, clippedSource.Height);
        if (!wanted.TryClipTo(dst.ClipRect, out var target))
        {
            return false;
        }

        source = new SurfaceRect(
            clippedSource.Left + (target.Left - wanted.Left),
            clippedSource.Top + (target.Top - wanted.Top),
            clippedSource.Right + (target.Right - wanted.Right),
            clippedSource.Bottom + (target.Bottom - wanted.Bottom));

        dstX = target.Left;
        dstY = target.Top;

        return !source.IsEmpty;
    }
}
