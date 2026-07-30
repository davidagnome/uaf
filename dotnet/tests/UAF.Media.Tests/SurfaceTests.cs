namespace UAF.Media.Tests;

/// <summary>Covers the surface's own state: clipping, the colour key and the content hash.</summary>
public class SurfaceTests
{
    [Fact]
    public void ClipRectDefaultsToTheWholeSurface()
    {
        var surface = new Surface(8, 4);

        Assert.Equal(new SurfaceRect(0, 0, 8, 4), surface.ClipRect);
    }

    /// <summary>
    /// A clip rect wider than the surface would let a blit address outside the buffer, so it is
    /// clamped on the way in. DirectDraw would have failed the blit instead; an <c>IndexOutOfRange</c>
    /// mid-frame is a worse outcome than a clamped rectangle.
    /// </summary>
    [Fact]
    public void ClipRectIsClampedToTheSurface()
    {
        var surface = new Surface(8, 4)
        {
            ClipRect = new SurfaceRect(-5, -5, 100, 100),
        };

        Assert.Equal(new SurfaceRect(0, 0, 8, 4), surface.ClipRect);
    }

    [Fact]
    public void FillRectHonoursTheClipRect()
    {
        var surface = new Surface(4, 1);
        surface.ClipRect = new SurfaceRect(1, 0, 3, 1);

        surface.FillRect(surface.Bounds, 0x00FFFFFF);

        // A new surface is zero-filled, including its alpha byte -- only what the layer writes is
        // forced opaque.
        Assert.Equal(0u, surface[0, 0]);
        Assert.Equal(0xFFFFFFFFu, surface[1, 0]);
        Assert.Equal(0xFFFFFFFFu, surface[2, 0]);
        Assert.Equal(0u, surface[3, 0]);
    }

    /// <summary>
    /// Every pixel a surface writes is opaque. The blitter's ancestry wrote 24 bits and left the top
    /// byte at zero, which DirectDraw ignored but an ARGB texture does not.
    /// </summary>
    [Fact]
    public void FillForcesOpaqueAlpha()
    {
        var surface = new Surface(2, 2);

        surface.Fill(0x00123456);

        Assert.All(surface.Pixels, pixel => Assert.Equal(0xFF123456u, pixel));
    }

    [Fact]
    public void ContentHashChangesWithOnePixel()
    {
        var a = new Surface(4, 4);
        var b = new Surface(4, 4);

        Assert.Equal(a.ContentHash(), b.ContentHash());

        b[2, 2] = 1;
        Assert.NotEqual(a.ContentHash(), b.ContentHash());
    }

    [Fact]
    public void FromPixelsRejectsAMismatchedBuffer()
    {
        Assert.Throws<ArgumentException>(() => Surface.FromPixels(2, 2, new uint[3]));
    }

    [Theory]
    [InlineData(SurfaceKind.Sprite, true)]
    [InlineData(SurfaceKind.Wall, true)]
    [InlineData(SurfaceKind.Door, true)]
    [InlineData(SurfaceKind.Overlay, true)]
    [InlineData(SurfaceKind.Icon, true)]
    [InlineData(SurfaceKind.Font, true)]
    [InlineData(SurfaceKind.Mouse, true)]
    [InlineData(SurfaceKind.TransBuffer, true)]
    [InlineData(SurfaceKind.SpecialGraphicsTransparent, true)]
    [InlineData(SurfaceKind.Common, false)]
    [InlineData(SurfaceKind.Combat, false)]
    [InlineData(SurfaceKind.Background, false)]
    [InlineData(SurfaceKind.OutdoorCombat, false)]
    [InlineData(SurfaceKind.BigPic, false)]
    [InlineData(SurfaceKind.Map, false)]
    [InlineData(SurfaceKind.SmallPic, false)]
    [InlineData(SurfaceKind.Buffer, false)]
    [InlineData(SurfaceKind.Title, false)]
    [InlineData(SurfaceKind.SpecialGraphicsOpaque, false)]
    [InlineData(SurfaceKind.Bogus, false)]
    public void TransparencyMatchesGraphicsUseTransparency(SurfaceKind kind, bool expected)
    {
        // Transcribed case for case from Graphics::UseTransparency (Shared/Graphics.cpp:131). A
        // surface kind that silently changes side here is a visual bug in one design's art only.
        Assert.Equal(expected, kind.UsesTransparency());
    }

    [Fact]
    public void SurfaceKindValuesStillMatchTheOriginalBitSet()
    {
        // ReleaseSurfaceTypes frees whole categories by mask, so the numbers are load-bearing.
        Assert.Equal(1u, (uint)SurfaceKind.Common);
        Assert.Equal(2048u, (uint)SurfaceKind.Sprite);
        Assert.Equal(65536u, (uint)SurfaceKind.TransBuffer);
        Assert.Equal(0x40000u, (uint)SurfaceKind.SpecialGraphicsTransparent);
    }
}
