namespace UAF.Media;

/// <summary>
/// The result of decoding an art file: dimensions plus opaque ARGB8888 pixels, in the layout
/// <see cref="Surface"/> uses.
/// </summary>
/// <remarks>
/// Separate from <see cref="Surface"/> because a decoder has no business deciding a surface's
/// <see cref="SurfaceKind"/>, and the kind is what determines whether the colour key applies. The
/// engine knows the kind from the design record it read the filename out of; the decoder only knows
/// pixels.
/// </remarks>
public readonly record struct DecodedImage(int Width, int Height, uint[] Pixels)
{
    /// <summary>
    /// Wraps the pixels in a surface without copying them, adopting the top-left pixel as the
    /// colour key when <paramref name="kind"/> is a transparent one.
    /// </summary>
    /// <remarks>
    /// The conditional key is the whole reason this helper exists. A design's art declares its
    /// transparent colour by putting it at pixel (0,0) — see
    /// <see cref="Surface.SetColorKeyFromTopLeft"/> — and that fact appears nowhere in the file
    /// format, so every caller that loads keyed art would otherwise have to remember it. Setting
    /// the key on an opaque kind is harmless (<see cref="Surface.IsKeyed"/> consults
    /// <see cref="SurfaceKindExtensions.UsesTransparency"/> as well), but leaving it unset on a
    /// keyed kind gives every sprite an opaque rectangle.
    /// </remarks>
    public Surface ToSurface(SurfaceKind kind = SurfaceKind.Common)
    {
        var surface = Surface.FromPixels(Width, Height, Pixels, kind);
        if (kind.UsesTransparency())
        {
            surface.SetColorKeyFromTopLeft();
        }
        return surface;
    }
}
