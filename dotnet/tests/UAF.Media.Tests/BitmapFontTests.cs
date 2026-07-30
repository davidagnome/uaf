namespace UAF.Media.Tests;

/// <summary>
/// Covers the half of <c>CDXBitmapFont</c> that has nothing to do with GDI: cell packing,
/// measurement, drawing, clipping and alignment.
/// </summary>
/// <remarks>
/// The atlases here are authored rather than rasterised, which is the point — this layer's
/// behaviour must not depend on where the glyph pixels came from, and testing it against synthetic
/// cells is what proves that.
/// </remarks>
public class BitmapFontTests
{
    private const uint Key = 0xFF000000;      // black, the atlas colour key
    private const uint Ink = 0xFFFFFFFF;      // white glyph pixels

    /// <summary>
    /// An atlas where character <c>c</c> is a solid block <c>(c % 5) + 1</c> pixels wide, so
    /// advances differ per character and a mistake in indexing shows up as a wrong width.
    /// </summary>
    private static FontAtlas BuildAtlas(int height = 4)
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        for (int i = 0; i < FontAtlas.CharacterCount; i++)
        {
            extents[i] = ((i % 5) + 1, height);
        }

        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, sheetHeight, SurfaceKind.Font);
        sheet.Fill(Key);
        sheet.ColorKey = Key;

        // Fill each cell with ink so a blit is visible, leaving the sheet's slack keyed.
        foreach (var glyph in glyphs)
        {
            for (int y = glyph.Source.Top; y < glyph.Source.Bottom; y++)
            {
                for (int x = glyph.Source.Left; x < glyph.Source.Right; x++)
                {
                    sheet[x, y] = Ink;
                }
            }
        }

        return new FontAtlas(sheet, glyphs);
    }

    private static int ExpectedWidth(string text) =>
        BitmapFont.Encode(text).Sum(c => (c % 5) + 1);

    [Fact]
    public void Layout_packs_cells_left_to_right_and_wraps_at_the_sheet_width()
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (100, 10));

        var glyphs = FontAtlas.Layout(extents, 320, out int sheetHeight);

        // Three 100px cells fit in 320; the fourth wraps.
        Assert.Equal(0, glyphs[0].Source.Left);
        Assert.Equal(100, glyphs[1].Source.Left);
        Assert.Equal(200, glyphs[2].Source.Left);
        Assert.Equal(0, glyphs[3].Source.Left);
        Assert.Equal(10, glyphs[3].Source.Top);

        // 256 cells at 3 per row is 86 rows, and the original adds 5 for the last row's descenders.
        Assert.Equal((86 * 10) + 5, sheetHeight);
    }

    [Fact]
    public void Layout_never_lets_a_cell_straddle_the_right_edge()
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (7, 3));

        var glyphs = FontAtlas.Layout(extents, 20, out _);

        Assert.All(glyphs, g => Assert.True(g.Source.Right <= 20,
            $"cell ends at {g.Source.Right}, past the 20px sheet"));
    }

    [Fact]
    public void A_glyph_wider_than_the_sheet_is_placed_rather_than_looping_forever()
    {
        // Degenerate, but the wrap test is "x > 0" precisely so an over-wide cell still advances.
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (50, 2));

        var glyphs = FontAtlas.Layout(extents, 10, out _);

        Assert.Equal(0, glyphs[0].Source.Left);
        Assert.Equal(0, glyphs[1].Source.Left);
        Assert.Equal(2, glyphs[1].Source.Top);
    }

    [Fact]
    public void Text_width_is_the_sum_of_advances()
    {
        var font = new BitmapFont(BuildAtlas());

        Assert.Equal(ExpectedWidth("Hello"), font.GetTextWidth("Hello"));
        Assert.Equal(0, font.GetTextWidth(string.Empty));
    }

    [Fact]
    public void A_negative_length_means_unlimited()
    {
        // Not a convenience: the original counts down and tests "length != 0", so -1 walks away
        // from zero and never stops the loop. Call sites depend on it.
        var font = new BitmapFont(BuildAtlas());

        Assert.Equal(font.GetTextWidth("Hello"), font.GetTextWidth("Hello", -1));
        Assert.Equal(font.GetTextWidth("Hello"), font.GetTextWidth("Hello", -99));
    }

    [Fact]
    public void A_positive_length_measures_only_that_many_characters()
    {
        var font = new BitmapFont(BuildAtlas());

        Assert.Equal(ExpectedWidth("Hel"), font.GetTextWidth("Hello", 3));
        Assert.Equal(0, font.GetTextWidth("Hello", 0));
    }

    [Fact]
    public void High_bytes_index_the_atlas_by_codepage_not_by_utf16()
    {
        // The trap: 'é' is U+00E9, and in Windows-1252 it is also byte 0xE9 -- but 'Œ' is U+0152
        // and byte 0x8C. A reader that cast the char to a byte would index cell 0x52 for it.
        var font = new BitmapFont(BuildAtlas());

        Assert.Equal((0x8C % 5) + 1, font.GetTextWidth("Œ"));
        Assert.Equal((0xE9 % 5) + 1, font.GetTextWidth("é"));
    }

    [Fact]
    public void Drawing_advances_by_each_glyphs_width()
    {
        var font = new BitmapFont(BuildAtlas());
        var target = new Surface(200, 20);
        target.Fill(0xFF202020);

        int end = font.Draw(target, 10, 2, "AB");

        Assert.Equal(10 + ExpectedWidth("AB"), end);

        // 'A' is 0x41 and 65 % 5 == 0, so 1 pixel wide; 'B' is 0x42, so 2 -- covering x 11 and 12.
        Assert.Equal(Ink, target[10, 2]);
        Assert.Equal(Ink, target[11, 2]);
        Assert.Equal(Ink, target[12, 2]);
        Assert.Equal(0xFF202020u, target[10 + ExpectedWidth("AB"), 2]);
    }

    [Fact]
    public void Drawing_is_clipped_to_the_destination_and_the_clip_is_restored()
    {
        var font = new BitmapFont(BuildAtlas());
        var target = new Surface(60, 20);
        target.Fill(0);

        var narrow = SurfaceRect.FromBounds(0, 0, 5, 20);
        font.DrawClipped(target, 0, 0, "AAAAAAAA", narrow);

        Assert.Equal(Ink, target[4, 0]);
        Assert.Equal(0xFF000000u, target[5, 0]);

        // Leaving a narrowed clip behind would silently truncate whatever drew next.
        Assert.Equal(target.Bounds, target.ClipRect);
    }

    [Fact]
    public void Centred_text_that_fits_is_offset_by_half_the_slack()
    {
        var font = new BitmapFont(BuildAtlas());
        var target = new Surface(200, 20);
        target.Fill(0);

        int width = font.GetTextWidth("AB");
        font.DrawAligned(target, 0, 0, 100, "AB", TextAlign.Center);

        int expectedX = (100 - width) / 2;
        Assert.Equal(Ink, target[expectedX, 0]);
        Assert.Equal(0xFF000000u, target[expectedX - 1, 0]);
    }

    [Fact]
    public void Centred_text_that_overflows_fills_the_field_and_stops()
    {
        // Transcribed rather than improved. The original adds each width to a running total and
        // breaks once that total *exceeds* the field, so a character landing exactly on the
        // boundary is drawn. ('A' is 0x41 and 65 % 5 == 0, so these glyphs are 1px wide.)
        var font = new BitmapFont(BuildAtlas());
        var target = new Surface(200, 20);
        target.Fill(0);

        int end = font.DrawAligned(target, 0, 0, 4, "AAAAAA", TextAlign.Center);

        Assert.Equal(4, end);
        Assert.Equal(Ink, target[3, 0]);          // the character on the boundary is drawn
        Assert.Equal(0xFF000000u, target[4, 0]);  // the one past it is not
    }

    [Fact]
    public void Text_exactly_as_wide_as_its_field_still_draws_in_full()
    {
        // The strictly-less guard on the centred branch sends this down the truncating path. The
        // outcome is identical, and pinning it is what makes that safe to rely on.
        var font = new BitmapFont(BuildAtlas());
        var target = new Surface(200, 20);
        target.Fill(0);

        int width = font.GetTextWidth("ABC");
        int end = font.DrawAligned(target, 0, 0, width, "ABC", TextAlign.Center);

        Assert.Equal(width, end);
        Assert.Equal(Ink, target[width - 1, 0]);
    }

    [Fact]
    public void Right_aligned_text_has_no_overflow_branch()
    {
        // The original computes an origin from the full width and draws everything, so over-long
        // right-aligned text runs off the left of its field -- and off the surface, where the
        // blitter clips it. Reproduced because a design's layout may depend on it.
        var font = new BitmapFont(BuildAtlas());
        var target = new Surface(200, 20);
        target.Fill(0);

        int width = font.GetTextWidth("ABCDE");
        int end = font.DrawAligned(target, 100, 0, 10, "ABCDE", TextAlign.Right);

        Assert.Equal(100 + 10 - width + width, end);
        Assert.True(width > 10, "the fixture must actually overflow for this to mean anything");
    }

    [Fact]
    public void Tinting_recolours_the_glyph_but_not_the_keyed_background()
    {
        // What replaces the original's eleven-atlases-per-colour scheme.
        var font = new BitmapFont(BuildAtlas());
        var target = new Surface(40, 10);
        target.Fill(0xFF123456);

        font.Draw(target, 0, 0, "B", tint: 0x00FF8000);

        // 'B' is 0x42, so 66 % 5 + 1 == 2 pixels wide.
        Assert.Equal(0xFFFF8000u, target[0, 0]);
        Assert.Equal(0xFFFF8000u, target[1, 0]);

        // Past the glyph, the destination is untouched.
        Assert.Equal(0xFF123456u, target[2, 0]);
    }

    [Fact]
    public void Tinted_drawing_respects_the_clip_rectangle()
    {
        var font = new BitmapFont(BuildAtlas());
        var target = new Surface(40, 10);
        target.Fill(0xFF123456);
        target.ClipRect = SurfaceRect.FromBounds(0, 0, 1, 10);

        font.Draw(target, 0, 0, "A", tint: 0x00FF8000);

        Assert.Equal(0xFFFF8000u, target[0, 0]);
        Assert.Equal(0xFF123456u, target[1, 0]);
    }

    [Fact]
    public void Zero_width_glyphs_draw_nothing_rather_than_throwing()
    {
        // Control characters commonly measure zero, and every string with a newline in it hits
        // this path. The original blits an empty rect, which DirectDraw ignores.
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (0, 0));
        var glyphs = FontAtlas.Layout(extents, 320, out int height);
        var sheet = new Surface(320, Math.Max(1, height), SurfaceKind.Font);
        var font = new BitmapFont(new FontAtlas(sheet, glyphs));

        var target = new Surface(10, 10);
        Assert.Equal(5, font.Draw(target, 5, 0, "abc"));
    }

    [Fact]
    public void Max_dimensions_are_taken_across_every_cell()
    {
        var atlas = BuildAtlas(height: 7);

        Assert.Equal(5, atlas.MaxCharWidth);
        Assert.Equal(7, atlas.MaxCharHeight);
    }

    [Fact]
    public void An_atlas_must_have_exactly_256_cells()
    {
        var sheet = new Surface(8, 8);
        Assert.Throws<ArgumentException>(() => new FontAtlas(sheet, new Glyph[255]));
    }
}
