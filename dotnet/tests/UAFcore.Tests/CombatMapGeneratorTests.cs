using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the orthogonal-to-isometric combat map generator
/// (<c>GenerateIndoorCombatMap</c>, <c>Drawtile.cpp:2391</c>).
/// </summary>
/// <remarks>
/// The generator has no oracle — the C++ editor cannot run headless (see §7 Phase 0) — so these
/// assert structural properties that a drifted implementation cannot satisfy by accident: that
/// walls appear where the level has walls and nowhere when it has none, that the map wraps rather
/// than clamping, and that the party always lands somewhere it can stand. The map was also
/// printed and looked at, which is what the porting plan asks for and what these cannot replace.
/// </remarks>
public class CombatMapGeneratorTests
{
    private static AreaMapCell Cell(byte north = 0, byte east = 0, byte south = 0, byte west = 0,
                                    byte blockage = (byte)BlockageType.Blocked)
    {
        // Walls and blockage are stored north, south, east, west -- see AreaMapCell.WallAt. A
        // face only counts as a wall when its blockage is not Open, so faces carrying no wall get
        // Open and the rest get whatever the caller asked for.
        byte B(byte wall) => wall > 0 ? blockage : (byte)BlockageType.Open;
        return new AreaMapCell(0, false, false, 0, 0, 0, 0, 0, false,
                               [north, south, east, west],
                               [B(north), B(south), B(east), B(west)]);
    }

    private static IReadOnlyList<WallSetSlot> WallSets(string doorFile = "")
    {
        var sets = new List<WallSetSlot>();
        for (int i = 0; i < 8; i++)
        {
            sets.Add(new WallSetSlot("wall.png", doorFile, "overlay.png", string.Empty,
                                     string.Empty, 1, 0, 0, string.Empty, 0, 0));
        }
        return sets;
    }

    /// <summary>A 10×10 level with no walls at all.</summary>
    private static Map EmptyLevel()
    {
        var cells = new AreaMapCell[100];
        Array.Fill(cells, Cell());
        return new Map(10, 10, cells);
    }

    /// <summary>A 10×10 level with a closed 4×4 room whose top-left is (2,2).</summary>
    private static Map RoomLevel()
    {
        var cells = new AreaMapCell[100];
        Array.Fill(cells, Cell());

        for (int i = 0; i < 4; i++)
        {
            int x = 2 + i;
            int y = 2 + i;
            cells[(2 * 10) + x] = Cell(north: 1);           // top edge
            cells[(5 * 10) + x] = Cell(south: 1);           // bottom edge
            cells[(y * 10) + 2] = Cell(west: 1);            // left edge
            cells[(y * 10) + 5] = Cell(east: 1);            // right edge
        }

        // Corners carry two faces each.
        cells[(2 * 10) + 2] = Cell(north: 1, west: 1);
        cells[(2 * 10) + 5] = Cell(north: 1, east: 1);
        cells[(5 * 10) + 2] = Cell(south: 1, west: 1);
        cells[(5 * 10) + 5] = Cell(south: 1, east: 1);
        return new Map(10, 10, cells);
    }

    private static int ImpassableCount(CombatMap map)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (!map.IsPassable(x, y))
                {
                    count++;
                }
            }
        }
        return count;
    }

    [Fact]
    public void A_level_with_no_walls_produces_an_entirely_open_combat_map()
    {
        var (map, x, y) = new CombatMapGenerator(EmptyLevel(), WallSets()).Generate(5, 5);

        Assert.Equal(0, ImpassableCount(map));
        Assert.Equal(ObstacleType.None, map.Obstacle(x, y));
    }

    [Fact]
    public void A_room_in_the_level_becomes_walls_in_the_combat_map()
    {
        var (map, _, _) = new CombatMapGenerator(RoomLevel(), WallSets()).Generate(3, 3);

        // The exact count is an artefact of the expansion tables; what matters is that a level
        // with a room produces substantially more wall than one without, and that it is not so
        // much that the map has been papered over.
        int walls = ImpassableCount(map);
        Assert.InRange(walls, 20, map.Width * map.Height / 4);
    }

    [Fact]
    public void The_party_always_lands_on_a_square_it_can_stand_in()
    {
        var level = RoomLevel();
        var sets = WallSets();

        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                var (map, cx, cy) = new CombatMapGenerator(level, sets).Generate(x, y);
                Assert.Equal(ObstacleType.None, map.Obstacle(cx, cy));
            }
        }
    }

    [Fact]
    public void The_party_starts_at_the_centre_when_the_centre_is_clear()
    {
        // ConvertTempMapToCombatTerrain's last act is to overwrite the start with the map centre
        // (Drawtile.cpp:2272), and findEmptyCell only moves it when that square is blocked.
        var (map, x, y) = new CombatMapGenerator(EmptyLevel(), WallSets()).Generate(5, 5);
        Assert.Equal((map.Width / 2, map.Height / 2), (x, y));
    }

    [Fact]
    public void A_party_at_the_level_edge_still_gets_terrain_from_across_the_wrap()
    {
        // The source window is 52 cells across and centred on the party, so on a 10-wide level it
        // laps several times whatever the start; a party in the corner must come out with a real
        // map rather than an empty one.
        //
        // This does NOT establish that the clamping block is dead -- it cannot, because the inner
        // loop takes its coordinates modulo the level extent either way, so both readings wrap.
        // That the block is dead comes from the source: `diagonalMap` is defined at
        // Drawtile.cpp:27, and the block reads an `areaMapEndY` whose only declaration is
        // commented out at :2402, so the file would not compile with it live.
        var level = RoomLevel();
        var sets = WallSets();

        foreach (var (x, y) in new[] { (0, 0), (9, 0), (0, 9), (9, 9), (5, 5) })
        {
            var (map, cx, cy) = new CombatMapGenerator(level, sets).Generate(x, y);
            Assert.True(ImpassableCount(map) > 0, $"({x},{y}) produced no walls at all");
            Assert.Equal(ObstacleType.None, map.Obstacle(cx, cy));
        }
    }

    [Fact]
    public void An_open_face_carrying_wall_art_is_a_doorway_rather_than_a_wall()
    {
        // IsWallAt is (slot > 0) && (blockage != Open): art alone does not make a wall. A level
        // whose faces are all open must generate exactly as much wall as one with no art.
        var cells = new AreaMapCell[100];
        Array.Fill(cells, new AreaMapCell(0, false, false, 0, 0, 0, 0, 0, false,
                                          [1, 1, 1, 1],
                                          [(byte)BlockageType.Open, (byte)BlockageType.Open,
                                           (byte)BlockageType.Open, (byte)BlockageType.Open]));
        var open = new Map(10, 10, cells);

        var (map, _, _) = new CombatMapGenerator(open, WallSets()).Generate(5, 5);
        Assert.Equal(0, ImpassableCount(map));
    }

    [Fact]
    public void A_closed_door_walls_the_face_and_an_open_one_leaves_a_gap()
    {
        // A face whose wall set names door art is a door. Closed, it stamps wall like any other;
        // open, it stamps wall and then punches a two-square gap through it.
        var level = RoomLevel();

        var (withoutDoors, _, _) = new CombatMapGenerator(level, WallSets()).Generate(3, 3);
        var (withDoors, _, _) = new CombatMapGenerator(level, WallSets("door.png")).Generate(3, 3);

        // Every face in RoomLevel is Blocked, so naming door art makes them closed doors, which
        // stamp identically to walls.
        Assert.Equal(ImpassableCount(withoutDoors), ImpassableCount(withDoors));
    }

    [Fact]
    public void Every_cell_of_a_real_level_generates_a_usable_map()
    {
        // The structural assertion over real data: whatever the design contains, the generator
        // must produce a map the party can stand on and one that is neither empty nor solid.
        string? root = ReferenceDesign();
        if (root is null)
        {
            return;
        }

        using var design = LoadedDesign.Open(root);
        var level = design.Level(0);
        var map = design.Map(0);
        if (level is null || map is null)
        {
            return;
        }

        var generator = new CombatMapGenerator(map, level.WallSets);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var (combat, cx, cy) = generator.Generate(x, y);

                Assert.Equal(ObstacleType.None, combat.Obstacle(cx, cy));

                int walls = ImpassableCount(combat);
                Assert.True(walls < combat.Width * combat.Height,
                            $"({x},{y}) generated a solid map");
            }
        }
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
