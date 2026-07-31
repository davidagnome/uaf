using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks <c>GLOBAL_STATS::Serialize(CAR&amp;)</c> through its attribute list on three compressed
/// designs (2.53, 3.55, 5.28).
/// </summary>
/// <remarks>
/// <para>
/// This closes the last gap in the ASL port. Every attribute list in <c>items.dat</c> has a count
/// of zero, so walking those proved only that the block could be <i>located</i>. The
/// <c>GLOBAL_STATS</c> list has four entries, so reaching it exercises the parts that were
/// previously unreachable: the key/flags/value loop, the compressed-only key fixup, and — most
/// interestingly — resolving values that are stored as string-table back-references rather than
/// as text.
/// </para>
/// <para>
/// The fixtures are gitignored; each test returns early when absent. CI fetches the 5.28 one.
/// </para>
/// </remarks>
public class GlobalStatsCompressedTests
{
    /// <summary>folder, version, design name, small-pic count, title screens, credit screens.</summary>
    public static TheoryData<string, double, string, int, int, int?> Designs => new()
    {
        { "dc-default/data-files", 5.28, "November 19, 2018", 24, 1, 1 },
        { "SomethingWild.dsn/Data", 3.55, "Something Wild", 6, 3, null },
        { "Case.dsn/Data", 2.53, "Case of Masterpiece", 22, 1, null },
    };

    /// <summary>The four keys in the order compressed designs store them (hash order).</summary>
    private static readonly string[] ExpectedKeys =
        ["GuidedTourVersion", "ItemUseEventVersion", "RunAsVersion", "SpecialItemKeyQtyVersion"];

    private static string? GameDat(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        string path = Path.Combine(dir!.FullName, "reference", rel, "game.dat");
        return File.Exists(path) ? path : null;
    }

    private static GlobalStatsPrefix Read(string path)
    {
        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        Assert.Equal(GameDataFraming.CompressedMidStream, cursor.Framing);
        // Stops at the level table: this class is about the prefix, the ASL and the record lists.
        // Reading further would hit the unported 5.x cell-content tables on one of the fixtures.
        return GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Prefix_reads_with_the_widths_the_writer_used(
        string rel, double expectedVersion, string expectedName,
        int expectedSmallPics, int expectedTitles, int? expectedCredits)
    {
        string? path = GameDat(rel);
        if (path is null) return;

        var g = Read(path);

        Assert.Equal(expectedVersion, g.Version.Value, 6);
        Assert.Equal(expectedName, g.DesignName);

        // startTime is 800 in every design shipped so far. A round decimal is the strongest cheap
        // evidence of alignment: a one-byte slip yields arbitrary 32-bit noise, not 800. It also
        // sits immediately after three consecutive BYTEs, which is exactly where a reader that
        // widens them to int would go wrong.
        Assert.Equal(800, g.StartTime);
        Assert.InRange(g.StartX, (byte)0, (byte)255);
        Assert.InRange(g.StartFacing, (byte)0, (byte)7);

        // Real filenames, not noise -- these follow the LOGFONT struct blit, so they also confirm
        // its size is right.
        Assert.Equal("AreaViewArt.png", g.MapArt);
        Assert.Equal("iconBackground.png", g.IconBackgroundArt);

        Assert.Equal(expectedSmallPics, g.SmallPicImports.Count);
        Assert.Equal(expectedSmallPics, g.IconPicImports.Count);
        Assert.All(g.SmallPicImports, p => Assert.All(p.FileName, ch => Assert.InRange(ch, ' ', '~')));

        Assert.NotNull(g.TitleData);
        Assert.Equal(expectedTitles, g.TitleData!.Titles.Count);

        // creditsData exists only at 5.25 and above; below that the credits art was a single
        // string read much earlier in the record. Both branches are covered by these fixtures.
        if (expectedCredits is null)
        {
            Assert.Null(g.CreditsData);
            Assert.True(g.Version < DesignVersion.V525);
        }
        else
        {
            Assert.Equal(expectedCredits, g.CreditsData!.Titles.Count);
            Assert.True(g.Version >= DesignVersion.V525);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Non_empty_compressed_asl_decodes_through_the_reader(
        string rel, double expectedVersion, string expectedName,
        int expectedSmallPics, int expectedTitles, int? expectedCredits)
    {
        string? path = GameDat(rel);
        if (path is null) return;

        var g = Read(path);

        // The payoff. Four entries, in hash order, read by AslReader's compressed overload with
        // an intern table built from the start of the stream.
        Assert.Equal(4, g.Attributes.Count);
        Assert.Equal(ExpectedKeys, g.Attributes.Select(e => e.Key));
        Assert.All(g.Attributes, e => Assert.Equal(AslFlags.Editor, (AslFlags)e.Flags));

        // All four values are the same string, so only the first is written literally and the
        // other three are table indices. Getting identical text back for all four is proof the
        // back-references resolved -- a broken table yields a wrong string, not an error.
        Assert.Single(g.Attributes.Select(e => e.Value).Distinct());
        Assert.NotEmpty(g.Attributes[0].Value);

        _ = (expectedVersion, expectedName, expectedSmallPics, expectedTitles, expectedCredits);
    }

    [Fact]
    public void Asl_values_are_the_design_format_version_not_the_file_version()
    {
        string? path = GameDat("dc-default/data-files");
        if (path is null) return;

        var g = Read(path);

        // Worth pinning because the two look interchangeable and are not: the file declares 5.28
        // while these attributes say 3.56. They record the version whose behaviour the design
        // wants, which is why the engine consults them separately from the container version.
        Assert.Equal(5.28, g.Version.Value, 6);
        Assert.All(g.Attributes, e => Assert.Equal("3.56", e.Value));
    }

    /// <summary>folder, art slot count, special items, quests.</summary>
    public static TheoryData<string, int, int, int> Tails => new()
    {
        { "dc-default/data-files", 11, 0, 0 },
        { "SomethingWild.dsn/Data", 10, 12, 0 },
        { "Case.dsn/Data", 10, 7, 171 },
    };

    [Theory]
    [MemberData(nameof(Tails))]
    public void Structures_after_the_asl_read_correctly(
        string rel, int expectedArt, int expectedItems, int expectedQuests)
    {
        string? path = GameDat(rel);
        if (path is null) return;

        var g = Read(path);

        // The art slot count is version-dependent: eight unconditional slots, plus
        // CharViewFrameVPArt at 5.26 and CombatPetrifiedIconArt at 0.930204, plus CombatDeathArt.
        // Only the 5.28 design gets the first of those, which is why the counts differ by one.
        Assert.Equal(expectedArt, g.Art.Count);
        Assert.All(g.Art, a => Assert.All(a.Name, ch => Assert.InRange(ch, ' ', '~')));

        // Real sound filenames this deep into the record mean the whole ASL and art block landed.
        Assert.NotNull(g.Sounds);
        Assert.Equal("sound_Hit.wav", g.Sounds!.CharHit);
        Assert.NotEmpty(g.Sounds.IntroMusic);

        Assert.Equal(expectedItems, g.SpecialItems.Count);
        Assert.Equal(expectedQuests, g.Quests.Count);

        // stage is a WORD in both record types; a 4-byte read would desynchronise the list.
        Assert.All(g.Quests, q => Assert.All(q.Name, ch => Assert.InRange(ch, ' ', '~')));
        Assert.All(g.SpecialItems, s => Assert.All(s.Name, ch => Assert.InRange(ch, ' ', '~')));
    }

    [Fact]
    public void Quest_records_carry_names_and_attributes()
    {
        string? path = GameDat("Case.dsn/Data");
        if (path is null) return;

        var g = Read(path);

        Assert.Equal(171, g.Quests.Count);
        Assert.Equal("Ba_Wi_Dw_no", g.Quests[0].Name);

        // Reading 171 consecutive quests, each ending in its own ASL, is only possible if every
        // record's WORD stage and attribute block were sized right.
        Assert.All(g.Quests, q => Assert.NotEmpty(q.Name));
    }

    [Fact]
    public void Hash_order_matches_what_the_raw_byte_decode_predicted()
    {
        string? path = GameDat("Case.dsn/Data");
        if (path is null) return;

        var g = Read(path);

        // AslCompressedTests derived this order by decoding raw bytes by hand, before any reader
        // could reach the block. Confirming the reader independently produces the same order
        // closes the loop on the "entries are hash-ordered, not insertion-ordered" finding.
        Assert.Equal("GuidedTourVersion", g.Attributes[0].Key);
        Assert.NotEqual("RunAsVersion", g.Attributes[0].Key);   // the uncompressed file's first key
        Assert.Equal(ExpectedKeys, g.Attributes.Select(e => e.Key));
    }

    [Fact]
    public void A_five_x_design_reads_past_the_cell_contents_gate()
    {
        string? path = GameDat("dc-default/data-files");
        if (path is null)
        {
            return;
        }

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        Assert.True(cursor.Version >= GlobalStatsTailReaders.CellContentsGate);

        // This threw until the two 5.x tables were ported -- LEVEL_STATS carries m_wallOverrides
        // and m_cellContents above _CELL_CONTENTS_VERSION, and stopping there was why dc-default
        // could not be walked past its level table at all.
        var globals = GlobalStatsReader.Read(cursor.Body, cursor.Version);

        Assert.NotNull(globals.Levels);
        Assert.NotEmpty(globals.Levels!.Levels);

        // Present but empty in every shipped design: the tables are sparse, and a level with no
        // per-cell overrides writes a zero count rather than nothing at all. Distinguishing
        // "read an empty table" from "did not read a table" is the point -- a reader that skipped
        // the counts would desynchronise everything after them.
        Assert.All(globals.Levels.Levels.Values, stats =>
        {
            Assert.NotNull(stats.Overrides);
            Assert.NotNull(stats.Contents);
        });
    }

    [Fact]
    public void Pre_five_x_designs_have_no_cell_content_tables_at_all()
    {
        string? path = GameDat("SomethingWild.dsn/Data");
        if (path is null)
        {
            return;
        }

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        Assert.True(cursor.Version < GlobalStatsTailReaders.CellContentsGate);

        var globals = GlobalStatsReader.Read(cursor.Body, cursor.Version);

        // Null rather than empty: below the gate these bytes are not in the file, and conflating
        // "absent" with "present and empty" would hide a version-gate mistake.
        Assert.All(globals.Levels!.Levels.Values, stats =>
        {
            Assert.Null(stats.Overrides);
            Assert.Null(stats.Contents);
        });
    }
}
