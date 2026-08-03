using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>Covers writing the special-abilities block, including real ones from shipped designs.</summary>
public class SpecabWriterTests
{
    /// <summary>
    /// A version above the 0.920 legacy gate, so the reader takes the pair path.
    /// </summary>
    /// <remarks>
    /// <c>_SPECIAL_ABILITIES_VERSION_</c> — 0.930, and named for this. Reading a written block at
    /// anything at or below 0.920 takes the legacy branch and finds nothing, because the writer has
    /// no version fork: an old design is read old and written new.
    /// </remarks>
    private static readonly DesignVersion Modern = DesignVersion.SpecialAbilities;

    private static MemoryStream Written(Action<IArchiveWriteCursor> write)
    {
        var stream = new MemoryStream();
        write(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)));
        stream.Position = 0;
        return stream;
    }

    private static SpecabBlock ReadBack(MemoryStream stream, DesignVersion? version = null) =>
        SpecabReader.Read(new MfcArchiveReader(stream), version ?? Modern);

    // ---- the round trip ------------------------------------------------------------------------

    [Fact]
    public void An_empty_block_round_trips()
    {
        var stream = Written(w => SpecabWriter.Write(w, new SpecabBlock([], [], [])));

        Assert.Empty(ReadBack(stream).Pairs);
        Assert.Equal(4, stream.Length);   // just the int count
    }

    [Fact]
    public void Pairs_come_back_in_order()
    {
        var block = new SpecabBlock(
            [new SpecabPair("SA_Flying", "1"), new SpecabPair("SA_Regenerate", "3")], [], []);

        Assert.Equal(block.Pairs, ReadBack(Written(w => SpecabWriter.Write(w, block))).Pairs);
    }

    [Fact]
    public void The_count_is_a_thirty_two_bit_int_not_a_word()
    {
        // The sibling ASL block counts with a WORD. Conflating them desynchronises by two bytes.
        var block = new SpecabBlock([new SpecabPair("k", "v")], [], []);
        var stream = Written(w => SpecabWriter.Write(w, block));

        Assert.Equal(1, new MfcArchiveReader(stream).ReadInt32());
    }

    [Fact]
    public void Strings_are_written_verbatim_rather_than_through_the_blank_convention()
    {
        // No DAS here, so an empty value stays empty and a literal "*" stays "*".
        var block = new SpecabBlock(
            [new SpecabPair("empty", string.Empty), new SpecabPair("star", "*")], [], []);

        var read = ReadBack(Written(w => SpecabWriter.Write(w, block)));

        Assert.Equal(string.Empty, read.Pairs[0].Value);
        Assert.Equal("*", read.Pairs[1].Value);
    }

    [Fact]
    public void The_block_is_written_the_same_way_whatever_the_designs_version()
    {
        // The reference's legacy branch is conditioned on !IsStoring(), so an old design is read
        // in the old shape and written back in the new one. There is no version fork when writing.
        var block = new SpecabBlock([new SpecabPair("k", "v")], [], []);
        var stream = Written(w => SpecabWriter.Write(w, block));

        Assert.Equal(block.Pairs, SpecabReader.Read(new MfcArchiveReader(stream), Modern).Pairs);

        // ...and a legacy reader handed that block runs off the end rather than quietly finding
        // nothing, which is the failure one wants: the two shapes are not mistakable for each
        // other at any useful size.
        stream.Position = 0;
        Assert.ThrowsAny<IOException>(
            () => SpecabReader.Read(new MfcArchiveReader(stream), SpecabReader.LegacyGate));
    }

    // ---- what cannot be written ----------------------------------------------------------------

    [Fact]
    public void A_block_still_in_the_legacy_shape_is_refused_rather_than_silently_emptied()
    {
        // Emitting an empty block would produce a file that reads back cleanly with every ability
        // gone and nothing to show it had happened.
        var legacy = new SpecabBlock([], [
            new LegacySpecabSlot("script", "bin", "", "", 0, 0, [])], []);

        Assert.False(SpecabWriter.CanWrite(legacy));
        Assert.Throws<NotSupportedException>(
            () => SpecabWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(new MemoryStream())), legacy));
    }

    [Fact]
    public void The_oldest_ordinal_form_is_refused_too()
    {
        var ordinals = new SpecabBlock([], [], [(ushort)3, (ushort)7]);

        Assert.False(SpecabWriter.CanWrite(ordinals));
        Assert.Throws<NotSupportedException>(
            () => SpecabWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(new MemoryStream())), ordinals));
    }

    [Fact]
    public void A_modern_block_can_be_written()
    {
        Assert.True(SpecabWriter.CanWrite(new SpecabBlock([new SpecabPair("k", "v")], [], [])));
        Assert.True(SpecabWriter.CanWrite(new SpecabBlock([], [], [])));
    }

    // ---- real blocks ---------------------------------------------------------------------------

    private static string? DesignRoot(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null ? null : Path.Combine(dir.FullName, "reference", name);
        return root is not null && Directory.Exists(root) ? root : null;
    }

    private static List<SpecabBlock> MonsterSpecabs(string design)
    {
        string? root = DesignRoot(design);
        string? path = root is null ? null : Path.Combine(root, "Data", "monsters.dat");
        if (path is null || !File.Exists(path))
        {
            return [];
        }

        using var stream = File.OpenRead(path);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database,
                                           DesignFileKind.ItemsFallback(DesignVersion.V0670));

        List<MonsterRecord> monsters;
        if (header.Tier == ArchiveTier.CompressedCar)
        {
            stream.Seek(16, SeekOrigin.Begin);
            monsters = MonsterRecordReader.ReadDatabase(CarArchiveReader.Open(stream),
                                                        header.Version, ArchiveRole.Engine);
        }
        else
        {
            stream.Seek(header.PayloadOffset, SeekOrigin.Begin);
            monsters = MonsterRecordReader.ReadDatabase(new MfcArchiveReader(stream),
                                                        header.Version, ArchiveRole.Engine);
        }

        return [.. monsters.Select(m => m.SpecialAbilities).Where(s => s is not null)];
    }

    [Theory]
    [InlineData("SomethingWild.dsn")]
    [InlineData("Case.dsn")]
    [InlineData("ci-tier3")]
    public void Every_real_monster_specab_block_round_trips(string design)
    {
        var blocks = MonsterSpecabs(design);
        if (blocks.Count == 0)
        {
            return;
        }

        foreach (var block in blocks.Where(SpecabWriter.CanWrite))
        {
            var stream = Written(w => SpecabWriter.Write(w, block));

            Assert.Equal(block.Pairs, ReadBack(stream).Pairs);
        }
    }

    [Fact]
    public void The_corpus_really_does_contain_blocks_to_round_trip()
    {
        if (DesignRoot("SomethingWild.dsn") is null)
        {
            return;
        }

        var blocks = MonsterSpecabs("SomethingWild.dsn");

        Assert.NotEmpty(blocks);
        Assert.All(blocks, b => Assert.True(SpecabWriter.CanWrite(b)));
    }
}
