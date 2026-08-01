namespace UAF.Media.Tests;

/// <summary>Covers <see cref="RestTimeForm"/>.</summary>
public class RestTimeFormTests
{
    private const uint Key = 0xFF00FF00;
    private const uint Ink = 0xFFFFFFFF;

    private static BitmapFont FixedFont()
    {
        var extents = new (int, int)[FontAtlas.CharacterCount];
        Array.Fill(extents, (6, 10));

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

    private static RestTimeForm Form(BitmapFont font, long d = 0, long h = 0, long m = 0)
    {
        var form = new RestTimeForm(20, 100);
        form.SetTime(font, d, h, m);
        return form;
    }

    [Fact]
    public void The_time_reads_as_two_digit_fields_between_colons()
    {
        var font = FixedFont();
        var form = Form(font, 1, 2, 3);

        Assert.Equal("REST TIME", form.Form.Field(RestTimeForm.Header)!.Text);
        Assert.Equal("01", form.Form.Field(RestTimeForm.DaysText)!.Text);
        Assert.Equal("02", form.Form.Field(RestTimeForm.HoursText)!.Text);
        Assert.Equal("03", form.Form.Field(RestTimeForm.MinutesText)!.Text);
        Assert.Equal(":", form.Form.Field(RestTimeForm.DaysColon)!.Text);
    }

    [Fact]
    public void The_fields_run_left_to_right_in_the_order_they_are_read()
    {
        var font = FixedFont();
        var form = Form(font, 1, 2, 3);

        int days = form.Form.Field(RestTimeForm.DaysText)!.Left;
        int hours = form.Form.Field(RestTimeForm.HoursText)!.Left;
        int minutes = form.Form.Field(RestTimeForm.MinutesText)!.Left;

        Assert.True(form.Form.Field(RestTimeForm.Header)!.Left < days);
        Assert.True(days < hours);
        Assert.True(hours < minutes);
    }

    [Fact]
    public void The_three_selection_fields_are_never_placed()
    {
        // They keep their SEL flags -- unlike ItemsForm's row marker, they are not inside an
        // auto-repeat block -- but showRestTime never gives them text, so they are never laid out.
        // They exist to name the tab stops, and the highlight goes on the number beside each.
        var font = FixedFont();
        var form = Form(font, 1, 2, 3);

        foreach (int stop in new[]
                 { RestTimeForm.DaysStop, RestTimeForm.HoursStop, RestTimeForm.MinutesStop })
        {
            var field = form.Form.Field(stop);
            Assert.NotNull(field);
            Assert.Equal(-1, field!.Left);
            Assert.Equal(string.Empty, field.Text);
        }
    }

    [Fact]
    public void Tab_cycles_the_three_fields_and_highlights_the_number()
    {
        var font = FixedFont();
        var form = Form(font);

        Assert.Equal(RestField.Days, form.Selection);
        Assert.True(form.Form.Field(RestTimeForm.DaysText)!.Highlight);

        form.Tab();
        Assert.Equal(RestField.Hours, form.Selection);
        Assert.False(form.Form.Field(RestTimeForm.DaysText)!.Highlight);
        Assert.True(form.Form.Field(RestTimeForm.HoursText)!.Highlight);

        form.Tab();
        form.Tab();
        Assert.Equal(RestField.Days, form.Selection);
    }

    [Fact]
    public void Incrementing_carries_upward()
    {
        var font = FixedFont();
        var form = Form(font, 0, 23, 59);
        form.Select(RestField.Minutes);

        Assert.True(form.Increment(font));

        // The minutes case checks the hour rollover itself rather than falling through, so a
        // single minute at 23:59 advances the day.
        Assert.Equal(1, form.Days);
        Assert.Equal(0, form.Hours);
        Assert.Equal(0, form.Minutes);
    }

    [Fact]
    public void Hours_roll_into_days()
    {
        var font = FixedFont();
        var form = Form(font, 0, 23, 0);
        form.Select(RestField.Hours);

        Assert.True(form.Increment(font));
        Assert.Equal(1, form.Days);
        Assert.Equal(0, form.Hours);
    }

    [Fact]
    public void Decrementing_refuses_at_zero_rather_than_borrowing()
    {
        // The asymmetry with Increment is the reference's, not an omission: 1 day 00:00 stays put
        // when a minute is taken off it. Making the two symmetric would let a player walk the
        // clock back past the rest they asked for.
        var font = FixedFont();
        var form = Form(font, 1, 0, 0);
        form.Select(RestField.Minutes);

        Assert.False(form.Decrement(font));
        Assert.Equal(1, form.Days);
        Assert.Equal(0, form.Hours);
        Assert.Equal(0, form.Minutes);

        // The day itself still comes off, because that field is not at zero.
        form.Select(RestField.Days);
        Assert.True(form.Decrement(font));
        Assert.Equal(0, form.Days);

        // ...and then refuses too.
        Assert.False(form.Decrement(font));
    }

    [Fact]
    public void Days_have_no_upper_bound()
    {
        var font = FixedFont();
        var form = Form(font, 99, 0, 0);
        form.Select(RestField.Days);

        Assert.True(form.Increment(font));
        Assert.Equal(100, form.Days);

        // Three digits, because the format pads to two rather than truncating to it.
        Assert.Equal("100", form.Form.Field(RestTimeForm.DaysText)!.Text);
    }

    [Fact]
    public void The_total_is_the_three_fields_in_minutes()
    {
        var font = FixedFont();
        var form = Form(font, 2, 3, 4);

        Assert.Equal((2 * 24 * 60) + (3 * 60) + 4, form.TotalMinutes);
    }

    [Fact]
    public void A_populated_form_draws_something()
    {
        var font = FixedFont();
        var form = Form(font, 1, 2, 3);
        var surface = new Surface(320, 200, SurfaceKind.Buffer);

        form.Display(surface, font);
        Assert.Contains(surface.Pixels, p => p != 0);
    }
}
