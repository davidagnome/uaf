using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// The six-bit compressed string table (<c>UAImportStrings</c>,
/// <c>UAFWinEd/UAImport.cpp:1781</c>).
/// </summary>
public class FruaStringTableTests
{
    private static string? Heirs()
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

        string design = Path.Combine(dir.FullName, "reference", "Unlimited Adventures -ENG",
                                     "DESIGNS", "UA", "HEIRS.DSN");
        return Directory.Exists(design) ? design : null;
    }

    /// <summary>Packs text the way FRUA does, so the decoder can be tested against it.</summary>
    /// <remarks>
    /// Written from the format's own description rather than from the decoder, so the two are not
    /// the same code read twice — but the real corpus below is what actually settles it.
    /// </remarks>
    private static byte[] Pack(string text)
    {
        var bits = new List<bool>();

        foreach (char c in text)
        {
            // The inverse of the reader: letters fold down by clearing the bit six cannot carry.
            int v = c >= 65 && c <= 95 ? c & 0x3F : c;
            for (int b = 5; b >= 0; b--)
            {
                bits.Add((v & (1 << b)) != 0);
            }
        }

        // A zero group terminates.
        for (int b = 0; b < 6; b++)
        {
            bits.Add(false);
        }

        var bytes = new byte[(bits.Count + 7) / 8];
        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                bytes[i / 8] |= (byte)(1 << (7 - (i % 8)));
            }
        }

        return bytes;
    }

    /// <summary>A level file carrying one packed string in slot 1.</summary>
    private static byte[] Level(string text, int slot = 1)
    {
        var b = new byte[FruaLevel.Length];
        var packed = Pack(text);

        b[FruaStringTable.CountAt] = 1;
        b[FruaStringTable.LengthsAt + slot - 1] = (byte)packed.Length;

        int at = 0;
        for (int i = 0; i < slot - 1; i++)
        {
            at += b[FruaStringTable.LengthsAt + i];
        }

        packed.CopyTo(b, FruaStringTable.StringsAt + at);
        return b;
    }

    [Theory]
    [InlineData("HELLO")]
    [InlineData("THE PARTY ARRIVES.")]
    [InlineData("0123456789")]
    [InlineData("A")]
    [InlineData("!\"#$%&'()*+,-./:;<=>?")]
    public void A_packed_string_round_trips(string text)
    {
        var table = FruaStringTable.Read(Level(text));

        Assert.Equal(text, table.Get(1));
    }

    /// <summary>
    /// Index 0 means "no string", which is how an event says it has nothing to say.
    /// </summary>
    [Fact]
    public void Index_zero_is_absence_rather_than_the_first_string()
    {
        var table = FruaStringTable.Read(Level("SOMETHING"));

        Assert.Null(table.Get(0));
        Assert.Equal("SOMETHING", table.Get(1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(401)]
    [InlineData(10_000)]
    public void An_index_outside_the_table_is_null(int index)
    {
        Assert.Null(FruaStringTable.Read(Level("X")).Get(index));
    }

    /// <summary>A slot with no bytes decodes to nothing, not to an empty string.</summary>
    [Fact]
    public void An_unused_slot_is_null()
    {
        Assert.Null(FruaStringTable.Read(Level("X")).Get(7));
    }

    /// <summary>
    /// A string reached through the length table, not through an offset table.
    /// </summary>
    /// <remarks>
    /// Slot 3's position is the sum of slots 1 and 2's compressed lengths, so a wrong length
    /// anywhere before it shifts everything after.
    /// </remarks>
    [Fact]
    public void A_later_slot_starts_after_every_length_before_it()
    {
        var b = new byte[FruaLevel.Length];
        var first = Pack("FIRST");
        var second = Pack("SECOND ONE");
        var third = Pack("THIRD");

        b[FruaStringTable.LengthsAt] = (byte)first.Length;
        b[FruaStringTable.LengthsAt + 1] = (byte)second.Length;
        b[FruaStringTable.LengthsAt + 2] = (byte)third.Length;

        first.CopyTo(b, FruaStringTable.StringsAt);
        second.CopyTo(b, FruaStringTable.StringsAt + first.Length);
        third.CopyTo(b, FruaStringTable.StringsAt + first.Length + second.Length);

        var table = FruaStringTable.Read(b);

        Assert.Equal("FIRST", table.Get(1));
        Assert.Equal("SECOND ONE", table.Get(2));
        Assert.Equal("THIRD", table.Get(3));
    }

    [Fact]
    public void A_file_too_short_to_hold_strings_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => FruaStringTable.Read(new byte[6000]));
    }

    // ---- the real DOS levels ---------------------------------------------------------------

    /// <summary>
    /// Real game text out of <c>HEIRS.DSN</c>.
    /// </summary>
    /// <remarks>
    /// <b>This is what proves the decoder.</b> The synthetic tests above pack with my own inverse,
    /// so a misreading of the format would round-trip through itself perfectly happily. Legible
    /// English out of bytes nobody in this port wrote cannot happen by accident.
    /// </remarks>
    [Fact]
    public void Real_dialogue_decodes_from_a_shipped_level()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        Assert.Equal(
            "\"HERE WE ARE AT LAST...SKULL CRAG.\" ",
            level.Strings.Get(4));

        Assert.Equal(
            "A GUARD YELLS ANGRILY AT THE PARTY, ",
            level.Strings.Get(3));
    }

    /// <summary>The overland level's terrain messages.</summary>
    [Fact]
    public void The_overland_zone_messages_decode()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 1) is not { } level)
        {
            return;
        }

        Assert.Equal("THE MOUNTAINS ARE TOO STEEP TO CONTINUE.", level.Strings.Get(3));
        Assert.Equal("THE ROAD IS IN GOOD REPAIR.", level.Strings.Get(4));
    }

    /// <summary>
    /// Every string in every shipped level decodes to the alphabet the format allows.
    /// </summary>
    /// <remarks>
    /// <b>Six bits cannot express a lower-case letter</b>, which is why FRUA dialogue is all
    /// capitals. A decoder that had the shift wrong would produce characters outside 0–95, and a
    /// sweep of the whole corpus is the cheapest way to see that it does not.
    /// </remarks>
    [Fact]
    public void Every_decoded_character_is_inside_the_six_bit_alphabet()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int decoded = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            for (int i = 1; i <= FruaStringTable.Capacity; i++)
            {
                if (level.Strings.Get(i) is not { } text)
                {
                    continue;
                }

                foreach (char c in text)
                {
                    Assert.InRange(c, (char)32, (char)95);
                    Assert.DoesNotContain(c, "abcdefghijklmnopqrstuvwxyz");
                }

                decoded++;
            }
        }

        Assert.True(decoded > 100, $"only {decoded} strings decoded across the whole design");
    }
}
