namespace UAF.Serialization.Tests;

/// <summary>
/// <c>DICEPLUS</c> and the two structures it contains, plus the tier-dependent count encoding.
/// </summary>
/// <remarks>
/// These are leaves in the dependency graph: <c>SPELL_DATA</c> needs <c>DICEPLUS</c> for its
/// duration and five parameters, and <c>BASECLASS_LIST</c> for its allowed classes. Neither record
/// type can be walked until they read correctly.
/// </remarks>
public class DicePlusTests
{
    /// <summary>Builds a plain (tier-1) cursor over a byte sequence.</summary>
    private static IArchiveCursor Cursor(params byte[] bytes) =>
        ArchiveCursor.For(new MfcArchiveReader(new MemoryStream(bytes)));

    private static byte[] CountedString(string s)
    {
        byte[] body = System.Text.Encoding.Latin1.GetBytes(s);
        return [(byte)body.Length, .. body];
    }

    [Fact]
    public void Modern_DP2_form_is_two_strings_and_nothing_else()
    {
        // The form every current design uses. Reading it as the numeric layout would consume 12+
        // bytes of what is actually text.
        byte[] data = [.. CountedString("DP2"), .. CountedString("1d6+2"), .. CountedString("*")];
        var ms = new MemoryStream(data);
        var dice = DicePlusReader.Read(ArchiveCursor.For(new MfcArchiveReader(ms)));

        Assert.Equal("DP2", dice.Tag);
        Assert.True(DicePlusReader.IsTextForm(dice.Tag));
        Assert.Equal("1d6+2", dice.Text);
        Assert.Equal(string.Empty, dice.Binary);      // "*" decodes to empty
        Assert.Empty(dice.Adjustments);

        Assert.Equal(data.Length, ms.Position);       // consumed exactly, nothing left
    }

    [Fact]
    public void Packed_DP1_form_uses_one_byte_fields_among_the_ints()
    {
        // char/BYTE/char, then two 32-bit clamps, then char -- 12 bytes, not 24. The names read
        // like integers (class.h:842), which is the trap.
        byte[] data =
        [
            .. CountedString("DP1"),
            3,                                        // m_numDice   -- char
            6,                                        // m_numSides  -- BYTE
            2,                                        // m_bonus     -- char
            1, 0, 0, 0,                               // m_min       -- int
            20, 0, 0, 0,                              // m_max       -- int
            1,                                        // m_sign      -- char
            0, 0,                                     // adjustment count (tier 1: WORD)
        ];
        var ms = new MemoryStream(data);
        var dice = DicePlusReader.Read(ArchiveCursor.For(new MfcArchiveReader(ms)));

        Assert.Equal("DP1", dice.Tag);
        Assert.Equal(3, dice.NumDice);
        Assert.Equal(6, dice.NumSides);
        Assert.Equal(2, dice.Bonus);
        Assert.Equal(1, dice.Min);
        Assert.Equal(20, dice.Max);
        Assert.Empty(dice.Adjustments);
        Assert.Equal(data.Length, ms.Position);
    }

    [Fact]
    public void Negative_dice_count_normalises_into_a_sign()
    {
        // class.cpp:2546 -- a negative count becomes a positive count plus sign -1, after reading.
        // Keeping the raw negative would disagree with the reference on every affected record.
        byte[] data =
        [
            .. CountedString("DP1"),
            0xFD,                                     // -3 as a signed char
            6, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0,
            0, 0,
        ];
        var dice = DicePlusReader.Read(Cursor(data));

        Assert.Equal(3, dice.NumDice);                // absolute value
        Assert.Equal(-1, dice.Sign);
    }

    [Fact]
    public void Legacy_DP0_stores_its_clamps_as_bytes()
    {
        // DP0 writes m_min/m_max as BYTE and widens them into int fields (class.cpp:2536).
        // Reading them as int here would swallow the adjustment count and beyond.
        byte[] data =
        [
            .. CountedString("DP0"),
            1, 8, 0,                                  // numDice, numSides, bonus
            2,                                        // m_min  -- BYTE, not int
            9,                                        // m_max  -- BYTE, not int
            0, 0,                                     // adjustment count
        ];
        var ms = new MemoryStream(data);
        var dice = DicePlusReader.Read(ArchiveCursor.For(new MfcArchiveReader(ms)));

        Assert.Equal("DP0", dice.Tag);
        Assert.Equal(2, dice.Min);
        Assert.Equal(9, dice.Max);
        Assert.Equal(data.Length, ms.Position);
    }

    [Fact]
    public void Unknown_tag_is_reported_rather_than_guessed_at()
    {
        // The reference logs and returns non-zero. Throwing is the right analogue for a reader:
        // in practice an unrecognised tag means the stream is misaligned, and continuing would
        // consume arbitrary bytes as a dice expression.
        var ex = Assert.Throws<InvalidDataException>(
            () => DicePlusReader.Read(Cursor([.. CountedString("DP9")])));
        Assert.Contains("DP9", ex.Message);
    }

    [Fact]
    public void Adjustment_is_three_shorts_then_three_chars_then_a_reference()
    {
        // MAX_ADJ is 3. Six bytes then three, not twelve then twelve (class.h:810).
        byte[] data =
        [
            1, 0, 2, 0, 3, 0,                         // short m_parameter[3]
            10, 20, 30,                               // char  m_operator[3]
            .. CountedString("Longsword"),            // GENERIC_REFERENCE.m_refName
            4,                                        // m_refType -- char, one byte
            7, 0, 0, 0,                               // m_refKey  -- int
        ];
        var ms = new MemoryStream(data);
        var adj = DicePlusReader.ReadAdjustment(ArchiveCursor.For(new MfcArchiveReader(ms)));

        Assert.Equal([(short)1, (short)2, (short)3], adj.Parameters);
        Assert.Equal([(sbyte)10, (sbyte)20, (sbyte)30], adj.Operators);
        Assert.Equal("Longsword", adj.Reference.RefName);
        Assert.Equal(4, adj.Reference.RefType);
        Assert.Equal(7, adj.Reference.RefKey);
        Assert.Equal(data.Length, ms.Position);
    }

    [Fact]
    public void Reference_decodes_the_blank_sentinel()
    {
        // class.cpp:1149 checks "*" inline rather than calling DAS. Same effect, since the blank
        // sentinel is "*", but worth pinning because it is spelled differently at this site.
        var reference = DicePlusReader.ReadReference(
            Cursor([.. CountedString("*"), 0, 0, 0, 0, 0]));

        Assert.Equal(string.Empty, reference.RefName);
    }

    [Fact]
    public void Baseclass_list_reads_names_not_ordinals()
    {
        byte[] data =
        [
            2, 0, 0, 0,                               // a plain int count here, NOT ReadCount
            .. CountedString("fighter"),
            .. CountedString("ranger"),
        ];
        var ms = new MemoryStream(data);
        var names = BaseclassListReader.Read(ArchiveCursor.For(new MfcArchiveReader(ms)));

        Assert.Equal(["fighter", "ranger"], names);
        Assert.Equal(data.Length, ms.Position);
    }

    [Fact]
    public void Count_encoding_differs_between_archive_tiers()
    {
        // CAR::ReadCount (class.cpp:11707) delegates to MFC's WORD-with-escape form only when
        // compressType is 0; types 1 and 2 write a flat DWORD. So the SAME call site is 2 bytes
        // in one archive and 4 in another, for identical small counts -- and the difference is
        // invisible until whatever follows the count is misread.
        //
        // Plain (tier 1) uses the MFC form: a WORD.
        Assert.Equal(5u, Cursor(5, 0).ReadCount());

        // ...escaping to a DWORD on 0xFFFF.
        Assert.Equal(70000u, Cursor(0xFF, 0xFF, 0x70, 0x11, 0x01, 0x00).ReadCount());
    }
}
