namespace UAFcore;

/// <summary>
/// Runs a monster-placement turtle program
/// (<c>MonsterPlacementCallback</c>, <c>Combatants.cpp:2749</c>).
/// </summary>
/// <remarks>
/// <para>
/// The program is a string of single-character commands executed against a
/// <see cref="MonsterArrangement"/>. It reaches the engine through GPDL — the design's
/// <c>CombatPlacement</c> special ability calls <c>$MonsterPlacement("16FbPV500E")</c> — so the
/// program is <b>design data</b>, and every one of these commands is available to a design even
/// though the shipped scripts use only six of them.
/// </para>
/// <para>
/// Commands, from the switch at <c>Combatants.cpp:2769</c>:
/// </para>
/// <list type="table">
///   <item><term><c>0</c>–<c>9</c></term><description>accumulate a repeat count</description></item>
///   <item><term><c>F R L B</c></term><description>move forward / right / left / back, <c>repeat</c> steps</description></item>
///   <item><term><c>V</c></term><description>require line of sight from a placed combatant</description></item>
///   <item><term><c>S</c></term><description>take the current monster's footprint</description></item>
///   <item><term><c>P</c></term><description>plant the current monster here</description></item>
///   <item><term><c>E</c></term><description>plant up to <c>repeat</c> monsters, searching outward</description></item>
///   <item><term><c>b f l r</c></term><description>set the back / front / left / right limit here</description></item>
///   <item><term><c>d</c></term><description>set the minimum distance to the current one</description></item>
///   <item><term><c>?</c></term><description>append what is under the turtle: <c>w s o i n</c></description></item>
///   <item><term><c>w n p s</c></term><description>jump to a party bounding-box edge</description></item>
///   <item><term><c>u o</c></term><description>push / pop the turtle position, two deep</description></item>
/// </list>
/// <para>
/// The return value is a string the script can inspect — <c>?</c> appends a terrain character,
/// <c>P</c> appends <c>'0'</c> when there was no monster left, and an unrecognised command appends
/// <c>'e'</c>. Most programs ignore it.
/// </para>
/// </remarks>
public static class TurtlePlacement
{
    /// <summary>
    /// The built-in programs (<c>defaultGlobalScripts</c>, <c>Specab.cpp:2081</c>, and the
    /// <c>CombatPlacement</c> ability every reference design ships).
    /// </summary>
    /// <remarks>
    /// Selected by encounter distance and then by party facing: the C++ script branches on
    /// <c>$GET_PARTY_FACING() >= 2</c>, which is south or west, and puts the turtle one square
    /// further out for those. Note the C++ <c>defaultGlobalScripts</c> table only carries
    /// <c>PlaceMonsterFar</c>; the other two come from the shipped ability.
    /// </remarks>
    public static string Default(EncounterDistance distance, Facing facing)
    {
        bool far = (int)facing >= 2;
        return distance switch
        {
            EncounterDistance.UpClose => far ? "FbPV500E" : "bPV500E",
            EncounterDistance.Nearby => far ? "10FbPV500E" : "9FbPV500E",
            _ => far ? "17FbPV500E" : "16FbPV500E",
        };
    }

    /// <summary>Runs a program, placing monsters onto <paramref name="map"/> as it goes.</summary>
    /// <param name="program">The turtle code.</param>
    /// <param name="state">The arrangement, already set up by <see cref="MonsterArrangement.BeginDirection"/>.</param>
    /// <param name="map">The combat grid, written to as monsters are planted.</param>
    /// <param name="icons">Every combatant's footprint, indexed as the slots are.</param>
    /// <returns>The program's output string.</returns>
    public static string Run(string program, MonsterArrangement state, CombatMap map,
                             IReadOnlyList<CombatantIcon> icons)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(icons);

        var result = new System.Text.StringBuilder();
        int repeat = 0;

        foreach (char c in program)
        {
            switch (c)
            {
                case 'V':
                    state.RequireLineOfSight = true;
                    repeat = 0;
                    break;

                case 'S':
                    if (state.CurrentMonster < 0) { FindNext(state); }
                    if (state.CurrentMonster >= 0)
                    {
                        state.TurtleWidth = icons[state.CurrentMonster].Width;
                        state.TurtleHeight = icons[state.CurrentMonster].Height;
                    }
                    repeat = 0;
                    break;

                case '?':
                    if (state.CurrentMonster < 0) { FindNext(state); }
                    if (state.CurrentMonster < 0)
                    {
                        result.Append('0');
                    }
                    else
                    {
                        var obstacle = map.Obstacle(state.TurtleX + state.PartyX,
                                                    state.TurtleY + state.PartyY,
                                                    state.TurtleWidth, state.TurtleHeight);
                        result.Append(obstacle switch
                        {
                            ObstacleType.Wall => 'w',
                            ObstacleType.LingeringSpell => 's',
                            ObstacleType.Occupied => 'o',
                            ObstacleType.OffMap => 'i',
                            _ => 'n',
                        });
                    }
                    repeat = 0;
                    break;

                case >= '0' and <= '9':
                    repeat = (10 * repeat) + (c - '0');
                    break;

                case 'L' or 'F' or 'R' or 'B':
                    if (repeat == 0) { repeat = 1; }
                    state.TurtleX += repeat * state.Dx[c - 'A'];
                    state.TurtleY += repeat * state.Dy[c - 'A'];
                    repeat = 0;
                    break;

                case 'P':
                    if (state.CurrentMonster < 0) { FindNext(state); }
                    if (state.CurrentMonster < 0)
                    {
                        result.Append('0');
                    }
                    else
                    {
                        // The return value is discarded, as in the original: P is "try here",
                        // not "place here or report".
                        Plant(state, map, icons, state.TurtleX, state.TurtleY);
                    }
                    repeat = 0;
                    break;

                case 'b':
                    SetLimit(state, back: true);
                    repeat = 0;
                    break;

                case 'f':
                    SetLimit(state, back: false);
                    repeat = 0;
                    break;

                case 'l':
                    SetSideLimit(state, left: true);
                    repeat = 0;
                    break;

                case 'r':
                    SetSideLimit(state, left: false);
                    repeat = 0;
                    break;

                case 'd':
                    state.MinDistanceSquared = MinDistanceToParty(state);
                    repeat = 0;
                    break;

                case 'E':
                    Expand(state, map, icons, repeat);
                    repeat = 0;
                    break;

                // The four bounding-box jumps, and the stack, deliberately do NOT clear repeat --
                // the original omits it for exactly these six commands (Combatants.cpp:3011
                // onward). A program that puts a count before one of them carries it to the next
                // command instead.
                case 'w':
                    JumpToPartyEdge(state, 0);
                    break;
                case 'n':
                    JumpToPartyEdge(state, 1);
                    break;
                case 'p':
                    JumpToPartyEdge(state, 2);
                    break;
                case 's':
                    JumpToPartyEdge(state, 3);
                    break;

                case 'u':
                    state.TurtleStack[3] = state.TurtleStack[1];
                    state.TurtleStack[2] = state.TurtleStack[0];
                    state.TurtleStack[0] = state.TurtleX;
                    state.TurtleStack[1] = state.TurtleY;
                    break;

                case 'o':
                    state.TurtleX = state.TurtleStack[0];
                    state.TurtleY = state.TurtleStack[1];
                    state.TurtleStack[0] = state.TurtleStack[2];
                    state.TurtleStack[1] = state.TurtleStack[3];
                    state.TurtleStack[2] = 0;
                    state.TurtleStack[3] = 0;
                    break;

                default:
                    result.Append('e');
                    repeat = 0;
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// The next unplaced monster approaching from the direction being run
    /// (<c>FindCurrentMonsterToPlace</c>, <c>Combatants.cpp:2637</c>).
    /// </summary>
    /// <remarks>
    /// Resets the turtle footprint to 1×1 as a side effect, which is why a program that wants a
    /// monster's real size has to issue <c>S</c> <i>after</i> the monster has been selected.
    /// </remarks>
    private static void FindNext(MonsterArrangement state)
    {
        state.TurtleWidth = 1;
        state.TurtleHeight = 1;

        for (int i = 0; i < state.Slots.Length; i++)
        {
            if (state.Slots[i].DirectionFromParty == state.CurrentDirection
                && !state.Slots[i].IsPlaced)
            {
                state.CurrentMonster = i;
                return;
            }
        }

        state.CurrentMonster = -1;
    }

    /// <summary>
    /// Tries to put the current monster at a party-relative square
    /// (<c>PlantCombatant</c>, <c>Combatants.cpp:2686</c>).
    /// </summary>
    private static bool Plant(MonsterArrangement state, CombatMap map,
                              IReadOnlyList<CombatantIcon> icons, int relX, int relY)
    {
        int n = state.CurrentMonster;
        var icon = icons[n];

        int x = relX + state.PartyX;
        int y = relY + state.PartyY;

        if (map.Obstacle(x, y, icon.Width, icon.Height) != ObstacleType.None)
        {
            return false;
        }

        // The X limits bound x - y, the sheared cross-axis. See MonsterArrangement's remarks.
        if (relX - relY < state.LimitMinX || relX - relY > state.LimitMaxX)
        {
            return false;
        }

        if (relY < state.LimitMinY || relY > state.LimitMaxY)
        {
            return false;
        }

        if (state.MinDistanceSquared >= 0)
        {
            foreach (var (px, py) in state.PartyPositions)
            {
                int dx = relX - px;
                int dy = relY - py;
                if ((dx * dx) + (dy * dy) < state.MinDistanceSquared)
                {
                    return false;
                }
            }
        }

        if (state.RequireLineOfSight && !WithinSight(state, map, relX, relY))
        {
            return false;
        }

        map.Place(x, y, n, icon.Width, icon.Height);
        state.Slots[n].PlaceX = x;
        state.Slots[n].PlaceY = y;
        state.CurrentMonster = -1;
        return true;
    }

    /// <summary>
    /// Whether anything already placed can see the turtle (<c>WithinSight</c>,
    /// <c>Combatants.cpp:2660</c>).
    /// </summary>
    /// <remarks>
    /// <b>The original's guard is <c>placeX &gt; 0</c>, not <c>&gt;= 0</c></b>, so a combatant
    /// standing in column 0 is invisible to this test. Reproduced: the combat map is 50 wide and
    /// the party starts at its centre, so column 0 is only reachable by a monster placed far to the
    /// west, but a design with a 25-wide map could hit it.
    /// </remarks>
    private static bool WithinSight(MonsterArrangement state, CombatMap map, int relX, int relY)
    {
        int tx = state.PartyX + relX;
        int ty = state.PartyY + relY;

        foreach (var slot in state.Slots)
        {
            if (slot.PlaceX > 0 && LineOfSight.Between(map, tx, ty, slot.PlaceX, slot.PlaceY))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>E</c> — plants up to <paramref name="count"/> monsters, searching outward in rings
    /// (<c>Combatants.cpp:2957</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A disc rather than a square: cells outside <c>dx² + dy² &gt; radius²</c> are skipped, so
    /// each ring fills in the corners left by the previous one. The visited set exists because
    /// successive radii overlap — without it the same cell is retried every ring.
    /// </para>
    /// <para>
    /// The original tracks visits in a flat <c>(2R+1)²</c> array whose index it advances by hand
    /// across three nested loops. The arithmetic is correct — the per-column stride works out to
    /// <c>2R+1</c> and the per-ring rewind to <c>(2r+2)(2R+1)+1</c>, which lands exactly on the
    /// next ring's top-left — but it is doing by increment what a relative index does directly, so
    /// this uses the relative index.
    /// </para>
    /// </remarks>
    private static void Expand(MonsterArrangement state, CombatMap map,
                               IReadOnlyList<CombatantIcon> icons, int count)
    {
        int maxRadius = Math.Min(map.Width, map.Height) / 4;
        var attempted = new bool[(2 * maxRadius) + 1, (2 * maxRadius) + 1];

        FindNext(state);
        if (count == 0) { count = 1; }
        if (state.CurrentMonster < 0) { return; }

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int x = state.TurtleX - radius; x <= state.TurtleX + radius; x++)
            {
                for (int y = state.TurtleY - radius; y <= state.TurtleY + radius; y++)
                {
                    if (state.CurrentMonster < 0 || count <= 0) { break; }

                    int dx = x - state.TurtleX;
                    int dy = y - state.TurtleY;
                    if ((dx * dx) + (dy * dy) > radius * radius) { continue; }

                    if (attempted[dx + maxRadius, dy + maxRadius]) { continue; }
                    attempted[dx + maxRadius, dy + maxRadius] = true;

                    if (Plant(state, map, icons, x, y))
                    {
                        count--;
                        FindNext(state);
                    }
                }
            }
        }
    }

    /// <summary>
    /// <c>b</c> and <c>f</c> — pin the near or far edge of the placement zone at the turtle.
    /// </summary>
    /// <remarks>
    /// Which field each writes depends on the approach direction, and for east and west it is the
    /// sheared <c>x − y</c> that is pinned rather than the row.
    /// </remarks>
    private static void SetLimit(MonsterArrangement state, bool back)
    {
        int cross = state.TurtleX - state.TurtleY;
        switch (state.CurrentDirection)
        {
            case 0:
                if (back) { state.LimitMaxY = state.TurtleY; } else { state.LimitMinY = state.TurtleY; }
                break;
            case 1:
                if (back) { state.LimitMinX = cross; } else { state.LimitMaxX = cross; }
                break;
            case 2:
                if (back) { state.LimitMinY = state.TurtleY; } else { state.LimitMaxY = state.TurtleY; }
                break;
            default:
                if (back) { state.LimitMaxX = cross; } else { state.LimitMinX = cross; }
                break;
        }
    }

    /// <summary><c>l</c> and <c>r</c> — pin the sides of the placement zone at the turtle.</summary>
    private static void SetSideLimit(MonsterArrangement state, bool left)
    {
        int cross = state.TurtleX - state.TurtleY;
        switch (state.CurrentDirection)
        {
            case 0:
                if (left) { state.LimitMinX = cross; } else { state.LimitMaxX = cross; }
                break;
            case 1:
                if (left) { state.LimitMinY = state.TurtleY; } else { state.LimitMaxY = state.TurtleY; }
                break;
            case 2:
                if (left) { state.LimitMaxX = cross; } else { state.LimitMinX = cross; }
                break;
            default:
                if (left) { state.LimitMaxY = state.TurtleY; } else { state.LimitMinY = state.TurtleY; }
                break;
        }
    }

    /// <summary>
    /// <c>d</c> — the squared distance from the turtle to the nearest party member
    /// (<c>Combatants.cpp:2934</c>).
    /// </summary>
    /// <remarks>
    /// <b>Reproduces an original bug.</b> The reference computes <c>dy</c> from
    /// <c>partyPositions[j].x</c> — the same field it uses for <c>dx</c> — so the "distance" is
    /// measured to a reflected point, not to the party member. Deterministic and harmless to
    /// reproduce; fixing it would change which squares a <c>d</c>-using design's monsters may
    /// occupy. No shipped program uses <c>d</c>.
    /// </remarks>
    private static int MinDistanceToParty(MonsterArrangement state)
    {
        int best = int.MaxValue;
        foreach (var (px, _) in state.PartyPositions)
        {
            int dx = state.TurtleX - px;
            int dy = state.TurtleY - px;      // .x on purpose -- see the remarks
            best = Math.Min(best, (dx * dx) + (dy * dy));
        }
        return best;
    }

    /// <summary>
    /// <c>w</c>, <c>n</c>, <c>p</c> and <c>s</c> — jump the turtle to an edge of the party's
    /// bounding box (<c>Combatants.cpp:3011</c>).
    /// </summary>
    /// <remarks>
    /// <b>Transcribed with its inconsistencies intact.</b> Several arms move the wrong axis or read
    /// the wrong bound — <c>n</c> for a northern approach calls <c>MoveTurtleX(partyMaxY)</c>,
    /// setting a column from a row — and the four commands do not form the symmetric set their
    /// names suggest. No shipped program uses any of them, so there is no observed behaviour to
    /// check a correction against, and inventing one would be guessing.
    /// </remarks>
    private static void JumpToPartyEdge(MonsterArrangement state, int which)
    {
        switch (which, state.CurrentDirection)
        {
            case (0, 0): state.MoveTurtleY(state.PartyMinY); break;
            case (0, 1): state.MoveTurtleX(state.PartyMaxX); break;
            case (0, 2): state.MoveTurtleY(state.PartyMaxY); break;
            case (0, 3): state.MoveTurtleX(state.PartyMinX); break;

            case (1, 0): state.MoveTurtleX(state.PartyMaxY); break;   // X from a Y bound
            case (1, 1): state.MoveTurtleX(state.PartyMinX); break;
            case (1, 2): state.MoveTurtleY(state.PartyMinY); break;
            case (1, 3): state.MoveTurtleX(state.PartyMaxX); break;

            case (2, 0): state.MoveTurtleX(state.PartyMinX); break;
            case (2, 1): state.MoveTurtleY(state.PartyMinY); break;
            case (2, 2): state.MoveTurtleX(state.PartyMaxX); break;
            case (2, 3): state.MoveTurtleY(state.PartyMaxY); break;

            case (3, 0): state.MoveTurtleX(state.PartyMaxX); break;
            case (3, 1): state.MoveTurtleY(state.PartyMaxY); break;
            case (3, 2): state.MoveTurtleX(state.PartyMinX); break;
            default: state.MoveTurtleY(state.PartyMinY); break;
        }
    }
}
