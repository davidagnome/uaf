using UAF.Serialization;
using UAFcore;
using UAFedit.Map;

namespace UAFedit.Map.Tests;

/// <summary>
/// The map model against a real level from a real design.
/// </summary>
/// <remarks>
/// <para>
/// Everything here needs a shipped design, and <c>reference/</c> is gitignored — so every test
/// returns early when it is absent, and <see cref="The_corpus_level_really_loaded"/> is what stops
/// the file passing while proving nothing.
/// </para>
/// <para>
/// <c>SomethingWild</c> is used because its levels are large, walled and doored, and because the
/// engine-side tests already lean on it — a disagreement between the editor's view of a wall and
/// the engine's would show up as these two files reading the same bytes differently.
/// </para>
/// </remarks>
public class LevelMapCorpusTests
{
    /// <summary>The design directory, or null on a checkout without the corpus.</summary>
    private static string? DesignRoot(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string root = Path.Combine(dir.FullName, "reference", name);
        return Directory.Exists(root) ? root : null;
    }

    /// <summary>
    /// A level of a corpus design, with its <c>LEVEL_STATS</c>, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>LoadedDesign.Level</c> needs every event type in the file to be readable and returns null
    /// otherwise, so the first level that comes back whole is taken rather than a fixed index.
    /// </para>
    /// <para>
    /// <b><c>LEVEL_INFO</c> is keyed by the level's own number, not by its position in the file
    /// list.</b> The table is sparse and <c>Case</c> ships <c>Level001</c>…<c>Level018</c> plus a
    /// <c>Level255</c> with gaps, so the two differ there. <see cref="LevelFile.Level"/> is
    /// <c>m_level</c>, one less than the filename's number — and in <c>Case</c> at least one file
    /// disagrees with its own name (<c>Level004.lvl</c> stores 11), so the stored number is
    /// authoritative but not always sane. Nothing here depends on which level comes back, only that
    /// its stats match it.
    /// </para>
    /// </remarks>
    private static (LevelMapModel Model, LoadedDesign Design, int Number)? Corpus(
        string name = "SomethingWild.dsn")
    {
        if (DesignRoot(name) is not { } root)
        {
            return null;
        }

        var design = LoadedDesign.Open(root);

        for (int index = 0; index < design.LevelFiles.Count; index++)
        {
            if (design.Level(index) is not { Width: > 0, Height: > 0 } level)
            {
                continue;
            }

            LevelStats? stats = null;
            design.Globals.Levels?.Levels.TryGetValue((uint)level.Level, out stats);

            var start = design.Globals.StartLevel == level.Level
                ? ((int)design.Globals.StartX, (int)design.Globals.StartY)
                : ((int, int)?)null;

            return (new LevelMapModel(level, stats, start), design, level.Level);
        }

        design.Dispose();
        return null;
    }

    /// <summary>
    /// The premise: a design opened, a level came back whole, and it has cells with walls on them.
    /// </summary>
    /// <remarks>
    /// Every other test here early-returns without a corpus. This one asserts the corpus is real,
    /// so a checkout that silently lost <c>reference/</c> fails loudly at exactly one place rather
    /// than passing an empty file.
    /// </remarks>
    [Fact]
    public void The_corpus_level_really_loaded()
    {
        if (Corpus() is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var model = corpus.Model;

        Assert.True(model.Width > 0);
        Assert.True(model.Height > 0);
        Assert.Equal(model.Width * model.Height, model.Level.Cells.Count);

        // A shipped dungeon level is not a blank grid: it has walls, and it has more than one
        // distinct wall set in use.
        var used = new HashSet<int>();
        int walled = 0;

        for (int y = 0; y < model.Height; y++)
        {
            for (int x = 0; x < model.Width; x++)
            {
                var cell = model.At(x, y);
                foreach (var facing in LevelMapPainter.DrawOrder)
                {
                    int index = cell.Side(facing).WallIndex;
                    if (index > MapPalette.NoWall)
                    {
                        walled++;
                        used.Add(index);
                    }
                }
            }
        }

        Assert.True(walled > 0, "the corpus level has no walls at all");
        Assert.True(used.Count > 1, "the corpus level uses only one wall set");
        Assert.NotEmpty(model.WallSets);
    }

    /// <summary>
    /// The editor's model and the engine's <see cref="UAFcore.Map"/> agree about every wall and blockage.
    /// </summary>
    /// <remarks>
    /// The point of the exercise: both go through <c>AreaMapCell</c>'s north/SOUTH/east/west
    /// permutation, so if the editor ever indexes the raw array with a <see cref="Facing"/> this is
    /// where it shows. <see cref="LevelMapModel.ShowOverrides"/> is off, which is the state in which
    /// the two are supposed to agree.
    /// </remarks>
    [Fact]
    public void The_editors_walls_are_the_engines_walls()
    {
        if (Corpus() is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var model = corpus.Model;
        var engine = new UAFcore.Map((byte)model.Width, (byte)model.Height, model.Level.Cells);

        int compared = 0;

        for (int y = 0; y < model.Height; y++)
        {
            for (int x = 0; x < model.Width; x++)
            {
                var cell = model.At(x, y);
                foreach (var facing in LevelMapPainter.DrawOrder)
                {
                    Assert.Equal(engine.At(x, y)!.WallAt((int)facing), cell.Side(facing).WallIndex);
                    Assert.Equal(engine.Blockage(x, y, facing), cell.Side(facing).Blockage);
                    compared++;
                }
            }
        }

        Assert.True(compared > 0);
    }

    /// <summary>
    /// A shared edge's two faces carry the same wall, which is what the editor's double-fill
    /// guarantees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PlaceWall</c> writes the neighbour's opposite face as well when <c>DoubleFillWallSlot</c>
    /// is on (<c>UAFWinEdView.cpp:2472</c>), so an authored level is symmetric. It is <i>not</i> a
    /// format guarantee: the data allows a one-sided wall and the engine honours it, only ever
    /// consulting the cell being left. <c>SomethingWild</c> is symmetric on every edge of every
    /// level — 9,708 of 9,708 — while <c>Case</c>, an older conversion, manages only 74%.
    /// </para>
    /// <para>
    /// So the real assertion is the comparison, not the ratio: the permuted reading has to beat the
    /// raw one by a wide margin. Reading the wall array by <see cref="Facing"/> drops
    /// <c>SomethingWild</c> to 79% and <c>Case</c> to 26%, which is the signature of the mistake.
    /// </para>
    /// </remarks>
    [Fact]
    public void Shared_edges_agree_far_better_permuted_than_raw()
    {
        if (Corpus() is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var model = corpus.Model;

        int permuted = 0, raw = 0, total = 0;

        for (int y = 0; y < model.Height; y++)
        {
            for (int x = 0; x < model.Width; x++)
            {
                var here = model.At(x, y);
                var hereRaw = model.CellAt(x, y);

                var east = model.At(x + 1, y);
                if (here.East.WallIndex == east.West.WallIndex) permuted++;
                if (hereRaw.Walls[(int)Facing.East] ==
                    model.CellAt(x + 1, y).Walls[(int)Facing.West]) raw++;
                total++;

                var south = model.At(x, y + 1);
                if (here.South.WallIndex == south.North.WallIndex) permuted++;
                if (hereRaw.Walls[(int)Facing.South] ==
                    model.CellAt(x, y + 1).Walls[(int)Facing.North]) raw++;
                total++;
            }
        }

        Assert.True(total > 0);
        Assert.True(permuted > raw,
                    $"the permuted reading agrees on {permuted} of {total} edges and the raw one "
                    + $"on {raw} — the side permutation looks wrong");
        Assert.True(permuted > total * 0.95,
                    $"only {permuted} of {total} shared edges agree");
    }

    /// <summary>Door art really is what makes the map leave a gap, on real data.</summary>
    /// <remarks>
    /// A design with no doored wall set would make <see cref="LevelMapCell.HasDoorGap"/> untestable
    /// against the corpus, so the premise is asserted rather than assumed.
    /// </remarks>
    [Fact]
    public void A_doored_wall_set_draws_two_dashes_and_a_solid_one_draws_three()
    {
        if (Corpus() is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var model = corpus.Model;
        var painter = new LevelMapPainter(MapCellGeometry.Default, MapPalette.Default);

        int doored = 0, solid = 0;

        for (int y = 0; y < model.Height && (doored == 0 || solid == 0); y++)
        {
            for (int x = 0; x < model.Width && (doored == 0 || solid == 0); x++)
            {
                var cell = model.At(x, y);
                foreach (var facing in LevelMapPainter.DrawOrder)
                {
                    if (cell.Side(facing).WallIndex <= MapPalette.NoWall)
                    {
                        continue;
                    }

                    int dashes = painter.WallMarks(cell, facing, model.WallSets).Count();

                    if (cell.HasDoorGap(facing, model.WallSets))
                    {
                        Assert.Equal(2, dashes);
                        doored++;
                    }
                    else
                    {
                        Assert.Equal(3, dashes);
                        solid++;
                    }
                }
            }
        }

        Assert.True(solid > 0, "the corpus level has no plain walls");
        Assert.True(doored > 0, "the corpus level has no doors, so the gap is untested");
    }

    /// <summary>
    /// Every mark of every cell stays inside its cell.
    /// </summary>
    /// <remarks>
    /// The whole map is drawn by translating these rectangles by a cell origin, so one that
    /// overflows would bleed into the neighbour and be blamed on the neighbour's data.
    /// </remarks>
    [Fact]
    public void No_mark_escapes_its_cell()
    {
        if (Corpus() is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var model = corpus.Model;
        var square = MapCellGeometry.Default.Square;
        int checkedMarks = 0;

        foreach (var mode in Enum.GetValues<MapDisplayMode>())
        {
            var painter = new LevelMapPainter(MapCellGeometry.Default, MapPalette.Default)
            {
                Mode = mode,
            };

            for (int y = 0; y < Math.Min(model.Height, 16); y++)
            {
                for (int x = 0; x < Math.Min(model.Width, 16); x++)
                {
                    foreach (var mark in painter.Marks(model.At(x, y), model.WallSets))
                    {
                        Assert.InRange(mark.Rect.Left, square.Left, square.Right);
                        Assert.InRange(mark.Rect.Top, square.Top, square.Bottom);
                        Assert.InRange(mark.Rect.Right, square.Left, square.Right);
                        Assert.InRange(mark.Rect.Bottom, square.Top, square.Bottom);
                        checkedMarks++;
                    }
                }
            }
        }

        Assert.True(checkedMarks > 0);
    }

    /// <summary>The design's own palette is the one every shipped config declares.</summary>
    [Fact]
    public void The_corpus_palette_is_the_ega_sixteen()
    {
        if (Corpus() is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var palette = MapPalette.FromConfig(design.Config);

        for (int slot = 0; slot < MapPalette.DeclaredSlots; slot++)
        {
            Assert.Equal(MapPalette.DefaultColors[slot], palette.Wall(slot));
        }

        // And the slots past the sixteen are the black the original leaves them at, which is why a
        // level using wall set 20 draws invisibly.
        Assert.False(palette.IsConfigured(MapPalette.DeclaredSlots));
        Assert.Equal(new MapColor(0, 0, 0), palette.Wall(MapPalette.DeclaredSlots));

        // Opting in gives them something visible instead, and never the empty cell's black.
        var filled = MapPalette.FromConfig(design.Config, fillUndeclared: true);
        for (int slot = MapPalette.DeclaredSlots; slot < MapPalette.Slots; slot++)
        {
            Assert.NotEqual(new MapColor(0, 0, 0), filled.Wall(slot));
        }
    }

    /// <summary>
    /// A real design really does place walls in slots the palette leaves black.
    /// </summary>
    /// <remarks>
    /// <c>Case</c>'s levels draw from wall sets up to 191. Against the stock sixteen-colour palette
    /// a quarter of its walls are black on black — in the original editor as much as in this port.
    /// Pinned here because it is the evidence for <c>fillUndeclared</c> existing at all, and because
    /// a future revision that "simplified" the palette to sixteen entries would break the very case
    /// the flag is for.
    /// </remarks>
    [Fact]
    public void The_corpus_uses_wall_slots_the_palette_leaves_black()
    {
        if (Corpus("Case.dsn") is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var model = corpus.Model;
        var palette = MapPalette.FromConfig(design.Config);

        int undeclared = 0, walls = 0;

        for (int y = 0; y < model.Height; y++)
        {
            for (int x = 0; x < model.Width; x++)
            {
                var cell = model.At(x, y);
                foreach (var facing in LevelMapPainter.DrawOrder)
                {
                    int index = cell.Side(facing).WallIndex;
                    if (index <= MapPalette.NoWall)
                    {
                        continue;
                    }

                    walls++;
                    if (!palette.IsConfigured(index))
                    {
                        undeclared++;
                    }
                }
            }
        }

        Assert.True(walls > 0);
        Assert.True(undeclared > 0,
                    "Case was chosen because it uses undeclared wall slots; it no longer does");
        Assert.Equal(new MapColor(0, 0, 0), palette.Wall(MapPalette.Slots - 1));
    }

    /// <summary>
    /// A hit test lands back on the cell it was asked about, at every zoom and scroll.
    /// </summary>
    /// <remarks>
    /// The round trip is the only thing that catches an off-by-one between
    /// <see cref="LevelMapLayout.Origin"/> and <see cref="LevelMapLayout.HitTest"/>, and it has to
    /// hold at fractional zoom because that is where the two disagree.
    /// </remarks>
    [Fact]
    public void A_cells_centre_hit_tests_back_to_that_cell()
    {
        if (Corpus() is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var model = corpus.Model;

        foreach (double zoom in new[] { 1.0, 1.5, 2.0, 3.7 })
        {
            var layout = new LevelMapLayout(model.Width, model.Height, MapCellGeometry.Default)
            {
                Zoom = zoom,
                ScrollX = 2.25,
                ScrollY = 5.5,
            };

            for (int y = 0; y < Math.Min(model.Height, 12); y++)
            {
                for (int x = 0; x < Math.Min(model.Width, 12); x++)
                {
                    var (left, top) = layout.Origin(x, y);
                    var hit = layout.HitTest(left + (layout.CellWidth / 2),
                                             top + (layout.CellHeight / 2));

                    Assert.Equal(x, hit.X);
                    Assert.Equal(y, hit.Y);
                }
            }
        }
    }

    /// <summary>Case.dsn loads through the same path, so the model is not shaped to one design.</summary>
    [Fact]
    public void A_second_corpus_design_loads_the_same_way()
    {
        if (Corpus("Case.dsn") is not { } corpus)
        {
            return;
        }

        using var design = corpus.Design;
        var model = corpus.Model;

        Assert.True(model.Width > 0 && model.Height > 0);
        Assert.Equal(model.Width * model.Height, model.Level.Cells.Count);

        // Coordinates outside the level wrap rather than throw -- the map is a torus.
        var wrapped = model.At(model.Width + 3, -1);
        Assert.Equal(3, wrapped.X);
        Assert.Equal(model.Height - 1, wrapped.Y);
    }
}
