namespace UAF.Media;

/// <summary>How a face should be rasterised into an atlas.</summary>
/// <param name="PixelHeight">
/// The height the design asked for. A design's <c>LOGFONT.lfHeight</c> is negative when it means
/// character height rather than cell height, so callers pass the absolute value.
/// </param>
/// <param name="Antialias">
/// <para>
/// Off by default, and that is the faithful setting rather than the cheap one. The most common
/// face in the reference designs is <c>SYSTEM</c>, a Windows <i>raster</i> font with no
/// antialiasing at all, and the original's glyphs reach the screen through a colour key that
/// removes only pixels matching the background <b>exactly</b> — so antialiased edge pixels were
/// never transparent in the original either. They survived as fringes.
/// </para>
/// <para>
/// It also decides whether <see cref="BitmapFont"/>'s flat colour replacement is exact: with a
/// 1-bit glyph every non-key pixel is ink, so replacing it with the tint is precisely right. Turn
/// this on and the tint should become a coverage-weighted blend, or edges will look chunky.
/// </para>
/// </param>
public readonly record struct FontRasterOptions(
    int PixelHeight, bool Bold = false, bool Italic = false, bool Antialias = false);

/// <summary>
/// Turns a font file into a <see cref="FontAtlas"/> — the half of <c>CDXBitmapFont</c> that was
/// GDI, and the only part of the font layer that needs a platform.
/// </summary>
/// <remarks>
/// <para>
/// Same optional-native shape as <see cref="IVideoDecoderFactory"/> and
/// <see cref="IImageDecoder"/>: probed rather than assumed, so <c>UAF.Media</c> keeps working
/// without it. Unlike those two there is no managed fallback — a TrueType rasteriser is not
/// something to hand-roll — so a build with no rasteriser has no text, which is why
/// <see cref="IsAvailable"/> is worth checking at startup rather than at the first draw.
/// </para>
/// <para>
/// The interface takes font <i>bytes</i>, not a face name. Resolving a design's requested face
/// (<c>SYSTEM</c>, <c>Garamond</c>, …) to an actual file is a policy question that differs per
/// platform and belongs above this layer — and it is one the original already had, since it warned
/// "Cannot find specified font named %s" (<c>GlobalData.cpp:5846</c>) whenever Windows could not
/// resolve one either.
/// </para>
/// </remarks>
public interface IFontRasterizer
{
    /// <summary>Whether the rasteriser's native dependencies are present.</summary>
    bool IsAvailable { get; }

    /// <summary>A short description of why not, when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    /// <summary>Rasterises all 256 cells of a single-byte codepage into one sheet.</summary>
    FontAtlas Rasterize(ReadOnlySpan<byte> fontFile, FontRasterOptions options);
}
