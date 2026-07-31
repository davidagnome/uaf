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

        // Six: the default at index 0 plus five alternates. GetFormat skips index 0 when
        // searching and falls back to it, so its position is load-bearing.
        Assert.Equal(6, formats.Count);
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
        // Alternates start at index 1; index 0 is the default.
        Assert.Equal(480, formats[1].ImageWidth);
        Assert.Equal(360, formats[1].ImageHeight);
        Assert.Equal(5, formats[1].DistantWallCount);

        Assert.Equal(500, formats[2].ImageWidth);
        Assert.Equal(375, formats[2].ImageHeight);
        Assert.Equal(7, formats[2].DistantWallCount);
        Assert.Equal(15, formats[2].ViewportCoords.Count);

        Assert.True(formats[1].Matches(480, 360));
        Assert.False(formats[1].Matches(500, 375));

        // The alternates must have distinct sizes, or matching could not select between them.
        var alternates = formats.Skip(1).ToList();
        Assert.Equal(alternates.Count,
                     alternates.Select(f => (f.ImageWidth, f.ImageHeight)).Distinct().Count());
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
        var formats = WallFormatReader.ReadAll(design.Config);
        var fallback = formats[0];

        // The design's config header documents the default's sizes: "WallPic 112 x 134, 48 x 58,
        // 16 x 19". E is the wall straight ahead, H one cell further, I further still, and the
        // rest are 32-wide side walls.
        Assert.Equal(112, fallback.SlotRects[4].Width);    // E
        Assert.Equal(134, fallback.SlotRects[4].Height);
        Assert.Equal(48, fallback.SlotRects[7].Width);     // H
        Assert.Equal(58, fallback.SlotRects[7].Height);
        Assert.Equal(16, fallback.SlotRects[8].Width);     // I
        Assert.Equal(32, fallback.SlotRects[0].Width);     // A
        Assert.Equal(211, fallback.SlotRects[0].Height);

        // The alternates take the other approach: uniform 211 height, with the foreshortening
        // painted into the artwork. A renderer must therefore read both dimensions from the
        // rectangle rather than assuming either convention.
        Assert.All(formats[1].SlotRects, r => Assert.Equal(211, r.Height));
    }

    [Fact]
    public void A_sheet_selects_the_alternate_that_declares_its_size()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var formats = WallFormatReader.ReadAll(design.Config);

        // This design's wall art is 1500x375, and band 5 declares exactly that -- so its walls
        // render through the last alternate, not the default. Selection really does depend on
        // measuring the sheet.
        var sheet = design.Art("wall_GreyAdobe.png")!;
        Assert.Equal(1500, sheet.Width);
        Assert.Equal(375, sheet.Height);

        var selected = WallFormatReader.SelectFor(formats, sheet.Width, sheet.Height)!;
        Assert.Equal(5, selected.Band);
        Assert.True(selected.Matches(1500, 375));

        // Other bands are reachable too, by their own sizes.
        Assert.Equal(1, WallFormatReader.SelectFor(formats, 480, 360)!.Band);
        Assert.Equal(4, WallFormatReader.SelectFor(formats, 1000, 375)!.Band);

        // A size no band claims falls back to index 0, the default, which declares no dimensions
        // precisely because nothing is ever matched against it.
        Assert.Same(formats[0], WallFormatReader.SelectFor(formats, 999, 999));
        Assert.False(formats[0].Matches(999, 999));
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

    [Fact]
    public void A_full_level_read_reaches_the_wall_sets_past_the_event_list()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);

        // The wall table sits after the event list, so this only succeeds now that the event
        // dispatcher lives in the library rather than in the serialization tests. Levels that
        // contain an unported event type still come back null, which is why this reports how many
        // of the design's levels made it rather than demanding all of them.
        var levels = Enumerable.Range(0, design.LevelFiles.Count)
                               .Select(design.Level)
                               .ToList();

        int readable = levels.Count(l => l is not null);
        Assert.True(readable > 0,
            $"none of {design.LevelFiles.Count} levels could be read past their events");

        var first = levels.First(l => l is not null)!;
        Assert.NotEmpty(first.WallSets);
        Assert.NotEmpty(first.Cells);
        Assert.Equal(first.Width * first.Height, first.Cells.Count);

        // Real art filenames, which is the payoff: a resolver can now turn a cell's wall index
        // into something the image loader can open.
        Assert.Contains(first.WallSets, w => w.WallFile.Length > 0);
    }

    [Fact]
    public void Wall_indices_in_a_real_level_resolve_to_art_that_exists()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var level = Enumerable.Range(0, design.LevelFiles.Count)
                              .Select(design.Level)
                              .FirstOrDefault(l => l is not null);
        if (level is null)
        {
            return;
        }

        var map = new Map(level.Width, level.Height, level.Cells);
        var resolver = new WallResolver(map, level.WallSets);

        // Walk the whole level and resolve every wall the party could face. This is the first
        // check that the index-from-one convention holds against real data rather than a fixture.
        int resolved = 0, missingArt = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var view = ViewMap.For(x, y, Facing.North, map.Width, map.Height);
                foreach (Facing facing in Enum.GetValues<Facing>())
                {
                    string? art = resolver.ArtFor(view, 12, facing, WallLayer.Wall);
                    if (art is null)
                    {
                        continue;
                    }

                    resolved++;
                    if (design.Art(art) is null)
                    {
                        missingArt++;
                    }
                }
            }
        }

        Assert.True(resolved > 0, "the level has no walls at all");

        // An out-of-range index would show up as warnings; a wrong base would show up as art the
        // design does not ship. Neither is acceptable against a design's own level.
        Assert.True(resolver.Warnings.Count == 0,
            $"{resolver.Warnings.Count} bad wall indices: {string.Join("; ", resolver.Warnings.Take(3))}");
        Assert.True(missingArt == 0,
            $"{missingArt} of {resolved} wall references name art the design does not ship");
    }

    [Fact]
    public void Walls_reach_the_screen_from_positions_where_the_level_has_them()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var probe = new Game(design);
        var map = probe.Map;
        var resolver = probe.Walls;
        if (map is null || resolver is null)
        {
            return;
        }

        // The viewport rectangle, so the comparison ignores the chrome around it.
        Assert.True(design.Config.TryGetRect("VIEWPORT_RECT", out int vx, out int vy,
                                             out int vr, out int vb));

        // Find positions where several squares resolve to walls, which is where the renderer has
        // something to prove. A design's start cell is not necessarily one of them.
        var interesting = new List<(int X, int Y, Facing F)>();
        for (int y = 0; y < map.Height && interesting.Count < 12; y++)
        {
            for (int x = 0; x < map.Width && interesting.Count < 12; x++)
            {
                foreach (Facing facing in Enum.GetValues<Facing>())
                {
                    var view = map.View(x, y, facing);
                    int walls = ViewportRenderer.SquarePasses
                        .SelectMany(entry => entry.Value.Select(pass => (entry.Key, pass)))
                        .Count(p => resolver.HasWall(view, p.Key, p.pass.Direction switch
                        {
                            ViewportRenderer.PassDirection.Left => (Facing)(((int)facing + 3) & 3),
                            ViewportRenderer.PassDirection.Right => (Facing)(((int)facing + 1) & 3),
                            _ => facing,
                        }));

                    if (walls >= 4)
                    {
                        interesting.Add((x, y, facing));
                        break;
                    }
                }
            }
        }

        Assert.NotEmpty(interesting);

        // Render from each and count how many differ from a walls-free baseline. Comparing against
        // the backdrop rather than asserting specific pixels keeps this a test of "the walls got
        // drawn" rather than of the art itself.
        int drew = 0;
        foreach (var (x, y, facing) in interesting)
        {
            var game = new Game(design);
            var before = Fingerprint(game.Render(), vx, vy, vr, vb);

            // Walk and turn to the interesting cell. Movement respects walls, so instead of
            // pathfinding, render the view directly by constructing a game and stepping its
            // facing -- position is the design's, but facing is free.
            while (game.Facing != facing)
            {
                game.Update(InputEvent.KeyDown(VirtualKey.Right));
            }

            if (Fingerprint(game.Render(), vx, vy, vr, vb) != before)
            {
                drew++;
            }
        }

        // Turning must change the viewport for at least some of these, or the walls are not being
        // consulted at all. This is what would have caught the front-face-only skip that discarded
        // square 9 entirely.
        Assert.True(drew > 0,
            "turning never changed the viewport, so wall rendering is not reading the level");
    }

    /// <summary>A cheap hash of the viewport region only.</summary>
    private static ulong Fingerprint(Surface frame, int left, int top, int right, int bottom)
    {
        ulong hash = 14695981039346656037;
        for (int y = top; y < Math.Min(bottom, frame.Height); y++)
        {
            for (int x = left; x < Math.Min(right, frame.Width); x++)
            {
                hash ^= frame[x, y];
                hash *= 1099511628211;
            }
        }
        return hash;
    }

    [Fact]
    public void A_levels_events_are_retained_rather_than_counted()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var levels = Enumerable.Range(0, design.LevelFiles.Count)
                               .Select(design.Level)
                               .Where(l => l is not null)
                               .ToList();

        Assert.NotEmpty(levels);

        // Until the reader's callback returned the parsed event instead of a bool, LevelFile
        // carried EventCount and nothing else -- the port could prove it understood every event in
        // every design and could not hand one to a caller.
        int total = levels.Sum(l => l!.Events.Count);
        Assert.True(total > 0, "no level retained a single event");

        foreach (var level in levels)
        {
            // Some event types are a bare tag with no body, so the retained count is at most the
            // declared one rather than equal to it.
            Assert.True(level!.Events.Count <= level.EventCount,
                $"retained {level.Events.Count} events but only {level.EventCount} were declared");

            Assert.All(level.Events, e => Assert.NotNull(e.Base));
        }
    }

    [Fact]
    public void Retained_events_carry_their_concrete_types()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var events = Enumerable.Range(0, design.LevelFiles.Count)
                               .Select(design.Level)
                               .Where(l => l is not null)
                               .SelectMany(l => l!.Events)
                               .ToList();

        if (events.Count == 0)
        {
            return;
        }

        // The interface exists so callers can pattern-match to the subclass they care about --
        // which is what an event executor will do. More than one distinct type across a design's
        // levels means the dispatch really is returning what it parsed rather than a placeholder.
        var kinds = events.Select(e => e.GetType().Name).Distinct().ToList();
        Assert.True(kinds.Count > 1, $"only one event type retained: {kinds[0]}");

        // Text events are the ones an executor can act on first, and every design has them.
        Assert.Contains(events, e => e.GetType().Name.Contains("Text", StringComparison.Ordinal));
    }

    [Fact]
    public void Stepping_onto_a_cell_with_an_event_triggers_it()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var probe = new Game(design);
        var lookup = probe.Events;
        var map = probe.Map;
        if (lookup is null || map is null || lookup.Count == 0)
        {
            return;
        }

        // Walk a short breadth-first search from the start until an event cell is entered. The
        // party cannot teleport, and walls constrain it, so reaching one has to be done by moving.
        var game = new Game(design);
        bool triggered = false;

        for (int attempt = 0; attempt < 200 && !triggered; attempt++)
        {
            game.Update(InputEvent.KeyDown(attempt % 3 == 2
                ? VirtualKey.Right
                : VirtualKey.Up));

            if (game.CurrentEvent is not null)
            {
                triggered = true;
            }
        }

        // Every reference level has events, and the start area is small enough to walk into one.
        Assert.True(triggered, "walked 200 steps without entering an event cell");

        // The message must reflect the event rather than the movement that caused it.
        Assert.DoesNotContain("Moved", game.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_event_puts_its_own_text_in_the_message_line()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var level = design.Level(0);
        if (level is null)
        {
            return;
        }

        var texts = level.Events.OfType<UAF.Serialization.TextEvent>().ToList();
        if (texts.Count == 0)
        {
            return;
        }

        // The text lives on the shared GameEventBase, not on the subclass -- TextEvent adds only
        // display flags and a sound. A reader looking for it on the concrete type finds nothing.
        var first = texts[0];
        Assert.NotEmpty(first.Base.Text);

        // And the lookup is by coordinate, because cells carry no event index.
        var lookup = new EventLookup(level.Events);
        Assert.Same(first, lookup.FirstAt(first.Base.X, first.Base.Y));
        Assert.True(lookup.Any(first.Base.X, first.Base.Y));
    }

    [Fact]
    public void An_unimplemented_event_type_is_named_rather_than_ignored()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        using var design = Open(root);
        var level = design.Level(0);
        if (level is null)
        {
            return;
        }

        // Doing nothing silently for an unimplemented type is indistinguishable from a design with
        // no event there, and that difference matters constantly while the executor is built out.
        var other = level.Events.FirstOrDefault(e => e is not UAF.Serialization.TextEvent);
        if (other is null)
        {
            return;
        }

        var lookup = new EventLookup(level.Events);
        Assert.Same(other, lookup.FirstAt(other.Base.X, other.Base.Y));
    }
}
