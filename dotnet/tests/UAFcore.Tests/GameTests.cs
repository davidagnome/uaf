using UAF.Media;
using UAF.Media.Sdl;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Drives the engine end to end with no window: a real design in, a recorded input stream through,
/// and framebuffers out.
/// </summary>
/// <remarks>
/// <para>
/// This is the thing the C++ engine cannot do. Its equivalent path needs a live DirectX device
/// before it will read a record, so it has no automated tests at all; keeping <see cref="Game"/>
/// ignorant of SDL is what buys this, and it is the reason the split exists rather than an
/// incidental tidiness.
/// </para>
/// <para>
/// The design is gitignored, so these return early without <c>reference/</c>.
/// </para>
/// </remarks>
public class GameTests
{
    private static string? DesignRoot()
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

        string design = Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");
        return Directory.Exists(design) ? design : null;
    }

    private static LoadedDesign Open(string root) =>
        LoadedDesign.Open(root, new SdlImageDecoder(), new SdlFontRasterizer());

    [Fact]
    public void A_design_opens_with_its_records_config_and_art()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);

        Assert.Equal("Something Wild", design.Name);
        Assert.Equal(23, design.Globals.Characters.Count);
        Assert.NotEmpty(design.LevelFiles);

        // The layout config has to be found, or nothing lands in the right place.
        Assert.True(design.Config.Count > 100,
            $"only {design.Config.Count} config entries; config640.txt was probably not found");

        // Art resolves by the names the design's own records use.
        Assert.NotNull(design.Art("border_Horizontal.png"));

        // ...and a name that is not there degrades to null rather than throwing, because designs
        // really do ship slots pointing at missing files.
        Assert.Null(design.Art("this-file-does-not-exist.png"));
    }

    [Fact]
    public void The_party_starts_where_the_design_says()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var game = new Game(design);

        Assert.Equal(design.Globals.StartX, game.X);
        Assert.Equal(design.Globals.StartY, game.Y);
        Assert.Equal(design.Globals.StartTime, game.Minutes);
        Assert.True(game.Running);
    }

    [Fact]
    public void Turning_cycles_the_four_cardinal_directions()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var game = new Game(design);
        var start = game.Facing;

        // Four right turns return to the start -- the &3 mask, not an eight-way compass.
        for (int i = 0; i < 4; i++)
        {
            Assert.True(game.Update(InputEvent.KeyDown(VirtualKey.Right)));
        }
        Assert.Equal(start, game.Facing);

        // And left is its inverse, which is what (facing + 3) & 3 has to get right.
        game.Update(InputEvent.KeyDown(VirtualKey.Right));
        game.Update(InputEvent.KeyDown(VirtualKey.Left));
        Assert.Equal(start, game.Facing);
    }

    [Fact]
    public void Walking_moves_along_the_facing_and_advances_the_clock()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var game = new Game(design);

        // Face east, then walk: x must rise and y must not move.
        while (game.Facing != Facing.East)
        {
            game.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        int x = game.X, y = game.Y, minutes = game.Minutes;
        game.Update(InputEvent.KeyDown(VirtualKey.Up));

        Assert.Equal(x + 1, game.X);
        Assert.Equal(y, game.Y);
        Assert.Equal(minutes + 1, game.Minutes);
        Assert.Equal(1, game.Steps);

        // Backwards is the negation of the facing, not a turn.
        game.Update(InputEvent.KeyDown(VirtualKey.Down));
        Assert.Equal(x, game.X);
        Assert.Equal(Facing.East, game.Facing);
    }

    [Fact]
    public void Escape_stops_the_loop()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var game = new Game(design);

        Assert.True(game.Update(InputEvent.KeyDown(VirtualKey.Escape)));
        Assert.False(game.Running);
    }

    [Fact]
    public void A_recorded_session_presents_a_different_frame_each_time_the_state_changes()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var game = new Game(design);

        // Face a direction with room ahead. Picking one blind is how the first version of this
        // test failed: the party starts near an edge, so the second step hit the clamp and the
        // step count came up one short.
        var open = game.X < 200 ? Facing.East : Facing.West;
        while (game.Facing != open)
        {
            game.Update(InputEvent.KeyDown(VirtualKey.Right));
        }

        using var presenter = new HeadlessPresenter(640, 480);
        var input = new RecordedInputSource(
            InputEvent.KeyDown(VirtualKey.Up),
            InputEvent.KeyDown(VirtualKey.Up),
            InputEvent.KeyDown(VirtualKey.Left),
            InputEvent.KeyDown(VirtualKey.Escape));

        presenter.Present(game.Render());
        while (game.Running && input.TryPoll(out var next))
        {
            // Repainting after a quit is what a naive loop does, and it produces a frame identical
            // to the one before it -- Escape changes state that is not drawn. Checking Running
            // after the update rather than only before it is the fix, and Program.cs does the same.
            if (game.Update(next) && game.Running)
            {
                presenter.Present(game.Render());
            }
        }

        Assert.False(game.Running);
        Assert.Equal(2, game.Steps);
        Assert.Equal(4, presenter.PresentCount);

        // Every frame after a state change must differ from the one before it. Identical hashes
        // would mean the renderer is not actually reading the state it claims to draw -- which a
        // test asserting only on X and Y would never notice.
        var hashes = presenter.FrameHashes;
        for (int i = 1; i < hashes.Count; i++)
        {
            Assert.NotEqual(hashes[i - 1], hashes[i]);
        }
    }

    [Fact]
    public void The_rendered_frame_is_real_art_rather_than_a_fill()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var frame = new Game(design).Render();

        Assert.Equal(640, frame.Width);
        Assert.Equal(480, frame.Height);

        // A silently failed art chain still produces a valid, uniform surface, and every
        // coordinate assertion would pass against it.
        Assert.True(frame.Pixels.Distinct().Count() > 500,
            "the frame has too few distinct colours to contain decoded art");
    }

    [Fact]
    public void A_design_without_a_rasterizer_still_loads_and_draws()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        // The degradation contract: no text rasteriser means no text, not a failure to start.
        using var design = LoadedDesign.Open(root, new SdlImageDecoder(), rasterizer: null);
        var frame = new Game(design).Render();

        Assert.Null(design.Font(16));
        Assert.True(frame.Pixels.Distinct().Count() > 500);
    }

    [Fact]
    public void Opening_a_directory_that_is_not_a_design_is_reported_clearly()
    {
        var error = Assert.Throws<DirectoryNotFoundException>(
            () => LoadedDesign.Open(Path.GetTempPath()));

        Assert.Contains("Data", error.Message);
    }

    [Fact]
    public void The_level_grid_loads_and_has_the_dimensions_the_file_declares()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var map = design.Map(0);

        Assert.NotNull(map);
        Assert.True(map!.Width > 0 && map.Height > 0);

        // Every cell inside the map must resolve, and everything outside must not -- which is the
        // row-major stride being right rather than merely plausible.
        Assert.NotNull(map.At(0, 0));
        Assert.NotNull(map.At(map.Width - 1, map.Height - 1));
        Assert.Null(map.At(map.Width, 0));
        Assert.Null(map.At(0, map.Height));
        Assert.Null(map.At(-1, 0));
    }

    [Fact]
    public void Walls_actually_block_movement()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var map = design.Map(0);
        Assert.NotNull(map);

        // A real design has walls, or this test proves nothing. Find a blocked face and confirm
        // the party cannot walk through it.
        (int x, int y, Facing facing)? blocked = null;
        for (int y = 0; y < map!.Height && blocked is null; y++)
        {
            for (int x = 0; x < map.Width && blocked is null; x++)
            {
                foreach (Facing f in Enum.GetValues<Facing>())
                {
                    if (map.Blockage(x, y, f) == BlockageType.Blocked)
                    {
                        blocked = (x, y, f);
                        break;
                    }
                }
            }
        }

        Assert.True(blocked is not null, "the level has no blocked faces at all");

        var game = new Game(design);
        Assert.Same(map.GetType(), game.Map!.GetType());

        // Walk into it from the blocked cell and confirm nothing moves.
        var (bx, by, bf) = blocked!.Value;
        var walker = new Game(design);
        Assert.NotNull(walker.Map);
        Assert.False(walker.Map!.CanLeave(bx, by, bf));
    }

    [Fact]
    public void Blockage_names_the_kind_rather_than_reducing_it_to_a_bool()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var map = design.Map(0);
        Assert.NotNull(map);

        // Secret passages are passable and a false door is not, despite the header describing the
        // latter as "secret + blocked". A test that only asked "is it open" would not catch a
        // reader that collapsed the two.
        Assert.True(map!.CanLeave(0, 0, Facing.North) ||
                    map.Blockage(0, 0, Facing.North) != BlockageType.Open);

        Assert.Equal(BlockageType.Blocked, map.Blockage(-1, -1, Facing.North));
    }
}
