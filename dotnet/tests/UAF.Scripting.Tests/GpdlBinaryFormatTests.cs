using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The <c>talk.bin</c> container: segment order, MFC counted-string encoding, and round-tripping.
/// </summary>
public class GpdlBinaryFormatTests
{
    private static GpdlProgram Program(uint[] code, string[] globals, params (string, uint)[] index) =>
        new(code, globals, index);

    [Fact]
    public void Segments_are_written_in_code_globals_index_order_with_no_header()
    {
        // src/GPDL/GPDL.cpp:96 -- WriteCode, WriteConstants, WriteDictionary. There is no magic and
        // no version, so this order is the only thing a reader has to go on.
        byte[] bytes = GpdlBinaryWriter.ToBytes(
            Program([0x06000001], ["", "hi"], ("f", 1u)));

        Assert.Equal(
            [
                0x01, 0x00, 0x00, 0x00,             // code length 1
                0x01, 0x00, 0x00, 0x06,             // the word, little-endian
                0x02, 0x00, 0x00, 0x00,             // global count 2
                0x00,                               // ""
                0x02, (byte)'h', (byte)'i',         // "hi"
                0x01, 0x00, 0x00, 0x00,             // public function count 1
                0x01, (byte)'f',                    // "f"
                0x01, 0x00, 0x00, 0x00,             // address 1
            ],
            bytes);
    }

    [Theory]
    // _AfxWriteStringLength thresholds. The boundaries are asymmetric and both directions have bitten
    // this project before: the single byte holds up to 254, not 255, and the WORD form stops at
    // 0xFFFD because 0xFFFE is the Unicode tag.
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(254, 1)]
    [InlineData(255, 3)]
    [InlineData(0xfffd, 3)]
    [InlineData(0xfffe, 7)]
    [InlineData(0xffff, 7)]
    public void String_length_prefix_widths(int length, int prefixBytes)
    {
        byte[] bytes = GpdlBinaryWriter.ToBytes(Program([], [new string('x', length)]));
        // Three counts of 4 bytes each (code, globals, index) frame the single string.
        Assert.Equal(12 + prefixBytes + length, bytes.Length);
    }

    [Fact]
    public void Strings_carry_no_NUL_terminator()
    {
        byte[] bytes = GpdlBinaryWriter.ToBytes(Program([], ["ab"]));
        Assert.Equal([0, 0, 0, 0, 1, 0, 0, 0, 2, (byte)'a', (byte)'b', 0, 0, 0, 0], bytes);
    }

    [Fact]
    public void Round_trip_preserves_all_three_segments()
    {
        var original = Program(
            [0x06000001, 0x02000001, 0x08000000],
            ["", "f(0)", "value"],
            ("f", 1u),
            ("outer@inner", 2u));

        byte[] bytes = GpdlBinaryWriter.ToBytes(original);
        using var ms = new MemoryStream(bytes);
        var reloaded = GpdlBinaryWriter.Read(ms);

        Assert.Equal(original.Code, reloaded.Code);
        Assert.Equal(original.Globals, reloaded.Globals);
        Assert.Equal(original.Index, reloaded.Index);
        Assert.Equal(bytes.Length, ms.Position);
    }

    [Fact]
    public void Non_ASCII_text_round_trips_through_the_codepage()
    {
        // Source and data are single-byte Windows-1252, not UTF-8. Round-tripping "café" through
        // UTF-8 would add a byte and shift every later offset.
        var original = Program([], ["café — dash"]);
        byte[] bytes = GpdlBinaryWriter.ToBytes(original);
        using var ms = new MemoryStream(bytes);
        Assert.Equal(original.Globals, GpdlBinaryWriter.Read(ms).Globals);

        // 11 characters, 11 bytes: the em dash is a single byte 0x97 in cp1252, three in UTF-8.
        Assert.Equal(12 + 1 + 11, bytes.Length);
        Assert.Contains((byte)0x97, bytes);
    }

    [Fact]
    public void Index_names_are_trimmed_on_read()
    {
        // INDEX::read trims both ends (GPDLexec.cpp:7340), so a padded name still resolves.
        var padded = Program([], [], ("  spaced  ", 5u));
        using var ms = new MemoryStream(GpdlBinaryWriter.ToBytes(padded));
        var reloaded = GpdlBinaryWriter.Read(ms);
        Assert.Equal("spaced", reloaded.Index[0].Name);
        Assert.Equal(5u, reloaded.Lookup("spaced"));
    }

    [Fact]
    public void Lookup_returns_zero_for_an_unknown_name()
    {
        // 0 is the sentinel, which is why the compiler reserves address 0 for a NOOP.
        Assert.Equal(0u, Program([], [], ("f", 1u)).Lookup("g"));
    }

    [Fact]
    public void Lookup_is_case_sensitive()
    {
        Assert.Equal(0u, Program([], [], ("Talk", 1u)).Lookup("talk"));
    }

    [Fact]
    public void A_truncated_file_is_reported_rather_than_read_as_zeros()
    {
        using var ms = new MemoryStream([0x02, 0x00, 0x00, 0x00, 0x01, 0x00]);
        Assert.Throws<EndOfStreamException>(() => GpdlBinaryWriter.Read(ms));
    }
}
