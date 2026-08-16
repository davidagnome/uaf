using UAF.Serialization;
using UAFcore;

namespace UAFedit.Levels.Tests;

/// <summary>
/// The eight entry points: their odd default, their hidden facings, and the write-back.
/// </summary>
public class EntryPointsTests
{
    /// <summary>A stats row with nothing but the fields these tests read.</summary>
    private static LevelStats Stats(int width = 20, int height = 16,
                                    IEnumerable<EntryPoint>? points = null,
                                    IEnumerable<AslEntry>? attributes = null) =>
        new((byte)height, (byte)width, Used: 1, Overland: 0, AreaViewStyle: 0, Name: "Test",
            EntryPoints: [.. points ?? Enumerable.Range(0, 8).Select(i => new EntryPoint(0, i))],
            StepSound: string.Empty, BumpSound: string.Empty,
            Sounds: null, Overrides: null, Contents: null,
            Attributes: [.. attributes ?? []]);

    /// <summary>
    /// A level nobody touched has eight entry points down its west edge, not eight at the origin.
    /// </summary>
    /// <remarks>
    /// <c>LEVEL_STATS::Clear</c> writes <c>(0, i)</c> (<c>GlobalData.cpp:3059</c>). Reading it as
    /// <c>(0, 0)</c> — which the shape of the data invites — would make every level look like it had
    /// eight entry points stacked on one square.
    /// </remarks>
    [Fact]
    public void An_untouched_levels_entry_points_run_down_the_west_edge()
    {
        var model = new EntryPointsViewModel(Stats());

        Assert.Equal(8, model.Points.Count);
        Assert.All(model.Points, p => Assert.Equal(0, p.X));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], model.Points.Select(p => p.Y));
        Assert.All(model.Points, p => Assert.True(p.IsDefault));

        model.Points[3].X = 5;
        Assert.False(model.Points[3].IsDefault);
        Assert.True(model.IsDirty);
    }

    /// <summary>A null stats row still gives eight slots and the dialog's fallback bounds.</summary>
    /// <remarks>
    /// The reference's <c>m_MaxX</c> initialises to "50" (<c>EntryPointDlg.cpp:56</c>) and is only
    /// overwritten from a real <c>LEVEL_STATS</c>, so a level with no stats row validates against 50.
    /// </remarks>
    [Fact]
    public void No_stats_row_still_gives_eight_slots()
    {
        var model = new EntryPointsViewModel(null);

        Assert.Equal(8, model.Points.Count);
        Assert.Equal(50, model.MaxX);
        Assert.Equal(50, model.MaxY);
        Assert.False(model.IsDirty);
    }

    /// <summary>The bounds are the stats extent less one, exactly as the dialog computes them.</summary>
    [Fact]
    public void The_bounds_are_one_short_of_the_stats_extent()
    {
        var model = new EntryPointsViewModel(Stats(width: 44, height: 34));

        Assert.Equal(43, model.MaxX);
        Assert.Equal(33, model.MaxY);
        Assert.Empty(model.Validate());

        model.Points[0].X = 44;
        var problems = model.Validate();
        Assert.Single(problems);
        Assert.Contains("X 44", problems[0]);
        Assert.Contains("0–43", problems[0]);
    }

    /// <summary>
    /// The facings come out of the level ASL, not out of the entry-point table.
    /// </summary>
    /// <remarks>
    /// <c>EPFace_<i>i</i></c> (<c>GlobalData.cpp:3383</c>). A reader that only looked at the eight
    /// <c>POINT</c>s would report every entry point facing north, which is the value the
    /// deserializer explicitly zeroes on the way in (<c>GlobalData.cpp:3240</c>).
    /// </remarks>
    [Fact]
    public void The_facings_are_read_out_of_the_level_asl()
    {
        var model = new EntryPointsViewModel(Stats(attributes:
        [
            new AslEntry("EPFace_0", 0, "1"),
            new AslEntry("EPFace_3", 0, "2"),
            new AslEntry("SomeDesignKey", 0, "hello"),
        ]));

        Assert.Equal(Facing.East, model.Points[0].Facing);
        Assert.Equal(Facing.North, model.Points[1].Facing);
        Assert.Equal(Facing.South, model.Points[3].Facing);
    }

    /// <summary>
    /// Applying puts the points back in the table and the facings back in the ASL.
    /// </summary>
    /// <remarks>
    /// And leaves the design's own attributes alone: a level ASL holds author-written keys as well
    /// as the eight the serializer parks there, and rebuilding it from the facings alone would
    /// delete them.
    /// </remarks>
    [Fact]
    public void Applying_writes_the_points_to_the_table_and_the_facings_to_the_asl()
    {
        var original = Stats(attributes: [new AslEntry("SomeDesignKey", 0, "hello")]);
        var model = new EntryPointsViewModel(original);

        model.Points[0].X = 12;
        model.Points[0].Y = 9;
        model.Points[0].Facing = Facing.West;
        model.Points[7].Facing = Facing.South;

        var applied = model.Apply(original);

        Assert.Equal(8, applied.EntryPoints.Count);
        Assert.Equal(new EntryPoint(12, 9), applied.EntryPoints[0]);
        Assert.Equal(new EntryPoint(0, 7), applied.EntryPoints[7]);

        // The design's own key survives...
        Assert.Contains(applied.Attributes, a => a.Key == "SomeDesignKey" && a.Value == "hello");

        // ...and all eight facings are written, the way PreSerialize writes them.
        Assert.Equal(8, applied.Attributes.Count(a => a.Key.StartsWith("EPFace_")));
        Assert.Contains(applied.Attributes, a => a.Key == "EPFace_0" && a.Value == "3");
        Assert.Contains(applied.Attributes, a => a.Key == "EPFace_7" && a.Value == "2");

        // Reading the result back gives what was put in -- the round trip the writer needs.
        var reloaded = new EntryPointsViewModel(applied);
        Assert.Equal(12, reloaded.Points[0].X);
        Assert.Equal(Facing.West, reloaded.Points[0].Facing);
        Assert.Equal(Facing.South, reloaded.Points[7].Facing);
    }

    /// <summary>Applying twice does not accumulate facing keys.</summary>
    [Fact]
    public void Applying_twice_leaves_eight_facing_keys()
    {
        var stats = Stats();
        var once = new EntryPointsViewModel(stats).Apply(stats);
        var twice = new EntryPointsViewModel(once).Apply(once);

        Assert.Equal(8, twice.Attributes.Count(a => a.Key.StartsWith("EPFace_")));
    }

    /// <summary>Reverting puts back what the stats row held.</summary>
    [Fact]
    public void Reverting_restores_the_stats_row()
    {
        var stats = Stats(points: [new EntryPoint(3, 4), .. Enumerable.Range(1, 7)
                                                              .Select(i => new EntryPoint(0, i))],
                          attributes: [new AslEntry("EPFace_0", 0, "1")]);

        var model = new EntryPointsViewModel(stats);
        model.Points[0].X = 99;
        model.Points[0].Facing = Facing.South;
        Assert.True(model.IsDirty);

        model.Revert();

        Assert.Equal(3, model.Points[0].X);
        Assert.Equal(4, model.Points[0].Y);
        Assert.Equal(Facing.East, model.Points[0].Facing);
        Assert.False(model.IsDirty);
    }

    /// <summary>The premise, and the real numbers: SomethingWild's first two levels.</summary>
    /// <remarks>
    /// <c>Introduction</c> sets entry points 0 and 1 and leaves the other six at their defaults;
    /// <c>Sigil</c> sets only the first. Pinned because it is the only evidence in the suite that
    /// the fixed table is read in the right order.
    /// </remarks>
    [Fact]
    public void The_corpus_entry_points_are_the_ones_the_design_set()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);

        levels.SelectByNumber(1);
        var intro = levels.Panel!.EntryPoints;
        Assert.Equal((1, 2), (intro.Points[0].X, intro.Points[0].Y));
        Assert.Equal((7, 7), (intro.Points[1].X, intro.Points[1].Y));
        Assert.Equal(6, intro.Points.Count(p => p.IsDefault));
        Assert.Empty(intro.Validate());

        levels.SelectByNumber(2);
        var sigil = levels.Panel!.EntryPoints;
        Assert.Equal((24, 17), (sigil.Points[0].X, sigil.Points[0].Y));
        Assert.Equal(7, sigil.Points.Count(p => p.IsDefault));
        Assert.Equal(43, sigil.MaxX);
        Assert.Equal(33, sigil.MaxY);
        Assert.Empty(sigil.Validate());
    }

    /// <summary>
    /// Every entry point of every corpus level is inside its own level.
    /// </summary>
    /// <remarks>
    /// Nothing clamps these at run time — the teleport path reads them straight into the party's
    /// position (<c>GameEvent.cpp:14654</c>) — so an out-of-range one is a real defect and the
    /// editor's validation is worth having only if it agrees with real data.
    /// </remarks>
    [Fact]
    public void No_corpus_entry_point_is_outside_its_level()
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

                foreach (var entry in catalog.Entries)
                {
                    var points = new EntryPointsViewModel(entry.Stats);
                    Assert.Empty(points.Validate());
                }
            }
        }
    }

    /// <summary>Case's levels are all still at the cleared default, which is worth knowing.</summary>
    /// <remarks>
    /// It means the map's entry-point mode shows a column of eight markers down the west edge of
    /// every one of them — the padding, not the author's data. Pinned so that a future change which
    /// starts hiding default slots is a deliberate one.
    /// </remarks>
    [Fact]
    public void Every_Case_level_is_at_the_cleared_entry_point_default()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var catalog = LevelCatalog.Build(design, readFiles: false);

        Assert.All(catalog.Entries, entry =>
            Assert.All(new EntryPointsViewModel(entry.Stats).Points,
                       p => Assert.True(p.IsDefault)));
    }
}
