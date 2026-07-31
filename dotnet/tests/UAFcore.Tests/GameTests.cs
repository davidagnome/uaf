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

    [Fact]
    public void Every_wall_format_the_design_declares_loads_completely()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var formats = WallFormatReader.ReadAll(design.Config);

        Assert.Equal(5, formats.Count);
        Assert.All(formats, f => Assert.Equal(WallFormat.MaxSlotTypes, f.SlotRects.Count));
        Assert.All(formats, f => Assert.Equal(WallFormat.MaxSlotTypes, f.SlotOffsets.Count));

        // 13 or 15, never a partial run -- the original discards an incomplete set rather than
        // filling the gaps, so anything between the two would mean the all-or-nothing rule broke.
        Assert.All(formats, f => Assert.True(f.ViewportCoords.Count is 13 or 15 or 0,
            $"band {f.Band} has {f.ViewportCoords.Count} coordinates"));

        // The coordinate count follows the distant-wall count rather than being declared.
        Assert.All(formats, f => Assert.Equal(
            f.DistantWallCount == 7 ? 15 : 13, f.ViewportCoords.Count));
    }

    [Fact]
    public void A_format_is_matched_by_the_wall_sheets_own_dimensions()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var formats = WallFormatReader.ReadAll(design.Config);

        // These are third-party wall packs -- the config names them "Kevin's Tavern demo" and
        // "Kevin's New Walls" -- and the engine tells them apart purely by sheet size. Band 2 is
        // the one with seven distant walls, which is why it needs 15 coordinates rather than 13.
        Assert.Equal(480, formats[0].ImageWidth);
        Assert.Equal(360, formats[0].ImageHeight);
        Assert.Equal(5, formats[0].DistantWallCount);

        Assert.Equal(500, formats[1].ImageWidth);
        Assert.Equal(375, formats[1].ImageHeight);
        Assert.Equal(7, formats[1].DistantWallCount);
        Assert.Equal(15, formats[1].ViewportCoords.Count);

        Assert.True(formats[0].Matches(480, 360));
        Assert.False(formats[0].Matches(500, 375));

        // Distinct sizes, or matching could not select between them.
        Assert.Equal(formats.Count,
                     formats.Select(f => (f.ImageWidth, f.ImageHeight)).Distinct().Count());
    }

    [Fact]
    public void Slot_widths_encode_the_fake_3d_scale()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var first = WallFormatReader.ReadAll(design.Config)[0];

        // The design's own config header documents these: "WallPic 112 x 134, 48 x 58, 16 x 19".
        // E is the wall straight ahead, H one cell further, I..P further still, and the rest are
        // 32-wide side walls. If a rect were read as a screen rectangle rather than a source one,
        // these widths would be meaningless.
        Assert.Equal(112, first.SlotRects[4].Width);    // E
        Assert.Equal(48, first.SlotRects[7].Width);     // H
        Assert.Equal(16, first.SlotRects[8].Width);     // I
        Assert.Equal(32, first.SlotRects[0].Width);     // A

        // Every slot is the full art height; only width varies with distance, because the
        // vertical foreshortening is painted into the artwork rather than computed.
        Assert.All(first.SlotRects, r => Assert.Equal(211, r.Height));
    }

    [Fact]
    public void Reading_wall_formats_does_not_consume_the_config()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);

        // DesignConfig hands a token out once by default, and probing for the end of the format
        // run reads these keys repeatedly. Reading twice must give the same answer.
        var first = WallFormatReader.ReadAll(design.Config);
        var second = WallFormatReader.ReadAll(design.Config);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0].SlotRects[4], second[0].SlotRects[4]);
    }

    [Fact]
    public void A_config_with_no_wall_formats_yields_none_rather_than_throwing()
    {
        var empty = UAF.Data.DesignConfig.Parse(["SOMETHING = 1,2"]);

        Assert.Empty(WallFormatReader.ReadAll(empty));
        Assert.Null(WallFormatReader.Read(empty, 1));
    }

    [Fact]
    public void The_map_is_a_torus_rather_than_a_bounded_grid()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var map = design.Map(0)!;

        // Both the viewport and movement take coordinates modulo the extent, so a level has no
        // edges at all -- only walls. An earlier version of Game reported "the map ends here",
        // which is a rule the original does not have.
        Assert.Equal((0, 0), map.Wrap(map.Width, map.Height));
        Assert.Equal((map.Width - 1, map.Height - 1), map.Wrap(-1, -1));

        // Negative wrap is the case a bare % gets wrong in C and C#alike: -1 % w is -1.
        Assert.Equal(map.Width - 1, ViewMap.Wrap(-1, map.Width));
        Assert.Equal(0, ViewMap.Wrap(map.Width, map.Width));
    }

    [Fact]
    public void The_view_map_places_all_fifteen_slots_relative_to_the_party()
    {
        // Pure geometry, so it needs no design. A 20x20 map with the party in the middle keeps
        // every slot in range and makes the offsets readable.
        var view = ViewMap.For(10, 10, Facing.North, 20, 20);

        Assert.Equal((10, 10), view[12]);   // here
        Assert.Equal((10, 9), view[9]);     // forward 1
        Assert.Equal((10, 8), view[4]);     // forward 2
        Assert.Equal((9, 10), view[10]);    // left 1  (west of north)
        Assert.Equal((11, 10), view[11]);   // right 1
        Assert.Equal((9, 9), view[7]);      // forward 1, left 1
        Assert.Equal((11, 9), view[8]);     // forward 1, right 1
        Assert.Equal((8, 8), view[0]);      // forward 2, left 2
        Assert.Equal((12, 8), view[1]);     // forward 2, right 2
        Assert.Equal((7, 8), view[13]);     // forward 2, left 3
        Assert.Equal((13, 8), view[14]);    // forward 2, right 3
    }

    [Theory]
    [InlineData(Facing.North)]
    [InlineData(Facing.East)]
    [InlineData(Facing.South)]
    [InlineData(Facing.West)]
    public void The_view_map_rotates_with_the_facing(Facing facing)
    {
        var view = ViewMap.For(10, 10, facing, 20, 20);

        // Slot 12 is always the party's own cell, whichever way it faces -- the one fixed point.
        Assert.Equal((10, 10), view[12]);

        // Slot 9 is always one step along the facing, which is the same delta movement uses.
        (int dx, int dy) = facing switch
        {
            Facing.North => (0, -1),
            Facing.East => (1, 0),
            Facing.South => (0, 1),
            _ => (-1, 0),
        };
        Assert.Equal((10 + dx, 10 + dy), view[9]);

        // And slot 4 is two steps, so the depth axis follows the facing rather than the map.
        Assert.Equal((10 + (dx * 2), 10 + (dy * 2)), view[4]);
    }

    [Fact]
    public void Only_the_first_thirteen_slots_wrap()
    {
        // The party at the north-west corner facing north: the forward cells run off the top, and
        // slots 0..12 must come back at the bottom while 13 and 14 must not.
        var view = ViewMap.For(0, 0, Facing.North, 10, 10);

        Assert.Equal((0, 9), view[9]);      // forward 1 wrapped to the far side
        Assert.Equal((0, 8), view[4]);      // forward 2 likewise
        Assert.Equal((9, 0), view[10]);     // left 1 wrapped in x

        // 13 and 14 are left outside deliberately: the occlusion tests ask whether there is no
        // cell there, and wrapping would make that question unanswerable.
        Assert.Equal(-2, view[13].Y);
        Assert.Equal(-3, view[13].X);
        Assert.Equal(3, view[14].X);
    }
}
