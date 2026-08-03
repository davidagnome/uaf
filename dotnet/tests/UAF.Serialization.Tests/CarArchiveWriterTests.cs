using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// The compressed <c>CAR</c> writer, read back through the reader that was diffed against the
/// C++ oracle.
/// </summary>
/// <remarks>
/// The reader walks every compressed design in the corpus to exact end-of-file, so agreeing with
/// it is the strongest available claim short of handing a file to the reference itself.
/// </remarks>
public class CarArchiveWriterTests
{
    private static MemoryStream Written(Action<CarArchiveWriter> write)
    {
        var stream = new MemoryStream();
        using (var writer = CarArchiveWriter.Open(stream))
        {
            write(writer);
        }

        return new MemoryStream(stream.ToArray());
    }

    private static CarArchiveReader ReaderOver(MemoryStream stream) =>
        CarArchiveReader.Open(stream);

    // ---- primitives -------------------------------------------------------------------------------

    [Fact]
    public void Every_primitive_width_round_trips()
    {
        var stream = Written(w =>
        {
            w.WriteByte(0xAB);
            w.WriteUInt16(0xBEEF);
            w.WriteInt16(-2);
            w.WriteInt32(int.MinValue);
            w.WriteUInt32(0xDEADBEEF);
            w.WriteDouble(-1.5);
            w.WriteSingle(2.25f);
        });

        var reader = ReaderOver(stream);
        Assert.Equal(0xAB, reader.ReadByte());
        Assert.Equal(0xBEEF, reader.ReadUInt16());
        Assert.Equal(-2, reader.ReadInt16());
        Assert.Equal(int.MinValue, reader.ReadInt32());
        Assert.Equal(0xDEADBEEF, reader.ReadUInt32());
        Assert.Equal(-1.5, reader.ReadDouble());
        Assert.Equal(2.25f, reader.ReadSingle());
    }

    [Fact]
    public void The_compression_type_byte_is_written_in_the_clear()
    {
        // CAR::Compress emits it before switching the flag on, so a reader consumes one plain
        // byte and only then starts decoding.
        var stream = Written(w => w.WriteInt32(1));

        Assert.Equal(CarArchiveWriter.CompressType, stream.ToArray()[0]);
        Assert.Equal(CarArchiveWriter.CompressType, ReaderOver(stream).CompressType);
    }

    [Fact]
    public void A_count_is_four_bytes_rather_than_the_escaping_scheme()
    {
        // CAR::WriteCount delegates to MFC's two-tier form only when compressType is 0, and this
        // writer is always type 2. Using the plain writer's form here would desynchronise by two
        // bytes on every small count.
        var stream = Written(w =>
        {
            w.WriteCount(3);
            w.WriteInt32(0x5EA1);
        });

        var reader = ReaderOver(stream);
        Assert.Equal(3u, ArchiveCursor.For(reader).ReadCount());
        Assert.Equal(0x5EA1, reader.ReadInt32());
    }

    // ---- string interning -------------------------------------------------------------------------

    [Fact]
    public void A_repeated_string_is_written_once_and_referenced_after()
    {
        var stream = Written(w =>
        {
            w.WriteString("Longsword");
            w.WriteString("Longsword");
            w.WriteString("Longsword");
        });

        var reader = ReaderOver(stream);
        Assert.Equal("Longsword", reader.ReadString());
        Assert.Equal("Longsword", reader.ReadString());
        Assert.Equal("Longsword", reader.ReadString());
        Assert.Equal(1, reader.InternedStringCount);
    }

    [Fact]
    public void Interning_really_does_shorten_the_stream()
    {
        // Otherwise "it round-trips" would pass on a writer that never interned anything.
        long shared = Written(w =>
        {
            for (int i = 0; i < 200; i++) { w.WriteString("a reasonably long item name"); }
        }).Length;

        long distinct = Written(w =>
        {
            for (int i = 0; i < 200; i++) { w.WriteString($"a reasonably long item name {i}"); }
        }).Length;

        Assert.True(shared < distinct, $"shared {shared} vs distinct {distinct}");
    }

    [Fact]
    public void Distinct_strings_take_successive_slots_and_come_back_in_order()
    {
        var stream = Written(w =>
        {
            w.WriteString("one");
            w.WriteString("two");
            w.WriteString("one");
            w.WriteString("three");
            w.WriteString("two");
        });

        var reader = ReaderOver(stream);
        Assert.Equal(["one", "two", "one", "three", "two"],
                     Enumerable.Range(0, 5).Select(_ => reader.ReadString()));
        Assert.Equal(3, reader.InternedStringCount);
    }

    [Fact]
    public void An_empty_string_is_interned_like_any_other()
    {
        var stream = Written(w =>
        {
            w.WriteString(string.Empty);
            w.WriteString(string.Empty);
        });

        var reader = ReaderOver(stream);
        Assert.Equal(string.Empty, reader.ReadString());
        Assert.Equal(string.Empty, reader.ReadString());
        Assert.Equal(1, reader.InternedStringCount);
    }

    [Fact]
    public void A_string_with_an_embedded_nul_is_written_every_time_and_never_interned()
    {
        // The reference takes a separate path that skips the table (class.cpp:11927), and the
        // reader has the matching exclusion -- so the two agree only if both skip it. Interning
        // it would shift every later index by one.
        var stream = Written(w =>
        {
            w.WriteString("a\0b");
            w.WriteString("a\0b");
            w.WriteString("plain");
        });

        var reader = ReaderOver(stream);
        Assert.Equal("a\0b", reader.ReadString());
        Assert.Equal("a\0b", reader.ReadString());
        Assert.Equal("plain", reader.ReadString());

        // Only "plain" made it into the table.
        Assert.Equal(1, reader.InternedStringCount);
    }

    [Fact]
    public void The_writers_intern_count_matches_the_readers()
    {
        var stream = new MemoryStream();
        int written;
        using (var writer = CarArchiveWriter.Open(stream))
        {
            writer.WriteString("one");
            writer.WriteString("two");
            writer.WriteString("one");
            written = writer.InternedStringCount;
        }

        var reader = ReaderOver(new MemoryStream(stream.ToArray()));
        for (int i = 0; i < 3; i++) { reader.ReadString(); }

        Assert.Equal(written, reader.InternedStringCount);
    }

    // ---- codepage ---------------------------------------------------------------------------------

    [Fact]
    public void A_codepage_character_survives()
    {
        var stream = Written(w => w.WriteString("café"));

        Assert.Equal("café", ReaderOver(stream).ReadString());
    }

    [Fact]
    public void The_length_is_in_bytes_rather_than_characters()
    {
        // A character the codepage cannot encode becomes a single '?', so the count has to come
        // from the encoded bytes.
        var stream = Written(w => w.WriteString("中文"));

        Assert.Equal("??", ReaderOver(stream).ReadString());
    }

    // ---- flushing ---------------------------------------------------------------------------------

    [Fact]
    public void Closing_is_what_writes_the_final_block()
    {
        // Without it the terminator never appears and the reader stops early on a short read,
        // silently returning whatever it had -- which looks like a truncated design.
        var unclosed = new MemoryStream();
        var writer = CarArchiveWriter.Open(unclosed);
        writer.WriteInt32(0x11223344);

        Assert.Equal(1, unclosed.Length);                // the type byte alone

        writer.Close();
        Assert.True(unclosed.Length > 1);
    }

    [Fact]
    public void Closing_twice_is_harmless()
    {
        var stream = new MemoryStream();
        var writer = CarArchiveWriter.Open(stream);
        writer.WriteInt32(1);

        writer.Close();
        long once = stream.Length;
        writer.Close();

        Assert.Equal(once, stream.Length);
    }

    // ---- a whole record ---------------------------------------------------------------------------

    [Fact]
    public void A_mixed_run_reads_back_in_order()
    {
        // The real risk in an archive layer is not one value but the boundary between two -- and
        // here every boundary also crosses the compressor.
        var stream = Written(w =>
        {
            w.WriteString("Goblin");
            w.WriteInt32(-7);
            w.WriteSingle(0.25f);
            w.WriteString("Goblin");
            w.WriteByte(0xEE);
            w.WriteUInt16(0xBEEF);
            w.WriteString(string.Empty);
            w.WriteDouble(0.5);
        });

        var reader = ReaderOver(stream);
        Assert.Equal("Goblin", reader.ReadString());
        Assert.Equal(-7, reader.ReadInt32());
        Assert.Equal(0.25f, reader.ReadSingle());
        Assert.Equal("Goblin", reader.ReadString());
        Assert.Equal(0xEE, reader.ReadByte());
        Assert.Equal(0xBEEF, reader.ReadUInt16());
        Assert.Equal(string.Empty, reader.ReadString());
        Assert.Equal(0.5, reader.ReadDouble());
    }
}
