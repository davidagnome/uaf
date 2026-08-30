namespace UAFcore;

/// <summary>
/// A combatant's footprint on the combat grid. Icons run up to 4×4.
/// </summary>
public readonly record struct CombatantIcon(int Width = 1, int Height = 1);

/// <summary>
/// Where a combatant ended up, or <see cref="Unplaced"/>.
/// </summary>
/// <remarks>
/// The original marks a combatant that could not be placed by setting <c>x = -1</c> and leaves it
/// in the array (<c>Combatants.cpp:253</c>), so "unplaced" is a negative coordinate rather than a
/// separate flag. Kept, because later passes test <c>x &lt; 0</c> to skip it.
/// </remarks>
public readonly record struct PlacedAt(int X, int Y)
{
    public static PlacedAt Unplaced => new(-1, -1);

    public bool IsPlaced => X >= 0 && Y >= 0;
}

/// <summary>
/// Puts the party on the combat grid
/// (<c>determineInitCombatPos</c> / <c>getNextCharCombatPos</c>, <c>Combatants.cpp:2424</c>,
/// <c>:4046</c>).
/// </summary>
/// <remarks>
/// <para>
/// Each member has a preferred square from the formation table (<see cref="PartyArrangements"/>),
/// and takes the first free square found by a square spiral outward from it. Members are placed in
/// marching order and each one occupies the grid as it lands, so a later member routes around an
/// earlier one.
/// </para>
/// <para>
/// <b>Monsters are not placed here, and cannot be yet.</b> Under <c>newMonsterArrangement</c> —
/// which is defined (<c>Combatants.h:67</c>) — the monster branch of <c>determineInitCombatPos</c>
/// is commented out entirely (<c>Combatants.cpp:2197</c>). Monsters go through
/// <c>MonsterPlacementCallback</c>, a ~350-line turtle-graphics interpreter driven by the design's
/// own <c>CombatPlacement</c> GPDL script, which calls <c>$MonsterPlacement("16FbPV500E")</c> and
/// friends. That needs the GPDL VM plus two builtins. See docs/PORTING-PLAN.md §11.
/// </para>
/// </remarks>
public static class CombatPlacement
{
    /// <summary>How far the spiral searches, in rings (<c>delta &lt; 8</c>).</summary>
    /// <remarks>
    /// Eight rings is a 15×15 window — "search 225 cells.....enough is enough!" as the source puts
    /// it. A member the search cannot place stays <see cref="PlacedAt.Unplaced"/>.
    /// </remarks>
    public const int MaxSearchRings = 8;

    /// <summary>
    /// Places a party in marching order, returning where each member landed.
    /// </summary>
    /// <param name="map">The grid, which is written to as members are placed.</param>
    /// <param name="originX">The party origin — normally the start square from the generator.</param>
    /// <param name="originY">The party origin.</param>
    /// <param name="facing">The party's facing, selecting the formation block.</param>
    /// <param name="icons">One entry per member, in marching order.</param>
    /// <param name="outdoor">Selects the outdoor formation table.</param>
    /// <param name="firstCombatantIndex">
    /// The index the first member occupies the grid under. The party comes first in
    /// <c>m_aCombatants</c>, so this is zero in practice; it is a parameter so a test can place a
    /// party alongside combatants it did not create.
    /// </param>
    /// <remarks>
    /// <see cref="CombatMap.CombatantCount"/> is raised to cover the party before anyone is
    /// placed. Occupancy reads reject an index at or above it and clear the square, so leaving it
    /// low would make each member invisible to the next and stack the whole party on one square.
    /// </remarks>
    public static IReadOnlyList<PlacedAt> PlaceParty(CombatMap map, int originX, int originY,
                                                     Facing facing,
                                                     IReadOnlyList<CombatantIcon> icons,
                                                     bool outdoor = false,
                                                     int firstCombatantIndex = 0,
                                                     string? table = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(icons);

        if (icons.Count == 0)
        {
            return [];
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(icons.Count,
                                                      PartyArrangements.MaxPartyMembers);

        // A design's own PartyArrangement hook may supply the whole table; the built-in for the
        // terrain is the default. The hook's own length check already happened upstream, so a table
        // that reaches this far is the right size.
        table ??= outdoor ? PartyArrangements.Outdoor : PartyArrangements.Indoor;
        map.CombatantCount = Math.Max(map.CombatantCount, firstCombatantIndex + icons.Count);

        var placed = new PlacedAt[icons.Count];
        for (int i = 0; i < icons.Count; i++)
        {
            var (dx, dy) = PartyArrangements.For(table, facing, icons.Count, i);
            var icon = icons[i];

            placed[i] = Spiral(map, originX + dx, originY + dy, icon);
            if (placed[i].IsPlaced)
            {
                map.Place(placed[i].X, placed[i].Y, firstCombatantIndex + i,
                          icon.Width, icon.Height);
            }
        }

        return placed;
    }

    /// <summary>
    /// The square spiral from <c>getNextCharCombatPos</c> (<c>Combatants.cpp:4072</c>): the
    /// preferred square first, then each surrounding ring outward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed rather than rewritten, because the loop is stranger than it looks. <c>dir</c>
    /// and <c>i</c> are initialised <i>once</i> outside the ring loop, not per ring, and the
    /// rotation step reuses <c>i</c> as scratch (<c>i = dx; dx = -dy; dy = i; i = 0;</c>). The
    /// initial <c>dir = 3</c> / <c>i = -1</c> is what makes ring 0 test exactly one square: its
    /// inner condition is <c>i &lt; 0</c>.
    /// </para>
    /// <para>
    /// The original also declares a <c>searchOrder</c> array here and never reads it
    /// (<c>Combatants.cpp:4056</c>) — a leftover from the neighbour-scan this replaced. Not ported.
    /// </para>
    /// </remarks>
    private static PlacedAt Spiral(CombatMap map, int bestX, int bestY, CombatantIcon icon)
    {
        int dir = 3;
        int i = -1;

        for (int delta = 0; delta < MaxSearchRings; delta++)
        {
            int dx = 1;
            int dy = 0;
            int x = bestX - delta;
            int y = bestY - delta;

            for (; dir < 4; dir++)
            {
                for (; i < 2 * delta; i++)
                {
                    if (map.Obstacle(x, y, icon.Width, icon.Height) == ObstacleType.None)
                    {
                        return new PlacedAt(x, y);
                    }

                    x += dx;
                    y += dy;
                }

                // Rotate 90°, reusing i as the swap temporary exactly as the original does.
                i = dx;
                dx = -dy;
                dy = i;
                i = 0;
            }

            dir = 0;
        }

        return PlacedAt.Unplaced;
    }
}
