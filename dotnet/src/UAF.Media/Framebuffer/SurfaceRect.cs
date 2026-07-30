namespace UAF.Media;

/// <summary>
/// A rectangle in pixels, with Win32 <c>RECT</c> semantics: <see cref="Left"/> and
/// <see cref="Top"/> are inclusive, <see cref="Right"/> and <see cref="Bottom"/> exclusive.
/// </summary>
/// <remarks>
/// Deliberately not <c>System.Drawing.Rectangle</c>. Every rectangle in the C++ tree is a
/// <c>RECT</c> and the blitter arithmetic is written in terms of <c>right - left</c>; keeping the
/// same four edges means the ports in <c>Drawtile.cpp</c> and <c>Viewport.cpp</c> transcribe
/// without an off-by-one at every call site.
/// </remarks>
public readonly record struct SurfaceRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>True when the rectangle encloses no pixels.</summary>
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public static SurfaceRect FromSize(int width, int height) => new(0, 0, width, height);

    public static SurfaceRect FromBounds(int x, int y, int width, int height) =>
        new(x, y, x + width, y + height);

    /// <summary>
    /// Clips this rectangle to <paramref name="bounds"/>, matching <c>CDXSurface::ClipRect</c>
    /// (<c>cdxsurface.cpp:1441</c>): returns false when the rectangle lies wholly outside, and
    /// otherwise trims each edge independently.
    /// </summary>
    public bool TryClipTo(SurfaceRect bounds, out SurfaceRect clipped)
    {
        clipped = this;

        if (Top >= bounds.Bottom || Left >= bounds.Right ||
            Bottom <= bounds.Top || Right <= bounds.Left)
        {
            return false;
        }

        clipped = new SurfaceRect(
            Math.Max(Left, bounds.Left),
            Math.Max(Top, bounds.Top),
            Math.Min(Right, bounds.Right),
            Math.Min(Bottom, bounds.Bottom));

        return !clipped.IsEmpty;
    }
}
