using UAF.Serialization;

namespace UAFcore;

/// <summary>One side's combatants, in the order <c>m_aCombatants</c> holds them.</summary>
/// <param name="Icon">The footprint.</param>
/// <param name="IsFriendly">Party and NPCs are friendly; everything else is a monster.</param>
public readonly record struct Combatant(CombatantIcon Icon, bool IsFriendly);

/// <summary>The result of setting up an encounter.</summary>
/// <param name="Map">The generated combat grid, with everybody placed on it.</param>
/// <param name="PartyX">The party's origin square.</param>
/// <param name="PartyY">The party's origin square.</param>
/// <param name="Positions">Where each combatant landed, indexed as <paramref name="Positions"/>.</param>
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
/// <b>Two pieces of the reference are missing and both would remove monsters, never add them.</b>
/// After placement the original deletes any monster with no path to the party
/// (<c>Combatants.cpp:255</c>), and it retries the whole placement at a shorter encounter distance
/// when nothing could be placed at all (the <c>for(;;)</c> at <c>:214</c>). Both need
/// <c>path.cpp</c>. Until they land, an encounter can leave a monster walled off in a pocket.
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

        for (int i = 0; i < combatants.Count; i++)
        {
            if (!combatants[i].IsFriendly && arrangement.Slots[i].IsPlaced)
            {
                positions[i] = new PlacedAt(arrangement.Slots[i].PlaceX,
                                            arrangement.Slots[i].PlaceY);
            }
        }

        return new CombatSetupResult(map, partyX, partyY, positions);
    }
}
