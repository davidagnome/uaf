namespace UAF.Media.Tests;

/// <summary>Covers <see cref="CharStatsForm"/>.</summary>
public class CharStatsFormTests
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

    private static CharacterSheet Sheet(params string[] experienceLines) => new(
        Name: "Sherlas of Hemlock", Gender: "MALE", Age: "17 YEARS", Status: "OKAY",
        Alignment: "NEUTRAL", Race: "HUMAN", Class: "RANGER", Level: "LEVEL 1",
        Hits: "10", MaxHits: "/10",
        ExperienceLines: experienceLines,
        Abilities: ["17", "12", "9", "14", "16", "11"],
        Coins: [new ItemsFormCoin("PLATINUM", "3"), new ItemsFormCoin("GOLD", "120")]);

    [Fact]
    public void The_sheet_fills_in_the_fields_the_port_can_supply()
    {
        var font = FixedFont();
        var form = new CharStatsForm();

        form.Populate(font, Sheet("FIGHTER 25460"));

        Assert.Equal("Sherlas of Hemlock", form.Form.Field(CharStatsFields.Name)!.Text);
        Assert.Equal("MALE", form.Form.Field(CharStatsFields.Gender)!.Text);
        Assert.Equal("HUMAN", form.Form.Field(CharStatsFields.Race)!.Text);
        Assert.Equal("10", form.Form.Field(CharStatsFields.Hits)!.Text);
        Assert.Equal("/10", form.Form.Field(CharStatsFields.MaxHits)!.Text);
        Assert.Equal("FIGHTER 25460", form.Form.Field(CharStatsFields.Exp1)!.Text);
        Assert.Equal("STR", form.Form.Field(CharStatsFields.AbilityLabels[0])!.Text);
        Assert.Equal("17", form.Form.Field(CharStatsFields.AbilityValues[0])!.Text);
        Assert.Equal("11", form.Form.Field(CharStatsFields.AbilityValues[5])!.Text);
    }

    [Fact]
    public void The_derived_combat_fields_are_laid_out_and_left_blank()
    {
        // AC, THAC0, damage, encumbrance and movement all come out of GameRules.cpp, which is not
        // ported. The labels are drawn and the values left empty rather than filled with plausible
        // numbers, because a wrong armour class looks exactly like a right one.
        var font = FixedFont();
        var form = new CharStatsForm();

        form.Populate(font, Sheet());

        Assert.Equal("ARMOR CLASS", form.Form.Field(CharStatsFields.ArmorClassLabel)!.Text);
        Assert.Equal("", form.Form.Field(CharStatsFields.ArmorClass)!.Text);
        Assert.Equal("THAC0", form.Form.Field(CharStatsFields.Thac0Label)!.Text);
        Assert.Equal("", form.Form.Field(CharStatsFields.Thac0)!.Text);
        Assert.Equal("ENCUMBRANCE", form.Form.Field(CharStatsFields.EncumbranceLabel)!.Text);
        Assert.Equal("", form.Form.Field(CharStatsFields.Movement)!.Text);
    }

    [Fact]
    public void A_multiclass_character_gets_a_line_per_baseclass()
    {
        var font = FixedFont();
        var form = new CharStatsForm();

        form.Populate(font, Sheet("CLERIC 8000", "FIGHTER 16000"));

        Assert.Equal("CLERIC 8000", form.Form.Field(CharStatsFields.Exp1)!.Text);
        Assert.Equal("FIGHTER 16000", form.Form.Field(CharStatsFields.Exp2)!.Text);
        Assert.Equal("", form.Form.Field(CharStatsFields.Exp3)!.Text);

        // The unused line still has a placement -- the level line below is positioned against the
        // first experience line, and every row after it hangs off its predecessor.
        Assert.NotEqual(-1, form.Form.Field(CharStatsFields.Exp3)!.Top);
    }

    [Fact]
    public void The_ability_rows_run_down_the_left_column()
    {
        var font = FixedFont();
        var form = new CharStatsForm();

        form.Populate(font, Sheet());

        int previous = int.MinValue;
        foreach (int label in CharStatsFields.AbilityLabels)
        {
            int top = form.Form.Field(label)!.Top;
            Assert.True(top > previous, "ability rows must descend");
            previous = top;
        }

        // The value sits to the right of its label on the same line.
        var str = form.Form.Field(CharStatsFields.AbilityLabels[0])!;
        var value = form.Form.Field(CharStatsFields.AbilityValues[0])!;
        Assert.Equal(str.Top, value.Top);
        Assert.True(value.Left > str.Left);
    }

    [Fact]
    public void The_six_ability_selection_fields_are_never_placed()
    {
        // Same as RestTimeForm: showCharStats never gives them text, so they name the tab stops
        // and the highlight goes on the value beside each. Unlike ItemsForm's row marker, nothing
        // has flattened their flags -- they would span label and value if they were ever placed.
        var font = FixedFont();
        var form = new CharStatsForm();

        form.Populate(font, Sheet());

        Assert.All(CharStatsFields.AbilityStops,
                   stop => Assert.Equal(-1, form.Form.Field(stop)!.Left));
    }

    [Fact]
    public void Tab_cycles_the_six_abilities_and_highlights_the_value()
    {
        var font = FixedFont();
        var form = new CharStatsForm();
        form.Populate(font, Sheet());

        Assert.Equal(-1, form.Selection);

        form.Tab();
        Assert.Equal(0, form.Selection);
        Assert.True(form.Form.Field(CharStatsFields.AbilityValues[0])!.Highlight);

        for (int i = 0; i < 5; i++)
        {
            form.Tab();
        }

        Assert.Equal(5, form.Selection);
        Assert.False(form.Form.Field(CharStatsFields.AbilityValues[0])!.Highlight);
        Assert.True(form.Form.Field(CharStatsFields.AbilityValues[5])!.Highlight);

        form.Tab();
        Assert.Equal(0, form.Selection);
    }

    [Fact]
    public void Only_the_denominations_the_character_carries_are_shown()
    {
        var font = FixedFont();
        var form = new CharStatsForm();

        form.Populate(font, Sheet());

        Assert.Equal("PLATINUM", form.Form.Field(CharStatsFields.CoinLabels[0])!.Text);
        Assert.Equal("3", form.Form.Field(CharStatsFields.CoinAmounts[0])!.Text);
        Assert.Equal("120", form.Form.Field(CharStatsFields.CoinAmounts[1])!.Text);
        Assert.Equal("", form.Form.Field(CharStatsFields.CoinLabels[9])!.Text);
    }

    [Fact]
    public void The_available_row_appears_only_during_score_distribution()
    {
        var font = FixedFont();
        var form = new CharStatsForm();

        form.Populate(font, Sheet());
        Assert.Equal("", form.Form.Field(CharStatsFields.AvailableLabel)!.Text);

        form.Populate(font, Sheet() with { Available = "4" });
        Assert.Equal("AVAIL", form.Form.Field(CharStatsFields.AvailableLabel)!.Text);
        Assert.Equal("4", form.Form.Field(CharStatsFields.Available)!.Text);
    }

    [Fact]
    public void The_sheet_draws_into_both_columns()
    {
        var font = FixedFont();
        var form = new CharStatsForm();
        var surface = new Surface(640, 480, SurfaceKind.Buffer);

        form.Populate(font, Sheet("FIGHTER 25460"));
        form.Display(surface, font);

        // Something on the left, something on the right: the two-column layout is the whole point.
        bool left = false, right = false;
        for (int y = 0; y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (surface[x, y] == 0) { continue; }
                if (x < 200) { left = true; } else { right = true; }
            }
        }

        Assert.True(left, "nothing drew in the left column");
        Assert.True(right, "nothing drew in the right column");
    }
}
