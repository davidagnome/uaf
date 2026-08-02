using UAF.Media;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the combat map renderer (<c>displayCombatWalls</c>, <c>Drawtile.cpp:3085</c>).
/// </summary>
public class CombatRendererTests
{
    private static CombatMap FilledMap(int size = 25)
    {
        var map = new CombatMap(size, size);
        map.FillHoles();
        return map;
    }

    /// <summary>A sheet big enough for the whole dungeon tile table, in a flat colour.</summary>
    private static Surface Sheet()
    {
        var sheet = new Surface(608, 176);
        for (int y = 0; y < sheet.Height; y++)
        {
            for (int x = 0; x < sheet.Width; x++)
            {
                sheet[x, y] = 0xFF203040;
            }
        }
        return sheet;
    }

    [Fact]
    public void A_square_maps_to_the_screen_by_tile_size_and_origin()
    {
        var renderer = new CombatRenderer { OriginX = 14, OriginY = 16 };

        Assert.Equal((14, 16), renderer.ToScreen(0, 0));
        Assert.Equal((14 + 48, 16 + 48), renderer.ToScreen(1, 1));

        renderer.ScrollX = 3;
        renderer.ScrollY = 2;
        Assert.Equal((14, 16), renderer.ToScreen(3, 2));
        Assert.Equal((14 - 48, 16 - 48), renderer.ToScreen(2, 1));
    }

    [Fact]
    public void The_screen_maps_back_to_a_square()
    {
        var map = FilledMap();
        var renderer = new CombatRenderer { ScrollX = 5, ScrollY = 4 };

        Assert.Equal((5, 4), renderer.FromScreen(map, 14, 16));
        Assert.Equal((6, 5), renderer.FromScreen(map, 14 + 48, 16 + 48));

        // Round-trips for every square in a small window.
        for (int ty = 5; ty < 10; ty++)
        {
            for (int tx = 5; tx < 10; tx++)
            {
                var (sx, sy) = renderer.ToScreen(tx, ty);
                Assert.Equal((tx, ty), renderer.FromScreen(map, sx + 1, sy + 1));
            }
        }
    }

    [Fact]
    public void Scrolling_centres_on_a_square_that_is_out_of_view()
    {
        var map = FilledMap(40);
        var renderer = new CombatRenderer();

        renderer.EnsureVisible(map, 30, 30, tilesAcross: 10, tilesDown: 8);

        Assert.InRange(30 - renderer.ScrollX, 0, 9);
        Assert.InRange(30 - renderer.ScrollY, 0, 7);
    }

    [Fact]
    public void A_square_already_in_view_does_not_move_the_scroll()
    {
        var map = FilledMap(40);
        var renderer = new CombatRenderer { ScrollX = 10, ScrollY = 10 };

        renderer.EnsureVisible(map, 12, 12, tilesAcross: 10, tilesDown: 8);

        Assert.Equal((10, 10), (renderer.ScrollX, renderer.ScrollY));
    }

    [Fact]
    public void Scrolling_never_runs_off_the_map()
    {
        var map = FilledMap(25);
        var renderer = new CombatRenderer();

        renderer.EnsureVisible(map, 0, 0, tilesAcross: 10, tilesDown: 8);
        Assert.Equal((0, 0), (renderer.ScrollX, renderer.ScrollY));

        renderer.EnsureVisible(map, 24, 24, tilesAcross: 10, tilesDown: 8);
        Assert.Equal((15, 17), (renderer.ScrollX, renderer.ScrollY));   // width - across
    }

    [Fact]
    public void The_terrain_is_drawn_into_the_area_and_nowhere_else()
    {
        var map = FilledMap();
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(14, 16, 14 + (10 * 48), 16 + (8 * 48));

        new CombatRenderer().DrawTerrain(screen, map, Sheet(), area);

        // Inside: drawn. Outside: untouched.
        Assert.NotEqual(0u, screen[area.Left + 5, area.Top + 5]);
        Assert.NotEqual(0u, screen[area.Right - 5, area.Bottom - 5]);
        Assert.Equal(0u, screen[area.Left - 5, area.Top - 5]);
        Assert.Equal(0u, screen[area.Right + 5, area.Top + 5]);
        Assert.Equal(0u, screen[area.Left + 5, area.Bottom + 5]);
    }

    [Fact]
    public void Every_square_of_the_visible_area_gets_covered()
    {
        // A gap between tiles would show as an unpainted column or row, which is exactly the kind
        // of defect that only shows up when you look at a frame.
        var map = FilledMap();
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(14, 16, 14 + (10 * 48), 16 + (8 * 48));

        new CombatRenderer().DrawTerrain(screen, map, Sheet(), area);

        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                Assert.True(screen[x, y] != 0, $"({x},{y}) was never painted");
            }
        }
    }

    [Fact]
    public void A_scrolled_map_still_covers_the_whole_area()
    {
        // The reference draws two squares beyond the view on every side so a part-scrolled edge
        // has something under it rather than a gap.
        var map = FilledMap(40);
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(14, 16, 14 + (10 * 48), 16 + (8 * 48));
        var renderer = new CombatRenderer { ScrollX = 7, ScrollY = 5 };

        renderer.DrawTerrain(screen, map, Sheet(), area);

        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                Assert.True(screen[x, y] != 0, $"({x},{y}) was never painted");
            }
        }
    }

    [Fact]
    public void Combatants_are_drawn_where_they_stand()
    {
        var map = FilledMap();
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(14, 16, 14 + (10 * 48), 16 + (8 * 48));

        var icon = new Surface(48, 48);
        for (int y = 0; y < 48; y++)
        {
            for (int x = 0; x < 48; x++)
            {
                icon[x, y] = 0xFF00FF00;
            }
        }

        var fighter = new Combatant(0, true, new CombatantIcon(1, 1), "f") { X = 3, Y = 2 };
        var renderer = new CombatRenderer();

        renderer.DrawCombatants(screen, [fighter],
                                _ => (icon, new SurfaceRect(0, 0, 48, 48)), area);

        var (sx, sy) = renderer.ToScreen(3, 2);
        Assert.Equal(0xFF00FF00u, screen[sx + 24, sy + 24]);
    }

    [Fact]
    public void A_combatant_off_the_map_is_skipped()
    {
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(14, 16, 200, 200);
        var gone = new Combatant(0, true, new CombatantIcon(1, 1), "g") { X = -1, Y = -1 };

        new CombatRenderer().DrawCombatants(screen, [gone],
                                            _ => throw new InvalidOperationException("drawn"),
                                            area);

        Assert.Equal(0u, screen[100, 100]);
    }

    [Fact]
    public void The_dying_are_drawn_before_the_living()
    {
        // The grid keeps two occupancy layers for exactly this: a combatant standing on a corpse
        // hides it, not the other way round.
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(14, 16, 14 + (10 * 48), 16 + (8 * 48));

        Surface Solid(uint colour)
        {
            var s = new Surface(48, 48);
            for (int y = 0; y < 48; y++)
            {
                for (int x = 0; x < 48; x++)
                {
                    s[x, y] = colour;
                }
            }
            return s;
        }

        var corpseArt = Solid(0xFFFF0000);
        var livingArt = Solid(0xFF00FF00);

        var corpse = new Combatant(0, true, new CombatantIcon(1, 1), "dead")
        {
            X = 2, Y = 2, Status = CharacterStatus.Dead,
        };
        var living = new Combatant(1, false, new CombatantIcon(1, 1), "live") { X = 2, Y = 2 };

        var renderer = new CombatRenderer();
        renderer.DrawCombatants(screen, [living, corpse],
                                c => c.IsOnCombatMap() ? (livingArt, new SurfaceRect(0, 0, 48, 48))
                                                       : (corpseArt, new SurfaceRect(0, 0, 48, 48)),
                                area);

        var (sx, sy) = renderer.ToScreen(2, 2);
        Assert.Equal(0xFF00FF00u, screen[sx + 24, sy + 24]);
    }

    // ---- the targeting cursor ---------------------------------------------------------------

    private static Surface Cursor()
    {
        var c = new Surface(48, 48);
        for (int y = 0; y < 48; y++)
        {
            for (int x = 0; x < 48; x++)
            {
                // Top-left is the colour key, so leave it distinct from the body.
                c[x, y] = (x, y) == (0, 0) ? 0xFF000000 : 0xFFF0A44A;
            }
        }
        return c;
    }

    [Fact]
    public void The_cursor_marks_the_square_it_sits_on()
    {
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(48, 54, 48 + (3 * 48), 54 + (4 * 48));
        var renderer = new CombatRenderer { OriginX = area.Left, OriginY = area.Top };

        renderer.DrawCursor(screen, Cursor(), 1, 1, area);

        var (sx, sy) = renderer.ToScreen(1, 1);
        Assert.NotEqual(0u, screen[sx + 24, sy + 24]);
        Assert.Equal(0u, screen[sx - 10, sy + 24]);      // nothing bled into the neighbour
    }

    [Fact]
    public void The_cursor_covers_every_square_of_a_large_combatant()
    {
        // coverFullIcon: a two-square monster is highlighted across its whole footprint, which is
        // how a player can see what is actually being targeted.
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(48, 54, 48 + (5 * 48), 54 + (4 * 48));
        var renderer = new CombatRenderer { OriginX = area.Left, OriginY = area.Top };

        var tiger = new Combatant(0, false, new CombatantIcon(2, 1), "Tiger") { X = 1, Y = 1 };
        renderer.DrawCursor(screen, Cursor(), tiger.X, tiger.Y, area, over: tiger);

        var (ax, ay) = renderer.ToScreen(1, 1);
        var (bx, by) = renderer.ToScreen(2, 1);
        Assert.NotEqual(0u, screen[ax + 24, ay + 24]);
        Assert.NotEqual(0u, screen[bx + 24, by + 24]);
    }

    [Fact]
    public void A_cursor_that_would_overhang_the_view_is_dropped_whole()
    {
        // The reference tests the full 48x48 against the view's edges and draws nothing rather
        // than clipping -- which is exactly what hid the cursor when the renderer's origin was
        // left at the C++ combat screen's (14,16) instead of the viewport's own corner.
        var screen = new Surface(640, 480);
        var area = new SurfaceRect(48, 54, 48 + (3 * 48), 54 + (4 * 48));
        var renderer = new CombatRenderer { OriginX = area.Left, OriginY = area.Top };

        renderer.DrawCursor(screen, Cursor(), 3, 1, area);       // one square past the right edge

        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                Assert.Equal(0u, screen[x, y]);
            }
        }
    }

    [Fact]
    public void The_origin_places_the_scroll_corner_at_the_views_corner()
    {
        // Terrain drawn from the wrong origin is clipped rather than aligned, so it looks almost
        // right and is not. Combat is drawn in the dungeon viewport here, not on its own screen.
        var renderer = new CombatRenderer { OriginX = 48, OriginY = 54, ScrollX = 21, ScrollY = 22 };

        Assert.Equal((48, 54), renderer.ToScreen(21, 22));
    }

    [Fact]
    public void A_real_encounter_renders_with_the_zones_own_art()
    {
        string? root = ReferenceDesign();
        if (root is null)
        {
            return;
        }

        using var design = LoadedDesign.Open(root);
        var level = design.Level(0);
        var levelMap = design.Map(0);
        if (level is null || levelMap is null)
        {
            return;
        }

        // The zone names the combat sheet, not the design.
        var cell = levelMap.At(1, 2)!;
        var zone = level.Zones.Zones[cell.Zone];
        Assert.False(string.IsNullOrEmpty(zone.IndoorCombatArt));

        var setup = CombatSetup.Begin(levelMap, level.WallSets, 1, 2, Facing.North,
            [.. Enumerable.Range(0, 4).Select(i =>
                new Combatant(i, i < 2, new CombatantIcon(1, 1), $"c{i}"))]);

        var screen = new Surface(640, 480);
        var area = new SurfaceRect(14, 16, 14 + (10 * 48), 16 + (8 * 48));
        var renderer = new CombatRenderer();
        renderer.EnsureVisible(setup.Map, setup.PartyX, setup.PartyY, 10, 8);

        // Art loading needs a decoder, which this fixture does not have; the geometry is still
        // worth asserting, and the drawn frame is covered above.
        Assert.InRange(setup.PartyX - renderer.ScrollX, 0, 9);
        Assert.InRange(setup.PartyY - renderer.ScrollY, 0, 7);
    }

    private static string? ReferenceDesign()
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
}
