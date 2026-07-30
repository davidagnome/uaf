namespace UAF.Media.Tests;

/// <summary>
/// Drives <see cref="PngDecoder"/> down the paths the reference corpus cannot reach.
/// </summary>
/// <remarks>
/// <see cref="PngOracleTests"/> is the real proof of correctness — 1312 real files diffed against
/// libpng. What it cannot cover is everything the designers' tools never emitted: greyscale,
/// 16-bit, sub-8-bit palettes, and four of the five row filters. Those are exercised here against
/// bytes authored by <see cref="TestPng"/>.
/// </remarks>
public class PngDecoderTests
{
    private static uint Argb(byte r, byte g, byte b) =>
        0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

    [Fact]
    public void Truecolour_round_trips_through_the_simplest_possible_file()
    {
        var image = PngDecoder.Decode(TestPng.Solid(4, 3, 0x12, 0x34, 0x56));

        Assert.Equal(4, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(12, image.Pixels.Length);
        Assert.All(image.Pixels, p => Assert.Equal(Argb(0x12, 0x34, 0x56), p));
    }

    [Theory]
    [InlineData(0)]  // None
    [InlineData(1)]  // Sub
    [InlineData(2)]  // Up
    [InlineData(3)]  // Average
    [InlineData(4)]  // Paeth
    public void Every_row_filter_reverses_to_the_same_pixels(int filter)
    {
        // A gradient, not a solid fill: with a flat image, Sub and Paeth both produce all-zero
        // filtered bytes, so a decoder that ignored the filter byte entirely would still pass.
        var rows = new List<byte[]>();
        for (int y = 0; y < 8; y++)
        {
            var row = new byte[8 * 3];
            for (int x = 0; x < 8; x++)
            {
                row[x * 3] = (byte)(x * 31);
                row[(x * 3) + 1] = (byte)(y * 17);
                row[(x * 3) + 2] = (byte)((x * y) + 3);
            }
            rows.Add(row);
        }

        var image = PngDecoder.Decode(TestPng.Build(8, 8, 8, 2, rows, filter: filter));

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal(Argb((byte)(x * 31), (byte)(y * 17), (byte)((x * y) + 3)),
                             image.Pixels[(y * 8) + x]);
            }
        }
    }

    [Fact]
    public void Alpha_is_stripped_rather_than_composited()
    {
        // A fully transparent red pixel. Compositing against anything would yield black or white;
        // the engine keeps the colour and discards the alpha entirely.
        List<byte[]> rows = [[0xFF, 0x00, 0x00, 0x00]];
        var image = PngDecoder.Decode(TestPng.Build(1, 1, 8, 6, rows));

        Assert.Equal(Argb(0xFF, 0, 0), image.Pixels[0]);
    }

    [Fact]
    public void Greyscale_expands_to_equal_channels()
    {
        List<byte[]> rows = [[0x00, 0x40, 0x80, 0xFF]];
        var image = PngDecoder.Decode(TestPng.Build(4, 1, 8, 0, rows));

        Assert.Equal([Argb(0, 0, 0), Argb(0x40, 0x40, 0x40),
                      Argb(0x80, 0x80, 0x80), Argb(0xFF, 0xFF, 0xFF)], image.Pixels);
    }

    [Theory]
    [InlineData(1, new byte[] { 0b10100000 }, new byte[] { 255, 0, 255, 0 })]
    [InlineData(2, new byte[] { 0b00011011 }, new byte[] { 0, 85, 170, 255 })]
    [InlineData(4, new byte[] { 0x0F, 0x8A }, new byte[] { 0, 255, 136, 170 })]
    public void Sub_byte_greyscale_is_scaled_to_the_full_range(int bitDepth, byte[] packed,
                                                               byte[] expected)
    {
        // Scaling, not left-shifting: at 1 bit a sample of 1 must become 255, not 1. libpng's
        // expansion multiplies by 255/max, which is why 2-bit 1 is 85 and not 64.
        var image = PngDecoder.Decode(TestPng.Build(4, 1, bitDepth, 0, [packed]));

        Assert.Equal(expected.Select(v => Argb(v, v, v)).ToArray(), image.Pixels);
    }

    [Fact]
    public void Palette_indices_are_looked_up_not_scaled()
    {
        byte[] palette = [0xFF, 0x00, 0x00,   // 0 red
                          0x00, 0xFF, 0x00,   // 1 green
                          0x00, 0x00, 0xFF];  // 2 blue
        var image = PngDecoder.Decode(TestPng.Build(3, 1, 8, 3, [[2, 0, 1]], palette));

        Assert.Equal([Argb(0, 0, 0xFF), Argb(0xFF, 0, 0), Argb(0, 0xFF, 0)], image.Pixels);
    }

    [Fact]
    public void Sub_byte_palette_indices_are_not_scaled()
    {
        // The trap this guards: greyscale samples get scaled to the full range, palette indices
        // must not. A 4-bit index of 1 is entry 1, not entry 17.
        byte[] palette = [0x11, 0x11, 0x11, 0x22, 0x22, 0x22, 0x33, 0x33, 0x33];
        var image = PngDecoder.Decode(TestPng.Build(2, 1, 4, 3, [[0x12]], palette));

        Assert.Equal([Argb(0x22, 0x22, 0x22), Argb(0x33, 0x33, 0x33)], image.Pixels);
    }

    [Fact]
    public void Sixteen_bit_samples_are_chopped_not_rescaled()
    {
        // png_set_strip_16 drops the low byte; png_set_scale_16 would round to 0x81 here. The C++
        // calls the former (cdximagepng.cpp:107).
        List<byte[]> rows = [[0x80, 0xFF, 0x40, 0x00, 0x12, 0x34]];
        var image = PngDecoder.Decode(TestPng.Build(1, 1, 16, 2, rows));

        Assert.Equal(Argb(0x80, 0x40, 0x12), image.Pixels[0]);
    }

    [Fact]
    public void Gamma_matching_the_default_is_a_no_op()
    {
        // 0.45455 x 2.2 = 1.00001. Both the declared-default and the absent case must leave pixels
        // untouched, which is what makes 1302 of the 1312 shipped files byte-identical.
        var declared = PngDecoder.Decode(TestPng.Solid(2, 2, 0x80, 0x40, 0x20, gamma: 45455));
        var absent = PngDecoder.Decode(TestPng.Solid(2, 2, 0x80, 0x40, 0x20));

        Assert.Equal(Argb(0x80, 0x40, 0x20), declared.Pixels[0]);
        Assert.Equal(absent.Pixels, declared.Pixels);
    }

    [Fact]
    public void A_significant_gamma_lightens_midtones()
    {
        // The value SomethingWild's art declares. Exponent 1/(0.55531 x 2.2) = 0.8185, so 128 rises
        // to 145. The direction is the whole point: the reciprocal convention would darken it to
        // about 109, and every file in the corpus that could tell them apart is one of these ten.
        var image = PngDecoder.Decode(TestPng.Solid(1, 1, 128, 0, 255, gamma: 55531));

        Assert.Equal(145, (byte)(image.Pixels[0] >> 16));

        // The endpoints are fixed under any power law, which is a useful invariant: it means a
        // wrong exponent can never show up as clipping, only as shifted midtones.
        Assert.Equal(0, (byte)(image.Pixels[0] >> 8));
        Assert.Equal(255, (byte)image.Pixels[0]);
    }

    [Fact]
    public void A_zero_gamma_falls_back_to_the_default_instead_of_dividing_by_it()
    {
        // SomethingWild's avz_outdoors.png really does carry gAMA = 0. libpng rejects the chunk and
        // uses the default; treating it as an exponent would divide by zero.
        var image = PngDecoder.Decode(TestPng.Solid(1, 1, 128, 64, 32, gamma: 0));

        Assert.Equal(Argb(128, 64, 32), image.Pixels[0]);
    }

    [Fact]
    public void Interlaced_files_are_refused_by_name()
    {
        var bytes = TestPng.Build(2, 1, 8, 2, [[1, 2, 3, 4, 5, 6]], interlaced: true);

        var error = Assert.Throws<NotSupportedException>(() => PngDecoder.Decode(bytes));
        Assert.Contains("interlaced", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 }, "signature")]
    public void Non_png_input_is_rejected(byte[] bytes, string expected)
    {
        var error = Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(bytes));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_truncated_chunk_is_reported_rather_than_read_past_the_end()
    {
        byte[] good = TestPng.Solid(2, 2, 1, 2, 3);
        byte[] truncated = good[..(good.Length - 20)];

        Assert.ThrowsAny<InvalidDataException>(() => PngDecoder.Decode(truncated));
    }

    [Fact]
    public void A_palette_image_with_no_PLTE_is_reported()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => PngDecoder.Decode(TestPng.Build(1, 1, 8, 3, [[0]])));

        Assert.Contains("PLTE", error.Message);
    }

    [Fact]
    public void An_out_of_range_palette_index_is_reported()
    {
        byte[] palette = [0xFF, 0x00, 0x00];   // one entry
        var error = Assert.Throws<InvalidDataException>(
            () => PngDecoder.Decode(TestPng.Build(1, 1, 8, 3, [[7]], palette)));

        Assert.Contains("palette index 7", error.Message);
    }

    [Fact]
    public void An_illegal_depth_for_the_colour_type_is_reported()
    {
        // 2-bit truecolour is not a legal combination; the spec allows only 8 and 16 there.
        var error = Assert.Throws<InvalidDataException>(
            () => PngDecoder.Decode(TestPng.Build(1, 1, 2, 2, [[0]])));

        Assert.Contains("bit depth 2", error.Message);
    }

    [Fact]
    public void A_surface_from_a_keyed_kind_adopts_the_top_left_pixel()
    {
        // The colour key convention is invisible in the file format, so this is the only place it
        // is checked: a sprite's transparent colour is whatever sits at (0,0).
        List<byte[]> rows =
        [
            [0xFF, 0x00, 0xFF,  0x10, 0x20, 0x30],
            [0x40, 0x50, 0x60,  0x70, 0x80, 0x90],
        ];
        var image = PngDecoder.Decode(TestPng.Build(2, 2, 8, 2, rows));

        var sprite = image.ToSurface(SurfaceKind.Sprite);
        Assert.True(sprite.IsKeyed);
        Assert.Equal(Argb(0xFF, 0x00, 0xFF), sprite.ColorKey);

        // The same pixels as an opaque kind must not become keyed, or backgrounds would develop
        // transparent holes wherever they happened to reuse their corner colour.
        var background = image.ToSurface(SurfaceKind.Background);
        Assert.False(background.IsKeyed);
    }

    [Fact]
    public void A_decoded_surface_aliases_the_decoded_pixels()
    {
        // Surface.FromPixels adopts rather than copies; art is large enough that a copy per load
        // would be pure waste. Documented here because it is a real aliasing contract.
        var image = PngDecoder.Decode(TestPng.Solid(2, 2, 9, 9, 9));
        var surface = image.ToSurface();

        Assert.Same(image.Pixels, surface.Pixels);
    }
}
