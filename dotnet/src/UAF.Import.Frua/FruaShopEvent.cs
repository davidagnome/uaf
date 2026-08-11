namespace UAF.Import.Frua;

/// <summary>
/// A <see cref="FruaEventType.Shop"/>'s payload
/// (<c>addShopEvent</c>, <c>UAFWinEd/UAImport.cpp:3776</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A shop's stock is four three-byte groups, and the first byte of each selects a page.</b>
/// <c>AssignShopItemBytes</c> is a sixteen-branch <c>if</c>/<c>else if</c> chain: the range the
/// first byte falls in picks which eight item indices each of the three bytes is a bitmask over.
/// Bit <i>n</i> set means "stock the item at index <i>n</i> of this page", and a zero index is a
/// hole the reference skips.
/// </para>
/// <para>
/// <b>The chain is order-dependent and not purely ranged</b> — two of its conditions also test the
/// second byte (<c>(b1 &lt; 47) &amp;&amp; (b2 &gt;= 248)</c> and
/// <c>(b1 &lt; 48) &amp;&amp; (b2 &gt;= 192)</c>) — so it is reproduced as written rather than
/// rewritten into a lookup. The index arrays are extracted from the C++ mechanically, per this
/// port's rule about generated tables; the reference documents the first page in a comment block
/// mapping bits to weapon names.
/// </para>
/// <para>
/// <b>Not every page uses all three bytes.</b> Six of the sixteen branches issue only one or two
/// calls, so the unused byte contributes nothing whatever its bits say.
/// </para>
/// </remarks>
public sealed record FruaShopEvent(
    byte PictureSlot, bool PictureIsLarge, FruaCostFactor CostFactor, bool ForceExit,
    IReadOnlyList<byte> ItemsAvailable)
{
    /// <summary>How many three-byte stock groups a shop carries.</summary>
    public const int StockGroups = 4;

    /// <summary>Reads the payload.</summary>
    public static FruaShopEvent Read(FruaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        byte flags = e.Byte(8);
        var items = new List<byte>();

        // Four groups at 9-11, 12-14, 15-17 and 18-20.
        for (int g = 0; g < StockGroups; g++)
        {
            int at = 9 + (g * 3);
            Decode(items, e.Byte(at), e.Byte(at + 1), e.Byte(at + 2));
        }

        return new FruaShopEvent(
            PictureSlot: e.Byte(7),
            PictureIsLarge: (flags & 128) != 0,
            CostFactor: FruaCost.Factor(e.Byte(6)),
            ForceExit: ((flags & 127) & 4) == 4,
            ItemsAvailable: items);
    }

    /// <summary>One three-byte group (<c>AssignShopItemBytes</c>).</summary>
    private static void Decode(List<byte> items, byte first, byte second, byte third)
    {
        // A leading zero means the group is unused.
        if (first == 0)
        {
            return;
        }

        if ((first > 0) && (first < 31))
        {
            Stock(items, first, [17, 18, 19, 20, 0, 0, 0, 0]);
            Stock(items, second, [9, 10, 11, 12, 13, 14, 15, 16]);
            Stock(items, third, [1, 2, 3, 4, 5, 6, 7, 8]);
        }
        else if ((first < 47) && (second >= 248))
        {
            Stock(items, first, [29, 30, 31, 0, 0, 0, 0, 0]);
            Stock(items, second, [21, 22, 23, 24, 25, 26, 27, 28]);
        }
        else if ((first < 48) && (second >= 192))
        {
            Stock(items, second, [40, 41, 42, 43, 44, 49, 0, 0]);
            Stock(items, third, [32, 33, 34, 35, 36, 37, 38, 39]);
        }
        else if (first < 64)
        {
            Stock(items, first, [66, 67, 68, 69, 0, 0, 0, 0]);
            Stock(items, second, [58, 59, 60, 61, 62, 63, 64, 65]);
            Stock(items, third, [50, 51, 52, 53, 54, 55, 56, 57]);
        }
        else if (first < 80)
        {
            Stock(items, first, [96, 97, 98, 99, 0, 0, 0, 0]);
            Stock(items, second, [88, 89, 90, 91, 92, 93, 94, 95]);
            Stock(items, third, [80, 81, 82, 83, 84, 85, 86, 87]);
        }
        else if (first < 96)
        {
            Stock(items, first, [126, 127, 128, 129, 0, 0, 0, 0]);
            Stock(items, second, [118, 119, 120, 121, 122, 123, 124, 125]);
            Stock(items, third, [110, 111, 112, 113, 114, 115, 116, 117]);
        }
        else if (first < 112)
        {
            Stock(items, first, [156, 157, 158, 159, 0, 0, 0, 0]);
            Stock(items, second, [148, 149, 150, 151, 152, 153, 154, 155]);
            Stock(items, third, [140, 141, 142, 143, 144, 145, 146, 147]);
        }
        else if (first < 128)
        {
            Stock(items, first, [186, 187, 188, 189, 0, 0, 0, 0]);
            Stock(items, second, [178, 179, 180, 181, 182, 183, 184, 185]);
            Stock(items, third, [170, 171, 172, 173, 174, 175, 176, 177]);
        }
        else if (first < 144)
        {
            Stock(items, first, [106, 107, 108, 109, 0, 0, 0, 0]);
            Stock(items, second, [78, 79, 100, 101, 102, 103, 104, 105]);
            Stock(items, third, [70, 71, 72, 73, 74, 75, 76, 77]);
        }
        else if (first < 160)
        {
            Stock(items, first, [166, 167, 168, 169, 0, 0, 0, 0]);
            Stock(items, second, [138, 139, 160, 161, 162, 163, 164, 165]);
            Stock(items, third, [130, 131, 132, 133, 134, 135, 136, 137]);
        }
        else if (first < 188)
        {
            Stock(items, first, [202, 203, 204, 205, 0, 0, 0, 0]);
            Stock(items, second, [198, 199, 45, 46, 47, 48, 200, 201]);
            Stock(items, third, [190, 191, 192, 193, 194, 195, 196, 197]);
        }
        else if (first < 207)
        {
            Stock(items, first, [233, 222, 0, 0, 0, 0, 0, 0]);
            Stock(items, second, [214, 215, 216, 217, 218, 219, 220, 221]);
            Stock(items, third, [206, 207, 208, 209, 210, 211, 212, 213]);
        }
        else if (first < 223)
        {
            Stock(items, second, [232, 234, 235, 236, 237, 0, 0, 0]);
            Stock(items, third, [223, 225, 226, 227, 228, 229, 230, 231]);
        }
        else if (first < 239)
        {
            Stock(items, third, [238, 239, 240, 241, 242, 0, 0, 0]);
        }
        else if ((first < 255) && (second == 255))
        {
            Stock(items, third, [243, 244, 245, 247, 248, 0, 0, 0]);
        }
        else if ((first == 255) && (second == 255))
        {
            Stock(items, third, [249, 250, 251, 252, 253, 254, 255, 0]);
        }
    }

    /// <summary>
    /// Adds the items a bitmask selects from one page (<c>AssignShopItems</c>).
    /// </summary>
    /// <remarks>
    /// Bit <i>n</i> of <paramref name="mask"/> selects <paramref name="page"/>[<i>n</i>], and an
    /// index of 0 is skipped — the reference guards every one with <c>(iN &gt; 0)</c>, which is
    /// what makes the zero-padded pages work.
    /// </remarks>
    private static void Stock(List<byte> items, byte mask, byte[] page)
    {
        for (int bit = 0; bit < 8; bit++)
        {
            if ((mask & (1 << bit)) != 0 && page[bit] > 0)
            {
                items.Add(page[bit]);
            }
        }
    }
}
