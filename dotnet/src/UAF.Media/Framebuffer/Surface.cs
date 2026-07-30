namespace UAF.Media;

/// <summary>
/// A software surface: a managed 32-bit pixel buffer plus the two pieces of state DirectDraw kept
/// alongside one, a source colour key and a clip rectangle.
/// </summary>
/// <remarks>
/// <para>
/// Pixels are ARGB8888 in host byte order, one <c>uint</c> each, packed 0xAARRGGBB. That is the
/// layout <c>SDL_PIXELFORMAT_ARGB8888</c> expects for a streaming texture, so presenting a surface
/// is a single upload with no format conversion (see the spike, <c>dotnet/spike/Sdl3Spike</c>).
/// The original ran at 15/16/24/32bpp depending on the display mode; the port fixes 32 because a
/// managed framebuffer has no reason to carry four blitters, and the 32bpp arithmetic is the one
/// case CDX implemented for every operation.
/// </para>
/// <para>
/// There is no pitch. The original had to cope with a DirectDraw surface whose stride exceeded its
/// width; a <c>uint[]</c> never does, so stride is always <see cref="Width"/>. Anything that reads
/// <c>lPitch</c> in the C++ becomes a row index here.
/// </para>
/// </remarks>
public sealed class Surface
{
    private const uint OpaqueAlpha = 0xFF000000;

    private SurfaceRect clipRect;

    public Surface(int width, int height, SurfaceKind kind = SurfaceKind.Common)
        : this(width, height, new uint[width * height], kind)
    {
    }

    private Surface(int width, int height, uint[] pixels, SurfaceKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        Kind = kind;
        Pixels = pixels;
        clipRect = SurfaceRect.FromSize(width, height);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>What the surface holds, which is also what decides whether blits from it are keyed.</summary>
    public SurfaceKind Kind { get; set; }

    /// <summary>
    /// The pixel buffer, row-major, <see cref="Width"/> entries per row. Exposed directly because
    /// the engine's tile and viewport code writes pixels in bulk and a property call per pixel
    /// would dominate the frame.
    /// </summary>
    public uint[] Pixels { get; }

    /// <summary>The surface's full extent, ignoring the clip rectangle.</summary>
    public SurfaceRect Bounds => SurfaceRect.FromSize(Width, Height);

    /// <summary>
    /// The clip rectangle every blit is validated against, as <c>CDXSurface::SetClipRect</c> did.
    /// Defaults to the whole surface and is itself clamped to it, so it can never let a blit
    /// address outside the buffer.
    /// </summary>
    public SurfaceRect ClipRect
    {
        get => clipRect;
        set => clipRect = value.TryClipTo(Bounds, out var clamped) ? clamped : default;
    }

    /// <summary>
    /// The colour treated as transparent by keyed blits, or null for none. Only consulted when
    /// <see cref="SurfaceKind.UsesTransparency"/> holds for <see cref="Kind"/>.
    /// </summary>
    public uint? ColorKey { get; set; }

    /// <summary>Whether blits from this surface skip <see cref="ColorKey"/> pixels.</summary>
    public bool IsKeyed => ColorKey.HasValue && Kind.UsesTransparency();

    public Span<uint> Row(int y) => Pixels.AsSpan(y * Width, Width);

    public uint this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }

    /// <summary>
    /// Adopts the top-left pixel as the colour key, which is what <c>CDXSurface::SetColorKey()</c>
    /// with no argument does (<c>cdxsurface.cpp:5661</c>).
    /// </summary>
    /// <remarks>
    /// Worth stating plainly because it is invisible in the file format and impossible to guess:
    /// a design's art declares its transparent colour by putting it at pixel (0,0). Loading art
    /// for a keyed surface kind and forgetting this call produces a picture with an opaque
    /// rectangle around every sprite.
    /// </remarks>
    public void SetColorKeyFromTopLeft() => ColorKey = Pixels[0];

    /// <summary>
    /// Clears the whole surface. The alpha byte is forced opaque to hold the invariant described
    /// on <see cref="Blitter"/>; callers pass colours as 0x00RRGGBB or 0xFFRRGGBB indifferently,
    /// as <c>Graphics::ClearSurface(key, color)</c> does with its plain <c>DWORD</c>.
    /// </summary>
    public void Fill(uint argb) => Array.Fill(Pixels, argb | OpaqueAlpha);

    /// <summary>Fills <paramref name="rect"/>, clipped to <see cref="ClipRect"/>.</summary>
    public void FillRect(SurfaceRect rect, uint argb)
    {
        if (!rect.TryClipTo(ClipRect, out var target))
        {
            return;
        }

        for (int y = target.Top; y < target.Bottom; y++)
        {
            Row(y).Slice(target.Left, target.Width).Fill(argb | OpaqueAlpha);
        }
    }

    /// <summary>
    /// Adopts an existing buffer without copying it — for decoded video frames and art loaders, which
    /// already own a correctly sized array.
    /// </summary>
    /// <remarks>
    /// The caller must not keep writing to <paramref name="pixels"/> afterwards: the surface aliases
    /// it. Adoption rather than a copy because a movie frame at viewport size is over a megabyte, and
    /// copying every one of them just to hand it to the blitter would be pure waste.
    /// </remarks>
    public static Surface FromPixels(int width, int height, uint[] pixels,
                                     SurfaceKind kind = SurfaceKind.Common)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException(
                $"buffer holds {pixels.Length} pixels, expected {width * height}", nameof(pixels));
        }

        return new Surface(width, height, pixels, kind);
    }

    /// <summary>
    /// An FNV-1a hash of the pixels, for the golden-framebuffer tests the plan's testing strategy
    /// (section 8, item 3) is built on. Cheap, stable across runs and platforms, and not a
    /// cryptographic claim.
    /// </summary>
    public ulong ContentHash()
    {
        ulong hash = 14695981039346656037;
        foreach (uint pixel in Pixels)
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(pixel >> shift);
                hash *= 1099511628211;
            }
        }
        return hash;
    }
}
