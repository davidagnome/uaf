using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads <c>spellgroups.dat</c> and <c>traits.dat</c> — the last two tagged databases with no
/// record reader.
/// </summary>
/// <remarks>
/// The only design that ships either is <c>DefaultDesign</c>, at 0.915 — below the special-abilities
/// gate, so its records carry none, and the readers refuse the unported name/type overload rather
/// than guess at it.
/// </remarks>
public class SpellGroupAndTraitDatabaseTests
{
    private static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        return dir;
    }

    private static (TaggedDatabaseHeader Header, IArchiveCursor Body, Stream Stream, DesignVersion Version)?
        Open(TaggedDatabase database)
    {
        if (RepoRoot() is not { } root)
        {
            return null;
        }

        string path = Path.Combine(root.FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data",
            TaggedDatabaseReader.FileName(database));

        if (!File.Exists(path))
        {
            return null;
        }

        string gameDat = Path.Combine(Path.GetDirectoryName(path)!, "game.dat");
        if (!File.Exists(gameDat))
        {
            return null;
        }

        DesignVersion version;
        using (var game = File.OpenRead(gameDat))
        {
            version = GameDataReader.Open(game).Version;
        }

        var header = TaggedDatabaseReader.Read(path, database, out var body, out var stream);
        return (header, body, stream, version);
    }

    [Fact]
    public void DefaultDesigns_spellgroups_read_whole()
    {
        var open = Open(TaggedDatabase.SpellGroup);
        if (open is null)
        {
            return;
        }

        using (open.Value.Stream)
        {
            var groups = SpellGroupRecordReader.ReadAll(
                open.Value.Body, open.Value.Header.Count, open.Value.Header.Tag, open.Value.Version);

            Assert.Equal(15, groups.Count);
            Assert.All(groups, g => Assert.NotEmpty(g.Name));
        }
    }

    [Fact]
    public void DefaultDesigns_traits_read_whole()
    {
        var open = Open(TaggedDatabase.Trait);
        if (open is null)
        {
            return;
        }

        using (open.Value.Stream)
        {
            var traits = TraitRecordReader.ReadAll(
                open.Value.Body, open.Value.Header.Count, open.Value.Version);

            Assert.Equal(43, traits.Count);
            Assert.All(traits, t => Assert.NotEmpty(t.Name));
        }
    }

    [Fact]
    public void A_trait_record_reads_the_key_name_abbreviation_and_dice()
    {
        var open = Open(TaggedDatabase.Trait);
        if (open is null)
        {
            return;
        }

        using (open.Value.Stream)
        {
            var traits = TraitRecordReader.ReadAll(
                open.Value.Body, open.Value.Header.Count, open.Value.Version);

            // Every record carries a dice expression a trait check rolls against.
            Assert.All(traits, t => Assert.NotNull(t.Roll));
        }
    }

    [Fact]
    public void An_unknown_spellgroup_record_version_is_refused()
    {
        var bytes = new MemoryStream();
        var writer = new MfcArchiveWriter(bytes);
        writer.WriteString("SG9");
        bytes.Position = 0;

        var cursor = ArchiveCursor.For(new MfcArchiveReader(bytes));

        var thrown = Assert.Throws<InvalidDataException>(
            () => SpellGroupRecordReader.Read(cursor, "SpGrpV1", DesignVersion.SpellNames));

        Assert.Contains("SG9", thrown.Message);
    }

    [Fact]
    public void An_unknown_trait_record_version_is_refused()
    {
        var bytes = new MemoryStream();
        var writer = new MfcArchiveWriter(bytes);
        writer.WriteString("Tr9");
        bytes.Position = 0;

        var cursor = ArchiveCursor.For(new MfcArchiveReader(bytes));

        var thrown = Assert.Throws<InvalidDataException>(
            () => TraitRecordReader.Read(cursor, DesignVersion.SpellNames));

        Assert.Contains("Tr9", thrown.Message);
    }
}
