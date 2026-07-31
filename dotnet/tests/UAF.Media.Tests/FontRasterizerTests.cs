using UAF.Media.Sdl;

namespace UAF.Media.Tests;

/// <summary>
/// Covers <see cref="SdlFontRasterizer"/> and the bundled fallback face.
/// </summary>
/// <remarks>
/// These run headless — SDL3_ttf needs no video device — so unlike the art-corpus tests they are
/// meaningful on a bare CI runner.
/// </remarks>
public class FontRasterizerTests
{
    private static FontAtlas Rasterize(int px, bool antialias = false)
    {
        using var rasterizer = new SdlFontRasterizer();
        Assert.True(rasterizer.IsAvailable, rasterizer.UnavailableReason ?? "no reason given");
        return rasterizer.Rasterize(EmbeddedFonts.Default,
                                    new FontRasterOptions(px, Antialias: antialias));
    }

    [Fact]
    public void The_rasterizer_reports_itself_available()
    {
        using var rasterizer = new SdlFontRasterizer();

        Assert.True(rasterizer.IsAvailable, rasterizer.UnavailableReason ?? "no reason given");
        Assert.Null(rasterizer.UnavailableReason);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Every_bundled_style_is_embedded_and_is_a_truetype_file(bool bold, bool italic)
    {
        byte[] font = EmbeddedFonts.PtSerif(bold, italic);

        Assert.NotEmpty(font);

        // 0x00010000 is the TrueType outline sfnt version.
        Assert.Equal([0x00, 0x01, 0x00, 0x00], font[..4]);
    }

    [Fact]
    public void The_four_styles_are_four_different_files()
    {
        // Guards the resource-name mapping: a slip there would silently serve the regular face for
        // every style, and that is easy not to notice.
        var lengths = new[]
        {
            EmbeddedFonts.PtSerif(false, false).Length,
            EmbeddedFonts.PtSerif(true, false).Length,
            EmbeddedFonts.PtSerif(false, true).Length,
            EmbeddedFonts.PtSerif(true, true).Length,
        };

        Assert.Equal(4, lengths.Distinct().Count());
    }

    [Fact]
    public void Real_bold_differs_from_the_regular_face_rather_than_being_emboldened()
    {
        using var rasterizer = new SdlFontRasterizer();
        var regular = rasterizer.Rasterize(EmbeddedFonts.PtSerif(), new FontRasterOptions(16));
        var bold = rasterizer.Rasterize(EmbeddedFonts.PtSerif(bold: true),
                                        new FontRasterOptions(16));

        // A drawn bold has its own advances; a synthesised one reuses the regular's.
        Assert.NotEqual(regular[(byte)'M'].Advance, bold[(byte)'M'].Advance);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(16)]
    [InlineData(24)]
    public void An_atlas_has_a_cell_for_every_codepage_byte(int pixelHeight)
    {
        var atlas = Rasterize(pixelHeight);

        Assert.Equal(FontAtlas.DefaultSheetWidth, atlas.Sheet.Width);
        Assert.True(atlas.MaxCharHeight > 0);

        // Letters and digits must all have drawable cells; a face that failed to load would give
        // 256 empty ones and still construct.
        foreach (byte c in "AZaz09"u8)
        {
            Assert.True(atlas[c].Width > 0, $"'{(char)c}' has no cell");
            Assert.True(atlas[c].Advance > 0, $"'{(char)c}' has no advance");
        }

        Assert.True(atlas.MaxCharHeight >= pixelHeight / 2,
            $"max cell height {atlas.MaxCharHeight} is implausible for a {pixelHeight}px face");
    }

    [Fact]
    public void A_space_has_an_advance_even_though_it_draws_nothing()
    {
        // The trap this guards: the advance comes from TTF_GetStringSize, not from the rendered
        // surface's width. A space renders to an empty bitmap, so taking the surface width would
        // give it a zero advance and collapse every gap between words in the game.
        var atlas = Rasterize(16);

        Assert.True(atlas[(byte)' '].Advance > 0, "space has no advance");
    }

    [Fact]
    public void Without_antialiasing_every_pixel_is_fully_on_or_fully_off()
    {
        var atlas = Rasterize(16, antialias: false);

        // This is what makes BitmapFont's tint an exact replacement rather than an approximation.
        var levels = new HashSet<uint>();
        foreach (uint pixel in atlas.Sheet.Pixels)
        {
            levels.Add(pixel & 0xFF);
        }

        Assert.Equal([0u, 255u], levels.OrderBy(v => v).ToArray());
    }

    [Fact]
    public void With_antialiasing_the_sheet_carries_intermediate_coverage()
    {
        var atlas = Rasterize(16, antialias: true);

        var levels = new HashSet<uint>();
        foreach (uint pixel in atlas.Sheet.Pixels)
        {
            levels.Add(pixel & 0xFF);
        }

        // Not just 0 and 255. An earlier revision thresholded coverage, which produced text that
        // was dilated by about a pixel rather than antialiased -- and passed every other test here.
        Assert.True(levels.Count > 2,
            $"only {levels.Count} coverage levels; antialiasing is being thresholded away");
        Assert.Contains(levels, v => v is > 0 and < 255);
    }

    [Fact]
    public void Partial_coverage_blends_toward_the_destination()
    {
        // A one-cell atlas built by hand, so the coverage value is known exactly.
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (1, 1));
        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int height);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, height, SurfaceKind.Font);
        sheet.Fill(0xFF000000);
        sheet.ColorKey = 0xFF000000;

        // Half coverage for 'A'.
        var cell = glyphs[(byte)'A'].Source;
        sheet[cell.Left, cell.Top] = 0xFF808080;

        var font = new BitmapFont(new FontAtlas(sheet, glyphs));
        var target = new Surface(4, 4);
        target.Fill(0xFF000000);

        font.Draw(target, 0, 0, "A", tint: 0x00FFFFFF);

        // 128/255 of white over black lands near mid-grey, not at either end.
        byte channel = (byte)(target[0, 0] >> 16);
        Assert.InRange(channel, 120, 136);
    }

    [Fact]
    public void The_face_covers_the_whole_windows_1252_repertoire()
    {
        // Worth pinning, because the obvious way to audit this is wrong and this project got it
        // wrong once. Bytes 0x80-0x9F look absent if you check Unicode U+0080-U+009F -- those are
        // C1 control characters and no font has them -- but Windows-1252 maps that block to
        // U+2018..U+2026, which PT Serif does have. What matters is whether each byte renders as
        // its own glyph rather than as .notdef.
        var atlas = Rasterize(16);

        // Distinct advances prove distinct glyphs: a byte the face lacked would fall back to
        // .notdef, and every such byte would then share one advance and one bitmap.
        Assert.NotEqual(atlas[0x97].Advance, atlas[(byte)'-'].Advance);   // em dash is not a hyphen
        Assert.NotEqual(atlas[0x92].Advance, atlas[0x97].Advance);        // quote is not a dash
        Assert.NotEqual(atlas[0x85].Advance, atlas[(byte)'.'].Advance);   // ellipsis is not a dot

        // Every defined byte in the codepage draws something.
        foreach (int value in Enumerable.Range(0x21, 0xDF))
        {
            byte b = (byte)value;
            if (b is 0xA0 or 0x7F or 0x81 or 0x8D or 0x8F or 0x90 or 0x9D)
            {
                continue;   // no-break space, DEL, and the five bytes cp1252 leaves undefined
            }

            Assert.True(atlas[b].Width > 0, $"0x{b:X2} has no cell");
        }
    }

    [Fact]
    public void A_disposed_rasterizer_refuses_further_work()
    {
        var rasterizer = new SdlFontRasterizer();
        rasterizer.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => rasterizer.Rasterize(EmbeddedFonts.Default,
                                       new FontRasterOptions(16)));
    }

    [Fact]
    public void Bigger_requests_produce_bigger_cells()
    {
        // Cheap, but it is the one assertion that would catch the size argument being dropped or
        // misinterpreted as points rather than pixels.
        var small = Rasterize(12);
        var large = Rasterize(24);

        Assert.True(large.MaxCharHeight > small.MaxCharHeight);
        Assert.True(large[(byte)'M'].Advance > small[(byte)'M'].Advance);
    }
}
