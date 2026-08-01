using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers party formation and placement onto the combat grid
/// (<c>Combatants.cpp:2424</c>, <c>:4046</c>).
/// </summary>
public class CombatPlacementTests
{
    private static CombatMap OpenMap(int width = 25, int height = 25)
    {
        var map = new CombatMap(width, height);
        map.FillHoles();
        return map;
    }

    private static CombatantIcon[] Party(int size) =>
        [.. Enumerable.Repeat(new CombatantIcon(1, 1), size)];

    [Theory]
    [InlineData('A', 0)]
    [InlineData('B', 1)]
    [InlineData('C', 2)]
    [InlineData('Z', 25)]
    [InlineData('a', 0)]
    [InlineData('b', -1)]
    [InlineData('c', -2)]
    [InlineData('z', -25)]
    [InlineData('0', 0)]
    [InlineData('?', 0)]
    public void Offsets_decode_up_from_A_and_down_from_a(char c, int expected)
    {
        // Not a sign bit: 'A' and 'a' are BOTH zero, so the negative range starts at 'b'. Reading
        // 'a' as -1 shifts every negative offset by one and skews the whole formation.
        Assert.Equal(expected, PartyArrangements.Decode(c));
    }

    [Fact]
    public void Both_tables_are_the_size_the_index_arithmetic_assumes()
    {
        // 4 direction blocks of (MAX+1)*MAX. The original indexes these with no bounds check at
        // all, so a short table would read whatever follows it in memory.
        Assert.Equal(4 * PartyArrangements.DirectionBlock, PartyArrangements.Indoor.Length);
        Assert.Equal(4 * PartyArrangements.DirectionBlock, PartyArrangements.Outdoor.Length);
        Assert.Equal(156, PartyArrangements.DirectionBlock);
    }

    [Fact]
    public void A_party_of_three_facing_north_stands_abreast()
    {
        // The north block's size-3 run is "bBABBB": (-1,+1), (0,+1), (+1,+1). Decoded by hand from
        // the C++ table, so this is a check on the generator and the index arithmetic together.
        var table = PartyArrangements.Indoor;
        Assert.Equal((-1, 1), PartyArrangements.For(table, Facing.North, 3, 0));
        Assert.Equal((0, 1), PartyArrangements.For(table, Facing.North, 3, 1));
        Assert.Equal((1, 1), PartyArrangements.For(table, Facing.North, 3, 2));
    }

    [Fact]
    public void Each_facing_selects_a_different_block()
    {
        // Size-1 runs, read off the four blocks: "AB", "bA", "AA", "BB".
        var table = PartyArrangements.Indoor;
        Assert.Equal((0, 1), PartyArrangements.For(table, Facing.North, 1, 0));
        Assert.Equal((-1, 0), PartyArrangements.For(table, Facing.East, 1, 0));
        Assert.Equal((0, 0), PartyArrangements.For(table, Facing.South, 1, 0));
        Assert.Equal((1, 1), PartyArrangements.For(table, Facing.West, 1, 0));
    }

    [Fact]
    public void A_party_too_large_for_the_table_is_rejected_rather_than_read_past_the_end()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PartyArrangements.For(PartyArrangements.Indoor, Facing.North, 13, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PartyArrangements.For(PartyArrangements.Indoor, Facing.North, 3, 3));
    }

    [Fact]
    public void On_an_open_map_everybody_gets_their_formation_square()
    {
        var map = OpenMap();
        var placed = CombatPlacement.PlaceParty(map, 10, 10, Facing.North, Party(3));

        Assert.Equal(new PlacedAt(9, 11), placed[0]);
        Assert.Equal(new PlacedAt(10, 11), placed[1]);
        Assert.Equal(new PlacedAt(11, 11), placed[2]);
        Assert.All(placed, p => Assert.True(p.IsPlaced));
    }

    [Fact]
    public void Each_member_occupies_the_grid_so_the_next_one_routes_around_it()
    {
        // Placement is sequential and mutates the map, which is why CombatantCount has to cover
        // the party up front -- an occupancy read at or above the count clears the square instead
        // of reporting it, and the whole party would stack on one spot.
        var map = OpenMap();
        var placed = CombatPlacement.PlaceParty(map, 10, 10, Facing.North, Party(6));

        var squares = placed.Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(squares.Count, squares.Distinct().Count());

        for (int i = 0; i < placed.Count; i++)
        {
            Assert.Equal(i, map.OccupantAt(placed[i].X, placed[i].Y));
        }
    }

    [Fact]
    public void A_blocked_formation_square_falls_back_to_the_first_ring()
    {
        // The spiral's ring 1 begins at the top-left corner and runs clockwise, so the first
        // alternative to a blocked square is (-1,-1) from it. Verified against the transcribed
        // loop's actual visit order, not assumed from the shape of the code.
        var map = OpenMap();
        map.SetTile(10, 11, 1);     // impassable; the size-1 north formation square for origin (10,10)

        var placed = CombatPlacement.PlaceParty(map, 10, 10, Facing.North, Party(1));
        Assert.Equal(new PlacedAt(9, 10), placed[0]);
    }

    [Fact]
    public void The_spiral_walks_rings_outward_rather_than_scanning_rows()
    {
        // Wall off everything within two rings of the formation square. A row-scan would land far
        // away on the same row; the spiral must come back to Chebyshev distance exactly 3.
        var map = OpenMap();
        for (int y = 9; y <= 13; y++)
        {
            for (int x = 8; x <= 12; x++)
            {
                map.SetTile(x, y, 1);
            }
        }

        var placed = CombatPlacement.PlaceParty(map, 10, 10, Facing.North, Party(1));
        Assert.True(placed[0].IsPlaced);

        int chebyshev = Math.Max(Math.Abs(placed[0].X - 10), Math.Abs(placed[0].Y - 11));
        Assert.Equal(3, chebyshev);
    }

    [Fact]
    public void A_member_with_nowhere_to_stand_is_left_unplaced_rather_than_dropped()
    {
        // The original marks failure with x = -1 and leaves the combatant in the array; later
        // passes test x < 0 to skip it. A thrown exception or a silent removal would both break
        // the index correspondence the round order depends on.
        var map = new CombatMap(25, 25);        // never filled: every square impassable
        var placed = CombatPlacement.PlaceParty(map, 12, 12, Facing.North, Party(2));

        Assert.Equal(2, placed.Count);
        Assert.All(placed, p => Assert.False(p.IsPlaced));
        Assert.All(placed, p => Assert.Equal(PlacedAt.Unplaced, p));
    }

    [Fact]
    public void A_large_icon_needs_room_for_its_whole_footprint()
    {
        var map = OpenMap();

        // Block one square of where a 2x2 would sit, and it has to move.
        map.SetTile(10, 12, 1);
        var placed = CombatPlacement.PlaceParty(map, 10, 10, Facing.North,
                                                [new CombatantIcon(2, 2)]);

        Assert.True(placed[0].IsPlaced);
        Assert.Equal(ObstacleType.None, map.Obstacle(placed[0].X, placed[0].Y, 2, 2,
                                                     ignoreCombatant: 0));
    }

    [Fact]
    public void An_empty_party_places_nothing()
    {
        var map = OpenMap();
        Assert.Empty(CombatPlacement.PlaceParty(map, 10, 10, Facing.North, []));
    }

    [Fact]
    public void Outdoor_formations_come_from_the_other_table()
    {
        // The two tables agree for a party of three facing north and diverge at four, which is
        // what makes this worth a test rather than an assumption.
        var map = OpenMap();
        var indoor = CombatPlacement.PlaceParty(OpenMap(), 10, 10, Facing.North, Party(4));
        var outdoor = CombatPlacement.PlaceParty(map, 10, 10, Facing.North, Party(4),
                                                 outdoor: true);

        Assert.NotEqual(indoor, outdoor);
    }

    [Fact]
    public void A_party_on_a_real_combat_map_lands_somewhere_it_can_stand()
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

        var generator = new CombatMapGenerator(levelMap, level.WallSets);

        for (int y = 0; y < levelMap.Height; y++)
        {
            for (int x = 0; x < levelMap.Width; x++)
            {
                foreach (var facing in Enum.GetValues<Facing>())
                {
                    var (combat, cx, cy) = generator.Generate(x, y);
                    var placed = CombatPlacement.PlaceParty(combat, cx, cy, facing, Party(6));

                    var squares = placed.Where(p => p.IsPlaced)
                                        .Select(p => (p.X, p.Y))
                                        .ToList();

                    Assert.True(squares.Count > 0,
                                $"({x},{y}) facing {facing}: nobody could be placed");

                    // Nobody stacked, and nobody in a wall.
                    Assert.Equal(squares.Count, squares.Distinct().Count());
                    Assert.All(squares, s => Assert.True(combat.IsPassable(s.X, s.Y)));
                }
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
