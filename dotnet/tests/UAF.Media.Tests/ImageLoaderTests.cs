namespace UAF.Media.Tests;

/// <summary>
/// Covers <see cref="ImageLoader"/>'s signature dispatch, ported from
/// <c>CDXImage::GetImage</c>'s <c>Validate</c> chain (<c>cdx/cdximage.cpp:246</c>).
/// </summary>
public class ImageLoaderTests
{
    [Fact]
    public void A_png_is_identified_and_decoded()
    {
        byte[] bytes = TestPng.Solid(2, 2, 0x11, 0x22, 0x33);

        Assert.Equal(ImageFormat.Png, ImageLoader.Identify(bytes));
        Assert.Equal(4, ImageLoader.Default.Decode(bytes).Pixels.Length);
    }

    [Theory]
    [InlineData(ImageFormat.Bmp, new byte[] { (byte)'B', (byte)'M', 0, 0, 0, 0 })]
    [InlineData(ImageFormat.Pcx, new byte[] { 0x0A, 0x05, 0x01, 0x08 })]
    [InlineData(ImageFormat.Psd, new byte[] { (byte)'8', (byte)'B', (byte)'P', (byte)'S' })]
    [InlineData(ImageFormat.Jpg, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    public void Other_formats_are_named_rather_than_called_corrupt(ImageFormat expected,
                                                                    byte[] header)
    {
        Assert.Equal(expected, ImageLoader.Identify(header));

        // The distinction that matters: "we cannot decode JPEGs yet" is a supported-formats
        // decision, "these bytes are nonsense" is a broken file. Collapsing them into one error
        // would make a missing decoder look like design corruption.
        var error = Assert.Throws<NotSupportedException>(() => ImageLoader.Default.Decode(header));
        Assert.Contains(expected.ToString(), error.Message);
    }

    [Fact]
    public void Unrecognised_bytes_are_reported_as_such()
    {
        byte[] noise = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05];

        Assert.Equal(ImageFormat.Unknown, ImageLoader.Identify(noise));
        Assert.Throws<InvalidDataException>(() => ImageLoader.Default.Decode(noise));
    }

    [Fact]
    public void Dispatch_follows_content_not_extension()
    {
        // The original sniffs bytes and never looks at the name, so a PNG called .bmp still loads.
        // Worth pinning: designs really do contain mislabelled art, and a port that trusted the
        // extension would reject files the engine accepts.
        using var file = TestPaths.Temp(".bmp", TestPng.Solid(3, 1, 7, 8, 9));

        var image = ImageLoader.Default.Load(file.Path);
        Assert.Equal(3, image.Width);
    }

    [Fact]
    public void A_failed_load_names_the_file()
    {
        // With 1300 art files, an error that does not say which one is nearly useless.
        using var file = TestPaths.Temp(".png", [0x00, 0x01, 0x02, 0x03]);

        var error = Assert.Throws<InvalidDataException>(() => ImageLoader.Default.Load(file.Path));
        Assert.Contains(Path.GetFileName(file.Path), error.Message);
    }

    [Fact]
    public void Loading_straight_to_a_surface_applies_the_kind()
    {
        using var file = TestPaths.Temp(".png", TestPng.Solid(2, 2, 0xFF, 0x00, 0xFF));

        var sprite = ImageLoader.Default.LoadSurface(file.Path, SurfaceKind.Sprite);

        Assert.Equal(SurfaceKind.Sprite, sprite.Kind);
        Assert.True(sprite.IsKeyed);
        Assert.Equal(0xFFFF00FFu, sprite.ColorKey);
    }

    [Fact]
    public void An_empty_file_is_rejected_rather_than_indexed_past()
    {
        Assert.Equal(ImageFormat.Unknown, ImageLoader.Identify([]));
        Assert.Throws<InvalidDataException>(() => ImageLoader.Default.Decode([]));
    }
}
