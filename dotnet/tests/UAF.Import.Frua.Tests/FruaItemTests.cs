using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Reading the DOS FRUA item database (<c>ImportUAItems</c>/<c>ImportUAItem</c>,
/// <c>UAFWinEd/UAImport.cpp:5391</c>).
/// </summary>
public class FruaItemTests
{
    /// <summary>The stock FRUA database, or null when the corpus is absent.</summary>
    private static string? StockDatabase()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string path = Path.Combine(dir.FullName, "reference", "Unlimited Adventures -ENG",
                                   "GAME", "UA", "DISK1");
        return Directory.Exists(path) ? path : null;
    }

    private static FruaItem Item(byte name1, byte name2, byte name3, byte identified = 0)
    {
        // Stored name3, name2, name1 -- the declaration's order, which is reversed.
        var b = new byte[FruaItem.Length];
        b[1] = name3;
        b[2] = name2;
        b[3] = name1;
        b[11] = identified;
        return FruaItem.Read(b);
    }

    [Fact]
    public void The_vocabulary_came_across_whole()
    {
        Assert.Equal(126, FruaItemVocabulary.Count);
        Assert.Equal(string.Empty, FruaItemVocabulary.Word(0));
        Assert.Equal("Battle Axe", FruaItemVocabulary.Word(1));
        Assert.Equal("Bundle of", FruaItemVocabulary.Word(FruaItemVocabulary.BundleOf));

        // Out of range contributes nothing rather than throwing.
        Assert.Equal(string.Empty, FruaItemVocabulary.Word(126));
        Assert.Equal(string.Empty, FruaItemVocabulary.Word(-1));
    }

    [Fact]
    public void A_name_is_composed_from_three_vocabulary_words()
    {
        Assert.Equal("Battle Axe", Item(1, 0, 0).Name);
        Assert.Equal("Battle Axe Hand Axe", Item(1, 2, 0).Name);
        Assert.Equal("Battle Axe Hand Axe Club", Item(1, 2, 3).Name);
    }

    /// <summary>
    /// <c>identified</c> is a mask over the three words, not a boolean.
    /// </summary>
    [Theory]
    [InlineData(0, "Battle Axe Hand Axe Club")]
    [InlineData(1, "Hand Axe Club")]
    [InlineData(2, "Battle Axe Club")]
    [InlineData(4, "Battle Axe Hand Axe")]
    [InlineData(7, "")]
    public void The_identified_mask_hides_words_one_at_a_time(byte mask, string expected)
    {
        Assert.Equal(expected, Item(1, 2, 3, mask).UnidentifiedName);

        // The identified name always shows everything, whatever the mask says.
        Assert.Equal("Battle Axe Hand Axe Club", Item(1, 2, 3, mask).Name);
    }

    /// <summary>A third word of 77 turns the second field into a count.</summary>
    [Fact]
    public void A_bundle_reads_its_second_field_as_a_quantity()
    {
        // name3 = 77 ("Bundle of"), name2 = 20 -> a quantity, not vocabulary word 20.
        var bundle = Item(0, 20, FruaItemVocabulary.BundleOf);

        Assert.Equal("20 Bundle of", bundle.Name);

        // Without the 77 it would be a word instead.
        Assert.Equal(FruaItemVocabulary.Word(20) + " " + FruaItemVocabulary.Word(3),
                     Item(0, 20, 3).Name);
    }

    [Fact]
    public void A_class_record_reads_both_damage_rolls()
    {
        var b = new byte[FruaItemClass.Length];
        b[0] = 0;                         // weapon hand
        b[1] = 1;                         // one handed
        b[2] = 1; b[3] = 8; b[4] = 2;     // versus large: 1d8+2
        b[9] = 2; b[10] = 4; b[11] = 1;   // versus small: 2d4+1
        b[12] = 5;                        // range
        b[14] = 4;                        // hand held

        var c = FruaItemClass.Read(b);

        Assert.Equal(new FruaDamage(1, 8, 2), c.VersusLarge);
        Assert.Equal(new FruaDamage(2, 4, 1), c.VersusSmall);
        Assert.Equal(5, c.Range);
        Assert.Equal(4, c.WeaponType);
    }

    // ---- the stock FRUA database ------------------------------------------------------------

    /// <summary>
    /// The database that ships with FRUA reads whole, with names a player would recognise.
    /// </summary>
    /// <remarks>
    /// <b>Neither file has a header, a count or any framing</b>, so a wrong record size would not
    /// fail — it would silently produce garbage names at the wrong stride. Legible weapon names at
    /// the right indices are what shows the stride is right, and the exact division of both files
    /// (2,048 / 16 and 4,572 / 18) corroborates it.
    /// </remarks>
    [Fact]
    public void The_stock_database_reads_with_recognisable_names()
    {
        if (StockDatabase() is not { } dir || FruaItemDatabase.Read(dir) is not { } db)
        {
            return;
        }

        Assert.Equal(128, db.Classes.Count);
        Assert.Equal(254, db.Items.Count);

        Assert.Equal("Arrow", db.Items[0].Name);
        Assert.Equal("Battle Axe", db.Items[1].Name);
        Assert.Equal("Hand Axe", db.Items[2].Name);
        Assert.Equal("Composite Short Bow", db.Items[3].Name);
        Assert.Equal("Long Bow", db.Items[5].Name);
        Assert.Equal("Club", db.Items[7].Name);
    }

    /// <summary>Prices and weights are sensible across the whole database.</summary>
    [Fact]
    public void Every_stock_item_names_a_class_that_exists()
    {
        if (StockDatabase() is not { } dir || FruaItemDatabase.Read(dir) is not { } db)
        {
            return;
        }

        int named = 0;

        foreach (var item in db.Items)
        {
            Assert.InRange(item.ClassIndex, 0, db.Classes.Count - 1);

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                named++;
            }
        }

        // A wrong stride would leave most records nameless, since the vocabulary guard drops any
        // index past 125.
        Assert.True(named > 200, $"only {named} of {db.Items.Count} items have a name");
    }

    /// <summary>A magical item costs more than its mundane counterpart.</summary>
    [Fact]
    public void The_magical_items_carry_their_bonus_in_the_name()
    {
        if (StockDatabase() is not { } dir || FruaItemDatabase.Read(dir) is not { } db)
        {
            return;
        }

        Assert.Equal("Bolt +1", db.Items[60].Name);
        Assert.Equal("Bolt +3", db.Items[120].Name);
        Assert.Equal("Silver Shield +4", db.Items[200].Name);

        // And the price climbs with it -- a +4 shield is 7,000 platinum.
        Assert.Equal(7000, db.Items[200].Price);
        Assert.True(db.Items[120].Price > db.Items[60].Price);
    }

    /// <summary>A directory with no item files is absence, not an error.</summary>
    [Fact]
    public void A_directory_without_the_files_reads_as_null()
    {
        Assert.Null(FruaItemDatabase.Read(Path.Combine(Path.GetTempPath(), "no-such-frua-dir")));
    }
}
