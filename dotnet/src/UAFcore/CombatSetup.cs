using UAF.Serialization;

namespace UAFcore;

/// <summary>The result of setting up an encounter.</summary>
/// <param name="Map">The generated combat grid, with everybody placed on it.</param>
/// <param name="PartyX">The party's origin square.</param>
/// <param name="PartyY">The party's origin square.</param>
/// <param name="Positions">Where each combatant landed, indexed as the combatant list is.</param>
/// <remarks>
/// The positions are also written back onto the combatants themselves — <see cref="Combatant.X"/>
/// and <see cref="Combatant.Y"/> — so a caller can work from either. They are returned separately
/// because "unplaced" is a distinct answer that the combatant records only as a negative
/// coordinate.
/// </remarks>
public sealed record CombatSetupResult(CombatMap Map, int PartyX, int PartyY,
                                       IReadOnlyList<PlacedAt> Positions);

/// <summary>
/// Builds a combat encounter: generates the map, places the party, then runs one monster-placement
/// turtle program per approach direction (<c>InitCombatData</c> and the no-argument
/// <c>determineInitCombatPos</c>, <c>Combatants.cpp:123</c>, <c>:2424</c>).
/// </summary>
/// <remarks>
/// <para>
/// The order is load-bearing. The party is placed first because the monster programs read the
/// party's bounding box and require line of sight to something already on the map; running them
/// first places monsters against an empty grid and the <c>V</c> rule rejects every square.
/// </para>
/// <para>
/// After placement, any monster with no route to the party is <b>removed</b>
/// (<c>Combatants.cpp:255</c>) — a monster sealed in a pocket would otherwise stall the round
/// forever. If that empties the encounter, the whole thing is retried at a shorter distance (the
/// <c>for(;;)</c> at <c>:214</c>), because "far away" on a cramped map can put every monster
/// somewhere unreachable.
/// </para>
/// </remarks>
public static class CombatSetup
{
    /// <summary>
    /// Sets up an encounter for a party standing at <paramref name="levelX"/>,
    /// <paramref name="levelY"/>.
    /// </summary>
    /// <param name="program">
    /// The turtle program. Defaults to the built-in one for the distance and facing; a design's
    /// own <c>CombatPlacement</c> script would supply this instead once GPDL can run it.
    /// </param>
    public static CombatSetupResult Begin(Map level, IReadOnlyList<WallSetSlot> wallSets,
                                          int levelX, int levelY, Facing facing,
                                          IReadOnlyList<Combatant> combatants,
                                          EncounterDirection direction = EncounterDirection.Any,
                                          EncounterDistance distance = EncounterDistance.FarAway,
                                          bool outdoor = false,
                                          string? program = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(combatants);

        bool wantsMonsters = combatants.Any(c => !c.IsFriendly);

        // Try the requested distance, then closer ones. A cramped map can put every monster
        // somewhere the party cannot reach, and the reference would rather fight up close than
        // present an encounter with nobody in it.
        var result = Attempt(level, wallSets, levelX, levelY, facing, combatants, direction,
                             distance, outdoor, program);

        if (wantsMonsters && program is null && !AnyMonsterPlaced(result, combatants))
        {
            foreach (var closer in Closer(distance))
            {
                result = Attempt(level, wallSets, levelX, levelY, facing, combatants, direction,
                                 closer, outdoor, program);
                if (AnyMonsterPlaced(result, combatants))
                {
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>The distances to fall back through, nearest last.</summary>
    private static IEnumerable<EncounterDistance> Closer(EncounterDistance from) => from switch
    {
        EncounterDistance.FarAway => [EncounterDistance.Nearby, EncounterDistance.UpClose],
        EncounterDistance.Nearby => [EncounterDistance.UpClose],
        _ => [],
    };

    private static bool AnyMonsterPlaced(CombatSetupResult result,
                                         IReadOnlyList<Combatant> combatants)
    {
        for (int i = 0; i < combatants.Count; i++)
        {
            if (!combatants[i].IsFriendly && result.Positions[i].IsPlaced)
            {
                return true;
            }
        }
        return false;
    }

    private static CombatSetupResult Attempt(Map level, IReadOnlyList<WallSetSlot> wallSets,
                                             int levelX, int levelY, Facing facing,
                                             IReadOnlyList<Combatant> combatants,
                                             EncounterDirection direction,
                                             EncounterDistance distance, bool outdoor,
                                             string? program)
    {
        var generator = new CombatMapGenerator(level, wallSets);
        var (map, partyX, partyY) = generator.Generate(levelX, levelY);

        var icons = combatants.Select(c => c.Icon).ToList();
        map.CombatantCount = combatants.Count;

        var positions = new PlacedAt[combatants.Count];
        Array.Fill(positions, PlacedAt.Unplaced);

        // -- the party, in marching order --------------------------------------------------
        var partyIndices = new List<int>();
        for (int i = 0; i < combatants.Count; i++)
        {
            if (combatants[i].IsFriendly) { partyIndices.Add(i); }
        }

        var arrangement = new MonsterArrangement { PartyX = partyX, PartyY = partyY };
        arrangement.Activate(combatants.Count);

        if (partyIndices.Count > 0)
        {
            var placedParty = CombatPlacement.PlaceParty(
                map, partyX, partyY, facing,
                [.. partyIndices.Select(i => icons[i])], outdoor,
                firstCombatantIndex: partyIndices[0]);

            for (int p = 0; p < partyIndices.Count; p++)
            {
                positions[partyIndices[p]] = placedParty[p];
                if (!placedParty[p].IsPlaced) { continue; }

                int rx = placedParty[p].X - partyX;
                int ry = placedParty[p].Y - partyY;
                arrangement.PartyPositions.Add((rx, ry));
                arrangement.PartyMinX = Math.Min(arrangement.PartyMinX, rx);
                arrangement.PartyMaxX = Math.Max(arrangement.PartyMaxX, rx);
                arrangement.PartyMinY = Math.Min(arrangement.PartyMinY, ry);
                arrangement.PartyMaxY = Math.Max(arrangement.PartyMaxY, ry);

                // The party occupies slots too: WithinSight walks every slot, so a monster's
                // line-of-sight test has something to see before any monster is down.
                arrangement.Slots[partyIndices[p]].PlaceX = placedParty[p].X;
                arrangement.Slots[partyIndices[p]].PlaceY = placedParty[p].Y;
            }
        }

        // -- deal the monsters across the permitted sides ----------------------------------
        var approach = new MonsterApproach(direction, facing);
        for (int i = 0; i < combatants.Count; i++)
        {
            if (combatants[i].IsFriendly) { continue; }

            int dir = approach.Next();
            arrangement.Slots[i].DirectionFromParty = dir;
            arrangement.CountByDirection[dir]++;
        }

        // -- one turtle run per side that has anybody on it --------------------------------
        for (int dir = 0; dir < 4; dir++)
        {
            if (arrangement.CountByDirection[dir] == 0) { continue; }

            arrangement.BeginDirection(dir);
            TurtlePlacement.Run(program ?? TurtlePlacement.Default(distance, facing),
                                arrangement, map, icons);
        }

        // -- drop anything the party cannot reach ------------------------------------------
        // The reference walks a 1x1 path from each monster to the party start and removes the
        // monster when there is none (Combatants.cpp:243). The footprint is deliberately 1x1
        // rather than the monster's own -- "1x1 good enough to let party reach" (:238) -- because
        // the question is whether the two sides can meet at all, not whether this particular
        // monster can squeeze through.
        var reach = new CombatPathFinder(map) { OccupantsBlock = false };

        for (int i = 0; i < combatants.Count; i++)
        {
            if (combatants[i].IsFriendly || !arrangement.Slots[i].IsPlaced)
            {
                continue;
            }

            int mx = arrangement.Slots[i].PlaceX;
            int my = arrangement.Slots[i].PlaceY;

            bool reachable = (mx == partyX && my == partyY)
                             || reach.IsAlreadyWithin(mx, my, partyX, partyY, partyX, partyY)
                             || reach.To(mx, my, partyX, partyY) is not null;

            if (reachable)
            {
                positions[i] = new PlacedAt(mx, my);
            }
            else
            {
                // Off the grid as well as out of the result, so it stops blocking a square.
                map.Remove(mx, my, combatants[i].Icon.Width, combatants[i].Icon.Height);
                arrangement.Slots[i].PlaceX = -1;
                arrangement.Slots[i].PlaceY = -1;
            }
        }

        // Mirror the outcome onto the combatants themselves, so the round can work from the
        // entity rather than carrying a parallel array around.
        for (int i = 0; i < combatants.Count; i++)
        {
            combatants[i].X = positions[i].X;
            combatants[i].Y = positions[i].Y;
        }

        return new CombatSetupResult(map, partyX, partyY, positions);
    }
}
