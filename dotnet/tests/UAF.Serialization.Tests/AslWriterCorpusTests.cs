using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips ASL blocks taken from shipped designs rather than from a fixture.
/// </summary>
/// <remarks>
/// <para>
/// A hand-built block exercises the encoder; a real one exercises what designs actually contain —
/// keys with punctuation, values with markup, flag bytes nobody would think to try.
/// </para>
/// <para>
/// <b>Monsters, because that is where the blocks are.</b> Every monster in every design checked
/// carries one — 195 of 195 in <c>SomethingWild</c>, 44 of 44 in <c>ci-tier3</c> — while
/// <b>no item in any of them does</b>, and only four spells in one. The first draft of this test
/// used items and passed by finding nothing to check.
/// </para>
/// <para>
/// What is round-tripped is the key <i>after</i> the compressed path's fixup
/// (<see cref="AslReader.FixUpCompressedKey"/>), since that is what reading a compressed design
/// yields. The writer is the uncompressed one, so this proves the plain encoding faithful to real
/// content — not that a compressed design can be rewritten.
/// </para>
/// </remarks>
public class AslWriterCorpusTests
{
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

    private static List<IReadOnlyList<AslEntry>> MonsterAttributes(string design)
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

        return [.. monsters.Select(m => m.Attributes).Where(a => a.Count > 0)];
    }

    [Theory]
    [InlineData("SomethingWild.dsn")]
    [InlineData("Case.dsn")]
    [InlineData("ci-tier3")]
    public void Every_real_monster_attribute_block_round_trips(string design)
    {
        var blocks = MonsterAttributes(design);
        if (blocks.Count == 0)
        {
            return;
        }

        foreach (var block in blocks)
        {
            var stream = new MemoryStream();
            AslWriter.Write(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)), DesignVersion.V0670,
                            AslMaps.MonsterData, block);
            stream.Position = 0;

            var read = AslReader.Read(new MfcArchiveReader(stream), DesignVersion.V0670,
                                      AslMaps.MonsterData);

            Assert.Equal(block, read);
        }
    }

    [Fact]
    public void The_corpus_really_does_contain_blocks_to_round_trip()
    {
        // The theory above passes vacuously when it finds nothing, which is the failure mode a
        // corpus test is most prone to -- and the one the first draft of it actually had.
        if (DesignRoot("SomethingWild.dsn") is null)
        {
            return;
        }

        var blocks = MonsterAttributes("SomethingWild.dsn");

        Assert.True(blocks.Count > 100, $"only {blocks.Count} blocks found");
        Assert.Contains(blocks, b => b.Count > 0);
    }

    [Fact]
    public void The_savegame_form_of_a_real_block_drops_only_its_read_only_entries()
    {
        if (DesignRoot("SomethingWild.dsn") is null)
        {
            return;
        }

        var block = MonsterAttributes("SomethingWild.dsn")[0];
        var expected = block.Where(AslReader.IsSavedInSavegame).ToList();

        var stream = new MemoryStream();
        AslWriter.Save(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)), DesignVersion.V0670,
                       AslMaps.MonsterData, block);
        stream.Position = 0;

        Assert.Equal(expected,
                     AslReader.Read(new MfcArchiveReader(stream), DesignVersion.V0670,
                                    AslMaps.MonsterData));
    }
}
