using System.Text;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Covers the byte layer of the writer by reading back everything it writes.
/// </summary>
/// <remarks>
/// The reader is the specification here — it was diffed against the C++ oracle, so agreeing with it
/// is the strongest claim available without regenerating goldens. What these do <i>not</i> prove is
/// that a whole design file round-trips; that needs the record writers on top.
/// </remarks>
public class MfcArchiveWriterTests
{
    private static MemoryStream Written(Action<MfcArchiveWriter> write)
    {
        var stream = new MemoryStream();
        write(new MfcArchiveWriter(stream));
        stream.Position = 0;
        return stream;
    }

    private static MfcArchiveReader ReaderOver(MemoryStream stream) => new(stream);

    // ---- primitives ----------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void An_integer_reads_back_as_itself(int value)
    {
        Assert.Equal(value, ReaderOver(Written(w => w.WriteInt32(value))).ReadInt32());
    }

    [Fact]
    public void Every_primitive_width_round_trips()
    {
        var stream = Written(w =>
        {
            w.WriteByte(0xAB);
            w.WriteUInt16(0xBEEF);
            w.WriteUInt32(0xDEADBEEF);
            w.WriteDouble(-1.5);
            w.WriteSingle(2.25f);
        });

        var reader = ReaderOver(stream);
        Assert.Equal(0xAB, reader.ReadByte());
        Assert.Equal(0xBEEF, reader.ReadUInt16());
        Assert.Equal(0xDEADBEEF, reader.ReadUInt32());
        Assert.Equal(-1.5, reader.ReadDouble());
        Assert.Equal(2.25f, reader.ReadSingle());
    }

    [Fact]
    public void Integers_are_little_endian()
    {
        // Not incidental: the format is a Windows memory dump and a big-endian writer would produce
        // a file that reads back through this port and through nothing else.
        var stream = Written(w => w.WriteUInt32(0x01020304));

        Assert.Equal([0x04, 0x03, 0x02, 0x01], stream.ToArray());
    }

    // ---- string lengths ------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(254, 1)]
    [InlineData(255, 3)]
    [InlineData(0xFFFE, 3)]
    [InlineData(0xFFFF, 7)]
    [InlineData(0x10000, 7)]
    public void A_string_length_escapes_at_each_tier(uint length, int expectedBytes)
    {
        // The boundaries are exclusive: 255 does not fit the byte tier, because 255 is the escape.
        var stream = Written(w => w.WriteStringLength(length));

        Assert.Equal(expectedBytes, stream.Length);
        Assert.Equal(length, ReaderOver(stream).ReadStringLength());
    }

    [Fact]
    public void A_short_string_costs_one_byte_of_length()
    {
        var stream = Written(w => w.WriteString("hi"));

        Assert.Equal(3, stream.Length);
        Assert.Equal("hi", ReaderOver(stream).ReadString());
    }

    [Fact]
    public void An_empty_string_is_a_single_zero_byte()
    {
        var stream = Written(w => w.WriteString(string.Empty));

        Assert.Equal([0], stream.ToArray());
        Assert.Equal(string.Empty, ReaderOver(stream).ReadString());
    }

    [Fact]
    public void A_string_past_the_byte_tier_round_trips()
    {
        string long_ = new('x', 300);

        Assert.Equal(long_, ReaderOver(Written(w => w.WriteString(long_))).ReadString());
    }

    [Fact]
    public void The_length_is_in_bytes_rather_than_characters()
    {
        // Windows-1252 makes those the same for everything it can encode, but a character it
        // cannot becomes a single '?' -- so the count has to come from the encoded bytes.
        var stream = Written(w => w.WriteString("中文"));

        Assert.Equal(3, stream.Length);
        Assert.Equal("??", ReaderOver(stream).ReadString());
    }

    [Fact]
    public void A_codepage_character_survives_the_round_trip()
    {
        Assert.Equal("café", ReaderOver(Written(w => w.WriteString("café"))).ReadString());
    }

    [Fact]
    public void Raw_string_bytes_are_written_verbatim()
    {
        byte[] raw = [0x41, 0x80, 0xFF];
        var stream = Written(w => w.WriteStringBytes(raw));

        Assert.Equal(raw, ReaderOver(stream).ReadStringBytes());
    }

    // ---- counts --------------------------------------------------------------------------------

    [Theory]
    [InlineData(0u, 2)]
    [InlineData(3u, 2)]
    [InlineData(0xFFFEu, 2)]
    [InlineData(0xFFFFu, 6)]
    [InlineData(0x10000u, 6)]
    public void A_count_has_two_tiers_and_no_byte_form(uint count, int expectedBytes)
    {
        var stream = Written(w => w.WriteCount(count));

        Assert.Equal(expectedBytes, stream.Length);
        Assert.Equal(count, ArchiveCursor.For(ReaderOver(stream)).ReadCount());
    }

    [Fact]
    public void A_count_and_a_string_length_of_three_are_different_sizes()
    {
        // The two schemes are not interchangeable: using one for the other reads back plausibly for
        // small values and desynchronises for large ones.
        Assert.Equal(2, Written(w => w.WriteCount(3)).Length);
        Assert.Equal(1, Written(w => w.WriteStringLength(3)).Length);
    }

    // ---- position ------------------------------------------------------------------------------

    [Fact]
    public void The_writer_reports_how_far_it_has_got()
    {
        var stream = new MemoryStream();
        var writer = new MfcArchiveWriter(stream);

        Assert.Equal(0, writer.Position);
        writer.WriteUInt32(1);
        Assert.Equal(4, writer.Position);
    }

    [Fact]
    public void A_mixed_run_reads_back_in_order()
    {
        // The real risk in a byte layer is not one value but the boundary between two.
        var stream = Written(w =>
        {
            w.WriteString("name");
            w.WriteCount(2);
            w.WriteInt32(-7);
            w.WriteString(string.Empty);
            w.WriteDouble(0.5);
        });

        var reader = ReaderOver(stream);
        var cursor = ArchiveCursor.For(reader);

        Assert.Equal("name", reader.ReadString());
        Assert.Equal(2u, cursor.ReadCount());
        Assert.Equal(-7, reader.ReadInt32());
        Assert.Equal(string.Empty, reader.ReadString());
        Assert.Equal(0.5, reader.ReadDouble());
        Assert.Equal(stream.Length, stream.Position);
    }
}
