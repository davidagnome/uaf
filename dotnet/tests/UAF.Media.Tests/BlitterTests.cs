namespace UAF.Media.Tests;

/// <summary>
/// Pins the software blitter against the arithmetic in <c>src/cdx/cdxsurface.cpp</c>.
/// </summary>
/// <remarks>
/// The expected values here are computed by hand from the C++ formula rather than captured from a
/// previous run, so a change in the blitter that happens to be self-consistent still fails. The two
/// things most worth guarding are the direction of the alpha argument and the clipping's push-back
/// onto the source rectangle: both are silently plausible when wrong.
/// </remarks>
public class BlitterTests
{
    private const uint Key = 0x00FF00FF;      // magenta, the usual key colour in this art
    private const uint Opaque = 0xFF000000;

    private static Surface Filled(int width, int height, uint colour,
                                  SurfaceKind kind = SurfaceKind.Common)
    {
        var surface = new Surface(width, height, kind);
        surface.Fill(colour);
        return surface;
    }

    [Fact]
    public void OpaqueBlitCopiesEveryPixel()
    {
        var source = Filled(4, 4, 0x00112233);
        var destination = Filled(4, 4, 0x00000000);

        Assert.True(Blitter.BlitOpaque(destination, 0, 0, source));

        Assert.All(destination.Pixels, pixel => Assert.Equal(Opaque | 0x00112233, pixel));
    }

    [Fact]
    public void KeyedBlitLeavesDestinationWhereSourceMatchesTheKey()
    {
        var source = new Surface(2, 1, SurfaceKind.Sprite) { ColorKey = Key };
        source[0, 0] = Key;
        source[1, 0] = 0x00204060;

        var destination = Filled(2, 1, 0x00808080);

        Assert.True(Blitter.BlitTransparent(destination, 0, 0, source));

        Assert.Equal(Opaque | 0x00808080, destination[0, 0]);
        Assert.Equal(Opaque | 0x00204060, destination[1, 0]);
    }

    [Fact]
    public void KeyOnlyAppliesToKindsTheOriginalTreatsAsTransparent()
    {
        // Graphics::UseTransparency: a background is opaque even with a key set, a sprite is not.
        var background = new Surface(1, 1, SurfaceKind.Background) { ColorKey = Key };
        background[0, 0] = Key;

        var sprite = new Surface(1, 1, SurfaceKind.Sprite) { ColorKey = Key };
        sprite[0, 0] = Key;

        Assert.False(background.IsKeyed);
        Assert.True(sprite.IsKeyed);

        var destination = Filled(1, 1, 0x00010203);
        Blitter.Blit(destination, 0, 0, background);
        Assert.Equal(Opaque | Key, destination[0, 0]);

        destination.Fill(0x00010203);
        Blitter.Blit(destination, 0, 0, sprite);
        Assert.Equal(Opaque | 0x00010203, destination[0, 0]);
    }

    [Fact]
    public void ColourKeyIsTakenFromTheTopLeftPixel()
    {
        // CDXSurface::SetColorKey() with no argument. Nothing in the file format records the key.
        var surface = new Surface(2, 2, SurfaceKind.Wall);
        surface[0, 0] = 0x00123456;
        surface[1, 1] = 0x00654321;

        surface.SetColorKeyFromTopLeft();

        Assert.Equal(0x00123456u, surface.ColorKey);
    }

    /// <summary>
    /// The alpha argument is the <b>destination's</b> weight: 0 draws the source opaquely, 256 leaves
    /// the destination alone. Inverting it is the easiest possible mistake here and would look
    /// "nearly right" on mid-range values.
    /// </summary>
    [Theory]
    [InlineData(0, 0x40)]      // all source
    [InlineData(256, 0x80)]    // all destination
    [InlineData(128, 0x60)]    // halfway: 0x40 + ((128 * (0x80 - 0x40)) >> 8) = 0x60
    public void AlphaArgumentWeightsTheDestination(int weight, uint expectedChannel)
    {
        uint source = 0x00404040;
        uint destination = 0x00808080;

        uint blended = Blitter.Blend(source, destination, weight);

        uint expected = Opaque | (expectedChannel << 16) | (expectedChannel << 8) | expectedChannel;
        Assert.Equal(expected, blended);
    }

    [Fact]
    public void BlendMatchesTheOriginalOnADescendingChannel()
    {
        // src > dst exercises the arithmetic shift of a negative difference, which is where a
        // transcription using division or an unsigned type diverges.
        // b: 0xC0 + ((64 * (0x10 - 0xC0)) >> 8) = 0xC0 + ((64 * -176) >> 8) = 0xC0 - 44 = 0x94
        uint blended = Blitter.Blend(0x000000C0, 0x00000010, 64);

        Assert.Equal(0x94u, blended & 0xFF);
    }

    [Fact]
    public void AlphaBlitSkipsKeyedPixelsWhenTheSourceIsTransparent()
    {
        var source = new Surface(2, 1, SurfaceKind.Sprite) { ColorKey = Key };
        source[0, 0] = Key;
        source[1, 0] = 0x00404040;

        var destination = Filled(2, 1, 0x00808080);

        Assert.True(Blitter.BlitTransparentAlpha(destination, 0, 0, source, 128));

        Assert.Equal(Opaque | 0x00808080, destination[0, 0]);
        Assert.Equal(Opaque | 0x00606060, destination[1, 0]);
    }

    [Fact]
    public void DarkenScalesTheDestinationTowardsBlack()
    {
        // DrawBlkShadow: dst = (SHADOW * dst) >> 8.
        var destination = Filled(2, 2, 0x00808080);

        Assert.True(Blitter.Darken(destination, destination.Bounds, 128));

        Assert.All(destination.Pixels, pixel => Assert.Equal(Opaque | 0x00404040, pixel));
    }

    [Fact]
    public void DarkenWithZeroBlacksOutTheRectangleOnly()
    {
        var destination = Filled(4, 1, 0x00FFFFFF);

        Blitter.Darken(destination, new SurfaceRect(1, 0, 3, 1), 0);

        Assert.Equal(Opaque | 0x00FFFFFF, destination[0, 0]);
        Assert.Equal(Opaque, destination[1, 0]);
        Assert.Equal(Opaque, destination[2, 0]);
        Assert.Equal(Opaque | 0x00FFFFFF, destination[3, 0]);
    }

    /// <summary>
    /// A blit that hangs off the left edge must draw its right-hand columns at the edge, not shift the
    /// whole image inwards. This is what <c>ValidateBlt</c>'s push-back onto the source rectangle is
    /// for, and getting it wrong makes tiles at a viewport boundary slide.
    /// </summary>
    [Fact]
    public void ClippingTrimsTheSourceRatherThanMovingTheImage()
    {
        var source = new Surface(4, 1, SurfaceKind.Common);
        for (int x = 0; x < 4; x++)
        {
            source[x, 0] = (uint)(0x00010000 * (x + 1));
        }

        var destination = Filled(4, 1, 0);

        Assert.True(Blitter.BlitOpaque(destination, -2, 0, source));

        // Source columns 2 and 3 land at destination 0 and 1; the rest is untouched.
        Assert.Equal(Opaque | 0x00030000, destination[0, 0]);
        Assert.Equal(Opaque | 0x00040000, destination[1, 0]);
        Assert.Equal(Opaque, destination[2, 0]);
        Assert.Equal(Opaque, destination[3, 0]);
    }

    [Fact]
    public void ClippingTrimsTheRightEdgeToo()
    {
        var source = new Surface(4, 1, SurfaceKind.Common);
        source.Fill(0x00AABBCC);

        var destination = Filled(4, 1, 0);

        Assert.True(Blitter.BlitOpaque(destination, 2, 0, source));

        Assert.Equal(Opaque, destination[0, 0]);
        Assert.Equal(Opaque, destination[1, 0]);
        Assert.Equal(Opaque | 0x00AABBCC, destination[2, 0]);
        Assert.Equal(Opaque | 0x00AABBCC, destination[3, 0]);
    }

    [Fact]
    public void BlitWhollyOffSurfaceDrawsNothingAndReportsIt()
    {
        var source = Filled(2, 2, 0x00FFFFFF);
        var destination = Filled(4, 4, 0x00000001);

        Assert.False(Blitter.BlitOpaque(destination, 10, 0, source));
        Assert.False(Blitter.BlitOpaque(destination, 0, -10, source));
        Assert.All(destination.Pixels, pixel => Assert.Equal(Opaque | 0x00000001, pixel));
    }

    [Fact]
    public void ClipRectOnTheDestinationConfinesTheBlit()
    {
        var source = Filled(4, 4, 0x00FFFFFF);
        var destination = Filled(4, 4, 0);
        destination.ClipRect = new SurfaceRect(1, 1, 3, 3);

        Assert.True(Blitter.BlitOpaque(destination, 0, 0, source));

        Assert.Equal(Opaque, destination[0, 0]);
        Assert.Equal(Opaque | 0x00FFFFFF, destination[1, 1]);
        Assert.Equal(Opaque | 0x00FFFFFF, destination[2, 2]);
        Assert.Equal(Opaque, destination[3, 3]);
    }

    [Fact]
    public void MirroredBlitReversesColumns()
    {
        var source = new Surface(3, 1, SurfaceKind.Icon);
        source[0, 0] = 0x00000001;
        source[1, 0] = 0x00000002;
        source[2, 0] = 0x00000003;

        var destination = Filled(3, 1, 0);

        Assert.True(Blitter.BlitMirrored(destination, 0, 0, source));

        Assert.Equal(Opaque | 0x00000003, destination[0, 0]);
        Assert.Equal(Opaque | 0x00000002, destination[1, 0]);
        Assert.Equal(Opaque | 0x00000001, destination[2, 0]);
    }

    [Fact]
    public void SourceRectSelectsASubImage()
    {
        var source = new Surface(4, 2, SurfaceKind.Common);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                source[x, y] = (uint)((y * 4) + x + 1);
            }
        }

        var destination = Filled(2, 1, 0);

        Assert.True(Blitter.BlitOpaque(destination, 0, 0, source, new SurfaceRect(2, 1, 4, 2)));

        Assert.Equal(Opaque | 7, destination[0, 0]);
        Assert.Equal(Opaque | 8, destination[1, 0]);
    }

    /// <summary>
    /// CDX clamps the alpha to 0..256 before shifting, so a design storing a nonsense
    /// <c>AlphaValue</c> or <c>BlendAmount</c> must not wrap round to the opposite blend.
    /// </summary>
    [Theory]
    [InlineData(-50, 0x40)]                                     // clamps to 0: all source
    [InlineData(5000, 0x80)]                                    // clamps to 256: all destination
    public void AlphaIsClampedToTheOriginalsRange(int weight, uint expectedChannel)
    {
        var source = Filled(1, 1, 0x00404040);
        var destination = Filled(1, 1, 0x00808080);

        Assert.True(Blitter.BlitAlpha(destination, 0, 0, source, weight));

        Assert.Equal(expectedChannel, destination[0, 0] & 0xFF);
    }
}
