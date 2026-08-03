using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>Covers writing the ASL block by reading every written block back.</summary>
public class AslWriterTests
{
    private const string Map = AslMaps.ItemData;

    private static readonly DesignVersion Modern = DesignVersion.V0670;

    private static AslEntry Entry(string key, string value, AslFlags flags = AslFlags.None) =>
        new(key, (byte)flags, value);

    private static MemoryStream Written(Action<IArchiveWriteCursor> write)
    {
        var stream = new MemoryStream();
        write(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)));
        stream.Position = 0;
        return stream;
    }

    private static List<AslEntry> ReadBack(MemoryStream stream, DesignVersion? version = null) =>
        AslReader.Read(new MfcArchiveReader(stream), version ?? Modern, Map);

    // ---- the round trip ------------------------------------------------------------------------

    [Fact]
    public void An_empty_block_round_trips()
    {
        var stream = Written(w => AslWriter.Write(w, Modern, Map, []));

        Assert.Empty(ReadBack(stream));
    }

    [Fact]
    public void Entries_come_back_with_their_keys_flags_and_values()
    {
        var entries = new[]
        {
            Entry("first", "one"),
            Entry("second", "two", AslFlags.Modified),
            Entry("third", string.Empty, AslFlags.Editor),
        };

        var read = ReadBack(Written(w => AslWriter.Write(w, Modern, Map, entries)));

        Assert.Equal(3, read.Count);
        Assert.Equal(entries, read);
    }

    [Fact]
    public void The_map_name_is_written_verbatim_as_the_sync_marker_it_is()
    {
        // Not through the DAS blank convention, which would turn an empty name into "*".
        var stream = Written(w => AslWriter.Write(w, Modern, Map, [Entry("k", "v")]));

        Assert.Equal(Map, new MfcArchiveReader(stream).ReadString());
    }

    [Fact]
    public void A_block_written_under_one_name_will_not_read_under_another()
    {
        // The mismatch is the cheapest reliable signal that a stream has desynchronised, so it
        // stays fatal rather than being skipped past.
        var stream = Written(w => AslWriter.Write(w, Modern, AslMaps.SpellData, [Entry("k", "v")]));

        Assert.Throws<InvalidDataException>(() => ReadBack(stream));
    }

    // ---- version gating ------------------------------------------------------------------------

    [Fact]
    public void Below_the_minimum_version_not_one_byte_is_written()
    {
        // Not an empty block -- nothing at all. A writer that emits a name and a zero count
        // produces a file the reference cannot read.
        var stream = Written(w => AslWriter.Write(w, DesignVersion.V0500, Map,
                                                  [Entry("k", "v")]));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void At_the_minimum_version_the_block_is_written()
    {
        var stream = Written(w => AslWriter.Write(w, AslReader.MinimumVersion, Map,
                                                  [Entry("k", "v")]));

        Assert.NotEqual(0, stream.Length);
        Assert.Single(ReadBack(stream, AslReader.MinimumVersion));
    }

    // ---- the savegame path ---------------------------------------------------------------------

    [Fact]
    public void A_savegame_holds_everything_except_read_only()
    {
        var entries = new[]
        {
            Entry("design", "fixed", AslFlags.ReadOnly),
            Entry("progress", "chapter2", AslFlags.Modified),
            Entry("plain", "value"),
        };

        var read = ReadBack(Written(w => AslWriter.Save(w, Modern, Map, entries)));

        Assert.Equal(["progress", "plain"], read.Select(e => e.Key));
    }

    [Fact]
    public void The_savegame_count_is_of_the_filtered_set_not_the_whole_one()
    {
        // Counting everything and writing some produces a file that reads back cleanly with
        // silently missing attributes -- the failure the reference's own ASSERT guards against.
        var entries = new[]
        {
            Entry("a", "1", AslFlags.ReadOnly),
            Entry("b", "2", AslFlags.ReadOnly),
            Entry("c", "3"),
        };

        var stream = Written(w =>
        {
            AslWriter.Save(w, Modern, Map, entries);

            // A marker straight after: if the count were wrong the reader would run into it.
            w.WriteInt32(0x5EA1);
        });

        var reader = new MfcArchiveReader(stream);
        Assert.Single(AslReader.Read(reader, Modern, Map));
        Assert.Equal(0x5EA1, reader.ReadInt32());
    }

    [Fact]
    public void A_block_of_nothing_but_read_only_entries_saves_as_empty()
    {
        var entries = new[] { Entry("a", "1", AslFlags.ReadOnly) };

        Assert.Empty(ReadBack(Written(w => AslWriter.Save(w, Modern, Map, entries))));
    }

    // ---- the wide-count path -------------------------------------------------------------------

    [Fact]
    public void The_deserialized_form_writes_a_thirty_two_bit_count()
    {
        // races.dat uses this third entry point. The two Serialize paths agree on a WORD, which
        // makes "the count is 16-bit" look like a property of the format; it is not.
        var entries = new[] { Entry("k", "v") };

        var stream = Written(w => AslWriter.WriteDeSerialized(w, Modern, Map, entries));
        var read = AslReader.ReadDeSerialized(
            ArchiveCursor.For(new MfcArchiveReader(stream)), Modern, Map);

        Assert.Equal(entries, read);
    }

    [Fact]
    public void The_two_count_widths_produce_different_lengths()
    {
        var entries = new[] { Entry("k", "v") };

        long narrow = Written(w => AslWriter.Write(w, Modern, Map, entries)).Length;
        long wide = Written(w => AslWriter.WriteDeSerialized(w, Modern, Map, entries)).Length;

        Assert.Equal(narrow + 2, wide);
    }

    // ---- awkward content -----------------------------------------------------------------------

    [Fact]
    public void Long_keys_and_values_survive_the_length_escape()
    {
        var entry = Entry(new string('k', 300), new string('v', 70000));

        Assert.Equal([entry], ReadBack(Written(w => AslWriter.Write(w, Modern, Map, [entry]))));
    }

    [Fact]
    public void Every_flag_byte_survives()
    {
        var entries = Enumerable.Range(0, 16)
            .Select(i => new AslEntry($"k{i}", (byte)i, "v"))
            .ToArray();

        Assert.Equal(entries, ReadBack(Written(w => AslWriter.Write(w, Modern, Map, entries))));
    }
}
