using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Builds the combat terrain grid from the level's orthogonal cell map
/// (<c>GenerateIndoorCombatMap</c>, <c>Drawtile.cpp:2391</c>).
/// </summary>
/// <remarks>
/// <para>
/// The level map is coarse — one cell per step, four faces each. The combat map is fine, 50×50 by
/// default, and <b>rotated 45°</b>: the party's dungeon is re-drawn as a diagonal corridor system
/// so that a combat map cut from it can be walked square by square. That rotation is the whole
/// difficulty of this file, and it happens in a temporary buffer eight times wider and seven times
/// taller than the finished map before being sampled back down.
/// </para>
/// <para>
/// Three passes:
/// </para>
/// <list type="number">
///   <item>Stamp each level cell's four faces into the oversized temp grid as runs of "wall",
///   each row offset one cell left of the row above, which is what produces the diagonal.</item>
///   <item>Reduce each wall square to a <see cref="TerrainWallType"/> — end cap, straight,
///   corner, T-junction, crossroads — from its four diagonal neighbours.</item>
///   <item>Sample a <see cref="CombatMap.Width"/>×<see cref="CombatMap.Height"/> window centred on
///   the party and expand each junction type into one to five terrain tiles.</item>
/// </list>
/// <para>
/// <b>The combat map is a torus and is never clamped to the level's edges.</b> The source has a
/// large <c>#ifndef diagonalMap</c> block that clamps the source window to the level bounds, and
/// it is dead: <c>diagonalMap</c> is defined at <c>Drawtile.cpp:27</c>, above every use. It is
/// dead beyond doubt rather than by inspection — the block reads <c>areaMapEndY</c>, whose only
/// declaration is <i>commented out</i> at <c>Drawtile.cpp:2402</c>, so with <c>diagonalMap</c>
/// undefined the file would not compile. Every source-map read here wraps instead, which matches
/// <see cref="Map.Wrap"/> and the rest of the engine.
/// </para>
/// </remarks>
public sealed class CombatMapGenerator
{
    /// <summary>Temp-grid columns per level cell (<c>MAP_H_MULTIPLIER</c>).</summary>
    public const int HorizontalMultiplier = 8;

    /// <summary>Temp-grid rows per level cell (<c>MAP_V_MULTIPLIER</c>).</summary>
    public const int VerticalMultiplier = 7;

    /// <summary>The largest wall-set index the door lookup will consider (<c>MAX_WALLSETS</c>).</summary>
    private const int MaxWallSets = WallResolver.MaxWallSets;

    /// <summary>What a level cell face contributes (<c>WALLTYPE</c>, <c>Drawtile.cpp:72</c>).</summary>
    private enum WallType
    {
        None = 0,
        Wall = 1,
        OpenDoor = 2,
        ClosedDoor = 3,
    }

    private readonly Map map;
    private readonly IReadOnlyList<WallSetSlot> wallSets;
    private readonly WallOverrides? overrides;
    private readonly bool useWallIndex;

    public CombatMapGenerator(Map map, IReadOnlyList<WallSetSlot> wallSets,
                              WallOverrides? overrides = null, bool useWallIndex = false)
    {
        this.map = map ?? throw new ArgumentNullException(nameof(map));
        this.wallSets = wallSets ?? throw new ArgumentNullException(nameof(wallSets));
        this.overrides = overrides;
        this.useWallIndex = useWallIndex;
    }

    /// <summary>
    /// Generates the combat map for a party standing at <paramref name="partyX"/>,
    /// <paramref name="partyY"/> on the level.
    /// </summary>
    /// <returns>
    /// The finished map and the party's square on it. <c>ConvertTempMapToCombatTerrain</c> ends by
    /// overwriting the start with the map's own centre, so the party always lands in the middle —
    /// the search below only moves it when the centre is blocked.
    /// </returns>
    public (CombatMap Map, int X, int Y) Generate(int partyX, int partyY,
                                                  int width = 50, int height = 50)
    {
        var combat = new CombatMap(width, height);
        int w = combat.Width;
        int h = combat.Height;

        // The source window's top-left, and where the party sits inside it. partyCount* is
        // computed BEFORE the diagonal shift below, so it keeps the unshifted value -- reordering
        // these two statements moves the party half a map east.
        int startY = partyY - (h / 2) - 2;
        int startX = partyX - (w / 2) - 2;
        int partyCountX = partyX - startX;
        int partyCountY = partyY - startY;

        // The diagonal shift (#ifdef diagonalMap).
        startX += w / 2;

        int tempWidth = ((w + 5) * HorizontalMultiplier) + 1;
        int tempHeight = ((h + 5) * VerticalMultiplier) + 1;

        var stamped = new WallType[tempHeight * tempWidth];
        var junctions = new TerrainWallType[tempHeight * tempWidth];

        var (startCol, startRow) = StampFaces(stamped, tempWidth, tempHeight,
                                              startX, startY, partyCountX, partyCountY, w, h);

        ReduceToJunctions(stamped, junctions, tempWidth, tempHeight, w, h);

        CombatTerrainExpander.Expand(combat, junctions, tempWidth, startCol, startRow);

        combat.FillHoles();

        // ConvertTempMapToCombatTerrain's last act is to put the start at the map centre.
        int x = w / 2;
        int y = h / 2;
        FindEmptyCell(combat, ref x, ref y);
        return (combat, x, y);
    }

    /// <summary>
    /// Pass 1 — stamps every level cell's four faces into the temp grid.
    /// </summary>
    /// <returns>The party's column and row in the temp grid.</returns>
    private (int Col, int Row) StampFaces(WallType[] stamped, int tempWidth, int tempHeight,
                                          int startX, int startY,
                                          int partyCountX, int partyCountY, int w, int h)
    {
        void Set(int x, int y, WallType value)
        {
            // The original writes these unguarded and relies on the temp grid's five cells of
            // slack in each direction. Guarding is free here and makes the bounds a stated fact
            // rather than an assumption about arithmetic spread over 200 lines.
            if (x >= 0 && x < tempWidth && y >= 0 && y < tempHeight)
            {
                stamped[(y * tempWidth) + x] = value;
            }
        }

        int startCol = 0;
        int startRow = 0;

        int areaY = Mod(startY - 1, map.Height);
        int ty = 0;

        for (int countY = 0; countY < h + 2; countY++)
        {
            // 8,7,6,...,1 then back to 8: each row starts one column further left, which is what
            // shears the orthogonal map into a diagonal one.
            int tx = HorizontalMultiplier - (countY % HorizontalMultiplier);
            int areaX = Mod(startX - countY - 1 + (countY / HorizontalMultiplier), map.Width);

            for (int countX = 0; countX < w + 2; countX++)
            {
                if (countX == partyCountX && countY == partyCountY)
                {
                    startCol = tx + (2 * HorizontalMultiplier);
                    startRow = ty + (VerticalMultiplier * 3 / 2);
                }

                // North face: a run along the cell's top edge.
                if (IsWall(areaX, areaY, 0))
                {
                    for (int i = 0; i < HorizontalMultiplier; i++)
                    {
                        Set(tx + i, ty, WallType.Wall);
                    }
                }

                var door = DoorAt(areaX, areaY, 0);
                if (door != WallType.None)
                {
                    for (int i = 0; i < HorizontalMultiplier; i++)
                    {
                        Set(tx + i, ty, WallType.Wall);
                    }
                    if (door == WallType.OpenDoor)
                    {
                        Set(tx + 4, ty, WallType.None);
                        Set(tx + 5, ty, WallType.None);
                    }
                }

                // South face: the run sits one cell lower and is shifted right by a cell width
                // less one, because the row below has already sheared left.
                if (IsWall(areaX, areaY, 2))
                {
                    for (int i = 0; i < HorizontalMultiplier; i++)
                    {
                        Set(tx + i + HorizontalMultiplier - 1, ty + VerticalMultiplier,
                            WallType.Wall);
                    }
                }

                door = DoorAt(areaX, areaY, 2);
                if (door != WallType.None)
                {
                    for (int i = 0; i < HorizontalMultiplier; i++)
                    {
                        Set(tx + i + HorizontalMultiplier - 1, ty + VerticalMultiplier,
                            WallType.Wall);
                    }
                    if (door == WallType.OpenDoor)
                    {
                        // Reproduced verbatim, and it is wrong in the original: the gap is punched
                        // at row `ty` -- the cell's NORTH edge -- rather than the `y` the same
                        // block computes and then never uses (Drawtile.cpp:2853). The column is
                        // off too, because `w` has escaped the loop above with the value 8, so
                        // these land 19 and 20 columns right of `tx`, inside a different cell.
                        // An open south door therefore holes a neighbour's north wall and leaves
                        // its own shut. Kept because designs have been played against these maps
                        // for 25 years; see docs/PORTING-PLAN.md.
                        int escapedW = HorizontalMultiplier;
                        int x = tx + escapedW + HorizontalMultiplier - 1;
                        Set(x + 4, ty, WallType.None);
                        Set(x + 5, ty, WallType.None);
                    }
                }

                // West face: a diagonal run down-right, one column per row.
                if (IsWall(areaX, areaY, 3))
                {
                    for (int i = 0; i < HorizontalMultiplier; i++)
                    {
                        Set(tx + i, ty + i, WallType.Wall);
                    }
                }

                door = DoorAt(areaX, areaY, 3);
                if (door != WallType.None)
                {
                    for (int i = 0; i < HorizontalMultiplier; i++)
                    {
                        Set(tx + i, ty + i, WallType.Wall);
                    }
                    if (door == WallType.OpenDoor)
                    {
                        Set(tx + 3, ty + 3, WallType.None);
                        Set(tx + 4, ty + 4, WallType.None);
                    }
                }

                // East face: the same diagonal, one cell width to the right.
                if (IsWall(areaX, areaY, 1))
                {
                    for (int i = 0; i < HorizontalMultiplier; i++)
                    {
                        Set(tx + HorizontalMultiplier + i, ty + i, WallType.Wall);
                    }
                }

                door = DoorAt(areaX, areaY, 1);
                if (door != WallType.None)
                {
                    for (int i = 0; i < HorizontalMultiplier; i++)
                    {
                        Set(tx + HorizontalMultiplier + i, ty + i, WallType.Wall);
                    }
                    if (door == WallType.OpenDoor)
                    {
                        Set(tx + HorizontalMultiplier + 3, ty + 3, WallType.None);
                        Set(tx + HorizontalMultiplier + 4, ty + 4, WallType.None);
                    }
                }

                tx += HorizontalMultiplier;
                areaX = (areaX + 1) % map.Width;
            }

            ty += VerticalMultiplier;
            areaY = (areaY + 1) % map.Height;
        }

        return (startCol, startRow);
    }

    /// <summary>
    /// Pass 2 — turns each stamped wall square into a junction type
    /// (<c>getTerrainWallType</c>, <c>Drawtile.cpp:2295</c>).
    /// </summary>
    /// <remarks>
    /// <b>The four "compass" neighbours are diagonal.</b> North is (x−1, y−1) and south is
    /// (x+1, y+1), because the grid is already rotated; east and west are the ordinary horizontal
    /// neighbours. Reading north as (x, y−1) here produces a map that looks plausible and has
    /// every junction wrong.
    /// </remarks>
    private static void ReduceToJunctions(WallType[] stamped, TerrainWallType[] junctions,
                                          int tempWidth, int tempHeight, int w, int h)
    {
        WallType At(int x, int y) =>
            x >= 0 && x < tempWidth && y >= 0 && y < tempHeight
                ? stamped[(y * tempWidth) + x]
                : WallType.None;

        int ty = VerticalMultiplier;
        for (int countY = 0; countY < h * VerticalMultiplier; countY++, ty++)
        {
            int tx = 2 * HorizontalMultiplier;
            for (int countX = 0; countX < w * HorizontalMultiplier; countX++, tx++)
            {
                if (At(tx, ty) != WallType.Wall)
                {
                    continue;
                }

                bool north = At(tx - 1, ty - 1) == WallType.Wall;
                bool south = At(tx + 1, ty + 1) == WallType.Wall;
                bool east = At(tx + 1, ty) == WallType.Wall;
                bool west = At(tx - 1, ty) == WallType.Wall;

                junctions[(ty * tempWidth) + tx] = Junction(north, south, east, west);
            }
        }
    }

    private static TerrainWallType Junction(bool north, bool south, bool east, bool west)
    {
        int count = (north ? 1 : 0) + (south ? 1 : 0) + (east ? 1 : 0) + (west ? 1 : 0);
        return count switch
        {
            // End caps. The naming is from the drawn tile, not the neighbour: a wall whose only
            // neighbour is to the north is the BOTTOM terminator of a vertical run.
            1 => north ? TerrainWallType.VerticalBottomTerminator
               : south ? TerrainWallType.VerticalTopTerminator
               : east ? TerrainWallType.HorizontalLeftTerminator
               : TerrainWallType.HorizontalRightTerminator,

            2 => north && south ? TerrainWallType.Vertical
               : east && west ? TerrainWallType.Horizontal
               : north && west ? TerrainWallType.UpperLeftCorner
               : north && east ? TerrainWallType.UpperRightCorner
               : south && west ? TerrainWallType.LowerLeftCorner
               : TerrainWallType.LowerRightCorner,

            3 => !north ? TerrainWallType.BottomT
               : !south ? TerrainWallType.TopT
               : !east ? TerrainWallType.LeftT
               : TerrainWallType.RightT,

            4 => TerrainWallType.Intersection,

            // Unreachable: the caller only asks about squares that are themselves walls, and a
            // count of zero just means an isolated one. The original logs and returns NO_TERRAIN.
            _ => TerrainWallType.None,
        };
    }

    /// <summary>
    /// Whether a level cell's face carries a wall (<c>IsWallAt</c>, <c>Drawtile.cpp:1794</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A slot with an <i>open</i> blockage is not a wall — it is a doorway you can walk through,
    /// and <see cref="DoorAt"/> is what turns it into one. So the test is "has art AND is not
    /// open", not "has art".
    /// </para>
    /// </remarks>
    private bool IsWall(int x, int y, int direction)
    {
        var (wallSlot, blockage) = Resolve(x, y, direction);
        return wallSlot > 0 && blockage != (byte)BlockageType.Open;
    }

    /// <summary>
    /// A cell face's wall slot and blockage, with the 5.x per-cell overrides applied
    /// (<c>IsWallAt</c>'s <c>GetMapOverride</c> calls, <c>Drawtile.cpp:1825</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The wall override is shifted and then added back one.</b> <c>GetMapOverride</c> returns the
    /// zero-based index — the stored value minus one unless <c>UseWallIndex</c> is set — and
    /// <c>IsWallAt</c>/<c>GetDoorAt</c> then add one back so the <c>&gt; 0</c> wall test reads it as a
    /// real wall. The viewport's own getters do <b>not</b> add that one back, so the two draw a
    /// different wall set for the same override; that is the original's behaviour, kept.
    /// </para>
    /// <para>
    /// <b>The blockage override has no <c>_INDEX</c> twin</b>, so it is taken at face value with no
    /// shift (<c>BLOCKAGE_OVERRIDE</c>, <c>GlobalData.cpp:2375</c>).
    /// </para>
    /// </remarks>
    private (int WallSlot, byte Blockage) Resolve(int x, int y, int direction)
    {
        var cell = map.At(x, y);
        int wallSlot = cell?.WallAt(direction) ?? 0;
        byte blockage = cell?.BlockageAt(direction) ?? (byte)BlockageType.Open;

        if (overrides is null)
        {
            return (wallSlot, blockage);
        }

        if (overrides.At(0, x, y, direction) is byte storedWall)
        {
            int index = storedWall - (useWallIndex ? 0 : 1);
            if (index >= 0)
            {
                wallSlot = index + 1;
            }
        }

        if (overrides.At(4, x, y, direction) is byte storedBlockage)
        {
            blockage = storedBlockage;
        }

        return (wallSlot, blockage);
    }

    /// <summary>
    /// Whether a level cell's face carries a door, and whether it stands open
    /// (<c>GetDoorAt</c>, <c>Drawtile.cpp:1886</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A face is a door when its wall set names door art at all. Openness comes from the blockage,
    /// so the same wall set is a door or a wall depending on the cell.
    /// </para>
    /// <para>
    /// <b>West is read correctly here and is not in the original.</b> <c>GetDoorAt</c> builds its
    /// permutation as <c>{0,2,1,4}</c> — note the 4 — where every other site in the codebase uses
    /// <c>{0,2,1,3}</c>. Both <c>wall</c> and <c>blockage</c> are declared <c>[4]</c>
    /// (<c>Level.h:87</c>), so asking for a west door reads one past the end of each: the slot
    /// comes from the first byte of <c>blockage[0]</c> and the blockage from beyond the struct
    /// entirely, into the next cell. That is undefined behaviour with no defined result to
    /// reproduce, so this uses 3. The visible effect is that west doors in the original are
    /// arbitrary — sometimes drawn, sometimes not, depending on neighbouring bytes.
    /// </para>
    /// </remarks>
    private WallType DoorAt(int x, int y, int direction)
    {
        var (wallSlot, blockage) = Resolve(x, y, direction);

        if (wallSlot <= 0 || wallSlot >= MaxWallSets || wallSlot >= wallSets.Count)
        {
            return WallType.None;
        }

        if (string.IsNullOrEmpty(wallSets[wallSlot].DoorFile))
        {
            return WallType.None;
        }

        return blockage == (byte)BlockageType.Open
            ? WallType.OpenDoor
            : WallType.ClosedDoor;
    }

    /// <summary>
    /// Moves a coordinate to the nearest square an icon fits in
    /// (<c>findEmptyCell</c>, <c>Drawtile.cpp:3841</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A widening square search around the starting point, returning the first free square. The
    /// original's live copy is the <c>#ifdef newMonsterArrangement</c> one — that symbol is
    /// defined at <c>Combatants.h:67</c>, which <c>Drawtile.cpp</c> includes before the two
    /// definitions, so the second is dead.
    /// </para>
    /// <para>
    /// <paramref name="reachableFrom"/> supplies the original's reachability rule: when given, a
    /// square is only accepted if a path exists from it to that point, which is what stops a
    /// combatant being dropped into a sealed-off pocket. The reference always applies it (via
    /// <c>pathMgr.GetPath</c> to the party start) <i>except</i> when the direction argument is
    /// <c>PathBAD</c>; passing null here is that case.
    /// </para>
    /// <para>
    /// <b>One piece of the original is still absent.</b> Its per-direction clamping restricts the
    /// search to one side of the start depending on which way the encounter came from, so monsters
    /// appear ahead of the party rather than behind it. Four of its eight directions call
    /// <c>die()</c> immediately (<c>Drawtile.cpp:3884</c> onward), so only the cardinals are
    /// reachable — and no caller in the port passes a direction yet, because monster placement
    /// goes through the turtle rather than through here.
    /// </para>
    /// </remarks>
    public static bool FindEmptyCell(CombatMap combat, ref int x, ref int y,
                                     int width = 1, int height = 1,
                                     (int X, int Y)? reachableFrom = null)
    {
        ArgumentNullException.ThrowIfNull(combat);

        CombatPathFinder? finder = reachableFrom is null
            ? null
            : new CombatPathFinder(combat) { PathWidth = 1, PathHeight = 1 };

        bool Accept(int cx, int cy)
        {
            if (combat.Obstacle(cx, cy, width, height) != ObstacleType.None)
            {
                return false;
            }

            if (finder is null)
            {
                return true;
            }

            var (tx, ty) = reachableFrom!.Value;

            // "Already there" counts as reachable; the reference's GetPath returns the same -1 for
            // that as for failure, but its caller only asks whether a route exists.
            return finder.IsAlreadyWithin(cx, cy, tx, ty, tx, ty)
                   || finder.To(cx, cy, tx, ty) is not null;
        }

        // The starting square is tested without the reachability rule, exactly as the reference
        // does: its first line is a bare ObsticalType check before the search begins.
        if (combat.Obstacle(x, y, width, height) == ObstacleType.None)
        {
            return true;
        }

        int maxRadius = Math.Max(combat.Width - 1, combat.Height - 1);
        for (int radius = 1; radius < maxRadius; radius++)
        {
            int left = Math.Max(0, x - radius);
            int top = Math.Max(0, y - radius);
            int right = Math.Min(combat.Width - 1, x + radius);
            int bottom = Math.Min(combat.Height - 1, y + radius);

            for (int j = top; j <= bottom; j++)
            {
                for (int i = left; i <= right; i++)
                {
                    if (Accept(i, j))
                    {
                        x = i;
                        y = j;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Euclidean modulo — the original adds <c>100 * extent</c> before taking <c>%</c>.</summary>
    private static int Mod(int value, int extent)
    {
        int result = value % extent;
        return result < 0 ? result + extent : result;
    }
}
