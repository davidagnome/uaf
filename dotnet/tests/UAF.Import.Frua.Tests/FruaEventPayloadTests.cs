using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// The per-event-type payload readers (<c>addTextEvent</c>, <c>addTeleporterEvent</c> and
/// <c>addStairsEvent</c>, <c>UAFWinEd/UAImport.cpp</c>).
/// </summary>
public class FruaEventPayloadTests
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

    /// <summary>An event whose payload bytes are set at the reference's own offsets.</summary>
    private static FruaEvent Event(byte type, params (int Offset, byte Value)[] payload)
    {
        var record = new byte[FruaEvent.Length];
        record[0] = type;

        foreach (var (offset, value) in payload)
        {
            // Offset 5 is the first payload byte; see FruaEvent's remarks.
            record[4 + (offset - 5)] = value;
        }

        return FruaEvent.Read(record);
    }

    private static void Word(FruaEvent _, byte[] record, int offset, ushort value)
    {
        record[4 + (offset - 5)] = (byte)value;
        record[4 + (offset - 5) + 1] = (byte)(value >> 8);
    }

    // ---- transfers ---------------------------------------------------------------------------

    /// <summary>
    /// The facing is a two-bit field, and 4|8 is west rather than east-or-south.
    /// </summary>
    /// <remarks>
    /// The reference tests 12 before 4 and 8. Testing 4 first would match 12 as well and turn
    /// every west into an east — a transposition that would look like a design bug, not a port
    /// bug, until someone walked through the door.
    /// </remarks>
    [Theory]
    [InlineData(0, FruaTransferFacing.North)]
    [InlineData(4, FruaTransferFacing.East)]
    [InlineData(8, FruaTransferFacing.South)]
    [InlineData(12, FruaTransferFacing.West)]
    public void The_facing_masks_are_tested_widest_first(byte flags, FruaTransferFacing expected)
    {
        var e = Event(34, (8, flags));

        Assert.Equal(expected, FruaTransferEvent.Read(e).Facing);
    }

    /// <summary>The transfer-on-yes bit reads inverted.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(64, false)]
    public void The_transfer_on_yes_bit_is_inverted(byte flags, bool expected)
    {
        Assert.Equal(expected, FruaTransferEvent.Read(Event(34, (8, flags))).TransferOnYes);
    }

    [Fact]
    public void The_destination_reads_x_and_y_the_way_they_are_stored()
    {
        // y at 9, x at 10 -- stored in that order, as everywhere else in this format.
        var e = Event(34, (9, 7), (10, 13));
        var t = FruaTransferEvent.Read(e);

        Assert.Equal(13, t.DestinationX);
        Assert.Equal(7, t.DestinationY);
    }

    /// <summary>
    /// An entry point is used only when bit 1 says so, and 0 folds back to "none".
    /// </summary>
    [Theory]
    [InlineData(0, 5, -1)]    // bit 1 clear: coordinates, not an entry point
    [InlineData(1, 5, 4)]     // stored 5 -> zero-based 4
    [InlineData(1, 1, -1)]    // stored 1 -> 0, which the reference maps back to -1
    [InlineData(1, 0, -1)]    // stored 0 -> -1 already
    public void An_entry_point_of_zero_means_none(byte destFlags, byte stored, int expected)
    {
        var e = Event(34, (13, destFlags), (14, stored));

        Assert.Equal(expected, FruaTransferEvent.Read(e).DestinationEntryPoint);
    }

    [Fact]
    public void The_large_picture_bit_is_split_from_the_flags()
    {
        Assert.True(FruaTransferEvent.Read(Event(34, (7, 9), (8, 128))).PictureIsLarge);
        Assert.False(FruaTransferEvent.Read(Event(34, (7, 9), (8, 0))).PictureIsLarge);

        // And it must not leak into the facing, which reads the low bits of the same byte.
        Assert.Equal(FruaTransferFacing.North,
                     FruaTransferEvent.Read(Event(34, (8, 128))).Facing);
    }

    // ---- text --------------------------------------------------------------------------------

    /// <summary>Five string slots are concatenated into one passage.</summary>
    [Fact]
    public void The_text_is_five_slots_joined()
    {
        var record = new byte[FruaEvent.Length];
        record[0] = 2;
        var e = FruaEvent.Read(record);

        // Slots 1..5 at offsets 9, 11, 13, 15, 17.
        for (int i = 0; i < 5; i++)
        {
            Word(e, record, 9 + (i * 2), (ushort)(i + 1));
        }

        var table = Table(("ONE ", "TWO ", "THREE ", "FOUR ", "FIVE"));
        var text = FruaTextEvent.Read(FruaEvent.Read(record), table);

        Assert.Equal("ONE TWO THREE FOUR FIVE", text.Text);
    }

    /// <summary>Each chunk carries its own highlight bit.</summary>
    [Fact]
    public void A_highlighted_chunk_is_wrapped_at_both_ends()
    {
        var record = new byte[FruaEvent.Length];
        record[0] = 2;
        Word(FruaEvent.Read(record), record, 9, 1);
        Word(FruaEvent.Read(record), record, 11, 2);
        record[4 + (8 - 5)] = 4;    // highlight the FIRST chunk only

        var text = FruaTextEvent.Read(FruaEvent.Read(record), Table(("ONE", "TWO")));

        Assert.Equal("/hONE/hTWO", text.Text);
    }

    /// <summary>Any of five pause bits means wait.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(8, true)]
    public void Wait_for_return_is_any_of_five_bits(byte control, bool expected)
    {
        var e = Event(2, (5, control));

        Assert.Equal(expected, FruaTextEvent.Read(e, Table()).WaitForReturn);
    }

    [Fact]
    public void Force_backup_is_its_own_bit()
    {
        Assert.True(FruaTextEvent.Read(Event(2, (5, 32)), Table()).ForceBackup);
        Assert.False(FruaTextEvent.Read(Event(2, (5, 1)), Table()).ForceBackup);
    }

    /// <summary>A table holding the given strings in slots 1..n.</summary>
    private static FruaStringTable Table(params (string, string)[] _)
    {
        // Only the slot-resolving behaviour matters here; build a real table so Get() is exercised.
        var level = new byte[FruaLevel.Length];
        return FruaStringTable.Read(level);
    }

    private static FruaStringTable Table((string A, string B, string C, string D, string E) five)
    {
        var level = new byte[FruaLevel.Length];
        PackInto(level, [five.A, five.B, five.C, five.D, five.E]);
        return FruaStringTable.Read(level);
    }

    private static FruaStringTable Table((string A, string B) two)
    {
        var level = new byte[FruaLevel.Length];
        PackInto(level, [two.A, two.B]);
        return FruaStringTable.Read(level);
    }

    /// <summary>Packs strings into a level's string table, six bits per character.</summary>
    private static void PackInto(byte[] level, string[] texts)
    {
        int at = 0;

        for (int slot = 0; slot < texts.Length; slot++)
        {
            var bits = new List<bool>();

            foreach (char c in texts[slot])
            {
                int v = c is >= (char)65 and <= (char)95 ? c & 0x3F : c;
                for (int b = 5; b >= 0; b--)
                {
                    bits.Add((v & (1 << b)) != 0);
                }
            }

            for (int b = 0; b < 6; b++)
            {
                bits.Add(false);
            }

            var packed = new byte[(bits.Count + 7) / 8];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                {
                    packed[i / 8] |= (byte)(1 << (7 - (i % 8)));
                }
            }

            level[FruaStringTable.LengthsAt + slot] = (byte)packed.Length;
            packed.CopyTo(level, FruaStringTable.StringsAt + at);
            at += packed.Length;
        }
    }

    // ---- the real DOS levels -----------------------------------------------------------------

    /// <summary>
    /// Every text event in <c>HEIRS.DSN</c> yields readable text.
    /// </summary>
    /// <remarks>
    /// <b>This joins four layers at once</b> — event record, payload offsets, string slots and the
    /// six-bit decoder — so it fails if any of them has drifted. 443 text events is a lot of
    /// chances to produce noise instead of English.
    /// </remarks>
    [Fact]
    public void Every_shipped_text_event_reads_as_text()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int withText = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => e.Type == FruaEventType.TextStatement))
            {
                var text = FruaTextEvent.Read(e, level.Strings);

                foreach (char c in text.Text.Replace(FruaTextEvent.HighlightMarker, ""))
                {
                    Assert.InRange(c, (char)32, (char)95);
                }

                if (text.Text.Length > 0)
                {
                    withText++;
                }
            }
        }

        Assert.True(withText > 300, $"only {withText} of the text events carried any text");
    }

    /// <summary>Every transfer event in the design points somewhere sane.</summary>
    [Fact]
    public void Every_shipped_transfer_names_a_reachable_destination()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        var transfers = new[] { FruaEventType.Teleporter, FruaEventType.Stairs,
                                FruaEventType.TransferModule };
        int seen = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => transfers.Contains(e.Type)))
            {
                var t = FruaTransferEvent.Read(e);

                Assert.InRange(t.DestinationX, 0, 255);
                Assert.InRange(t.DestinationY, 0, 255);
                Assert.InRange(t.DestinationEntryPoint, -1, 7);
                seen++;
            }
        }

        Assert.True(seen > 100, $"only {seen} transfer events found; expected ~187");
    }
}
