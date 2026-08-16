namespace UAFedit.Levels.Tests;

/// <summary>
/// The file-to-stats pairing, which is the one thing in this namespace that is easy to get wrong.
/// </summary>
public class LevelCatalogTests
{
    [Theory]
    [InlineData("Level001.lvl", 1)]
    [InlineData("Level004.lvl", 4)]
    [InlineData("Level018.lvl", 18)]
    [InlineData("Level255.lvl", 255)]
    // The prefix is matched case-insensitively because the reference builds these names on a
    // case-insensitive filesystem and shipped designs are inconsistent about it.
    [InlineData("level012.LVL", 12)]
    [InlineData("/some/where/Data/Level007.lvl", 7)]
    public void A_level_files_name_gives_its_number(string path, int expected) =>
        Assert.Equal(expected, LevelCatalog.NumberFromFileName(path));

    [Theory]
    // Zero is not a level: the name is formatted from index+1, so the lowest is Level001.
    [InlineData("Level000.lvl")]
    [InlineData("Backup.lvl")]
    [InlineData("Level.lvl")]
    [InlineData("LevelA01.lvl")]
    public void A_name_that_breaks_the_convention_has_no_number(string path) =>
        Assert.Equal(-1, LevelCatalog.NumberFromFileName(path));

    /// <summary>The name is three digits, so 255 is not truncated and 1 is not "Level1".</summary>
    [Theory]
    [InlineData(1, "Level001.lvl")]
    [InlineData(18, "Level018.lvl")]
    [InlineData(255, "Level255.lvl")]
    public void A_number_gives_back_the_file_name(int number, string expected)
    {
        Assert.Equal(expected, LevelCatalog.FileNameFor(number));
        Assert.Equal(number, LevelCatalog.NumberFromFileName(expected));
    }

    /// <summary>
    /// The premise: both corpus designs open and hold levels.
    /// </summary>
    /// <remarks>
    /// Every other corpus test here early-returns without <c>reference/</c>. This one asserts the
    /// corpus is real, so a checkout that lost it fails at exactly one place.
    /// </remarks>
    [Fact]
    public void The_corpus_designs_really_loaded()
    {
        foreach (string name in new[] { Corpus.SomethingWild, Corpus.Case })
        {
            if (Corpus.Open(name) is not { } design)
            {
                return;
            }

            using (design)
            {
                var catalog = LevelCatalog.Build(design, readFiles: false);

                Assert.NotEmpty(catalog.Entries);
                Assert.All(catalog.Entries, e => Assert.True(e.Number > 0));
                Assert.All(catalog.Entries, e => Assert.NotNull(e.Stats));
                Assert.Contains(catalog.Entries, e => e.IsUsed);
                Assert.Contains(catalog.Entries, e => e.Name.Length > 0);
            }
        }
    }

    /// <summary>
    /// <b>Case.dsn's numbering has holes, and this is what they look like.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ten files numbered 1-4, 11-13, 16, 18 and 255 at positions 0-9. So from the fifth file on,
    /// the position and the level number part company, and the tenth file is level 255 — its stats
    /// live at <c>stats[254]</c>, not at <c>stats[9]</c>, which does not exist at all.
    /// </para>
    /// <para>
    /// This is the pin. Any change that starts keying <c>LEVEL_STATS</c> by position rather than by
    /// number-minus-one fails here and passes on every hole-free design, which is why it has to be
    /// asserted against this one specifically.
    /// </para>
    /// </remarks>
    [Fact]
    public void Case_dsn_numbers_its_levels_with_holes()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var catalog = LevelCatalog.Build(design, readFiles: false);

        Assert.Equal(
            [1, 2, 3, 4, 11, 12, 13, 16, 18, 255],
            catalog.Entries.Select(e => e.Number));

        Assert.Equal(
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9],
            catalog.Entries.Select(e => e.Position));

        // The key is always the number less one, never the position.
        Assert.All(catalog.Entries, e => Assert.Equal(e.Number - 1, e.StatsIndex));

        // And the two agree only up to the first hole.
        Assert.Equal(4, catalog.Entries.Count(e => e.PositionMatchesNumber));

        var last = catalog.Entries[^1];
        Assert.Equal(9, last.Position);
        Assert.Equal(255, last.Number);
        Assert.Equal(254, last.StatsIndex);
        Assert.Equal("Level255.lvl", last.FileName);
        Assert.Equal("Reeftest", last.Name);
        Assert.Equal("10 x 10", last.StatsSize);

        // The table it would have been read from by position is not even populated. A lookup keyed
        // that way answers nothing here and answers the WRONG level for positions 4 to 8.
        var levels = design.Globals.Levels!.Levels;
        Assert.False(levels.ContainsKey(9));
        Assert.Equal("Dartcave_Amis", levels[11].Name);            // position 5's key
        Assert.Equal("Dartcave_Bastias", levels[10].Name);         // position 4's key
    }

    /// <summary>
    /// Keying by position on Case loses the stats of six of its ten levels outright.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated as the damage rather than as the rule. A position-keyed lookup does not fetch the
    /// wrong row on <c>Case</c> — it fetches <i>nothing</i>, because the table's populated keys are
    /// 0-3, 10-12, 15, 17 and 254 and positions 4-9 are none of them. So the symptom is a level with
    /// no name, no size and, in the engine, <b>no entry points at all</b>: a teleport arriving there
    /// gets a null and falls back.
    /// </para>
    /// <para>
    /// The wrong-row version is reachable too — a design whose positions happened to land on
    /// populated keys would silently show another level's data — which is why the correct key is
    /// derived rather than guarded against.
    /// </para>
    /// </remarks>
    [Fact]
    public void Keying_by_position_loses_the_stats_of_six_of_Cases_ten_levels()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var catalog = LevelCatalog.Build(design, readFiles: false);
        var levels = design.Globals.Levels!.Levels;

        int lost = 0;

        foreach (var entry in catalog.Entries)
        {
            levels.TryGetValue((uint)entry.Position, out var byPosition);

            if (!ReferenceEquals(byPosition, entry.Stats))
            {
                lost++;
                Assert.Null(byPosition);
            }

            // The number-derived key always finds the row, on every one of the ten.
            Assert.True(levels.ContainsKey((uint)entry.StatsIndex));
        }

        Assert.Equal(6, lost);
    }

    /// <summary>
    /// A level file can record a different number than its own name, and the name wins.
    /// </summary>
    /// <remarks>
    /// <c>Case</c>'s <c>Level004.lvl</c> stores <c>m_level</c> 11 — it was saved as a copy of level
    /// 12. Every path the engine takes builds the file name from the index it was asked for
    /// (<c>Level.cpp:3643</c>), so the bytes inside never decide anything; a catalog that trusted
    /// them would pair this grid with <c>Dartcave_Amis</c>'s stats instead of
    /// <c>Dartcave_Amis_SafetyCopy20160512</c>'s.
    /// </remarks>
    [Fact]
    public void A_files_stored_number_can_disagree_with_its_name()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var catalog = LevelCatalog.Build(design);

        var level4 = catalog.ByNumber(4)!;

        Assert.Equal(12, level4.StoredNumber);
        Assert.False(level4.AgreesWithFileName);
        Assert.Equal("Dartcave_Amis_SafetyCopy20160512", level4.Name);

        // Exactly one file in Case is like this, so the assertion above is a fact about the design
        // rather than a fact about whichever file happened to be checked.
        Assert.Equal(1, catalog.Entries.Count(e => !e.AgreesWithFileName));
    }

    /// <summary>
    /// A design numbered without holes is where position and number agree — which is why
    /// conflating them survives so long.
    /// </summary>
    [Fact]
    public void SomethingWild_has_no_holes_so_position_and_number_agree()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var catalog = LevelCatalog.Build(design);

        Assert.Equal(8, catalog.Entries.Count);
        Assert.All(catalog.Entries, e => Assert.True(e.PositionMatchesNumber));
        Assert.All(catalog.Entries, e => Assert.True(e.AgreesWithFileName));
        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8],
            catalog.Entries.Select(e => e.Number));
    }

    /// <summary>Every stats row of both corpus designs has a file, so nothing is orphaned.</summary>
    /// <remarks>
    /// Worth pinning: an orphan is what the original's chooser pops an error box about, and a
    /// regression that started producing spurious ones would look exactly like a numbering bug.
    /// </remarks>
    [Fact]
    public void The_corpus_designs_have_no_orphan_stats_rows()
    {
        foreach (string name in new[] { Corpus.SomethingWild, Corpus.Case })
        {
            if (Corpus.Open(name) is not { } design)
            {
                return;
            }

            using (design)
            {
                var catalog = LevelCatalog.Build(design, readFiles: false);

                Assert.Empty(catalog.Orphans);
                Assert.Empty(catalog.Unnamed);
                Assert.Equal(catalog.Entries.Count, catalog.DeclaredLevelCount);
            }
        }
    }

    /// <summary>Reading the files fills in the extents, and they match what the stats claim.</summary>
    /// <remarks>
    /// The two are stored separately — <c>LEVEL_STATS</c> in <c>game.dat</c>, the grid in the
    /// <c>.lvl</c> — and nothing in the format keeps them in step. They agree on every level of both
    /// corpus designs, which is what makes a disagreement worth reporting rather than tolerating.
    /// </remarks>
    [Fact]
    public void The_stats_extent_and_the_grid_extent_agree_on_the_corpus()
    {
        foreach (string name in new[] { Corpus.SomethingWild, Corpus.Case })
        {
            if (Corpus.Open(name) is not { } design)
            {
                return;
            }

            using (design)
            {
                var catalog = LevelCatalog.Build(design);

                Assert.All(catalog.Entries, e => Assert.True(e.IsReadable, e.FileName));
                Assert.All(catalog.Entries, e => Assert.NotNull(e.Width));
                Assert.All(catalog.Entries, e => Assert.True(e.SizeAgrees,
                    $"{e.FileName}: stats say {e.StatsSize}, grid is {e.FileSize}"));

                // Every level of every shipped design writes the full wall and zone tables.
                Assert.All(catalog.Entries, e => Assert.Equal(WallSlotsViewModel.MaxSlots,
                                                              e.WallSetCount));
                Assert.All(catalog.Entries, e => Assert.Equal(ZonesViewModel.MaxZones,
                                                              e.ZoneCount));
            }
        }
    }

    /// <summary>Not reading the files leaves the file-derived columns empty and costs nothing.</summary>
    [Fact]
    public void A_catalog_built_without_reading_still_knows_the_numbering()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var catalog = LevelCatalog.Build(design, readFiles: false);

        Assert.Equal(10, catalog.Entries.Count);
        Assert.All(catalog.Entries, e => Assert.Null(e.Width));
        Assert.All(catalog.Entries, e => Assert.Null(e.StoredNumber));

        // With nothing read there is nothing to disagree with, so the name is taken at its word.
        Assert.All(catalog.Entries, e => Assert.True(e.AgreesWithFileName));
        Assert.Equal(255, catalog.Entries[^1].Number);
    }

    /// <summary>The area-view label changes wording with the level's kind, as the original's does.</summary>
    [Fact]
    public void The_area_view_label_reads_differently_on_a_wilderness_level()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var catalog = LevelCatalog.Build(design, readFiles: false);

        // 'Land of the Hunt' is overland with AVStyle 1; 'Cat Lord's Realm' is a dungeon with 2.
        var overland = catalog.ByNumber(3)!;
        Assert.True(overland.IsOverland);
        Assert.Equal(AreaViewStyle.OnlyAreaView, overland.AreaViewStyle);
        Assert.Equal("Large Only", overland.AreaViewStyleText);

        var dungeon = catalog.ByNumber(7)!;
        Assert.False(dungeon.IsOverland);
        Assert.Equal(AreaViewStyle.Only3DView, dungeon.AreaViewStyle);
        Assert.Equal("3D Only", dungeon.AreaViewStyleText);

        Assert.Equal("Any", catalog.ByNumber(1)!.AreaViewStyleText);
    }

    /// <summary>Lookup by number and by position each find the row they name.</summary>
    [Fact]
    public void Lookup_by_number_and_by_position_disagree_on_Case()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var catalog = LevelCatalog.Build(design, readFiles: false);

        Assert.Equal(255, catalog.ByPosition(9)!.Number);
        Assert.Equal(9, catalog.ByNumber(255)!.Position);

        // Number 9 does not exist; position 9 does. The two spaces are not interchangeable.
        Assert.Null(catalog.ByNumber(9));
        Assert.NotNull(catalog.ByPosition(9));
    }
}
