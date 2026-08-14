using UAF.Media.Sdl;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The map-override calls against a loaded design.
/// </summary>
/// <remarks>
/// What the unhosted environment cannot cover: coordinates only wrap once a level has a width and
/// a height, and a read only falls through to the design once there is a design to fall through
/// to.
/// </remarks>
public class GameScriptHostMapOverrideTests
{
    private static Game? Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null
            ? null
            : Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");

        if (root is null || !Directory.Exists(root))
        {
            return null;
        }

        var design = LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());
        return new Game(design, levelIndex: 1) { Dice = _ => 20 };
    }

    /// <summary>
    /// The first level the design ships that has an extent, numbered as a script names it.
    /// </summary>
    /// <remarks>
    /// <b>The table is keyed from zero and the script counts from one.</b> The design writes each
    /// level out under its raw <c>stats[]</c> index, and <c>GetMapOverride</c> reads
    /// <c>stats[level - 1]</c> — so the number handed to the host is the key plus one, and a test
    /// that passed the key straight through would be addressing the level before the one it
    /// measured.
    /// </remarks>
    private static (int Number, LevelStats Stats)? FirstLevel(Game game)
    {
        var levels = game.Design.Globals.Levels?.Levels;

        if (levels is null)
        {
            return null;
        }

        foreach (var (key, stats) in levels.OrderBy(l => l.Key))
        {
            if (stats.Width > 0 && stats.Height > 0)
            {
                return ((int)key + 1, stats);
            }
        }

        return null;
    }

    /// <summary>
    /// A square written by a script reads back, and does not disturb its neighbours.
    /// </summary>
    [Fact]
    public void A_written_square_reads_back()
    {
        if (Load() is not { } game || FirstLevel(game) is not { } level)
        {
            return;
        }

        var host = new GameScriptHost(game);

        int before = host.GetMapOverride(GpdlMapOverrideKind.Wall, level.Number, 2, 3, 1);
        host.SetMapOverride(GpdlMapOverrideKind.Wall, level.Number, 2, 3, 1, 12);

        Assert.Equal(12, host.GetMapOverride(GpdlMapOverrideKind.Wall, level.Number, 2, 3, 1));

        // The next side along is untouched -- whatever it was, it is not what was just written.
        Assert.NotEqual(12, host.GetMapOverride(GpdlMapOverrideKind.Wall, level.Number, 2, 3, 2));

        // And the write really changed something, rather than the square already holding 12.
        Assert.NotEqual(12, before);
    }

    /// <summary>
    /// Coordinates fold onto the level's grid, so a negative one is the far edge.
    /// </summary>
    /// <remarks>
    /// <b>The wrap is the map's geometry, not a bounds check</b> — and it is why this test needs a
    /// loaded design at all: without a width and a height there is nothing to fold into. A read at
    /// <c>-1</c> and a read at <c>width - 1</c> are the same square, so a port that treated
    /// out-of-range as "nothing there" would answer 255 to one and a value to the other.
    /// </remarks>
    [Fact]
    public void A_coordinate_past_the_edge_comes_round_the_other_side()
    {
        if (Load() is not { } game || FirstLevel(game) is not { } level)
        {
            return;
        }

        var host = new GameScriptHost(game);
        int width = level.Stats.Width;
        int height = level.Stats.Height;

        host.SetMapOverride(GpdlMapOverrideKind.Overlay, level.Number, width - 1, height - 1, 0,
                            33);

        // The same square, named three other ways.
        Assert.Equal(33, host.GetMapOverride(GpdlMapOverrideKind.Overlay, level.Number, -1, -1, 0));
        Assert.Equal(33, host.GetMapOverride(GpdlMapOverrideKind.Overlay, level.Number,
                                             width - 1 + width, height - 1, 0));
        Assert.Equal(33, host.GetMapOverride(GpdlMapOverrideKind.Overlay, level.Number,
                                             -1, height - 1 - height, 0));
    }

    /// <summary>
    /// A write wraps too, so a script that writes past the edge and reads inside it agrees.
    /// </summary>
    [Fact]
    public void A_write_past_the_edge_lands_on_the_wrapped_square()
    {
        if (Load() is not { } game || FirstLevel(game) is not { } level)
        {
            return;
        }

        var host = new GameScriptHost(game);

        host.SetMapOverride(GpdlMapOverrideKind.Door, level.Number, -1, 0, 0, 21);

        Assert.Equal(21, host.GetMapOverride(GpdlMapOverrideKind.Door, level.Number,
                                             level.Stats.Width - 1, 0, 0));
    }

    /// <summary>
    /// Neither corpus design ships a single map override, so nothing here can read one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the reason the design fall-through is tested elsewhere, and it is worth stating
    /// rather than leaving as a silent gap.</b> <c>LEVEL_STATS</c> only carries a wall-override
    /// table from design version 5.0 (<c>GlobalStatsTailReaders.CellContentsGate</c>); Case.dsn is
    /// 2.53 and SomethingWild.dsn is 3.55, so <c>LevelStats.Overrides</c> is null on every level of
    /// both. A test that walked the corpus looking for a shipped override would find none and pass
    /// without asserting anything.
    /// </para>
    /// <para>
    /// The lookup itself is covered by <c>WallOverridesLookupTests</c> against a table built by
    /// hand, and what is pinned here is the premise: if a 5.x design is ever added to the corpus,
    /// this fails and the fall-through becomes testable for real.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_corpus_design_ships_an_override_to_fall_through_to()
    {
        if (Load() is not { } game || game.Design.Globals.Levels?.Levels is not { } levels)
        {
            return;
        }

        Assert.NotEmpty(levels);
        Assert.All(levels.Values, stats => Assert.Null(stats.Overrides));
    }

    /// <summary>A level the design does not have answers 255 rather than throwing.</summary>
    [Fact]
    public void A_level_the_design_does_not_have_answers_nothing()
    {
        if (Load() is not { } game)
        {
            return;
        }

        var host = new GameScriptHost(game);

        Assert.Equal(GpdlMapOverride.None,
                     host.GetMapOverride(GpdlMapOverrideKind.Wall, 250, 0, 0, 0));

        // And writing to it is ignored rather than remembered.
        host.SetMapOverride(GpdlMapOverrideKind.Wall, 300, 0, 0, 0, 5);
        Assert.Equal(GpdlMapOverride.None,
                     host.GetMapOverride(GpdlMapOverrideKind.Wall, 300, 0, 0, 0));
    }
}
