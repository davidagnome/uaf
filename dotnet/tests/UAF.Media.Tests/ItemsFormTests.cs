namespace UAF.Media.Tests;

/// <summary>
/// Covers <see cref="ItemsForm"/> — the first concrete form built on <see cref="TextForm"/>.
/// </summary>
public class ItemsFormTests
{
    private const uint Key = 0xFF00FF00;
    private const uint Ink = 0xFFFFFFFF;

    /// <summary>Every glyph 6 × 10, so a width is a length times six.</summary>
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

    private static ItemsFormRow Row(string name, string qty = "1", string cost = "10",
                                    string ready = "NO") => new(ready, qty, cost, name);

    [Fact]
    public void Every_field_id_carries_the_white_colour_bit()
    {
        // The C++ enum assigns STIF_white mid-list, so the count restarts at 0x10000000 and every
        // later id inherits it. Dropping the bit would break every relative placement, because
        // fieldNumMask keeps colour.
        foreach (int id in new[]
                 {
                     ItemsFormFields.ReadyLabel, ItemsFormFields.NameLabel,
                     ItemsFormFields.MoneyLabel, ItemsFormFields.Ready, ItemsFormFields.Row,
                     ItemsFormFields.CoinLabels[0], ItemsFormFields.CoinAmounts[9],
                 })
        {
            Assert.Equal((int)FormFlags.White, id & (int)FormFlags.White);
        }
    }

    [Fact]
    public void The_layout_expands_to_five_fields_per_page_row()
    {
        var form = new ItemsForm(pageSize: 8);

        // 4 headers + money label + 10 coin labels + 10 coin amounts + 8 rows x 5 fields.
        Assert.Equal(4 + 1 + 20 + (8 * 5), form.Form.Count);
    }

    [Fact]
    public void Headers_are_blanked_rather_than_removed_when_a_column_is_unused()
    {
        // The name column is placed relative to the cost column, so removing an unused header
        // would move every item name. Blanking keeps the geometry and hides the text.
        var font = FixedFont();
        var form = new ItemsForm(pageSize: 2);

        form.Populate(font, [Row("Sword")], useCost: false);

        Assert.Equal("", form.Form.Field(ItemsFormFields.CostLabel)!.Text);
        Assert.NotEqual(-1, form.Form.Field(ItemsFormFields.CostLabel)!.Left);
        Assert.Equal("NAME", form.Form.Field(ItemsFormFields.NameLabel)!.Text);
    }

    [Fact]
    public void Rows_stack_down_the_page_at_the_font_height()
    {
        var font = FixedFont();
        var form = new ItemsForm(pageSize: 3);

        form.Populate(font, [Row("Sword"), Row("Shield"), Row("Bow")]);

        int first = form.Form.Field(ItemsFormFields.Name)!.Top;
        int second = form.Form.Field(ItemsFormFields.Name + ItemsFormFields.RowOffset(1))!.Top;
        int third = form.Form.Field(ItemsFormFields.Name + ItemsFormFields.RowOffset(2))!.Top;

        Assert.Equal(first + 10, second);
        Assert.Equal(second + 10, third);
    }

    [Fact]
    public void A_short_page_still_places_every_row()
    {
        // Blank rows are written, not skipped: an unwritten field has no placement, and the row
        // below is positioned against the row above it.
        var font = FixedFont();
        var form = new ItemsForm(pageSize: 4);

        form.Populate(font, [Row("Sword")]);

        var last = form.Form.Field(ItemsFormFields.Name + ItemsFormFields.RowOffset(3))!;
        Assert.Equal("", last.Text);
        Assert.NotEqual(-1, last.Top);
    }

    [Fact]
    public void Only_the_denominations_a_design_uses_are_shown()
    {
        // Designs rename and omit denominations -- Ambassador's_Letter configures gold, silver and
        // copper only -- so the labels come from the design rather than from a fixed list.
        var font = FixedFont();
        var form = new ItemsForm(pageSize: 1);

        var coins = new ItemsFormCoin[10];
        coins[0] = new ItemsFormCoin("PLATINUM", null);      // not in use
        coins[1] = new ItemsFormCoin("Crowns", "12");        // a renamed gold

        form.Populate(font, [Row("Sword")], moneyLabel: "Aramil's Money", coins: coins);

        Assert.Equal("", form.Form.Field(ItemsFormFields.CoinLabels[0])!.Text);
        Assert.Equal("Crowns", form.Form.Field(ItemsFormFields.CoinLabels[1])!.Text);
        Assert.Equal("12", form.Form.Field(ItemsFormFields.CoinAmounts[1])!.Text);
        Assert.Equal("Aramil's Money", form.Form.Field(ItemsFormFields.MoneyLabel)!.Text);
    }

    [Fact]
    public void The_row_marker_is_a_zero_width_placeholder_not_a_selection_box()
    {
        // It is written `ready+SEL / name+SEL`, which reads as "span the four columns" -- and that
        // is not what happens. Auto-repeat expansion overwrites both relative values with plain
        // field ids, dropping the SEL bit, so the marker takes its left from Ready, its top from
        // Name, and has no width because it carries no text.
        //
        // This is the reference's behaviour, not a shortcut: it is why showItems keeps a separate
        // InventoryRects list to hit-test rows with.
        var font = FixedFont();
        var form = new ItemsForm(pageSize: 1);

        form.Populate(font, [Row("Longsword")]);

        var marker = form.Form.Field(ItemsFormFields.Row)!;
        var ready = form.Form.Field(ItemsFormFields.Ready)!;
        var name = form.Form.Field(ItemsFormFields.Name)!;

        Assert.Equal(ready.Left, marker.Left);
        Assert.Equal(name.Top, marker.Top);
        Assert.Equal(marker.Left, marker.Right);      // no width at all
        Assert.NotEqual(name.Right, marker.Right);
    }

    [Fact]
    public void A_click_on_a_rows_text_returns_that_row()
    {
        var font = FixedFont();
        var form = new ItemsForm(pageSize: 3);
        form.Populate(font, [Row("Sword"), Row("Shield"), Row("Bow")]);

        // Any of the row's four columns identifies it, since the marker cannot be hit.
        var secondName = form.Form.Field(ItemsFormFields.Name + ItemsFormFields.RowOffset(1))!;
        var secondCost = form.Form.Field(ItemsFormFields.Cost + ItemsFormFields.RowOffset(1))!;

        Assert.Equal(1, form.RowAt(secondName.Left + 1, secondName.Top + 1));
        Assert.Equal(1, form.RowAt(secondCost.Left + 1, secondCost.Top + 1));
        Assert.Equal(0, form.RowAt(form.Form.Field(ItemsFormFields.Name)!.Left + 1,
                                   form.Form.Field(ItemsFormFields.Name)!.Top + 1));
        Assert.Equal(-1, form.RowAt(10000, 10000));
    }

    [Fact]
    public void Selecting_a_row_highlights_its_four_columns_and_releases_the_last()
    {
        var font = FixedFont();
        var form = new ItemsForm(pageSize: 2);
        form.Populate(font, [Row("Sword"), Row("Shield")]);

        form.Select(0);
        Assert.True(form.Form.Field(ItemsFormFields.Name)!.Highlight);
        Assert.True(form.Form.Field(ItemsFormFields.Cost)!.Highlight);

        form.Select(1);
        Assert.False(form.Form.Field(ItemsFormFields.Name)!.Highlight);
        Assert.True(form.Form.Field(ItemsFormFields.Name
                                    + ItemsFormFields.RowOffset(1))!.Highlight);

        form.Select(-1);
        Assert.Equal(-1, form.Selection);
        Assert.False(form.Form.Field(ItemsFormFields.Name
                                     + ItemsFormFields.RowOffset(1))!.Highlight);
    }

    [Fact]
    public void A_populated_form_draws_something()
    {
        var font = FixedFont();
        var form = new ItemsForm(pageSize: 2);
        var surface = new Surface(640, 480, SurfaceKind.Buffer);

        form.Populate(font, [Row("Sword"), Row("Shield")]);
        form.Display(surface, font);

        Assert.Contains(surface.Pixels, p => p != 0);
    }
}
