
namespace UAF.Media.Tests;

/// <summary>
/// Covers the text layer: the <c>/</c> markup scanner, word wrap, and box paging.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation here was derived by hand-tracing <c>UAFWin/FormattedText.cpp</c> against the
/// input, not by running this implementation and recording what it did. That is the only thing that
/// makes the tests worth having — the whole point of the layer is to break lines where the original
/// broke them.
/// </para>
/// <para>
/// The font is deliberately fixed-width so the wrap arithmetic can be checked in the head. The
/// proportional case is covered separately, since a uniform advance would hide an indexing mistake.
/// </para>
/// </remarks>
public class FormattedTextTests
{
    private const uint Key = 0xFF000000;
    private const uint Ink = 0xFFFFFFFF;

    /// <summary>An atlas where every character is <paramref name="advance"/> pixels wide.</summary>
    private static BitmapFont FixedFont(int advance = 10, int height = 16)
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (advance, height));
        return BuildFont(extents);
    }

    /// <summary>An atlas where 'i' is narrow and 'W' is wide, so advances actually differ.</summary>
    private static BitmapFont ProportionalFont()
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (8, 16));
        extents['i'] = (3, 16);
        extents['W'] = (14, 16);
        extents[' '] = (4, 16);
        return BuildFont(extents);
    }

    private static BitmapFont BuildFont((int, int)[] extents)
    {
        var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
        var sheet = new Surface(FontAtlas.DefaultSheetWidth, sheetHeight, SurfaceKind.Font);
        sheet.Fill(Key);
        sheet.ColorKey = Key;

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

        return new BitmapFont(new FontAtlas(sheet, glyphs));
    }

    private static string[] Text(TextDisplayData data) =>
        [.. data.Lines.Select(l => BitmapFont.Decode(l.Text))];

    /// <summary>Runs the scanner to exhaustion, collecting what it yielded.</summary>
    private static List<(FormattedTextStatus Status, char Char)> Scan(string text)
    {
        var scanner = new FormattedTextScanner();
        scanner.Initialize(BitmapFont.Encode(text), 99999, (byte)'W', FontColor.White, 0, 0);

        var seen = new List<(FormattedTextStatus, char)>();
        FormattedTextStatus status;
        while ((status = scanner.NextChar()) != FormattedTextStatus.EndOfText)
        {
            seen.Add((status, (char)scanner.CurrentCharacter));
        }

        return seen;
    }

    /// <summary>Just the characters that would be drawn, dropping the tag machinery.</summary>
    private static string Printed(List<(FormattedTextStatus Status, char Char)> seen) =>
        new([.. seen.Where(s => s.Status == FormattedTextStatus.Printable).Select(s => s.Char)]);

    // ---- the scanner ------------------------------------------------------------------------

    [Fact]
    public void Plain_text_yields_one_printable_per_character()
    {
        var seen = Scan("abc");

        Assert.Equal(3, seen.Count);
        Assert.All(seen, s => Assert.Equal(FormattedTextStatus.Printable, s.Status));
        Assert.Equal("abc", new string([.. seen.Select(s => s.Char)]));
    }

    [Theory]
    [InlineData("/W", FontColor.White)]
    [InlineData("/Y", FontColor.Yellow)]
    [InlineData("/O", FontColor.Orange)]
    [InlineData("/T", FontColor.BrightOrange)]
    [InlineData("/R", FontColor.Red)]
    [InlineData("/G", FontColor.Green)]
    [InlineData("/B", FontColor.Blue)]
    [InlineData("/V", FontColor.Cyan)]
    [InlineData("/K", FontColor.Black)]
    [InlineData("/M", FontColor.Magenta)]
    [InlineData("/S", FontColor.Silver)]
    // Lower case selects the same colour -- every case in the switch pairs the two.
    [InlineData("/r", FontColor.Red)]
    [InlineData("/v", FontColor.Cyan)]
    public void Colour_tags_select_their_colour(string tag, FontColor expected)
    {
        var scanner = new FormattedTextScanner();
        scanner.Initialize(BitmapFont.Encode(tag + "x"), 99999, (byte)'W', FontColor.White, 0, 0);

        while (scanner.NextChar() != FormattedTextStatus.EndOfText)
        {
        }

        Assert.Equal(expected, scanner.CurrentColor);
    }

    [Fact]
    public void A_font_tag_needs_both_digits()
    {
        var scanner = new FormattedTextScanner();
        scanner.Initialize(BitmapFont.Encode("/26x"), 99999, (byte)'W', FontColor.White, 0, 0);

        Assert.Equal(FormattedTextStatus.Escape, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.Digit, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.FontTag, scanner.NextChar());
        Assert.Equal(26, scanner.CurrentFont);
    }

    [Fact]
    public void A_slash_with_one_digit_is_printed_rather_than_eaten()
    {
        // "3/4 of the way" must survive. The scanner commits to a tag, finds the second character
        // is not a digit, then rewinds two and re-issues the '/' -- so Escape and Digit are yielded
        // on the way and it is the printable run that has to come back whole.
        var seen = Scan("/2x");

        Assert.Equal([FormattedTextStatus.Escape, FormattedTextStatus.Digit],
                     seen.Take(2).Select(s => s.Status));
        Assert.Equal("/2x", Printed(seen));
    }

    [Fact]
    public void An_unrecognised_tag_is_printed_rather_than_eaten()
    {
        var seen = Scan("/Qz");

        Assert.Equal(FormattedTextStatus.Escape, seen[0].Status);
        Assert.Equal("/Qz", Printed(seen));
    }

    [Fact]
    public void Hash_swallows_exactly_the_next_colour_tag()
    {
        var scanner = new FormattedTextScanner();
        scanner.Initialize(BitmapFont.Encode("/#/R/Gx"), 99999, (byte)'W', FontColor.White, 0, 0);

        Assert.Equal(FormattedTextStatus.Escape, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.SkipNextColor, scanner.NextChar());

        // The /R is consumed and discarded...
        Assert.Equal(FormattedTextStatus.Escape, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.Color, scanner.NextChar());
        Assert.Equal(FontColor.White, scanner.CurrentColor);

        // ...and the /G after it applies normally, so the skip really is one-shot.
        Assert.Equal(FormattedTextStatus.Escape, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.Color, scanner.NextChar());
        Assert.Equal(FontColor.Green, scanner.CurrentColor);
    }

    [Fact]
    public void Custom_colour_survives_a_font_change_and_is_cleared_by_a_colour()
    {
        var scanner = new FormattedTextScanner();
        scanner.Initialize(BitmapFont.Encode("/C/26a/Rb"), 99999, (byte)'W', FontColor.White, 0, 0);

        scanner.NextChar();                        // Escape
        scanner.NextChar();                        // Color -- /C
        Assert.True(scanner.IsCustomColorActive);

        scanner.NextChar();                        // Escape
        scanner.NextChar();                        // Digit
        scanner.NextChar();                        // FontTag -- /26
        Assert.True(scanner.IsCustomColorActive);
        Assert.Equal(26, scanner.CurrentFont);

        scanner.NextChar();                        // 'a'
        scanner.NextChar();                        // Escape
        scanner.NextChar();                        // Color -- /R
        Assert.False(scanner.IsCustomColorActive);
    }

    [Fact]
    public void Carriage_return_and_newline_pair_up_in_either_order()
    {
        var scanner = new FormattedTextScanner();
        scanner.Initialize(BitmapFont.Encode("a\r\nb"), 99999, (byte)'W', FontColor.White, 0, 0);

        Assert.Equal(FormattedTextStatus.Printable, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.CarriageReturn, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.CrNl, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.Printable, scanner.NextChar());
    }

    [Fact]
    public void A_newline_followed_by_a_carriage_return_is_fatal_as_it_is_in_the_reference()
    {
        // TestNextChar produces FTNLCR and NextChar's dispatch has no case for it, so the next
        // call reaches die(0x551b0a) -- MessageBox + abort(), in every build.
        var scanner = new FormattedTextScanner();
        scanner.Initialize(BitmapFont.Encode("a\n\rb"), 99999, (byte)'W', FontColor.White, 0, 0);

        Assert.Equal(FormattedTextStatus.Printable, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.NewLine, scanner.NextChar());
        Assert.Equal(FormattedTextStatus.NlCr, scanner.NextChar());

        var ex = Assert.Throws<InvalidOperationException>(() => scanner.NextChar());
        Assert.Contains("0x551b0a", ex.Message, StringComparison.Ordinal);
    }

    // ---- word wrap --------------------------------------------------------------------------

    [Fact]
    public void Text_breaks_at_the_last_space_that_fits()
    {
        // 10px per character, 50px of room: "aaa " measures 40, the second 'b' takes it to 60 and
        // triggers the break, which rewinds to the space at index 3.
        var data = TextFormatter.Format("aaa bbb ccc", 50, FixedFont());

        Assert.Equal(["aaa", "bbb", "ccc"], Text(data));
    }

    [Fact]
    public void A_colour_is_restated_at_the_head_of_each_wrapped_line()
    {
        // This is what the preamble exists for: a line has to render correctly on its own, without
        // replaying the lines above it.
        var data = TextFormatter.Format("/Raaa bbb", 50, FixedFont());

        Assert.Equal(["/Raaa", "/Rbbb"], Text(data));
    }

    [Fact]
    public void A_font_tag_is_restated_too_and_pairs_with_the_colour()
    {
        var data = TextFormatter.Format("/R/26aaa bbb", 50, FixedFont());

        Assert.Equal(["/R/26aaa", "/R/26bbb"], Text(data));
    }

    [Fact]
    public void White_needs_no_preamble_but_a_non_default_font_still_does()
    {
        var data = TextFormatter.Format("/26aaa bbb", 50, FixedFont());

        Assert.Equal(["/26aaa", "/26bbb"], Text(data));
    }

    [Fact]
    public void Tags_take_no_width_so_a_marked_up_line_holds_as_many_characters_as_a_plain_one()
    {
        var plain = TextFormatter.Format("aaaaa bb", 50, FixedFont());
        var tagged = TextFormatter.Format("/Raaaaa bb", 50, FixedFont());

        Assert.Equal(["aaaaa", "bb"], Text(plain));
        Assert.Equal(["/Raaaaa", "/Rbb"], Text(tagged));
    }

    [Fact]
    public void Only_a_carriage_return_starts_a_new_line_a_bare_newline_does_not()
    {
        // "We only process FTCR" (FormattedText.cpp:1071). Text with Unix line endings does not
        // break at all -- it wraps only on width, and PostProcess strips the stray '\n'.
        var withCr = TextFormatter.Format("ab\rcd", 500, FixedFont());
        var withNl = TextFormatter.Format("ab\ncd", 500, FixedFont());

        Assert.Equal(["ab", "cd"], Text(withCr));
        Assert.Equal(["abcd"], Text(withNl));
    }

    [Fact]
    public void A_crlf_leaves_no_stray_control_character_in_the_next_line()
    {
        // The '\n' lands at the head of the following line -- GetString trims trailing control
        // characters, not leading ones -- and PostProcessText is what removes it.
        var data = TextFormatter.Format("ab\r\ncd", 500, FixedFont());

        Assert.Equal(["ab", "cd"], Text(data));
    }

    [Fact]
    public void Slash_N_ends_the_line_and_marks_it_as_waiting()
    {
        var data = TextFormatter.Format("ab/Ncd", 500, FixedFont());

        // The tag stays in the line text: the draw path re-scans each line, so markup is not
        // stripped at wrap time.
        Assert.Equal(["ab/N", "cd"], Text(data));
        Assert.True(data.Lines[0].WaitForReturn);
        Assert.False(data.Lines[1].WaitForReturn);
    }

    [Fact]
    public void Wrapping_uses_each_characters_own_advance()
    {
        // 'i' is 3px, 'W' 14px, space 4px, everything else 8px. "WWW" is 42, plus the space is 46;
        // the first 'i' of "iii" takes it to 49, the second to 52 -- which overflows 50 and breaks
        // back to the space. A fixed-width assumption would put the break elsewhere.
        var data = TextFormatter.Format("WWW iii xx", 50, ProportionalFont());

        Assert.Equal(["WWW", "iii xx"], Text(data));
    }

    [Fact]
    public void An_empty_string_produces_no_lines()
    {
        Assert.Empty(TextFormatter.Format("", 50, FixedFont()).Lines);
    }

    [Fact]
    public void A_word_longer_than_the_line_is_hard_cut_not_left_to_overrun()
    {
        // No whitespace means no break index, so Backup declines to rewind -- which cuts the line
        // at the overflowing character rather than leaving it uncut. Three lines of 30px, not one
        // long one. The <= 0 guard is about not looping forever, not about keeping the word whole.
        var data = TextFormatter.Format("aaaaaaaaaa", 30, FixedFont());

        Assert.Equal(["aaaa", "aaaa", "aa"], Text(data));
    }

    [Fact]
    public void A_run_of_spaces_breaks_at_the_last_one_and_stays_on_the_line_it_ends()
    {
        // Every space updates the break index, so the rewind lands on the LAST space before the
        // overflow -- leaving the earlier ones as trailing whitespace on the line just closed.
        // SkipSpace then eats exactly that one space, so the next line is not indented.
        var data = TextFormatter.Format("aaa   bbb", 50, FixedFont());

        Assert.Equal(["aaa  ", "bbb"], Text(data));
    }

    [Fact]
    public void Post_processing_replaces_only_byte_0x80_not_the_whole_high_range()
    {
        // StripInvalidChars reads as a range check and reaches exactly one byte, because char is
        // signed. Accented characters in a design's prose must survive.
        Span<byte> bytes = [0x80, 0xE9, 0x41, 0xFF];
        TextFormatter.StripInvalidChars(bytes);

        Assert.Equal([(byte)' ', (byte)0xE9, (byte)0x41, (byte)0xFF], bytes.ToArray());
    }

    // ---- box paging -------------------------------------------------------------------------

    private static TextDisplayData Paged(int lines, int perBox, params int[] waitAt)
    {
        var data = new TextDisplayData { LinesPerBox = perBox };
        for (int i = 0; i < lines; i++)
        {
            data.Add(new TextLine(BitmapFont.Encode($"line{i}"), waitAt.Contains(i)));
        }

        return data;
    }

    [Fact]
    public void Paging_walks_a_box_at_a_time_and_knows_when_it_is_last()
    {
        var data = Paged(lines: 7, perBox: 3);

        Assert.True(data.IsFirstBox);
        Assert.False(data.IsLastBox());

        data.NextBox();
        Assert.Equal(3, data.CurrentLine);
        Assert.False(data.IsLastBox());

        data.NextBox();
        Assert.Equal(6, data.CurrentLine);
        Assert.True(data.IsLastBox());
    }

    [Fact]
    public void A_wait_ends_the_box_after_the_waiting_line()
    {
        var data = Paged(lines: 7, perBox: 3, waitAt: 1);

        Assert.True(data.WaitForReturn());
        Assert.False(data.IsLastBox());

        data.NextBox();
        Assert.Equal(2, data.CurrentLine);
    }

    [Fact]
    public void The_current_box_stops_at_a_waiting_line()
    {
        var data = Paged(lines: 7, perBox: 3, waitAt: 1);

        Assert.Equal(["line0", "line1"],
                     data.CurrentBox().Select(l => BitmapFont.Decode(l.Text)));
    }

    [Fact]
    public void Paging_back_over_plain_text_is_the_exact_inverse_of_paging_forward()
    {
        var data = Paged(lines: 9, perBox: 4);

        data.NextBox();
        Assert.Equal(4, data.CurrentLine);

        data.PrevBox();
        Assert.Equal(0, data.CurrentLine);
    }

    [Fact]
    public void Paging_back_across_a_wait_lands_a_line_early_as_it_does_in_the_reference()
    {
        // PrevBox decrements twice before it starts checking for a wait, so the box boundary a
        // '/N' created one line above is stepped straight past. Reproduced, not corrected.
        var data = Paged(lines: 7, perBox: 3, waitAt: 4);

        data.NextBox();                       // lines 0-2
        Assert.Equal(3, data.CurrentLine);

        data.NextBox();                       // lines 3-4, ended by the wait on 4
        Assert.Equal(5, data.CurrentLine);

        data.PrevBox();
        Assert.Equal(2, data.CurrentLine);    // not the 3 a symmetric implementation would give
    }

    [Fact]
    public void Slow_text_applies_only_to_a_boxs_first_showing()
    {
        var data = Paged(lines: 3, perBox: 3);
        data.SlowText = true;

        Assert.True(data.UseSlowText);
        Assert.True(data.NeedsFrontBuffer);

        data.InitialBoxDisplay = false;
        Assert.False(data.UseSlowText);
    }

    // ---- box metrics ------------------------------------------------------------------------

    [Fact]
    public void The_old_textbox_form_takes_its_width_from_the_screen()
    {
        // Every shipped design uses this form: TEXTBOX = 18,328 with TEXTBOX_LINES = 6.
        var box = TextBoxMetrics.FromConfig(screenWidth: 640, textbox: (18, 328), lines: 6);

        Assert.Equal(18, box.X);
        Assert.Equal(328, box.Y);
        Assert.Equal(640 - 36, box.Width);
        Assert.Equal(16 * 6, box.Height);
        Assert.Equal(6, box.Lines);
    }

    [Fact]
    public void The_rect_form_wins_over_the_old_one_and_carries_the_references_off_by_one()
    {
        var box = TextBoxMetrics.FromConfig(screenWidth: 640, textbox: (18, 328), lines: 6,
                                            rect: (10, 300, 610, 396));

        Assert.Equal(10, box.X);
        Assert.Equal(300, box.Y);
        Assert.Equal(599, box.Width);        // 610 - 10 - 1
        Assert.Equal(95, box.Height);        // 396 - 300 - 1
        Assert.Equal(5, box.Lines);          // 95 / 16
    }

    [Fact]
    public void The_usable_width_is_the_box_less_half_a_character()
    {
        var box = TextBoxMetrics.FromConfig(screenWidth: 640, textbox: (18, 328), lines: 6)
                                .ForFont(FixedFont(advance: 10, height: 16));

        Assert.Equal(604 - 5, box.Width);
        Assert.Equal(6, box.Lines);          // 96 / 16, the font's own height this time
    }

    [Fact]
    public void The_line_count_is_re_derived_from_the_font_not_the_configs_sixteen()
    {
        // Config computes lines against a hardcoded 16; a taller font fits fewer in the same box.
        var box = TextBoxMetrics.FromConfig(screenWidth: 640, textbox: (18, 328), lines: 6)
                                .ForFont(FixedFont(advance: 10, height: 24));

        Assert.Equal(4, box.Lines);          // 96 / 24
    }
}
