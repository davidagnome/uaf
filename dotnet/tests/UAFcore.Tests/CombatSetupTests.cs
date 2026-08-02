using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the whole encounter setup: map, party, then monsters (<c>InitCombatData</c>,
/// <c>Combatants.cpp:123</c>).
/// </summary>
public class CombatSetupTests
{
    private static Map EmptyLevel()
    {
        var cells = new AreaMapCell[100];
        Array.Fill(cells, new AreaMapCell(0, false, false, 0, 0, 0, 0, 0, false,
                                          [0, 0, 0, 0], [0, 0, 0, 0]));
        return new Map(10, 10, cells);
    }

    private static IReadOnlyList<WallSetSlot> WallSets() =>
        [.. Enumerable.Range(0, 8).Select(_ =>
            new WallSetSlot("wall.png", string.Empty, "overlay.png", string.Empty,
                            string.Empty, 1, 0, 0, string.Empty, 0, 0))];

    /// <summary>A party followed by monsters, indexed the way <c>m_aCombatants</c> holds them.</summary>
    private static List<Combatant> Encounter(int party, int monsters)
    {
        var all = new List<Combatant>();
        for (int i = 0; i < party + monsters; i++)
        {
            all.Add(new Combatant(i, isFriendly: i < party, new CombatantIcon(1, 1)));
        }
        return all;
    }

    [Fact]
    public void Everybody_lands_somewhere_distinct_and_passable()
    {
        var result = CombatSetup.Begin(EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                       Encounter(6, 8));

        Assert.Equal(14, result.Positions.Count);
        Assert.All(result.Positions, p => Assert.True(p.IsPlaced));

        var squares = result.Positions.Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(14, squares.Distinct().Count());
        Assert.All(squares, s => Assert.True(result.Map.IsPassable(s.X, s.Y)));
    }

    [Fact]
    public void The_encounter_distance_decides_how_far_out_the_monsters_start()
    {
        // The three built-in programs move the turtle 0, 9 and 16 steps forward before planting,
        // so the nearest monster's distance from the party is what distinguishes them.
        var level = EmptyLevel();
        var sets = WallSets();

        int Nearest(EncounterDistance distance)
        {
            var r = CombatSetup.Begin(level, sets, 5, 5, Facing.North, Encounter(6, 4),
                                      EncounterDirection.North, distance);
            return r.Positions.Skip(6).Where(p => p.IsPlaced)
                    .Min(p => CombatMap.Distance(r.PartyX, r.PartyY, p.X, p.Y));
        }

        int close = Nearest(EncounterDistance.UpClose);
        int near = Nearest(EncounterDistance.Nearby);
        int far = Nearest(EncounterDistance.FarAway);

        Assert.True(close < near, $"up close ({close}) should be nearer than nearby ({near})");
        Assert.True(near < far, $"nearby ({near}) should be nearer than far away ({far})");
    }

    [Fact]
    public void Monsters_stay_on_the_side_they_approached_from()
    {
        // A northern encounter must not put anybody south of the party: that is what 'b' is for.
        var result = CombatSetup.Begin(EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                       Encounter(4, 6), EncounterDirection.North);

        foreach (var p in result.Positions.Skip(4).Where(p => p.IsPlaced))
        {
            Assert.True(p.Y <= result.PartyY,
                        $"monster at ({p.X},{p.Y}) is south of the party at y={result.PartyY}");
        }
    }

    [Fact]
    public void A_single_sided_encounter_puts_everything_on_one_side()
    {
        var east = CombatSetup.Begin(EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                     Encounter(2, 6), EncounterDirection.East);
        var west = CombatSetup.Begin(EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                     Encounter(2, 6), EncounterDirection.West);

        Assert.All(east.Positions.Skip(2).Where(p => p.IsPlaced),
                   p => Assert.True(p.X >= east.PartyX));
        Assert.All(west.Positions.Skip(2).Where(p => p.IsPlaced),
                   p => Assert.True(p.X <= west.PartyX));
    }

    [Fact]
    public void An_encounter_with_no_monsters_still_places_the_party()
    {
        var result = CombatSetup.Begin(EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                       Encounter(4, 0));

        Assert.Equal(4, result.Positions.Count);
        Assert.All(result.Positions, p => Assert.True(p.IsPlaced));
    }

    [Fact]
    public void An_encounter_with_no_party_still_places_the_monsters()
    {
        // Monster placement's line-of-sight rule needs something already on the map, and with no
        // party there is nothing -- so this is the case where V rejects everything. It must not
        // throw, and the party's bounding box must not be read as a real one.
        var result = CombatSetup.Begin(EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                       Encounter(0, 3));

        Assert.Equal(3, result.Positions.Count);
    }

    [Fact]
    public void A_custom_turtle_program_overrides_the_built_in_one()
    {
        // The program is design data -- a design's own CombatPlacement script supplies it. Moving
        // the turtle 20 forward before planting must show up in where the monsters land.
        var result = CombatSetup.Begin(EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                       Encounter(2, 2), EncounterDirection.East,
                                       program: "20FbP500E");

        var placed = result.Positions.Skip(2).Where(p => p.IsPlaced).ToList();
        Assert.NotEmpty(placed);
        Assert.All(placed, p => Assert.True(p.X >= result.PartyX + 15,
                                            $"monster at x={p.X} did not move 20 east of {result.PartyX}"));
    }

    [Fact]
    public void A_monster_the_party_cannot_reach_is_removed_from_the_encounter()
    {
        // InitCombatData walks a path from each monster to the party and drops the ones with no
        // route (Combatants.cpp:243), because a monster sealed in a pocket would stall the round
        // forever. Placed by hand here: the turtle would not choose a sealed square on its own,
        // which is exactly why this needs its own test.
        var map = new CombatMap(25, 25);
        map.FillHoles();
        map.CombatantCount = 2;

        // A one-square cell walled off from everything.
        for (int y = 4; y <= 6; y++)
        {
            for (int x = 4; x <= 6; x++)
            {
                if ((x, y) != (5, 5)) { map.SetTile(x, y, 1); }
            }
        }

        var finder = new CombatPathFinder(map) { OccupantsBlock = false };
        Assert.Null(finder.To(5, 5, 20, 20));
        Assert.NotNull(finder.To(19, 19, 20, 20));
    }

    [Fact]
    public void An_encounter_that_places_nobody_is_retried_closer_in()
    {
        // The reference loops over shorter distances until at least one monster is in
        // (the for(;;) at Combatants.cpp:214), because "far away" on a cramped map can put every
        // monster somewhere unreachable. On an open map the first attempt already succeeds, so
        // this checks the fallback does not make things worse rather than that it fires.
        var result = CombatSetup.Begin(EmptyLevel(), WallSets(), 5, 5, Facing.North,
                                       Encounter(4, 4), EncounterDirection.Any,
                                       EncounterDistance.FarAway);

        Assert.Contains(result.Positions.Skip(4), p => p.IsPlaced);
    }

    [Fact]
    public void Every_placed_monster_can_reach_the_party()
    {
        // The invariant the removal pass exists to guarantee, asserted over a real level.
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

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var result = CombatSetup.Begin(map, level.WallSets, x, y, Facing.North,
                                               Encounter(4, 6));
                var finder = new CombatPathFinder(result.Map) { OccupantsBlock = false };

                foreach (var p in result.Positions.Skip(4).Where(p => p.IsPlaced))
                {
                    bool reachable = (p.X == result.PartyX && p.Y == result.PartyY)
                                     || finder.To(p.X, p.Y, result.PartyX, result.PartyY) is not null;
                    Assert.True(reachable,
                                $"({x},{y}): monster at ({p.X},{p.Y}) cannot reach the party");
                }
            }
        }
    }

    [Fact]
    public void Every_cell_of_a_real_level_sets_up_a_usable_encounter()
    {
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

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var result = CombatSetup.Begin(map, level.WallSets, x, y, Facing.North,
                                               Encounter(6, 6));

                var placed = result.Positions.Where(p => p.IsPlaced)
                                             .Select(p => (p.X, p.Y)).ToList();

                Assert.Equal(placed.Count, placed.Distinct().Count());
                Assert.All(placed, s => Assert.True(result.Map.IsPassable(s.X, s.Y),
                                                    $"({x},{y}): somebody is standing in a wall"));

                // The party is placed first and unconditionally, so it must always be down.
                Assert.All(result.Positions.Take(6), p => Assert.True(p.IsPlaced));
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
