using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// A shop's stock bitmasks (<c>AssignShopItemBytes</c>, <c>UAFWinEd/UAImport.cpp:1314</c>).
/// </summary>
public class FruaShopEventTests
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

    private static FruaEvent Shop(params (int Offset, byte Value)[] payload)
    {
        var record = new byte[FruaEvent.Length];
        record[0] = 8;

        foreach (var (offset, value) in payload)
        {
            record[4 + (offset - 5)] = value;
        }

        return FruaEvent.Read(record);
    }

    /// <summary>
    /// The first page, whose bit-to-item mapping the reference documents in a comment.
    /// </summary>
    /// <remarks>
    /// Its third byte is the one the comment lists first: bit 1 is 20 Arrows, bit 2 a Battle Axe,
    /// bit 4 a Hand Axe. Those are item indices 1, 2 and 3.
    /// </remarks>
    [Fact]
    public void The_first_page_maps_bits_to_the_documented_items()
    {
        // first must be 1..30 to select page one -- and it is ALSO a bitmask over that page, so
        // 16 is used here: page one's bit 4 is a zero index, which selects nothing.
        var s = FruaShopEvent.Read(Shop((9, 16), (10, 0), (11, 1 | 2 | 4)));

        Assert.Equal([(byte)1, (byte)2, (byte)3], s.ItemsAvailable);
    }

    /// <summary>A zero index in a page is a hole the mask cannot select.</summary>
    [Fact]
    public void A_zero_index_is_skipped()
    {
        // Page one's first byte is [17, 18, 19, 20, 0, 0, 0, 0]. Bit 0 takes 17; bit 4 is a zero
        // index and takes nothing. 17 = both bits, and stays under the branch's < 31.
        var s = FruaShopEvent.Read(Shop((9, 1 | 16), (10, 0), (11, 0)));

        Assert.Equal([(byte)17], s.ItemsAvailable);
    }

    /// <summary>A group whose first byte is zero contributes nothing at all.</summary>
    [Fact]
    public void An_empty_group_is_skipped_whatever_its_other_bytes_say()
    {
        var s = FruaShopEvent.Read(Shop((9, 0), (10, 0xFF), (11, 0xFF)));

        Assert.Empty(s.ItemsAvailable);
    }

    /// <summary>All four groups are read, so a shop can stock from several pages.</summary>
    [Fact]
    public void All_four_stock_groups_are_read()
    {
        var s = FruaShopEvent.Read(Shop(
            (9, 16), (11, 1),       // group 1 -> item 1
            (12, 16), (14, 2),      // group 2 -> item 2
            (15, 16), (17, 4),      // group 3 -> item 3
            (18, 16), (20, 8)));    // group 4 -> item 4

        Assert.Equal([(byte)1, (byte)2, (byte)3, (byte)4], s.ItemsAvailable);
    }

    /// <summary>
    /// The chain tests the second byte too, so page selection is not purely by the first.
    /// </summary>
    /// <remarks>
    /// <c>(b1 &lt; 47) &amp;&amp; (b2 &gt;= 248)</c> is reached only when the second byte is high,
    /// and its first-byte page is <c>[29, 30, 31, 0, …]</c> rather than page one's
    /// <c>[17, 18, 19, 20, …]</c>. The same first byte therefore means different stock depending on
    /// what follows it.
    /// </remarks>
    [Fact]
    public void The_second_byte_can_change_which_page_applies()
    {
        // Same first byte, same bits set in the second -- only its magnitude differs.
        var low = FruaShopEvent.Read(Shop((9, 40), (10, 1), (11, 0)));
        var high = FruaShopEvent.Read(Shop((9, 40), (10, 248 | 1), (11, 0)));

        // (b1 < 47) && (b2 >= 248) selects [21, 22, ...] for the second byte, so bit 0 there
        // yields 21 -- an item the branch the low value falls to cannot produce.
        Assert.Contains((byte)21, high.ItemsAvailable);
        Assert.DoesNotContain((byte)21, low.ItemsAvailable);
        Assert.NotEqual(low.ItemsAvailable, high.ItemsAvailable);
    }

    [Fact]
    public void A_shop_reads_its_cost_and_exit_flags()
    {
        var s = FruaShopEvent.Read(Shop((6, 12), (7, 5), (8, 128 | 4)));

        Assert.Equal(FruaCostFactor.Mult2, s.CostFactor);
        Assert.Equal(5, s.PictureSlot);
        Assert.True(s.PictureIsLarge);
        Assert.True(s.ForceExit);
    }

    /// <summary>Every shipped shop stocks items that exist in the database.</summary>
    [Fact]
    public void The_shipped_shops_stock_real_item_indices()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        int shops = 0;
        int stocked = 0;

        foreach (var (_, level) in FruaLevel.ReadAll(design))
        {
            foreach (var e in level.Events.Where(e => e.Type == FruaEventType.Shop))
            {
                var s = FruaShopEvent.Read(e);

                foreach (byte item in s.ItemsAvailable)
                {
                    // 254 items in the stock database; 0 can never be stocked.
                    Assert.InRange(item, 1, 254);
                }

                stocked += s.ItemsAvailable.Count;
                shops++;
            }
        }

        Assert.True(shops > 10, $"only {shops} shop events; expected ~15");
        Assert.True(stocked > 0, "no shop stocked anything, which cannot be right");
    }
}
