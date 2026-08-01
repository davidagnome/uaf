namespace UAF.Media.Tests;

/// <summary>
/// Covers <see cref="TextForm"/> — the layout engine every one of the game's forms is built on.
/// </summary>
/// <remarks>
/// Authored atlases rather than a rasteriser, so these run anywhere and the geometry is exact:
/// every glyph is a known width, which makes a relative placement a number this test can state
/// rather than approximate.
/// </remarks>
public class TextFormTests
{
    private const uint Key = 0xFF00FF00;
    private const uint Ink = 0xFFFFFFFF;

    /// <summary>A font whose every glyph is 6 wide and 10 tall, so widths are just lengths × 6.</summary>
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

    private const int Name = 1;
    private const int Value = 2;
    private const int Third = 3;

    [Fact]
    public void An_absolute_field_lands_where_the_table_puts_it()
    {
        var form = new TextForm([new FormField(0, 0, Name, 10, 20)]);
        var font = FixedFont();

        var rect = form.SetText(Name, "abc", font);

        Assert.Equal(10, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(28, rect.Right);    // 10 + 3 glyphs x 6
        Assert.Equal(30, rect.Bottom);   // 20 + the height of 'H'
    }

    [Fact]
    public void End_places_a_field_after_the_one_it_names()
    {
        // The second field's x is an offset from the first field's right edge, not from zero.
        var form = new TextForm([
            new FormField(0, 0, Name, 10, 20),
            new FormField(Name | (int)FormFlags.End, 0, Value, 4, 20),
        ]);
        var font = FixedFont();

        form.SetText(Name, "abc", font);
        var rect = form.SetText(Value, "de", font);

        Assert.Equal(32, rect.Left);     // 28 (first field's right) + 4
        Assert.Equal(44, rect.Right);
    }

    [Fact]
    public void RightJust_pulls_the_text_back_to_end_at_its_own_x()
    {
        var form = new TextForm([new FormField((int)FormFlags.RightJust, 0, Name, 100, 5)]);
        var font = FixedFont();

        var rect = form.SetText(Name, "abcd", font);

        Assert.Equal(76, rect.Left);     // 100 - 4 glyphs x 6
        Assert.Equal(100, rect.Right);
    }

    [Fact]
    public void A_field_placed_against_an_unplaced_one_fails_loudly()
    {
        // The reference asserts here. Left as a throw rather than a silent 0,0 placement, because
        // an unplaced anchor is a table-ordering mistake and looks nothing like one on screen.
        var form = new TextForm([
            new FormField(0, 0, Name, 10, 20),
            new FormField(Name | (int)FormFlags.End, 0, Value, 4, 20),
        ]);
        var font = FixedFont();

        var error = Assert.Throws<InvalidOperationException>(() => form.SetText(Value, "de", font));
        Assert.Contains("table order", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colour_in_the_field_id_applies_when_the_caller_gives_none()
    {
        var form = new TextForm([
            new FormField(0, 0, Name | (int)FormFlags.Green, 0, 0),
            new FormField(0, 0, Value, 0, 20),
        ]);
        var font = FixedFont();

        form.SetText(Name | (int)FormFlags.Green, "x", font);
        form.SetText(Value, "y", font);

        Assert.Equal(FontColor.Green, form.Field(Name | (int)FormFlags.Green)!.Color);
        Assert.Equal(FontColor.White, form.Field(Value)!.Color);   // no colour bits means white

        // ...and an explicit colour wins over the id's.
        form.SetText(Value, "y", font, FontColor.Red);
        Assert.Equal(FontColor.Red, form.Field(Value)!.Color);
    }

    [Fact]
    public void Tab_visits_only_tab_stops_and_wraps()
    {
        int first = Name | (int)FormFlags.Tab;
        int second = Third | (int)FormFlags.Tab;

        var form = new TextForm([
            new FormField(0, 0, first, 0, 0),
            new FormField(0, 0, Value, 0, 10),     // not a stop
            new FormField(0, 0, second, 0, 20),
        ]);

        Assert.Equal(first, form.Tab(-1));
        Assert.Equal(second, form.Tab(first));
        Assert.Equal(first, form.Tab(second));     // wraps
    }

    [Fact]
    public void A_form_with_one_tab_stop_stays_on_it_rather_than_cycling()
    {
        int only = Name | (int)FormFlags.Tab;
        var form = new TextForm([
            new FormField(0, 0, only, 0, 0),
            new FormField(0, 0, Value, 0, 10),
        ]);

        Assert.Equal(only, form.Tab(only));
    }

    [Fact]
    public void A_click_takes_the_largest_field_it_lands_in_not_the_first()
    {
        // Selection boxes overlap the text inside them. Taking the first match returns the label;
        // the player meant the row.
        var form = new TextForm([
            new FormField(0, 0, Name, 10, 10),
            new FormField(0, 0, Value, 0, 0),
        ]);
        var font = FixedFont();

        form.SetText(Name, "ab", font);         // 10,10 - 22,20 : area 120
        form.SetText(Value, "abcdefghij", font); // 0,0 - 60,10 ... widen it below

        // Make the second field genuinely larger and overlapping.
        var big = form.Field(Value)!;
        big.Left = 0;
        big.Top = 0;
        big.Right = 100;
        big.Bottom = 100;

        Assert.Equal(Value, form.MouseClick(15, 15));
    }

    [Fact]
    public void A_click_outside_every_field_selects_nothing()
    {
        var form = new TextForm([new FormField(0, 0, Name, 10, 10)]);
        form.SetText(Name, "ab", FixedFont());

        Assert.Equal(-1, form.MouseClick(500, 500));
    }

    [Fact]
    public void Auto_repeat_generates_a_row_per_repeat_with_incrementing_ids()
    {
        // Three rows of two fields. The block header is not a field: its Y is the row count and its
        // X is how many of the following fields make up a row.
        var form = new TextForm([
            new FormField((int)FormFlags.AutoRepeat, 0, 99, 2, 3),
            new FormField(0, 0, Name, 0, 0),
            new FormField(Name | (int)FormFlags.End, 0, Value, 2, 0),
        ]);

        Assert.Equal(6, form.Count);

        // Row 0 keeps the authored ids; each later row adds RepeatIncrement once.
        Assert.NotNull(form.Field(Name));
        Assert.NotNull(form.Field(Name + (int)FormFlags.RepeatIncrement));
        Assert.NotNull(form.Field(Name + ((int)FormFlags.RepeatIncrement * 2)));
        Assert.Null(form.Field(Name + ((int)FormFlags.RepeatIncrement * 3)));
    }

    [Fact]
    public void Later_auto_repeat_rows_stack_under_the_row_above()
    {
        var form = new TextForm([
            new FormField((int)FormFlags.AutoRepeat, 0, 99, 1, 3),
            new FormField(0, 0, Name, 5, 7),
        ]);
        var font = FixedFont();

        form.SetText(Name, "a", font);
        var second = form.SetText(Name + (int)FormFlags.RepeatIncrement, "b", font);
        var third = form.SetText(Name + ((int)FormFlags.RepeatIncrement * 2), "c", font);

        // The first row keeps its authored y; the rest sit directly below their predecessor, and
        // their own y is zeroed so it cannot be added twice.
        Assert.Equal(7, form.Field(Name)!.Top);
        Assert.Equal(17, second.Top);
        Assert.Equal(27, third.Top);
        Assert.Equal(5, second.Left);   // x is unchanged down the column
    }

    [Fact]
    public void Columns_are_pushed_right_until_they_clear_every_column_before_them()
    {
        // This is what lines a variable-width table up without measuring it in advance.
        var form = new TextForm([
            new FormField(0, 0, Name, 0, 0, Column: 1),
            new FormField(0, 0, Value, 0, 0, Column: 2, Space: 4),
        ]);
        var font = FixedFont();

        form.SetText(Name, "abcdef", font);   // 0..36 in column 1
        form.SetText(Value, "xy", font);      // 0..12 in column 2, wants 4 of clearance

        var adjustments = form.ColumnAdjustments();

        Assert.Equal(0, adjustments[1]);
        Assert.Equal(40, adjustments[2]);     // 36 + 4 - 0
    }

    [Fact]
    public void A_hidden_or_empty_field_is_not_drawn()
    {
        var form = new TextForm([
            new FormField(0, 0, Name, 0, 0),
            new FormField(0, 0, Value, 0, 20),
        ]);
        var font = FixedFont();
        var surface = new Surface(64, 64, SurfaceKind.Buffer);

        form.SetText(Name, "a", font, FontColor.White);
        form.SetText(Value, "b", font, FontColor.White);
        form.EnableItem(Value, false);

        form.Display(surface, font);

        // Only the visible field left ink. Row 0 is drawn, row 20 is not.
        Assert.Contains(surface.Pixels[..(64 * 10)], p => p != 0);
        Assert.All(surface.Pixels[(64 * 20)..(64 * 30)], p => Assert.Equal(0u, p));
    }
}
