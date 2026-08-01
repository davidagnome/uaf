namespace UAF.Media.Tests;

/// <summary>Covers <see cref="SpellForm"/>, the last of the five forms.</summary>
public class SpellFormTests
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

    private static SpellFormRow Row(string name, string level = "1") =>
        new(level, "", "1", "", name);

    [Fact]
    public void The_layout_expands_to_five_fields_per_page_row()
    {
        var form = new SpellForm(pageSize: 6);

        // 5 headers + 7 class label/value pairs + 6 rows x 5 fields.
        Assert.Equal(5 + 14 + (6 * 5), form.Form.Count);
    }

    [Fact]
    public void The_headers_run_across_and_the_rows_run_down()
    {
        var font = FixedFont();
        var form = new SpellForm(pageSize: 3);

        form.Populate(font, [Row("Sleep"), Row("Magic Missile"), Row("Shield")]);

        Assert.Equal("LEVEL", form.Form.Field(SpellFormFields.LevelLabel)!.Text);
        Assert.Equal("SPELL", form.Form.Field(SpellFormFields.NameLabel)!.Text);
        Assert.True(form.Form.Field(SpellFormFields.LevelLabel)!.Left
                    < form.Form.Field(SpellFormFields.NameLabel)!.Left);

        int first = form.Form.Field(SpellFormFields.Name)!.Top;
        int second = form.Form.Field(SpellFormFields.Name + SpellFormFields.RowOffset(1))!.Top;
        Assert.Equal(first + 10, second);
        Assert.Equal("Magic Missile",
                     form.Form.Field(SpellFormFields.Name + SpellFormFields.RowOffset(1))!.Text);
    }

    [Fact]
    public void An_unused_column_is_blanked_rather_than_removed()
    {
        // COST is off for memorising and on for shopping. The name column is placed relative to it,
        // so the field has to stay in the layout either way.
        var font = FixedFont();
        var form = new SpellForm(pageSize: 2);

        form.Populate(font, [Row("Sleep")], useCost: false);

        Assert.Equal("", form.Form.Field(SpellFormFields.CostLabel)!.Text);
        Assert.NotEqual(-1, form.Form.Field(SpellFormFields.CostLabel)!.Left);
        Assert.Equal("SPELL", form.Form.Field(SpellFormFields.NameLabel)!.Text);
    }

    [Fact]
    public void Only_the_classes_a_character_belongs_to_are_listed()
    {
        var font = FixedFont();
        var form = new SpellForm(pageSize: 2);

        // A cleric and nothing else.
        string?[] available = [null, "3", null, null, null, null, null];
        form.Populate(font, [Row("Cure Light Wounds")], available: available);

        Assert.Equal("", form.Form.Field(SpellFormFields.ClassLabels[0])!.Text);
        Assert.Equal("CLERIC", form.Form.Field(SpellFormFields.ClassLabels[1])!.Text);
        Assert.Equal("3", form.Form.Field(SpellFormFields.ClassValues[1])!.Text);
        Assert.Equal("", form.Form.Field(SpellFormFields.ClassValues[6])!.Text);
    }

    [Fact]
    public void Ranger_shares_paladins_row_and_druid_shares_thiefs()
    {
        // Deliberate in the reference: both hang off the same anchor with the same x, "moved up
        // from bottom to avoid being displayed over border graphics". No character is both a
        // paladin and a ranger, or both a thief and a druid, so only one of each pair is ever
        // filled and the overlap never shows. Separating them would be tidier and would not match.
        var font = FixedFont();
        var form = new SpellForm(pageSize: 1);

        string?[] all = ["1", "2", "3", "4", "5", "6", "7"];
        form.Populate(font, [Row("Sleep")], available: all);

        // Same row and same right edge -- not the same left. RIGHT right-*aligns* against the
        // anchor, so PALADIN and RANGER end flush and start six pixels apart because one is a
        // letter longer.
        var labels = SpellFormFields.ClassLabels;
        Assert.Equal(form.Form.Field(labels[4])!.Top, form.Form.Field(labels[5])!.Top);
        Assert.Equal(form.Form.Field(labels[4])!.Right, form.Form.Field(labels[5])!.Right);

        Assert.Equal(form.Form.Field(labels[2])!.Top, form.Form.Field(labels[6])!.Top);
        Assert.Equal(form.Form.Field(labels[2])!.Right, form.Form.Field(labels[6])!.Right);
    }

    [Fact]
    public void The_class_counts_line_up_on_their_right_edge()
    {
        // Every label is right-aligned against the magic-user label, so the column ends flush
        // however long the class names are.
        var font = FixedFont();
        var form = new SpellForm(pageSize: 1);

        string?[] all = ["1", "2", "3", "4", "5", "6", "7"];
        form.Populate(font, [Row("Sleep")], available: all);

        int expected = form.Form.Field(SpellFormFields.ClassLabels[0])!.Right;
        foreach (int label in SpellFormFields.ClassLabels)
        {
            Assert.Equal(expected, form.Form.Field(label)!.Right);
        }
    }

    [Fact]
    public void Selecting_a_row_highlights_all_five_columns()
    {
        var font = FixedFont();
        var form = new SpellForm(pageSize: 2);
        form.Populate(font, [Row("Sleep"), Row("Shield")]);

        form.Select(1);

        int offset = SpellFormFields.RowOffset(1);
        Assert.True(form.Form.Field(SpellFormFields.Name + offset)!.Highlight);
        Assert.True(form.Form.Field(SpellFormFields.Level + offset)!.Highlight);
        Assert.False(form.Form.Field(SpellFormFields.Name)!.Highlight);

        form.Select(-1);
        Assert.Equal(-1, form.Selection);
        Assert.False(form.Form.Field(SpellFormFields.Name + offset)!.Highlight);
    }

    [Fact]
    public void A_click_on_a_rows_text_returns_that_row()
    {
        var font = FixedFont();
        var form = new SpellForm(pageSize: 3);
        form.Populate(font, [Row("Sleep"), Row("Magic Missile"), Row("Shield")]);

        var second = form.Form.Field(SpellFormFields.Name + SpellFormFields.RowOffset(1))!;

        Assert.Equal(1, form.RowAt(second.Left + 1, second.Top + 1));
        Assert.Equal(-1, form.RowAt(10000, 10000));
    }

    [Fact]
    public void A_populated_form_draws_something()
    {
        var font = FixedFont();
        var form = new SpellForm(pageSize: 2);
        var surface = new Surface(640, 480, SurfaceKind.Buffer);

        string?[] available = [null, "3", null, null, null, null, null];
        form.Populate(font, [Row("Cure Light Wounds")], available: available);
        form.Display(surface, font);

        Assert.Contains(surface.Pixels, p => p != 0);
    }
}
