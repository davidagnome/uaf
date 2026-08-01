namespace UAFcore;

/// <summary>
/// One combat terrain tile's metadata (<c>TILE_DATA</c>, <c>Drawtile.h:72</c>).
/// </summary>
/// <param name="SourceX">Left edge of the tile's frame in the terrain art sheet.</param>
/// <param name="SourceY">Top edge of the tile's frame in the terrain art sheet.</param>
/// <param name="SeeThrough">Whether line of sight passes through. <b>The C++ field is named
/// <c>tile_invisible</c>, which reads backwards</b> — the comment at <c>Drawtile.cpp:205</c>
/// documents 1 as "see-thru" and 0 as "blocks visible", so the sense is inverted from the
/// name.</param>
/// <param name="Passable">Whether a combatant can move into the tile.</param>
/// <param name="Enabled">Whether the tile may be placed at all. <c>SetDungeon</c> and
/// <c>SetWilderness</c> silently drop a write to a disabled tile.</param>
public readonly record struct CombatTile(
    int SourceX, int SourceY, bool SeeThrough, bool Passable, bool Enabled);

/// <summary>Why a cell cannot be entered (<c>OBSTICAL_TYPE</c>, <c>Drawtile.h:95</c>).</summary>
/// <remarks>
/// The original's spelling of "obstacle" is not preserved; the values are.
/// </remarks>
public enum ObstacleType
{
    None = 0,
    Wall = 1,
    Occupied = 2,
    OffMap = 3,
    LingeringSpell = 4,
}

/// <summary>Which tile table a combat map is drawn from.</summary>
public enum CombatTerrainKind
{
    Dungeon,
    Wilderness,
}

/// <summary>
/// The combat terrain grid: what is in each square, and who is standing on it.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>terrain[][]</c> (<c>Drawtile.cpp:114</c>) together with the free functions in
/// <c>Drawtile.cpp</c> that read and write it — <c>ValidCoords</c>, <c>HaveMovability</c>,
/// <c>ObsticalType</c>, <c>placeCombatant</c>, <c>getCombatantInCell</c> and the rest. In the
/// original these are file-scope functions over a global; here they are instance members, because
/// nothing about them needs to be global and a combat map that can be constructed in a test is
/// the whole point of porting this first.
/// </para>
/// <para>
/// <b>The grid is not the level map.</b> It is a separate, much finer square grid generated from
/// the level's cells when combat starts — see <see cref="CombatMapGenerator"/>. Its size comes
/// from <c>config.txt</c> (<c>COMBAT_MAP_WIDTH</c> / <c>COMBAT_MAP_HEIGHT</c>, default 50×50,
/// clamped to 25..500 at <c>Globals.cpp:2861</c>), not from the level.
/// </para>
/// <para>
/// Occupancy is stored on the grid rather than derived from a combatant list, exactly as the
/// original does: each cell holds the index of the combatant standing on it, and a second index
/// for a dying one, so that a corpse and a live combatant can share a square and the corpse draws
/// first.
/// </para>
/// </remarks>
public sealed class CombatMap
{
    /// <summary>No combatant (<c>NO_DUDE</c>, <c>Char.h:183</c>).</summary>
    public const int NoDude = -1;

    /// <summary>An empty terrain square (<c>NO_TERRAIN</c>, <c>Drawtile.cpp:123</c>).</summary>
    public const int NoTerrain = 0;

    /// <summary>Combat tile size in pixels (<c>Externs.h:852</c>).</summary>
    public const int TileWidth = 48;

    /// <summary>Combat tile size in pixels (<c>Externs.h:852</c>).</summary>
    public const int TileHeight = 48;

    /// <summary>The smallest map <c>config.txt</c> may ask for (<c>Globals.cpp:2868</c>).</summary>
    public const int MinExtent = 25;

    /// <summary>The largest map <c>config.txt</c> may ask for (<c>Globals.cpp:2869</c>).</summary>
    public const int MaxExtent = 500;

    private readonly short[] cells;
    private readonly short[] occupants;
    private readonly short[] dying;

    /// <summary>Creates an empty map. Extents are clamped the way the original clamps them.</summary>
    public CombatMap(int width = 50, int height = 50, CombatTerrainKind kind = CombatTerrainKind.Dungeon)
    {
        // Globals.cpp:2868 applies max(25, ...) then min(500, ...) to whatever config.txt asked
        // for. Doing it here rather than at the config boundary means a map built directly by a
        // test cannot be a size the engine would never see.
        Width = Math.Clamp(width, MinExtent, MaxExtent);
        Height = Math.Clamp(height, MinExtent, MaxExtent);
        Kind = kind;

        cells = new short[Width * Height];
        occupants = new short[Width * Height];
        dying = new short[Width * Height];
        Array.Fill(occupants, (short)NoDude);
        Array.Fill(dying, (short)NoDude);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Which tile table <see cref="Tiles"/> resolves against.</summary>
    public CombatTerrainKind Kind { get; }

    /// <summary>
    /// The active tile table (<c>CurrentTileData</c>, set by whichever generator ran).
    /// </summary>
    public CombatTile[] Tiles =>
        Kind == CombatTerrainKind.Dungeon ? CombatTiles.Dungeon : CombatTiles.Wilderness;

    /// <summary>
    /// How many combatants exist, used to reject a stale occupant index.
    /// </summary>
    /// <remarks>
    /// <c>getCombatantInCell</c> compares the stored index against
    /// <c>combatData.NumCombatants()</c> and <b>clears the cell</b> when it is out of range
    /// (<c>Drawtile.cpp:1531</c>) — a self-healing read, not just a guard. Reproduced, including
    /// the write. Left at zero the grid reports nobody, which is the right answer for a map with
    /// no combat running.
    /// </remarks>
    public int CombatantCount { get; set; }

    /// <summary>
    /// Whether a coordinate is on the grid (<c>ValidCoords</c>, <c>Drawtile.cpp:1233</c>).
    /// </summary>
    /// <remarks>
    /// <b>The original takes y first.</b> Every call site writes <c>ValidCoords(y, x)</c> while
    /// its neighbours <c>coordsOnMap</c> and <c>ObsticalType</c> take x first. Both orders are
    /// x-then-y here; the transposition is a trap in the source, not a property of the format.
    /// </remarks>
    public bool Contains(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>
    /// Whether an <paramref name="width"/>×<paramref name="height"/> icon fits entirely on the
    /// grid with its top-left at <paramref name="x"/>,<paramref name="y"/>
    /// (<c>coordsOnMap</c>, <c>Drawtile.cpp:1444</c>).
    /// </summary>
    /// <remarks>
    /// Combat icons are up to 4×4, and the coordinate given is always the top-left corner.
    /// </remarks>
    public bool Fits(int x, int y, int width, int height)
    {
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                if (!Contains(x + j, y + i))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>The terrain tile index in a square, or <see cref="NoTerrain"/>.</summary>
    public int CellAt(int x, int y) => Contains(x, y) ? cells[(y * Width) + x] : NoTerrain;

    /// <summary>Whether a square has no terrain yet (<c>isEmpty</c>, <c>Drawtile.cpp:1245</c>).</summary>
    /// <remarks>Off-map reads as <b>not</b> empty, which is what the original returns.</remarks>
    public bool IsEmpty(int x, int y) => Contains(x, y) && cells[(y * Width) + x] == NoTerrain;

    /// <summary>
    /// Places a terrain tile, ignoring the write when the tile is out of range or disabled
    /// (<c>SetDungeon</c> / <c>SetWilderness</c>, <c>Drawtile.cpp:1277</c>, <c>:1290</c>).
    /// </summary>
    /// <remarks>
    /// The silent drop is deliberate in the original and load-bearing: the dungeon table's tile 26
    /// and eleven of the wilderness tiles are disabled, and the conversion tables still name them.
    /// Throwing here would turn a normal map generation into an error.
    /// </remarks>
    public void SetTile(int x, int y, int tile)
    {
        if (!Contains(x, y))
        {
            return;
        }

        var table = Tiles;
        if (tile < 1 || tile >= table.Length || !table[tile].Enabled)
        {
            return;
        }

        cells[(y * Width) + x] = (short)tile;
    }

    /// <summary>
    /// Whether a combatant can stand in a square (<c>HaveMovability</c>, <c>Drawtile.cpp:1336</c>).
    /// </summary>
    /// <remarks>
    /// Terrain only — occupancy is <see cref="Obstacle"/>'s business. An empty square is
    /// <b>impassable</b>, because the index guard rejects <c>cell &lt; 1</c>; the generators fill
    /// every hole with a floor tile before anyone walks on the map.
    /// </remarks>
    public bool IsPassable(int x, int y)
    {
        int cell = CellAt(x, y);
        var table = Tiles;
        return cell >= 1 && cell < table.Length && table[cell].Passable;
    }

    /// <summary>
    /// Whether line of sight passes through a square (<c>HaveVisibility</c>,
    /// <c>Drawtile.cpp:1305</c>).
    /// </summary>
    /// <remarks>
    /// <paramref name="reflects"/> mirrors the original's out-parameter: a square that blocks
    /// sight because it holds a wall reflects, whereas one that is off-map or empty does not.
    /// Spell bouncing needs the distinction.
    /// </remarks>
    public bool IsSeeThrough(int x, int y, out bool reflects)
    {
        reflects = false;
        int cell = CellAt(x, y);
        var table = Tiles;
        if (!Contains(x, y) || cell < 1 || cell >= table.Length)
        {
            return false;
        }

        if (table[cell].SeeThrough)
        {
            return true;
        }

        reflects = true;
        return false;
    }

    /// <summary>
    /// What stops an icon being placed at a square (<c>ObsticalType</c>, <c>Drawtile.cpp:1359</c>).
    /// </summary>
    /// <param name="ignoreCombatant">
    /// A combatant index to treat as absent — the original passes the current combatant, so that
    /// asking "can I stand here?" does not trip over yourself. <see cref="NoDude"/> ignores
    /// nobody.
    /// </param>
    /// <remarks>
    /// <para>
    /// The order of the three tests matters and is preserved: off-map beats wall, wall beats
    /// occupied. A caller that only wants "is it free" can compare against
    /// <see cref="ObstacleType.None"/>, but the round state machine wants to know which.
    /// </para>
    /// <para>
    /// <see cref="ObstacleType.LingeringSpell"/> is never returned yet — it needs the active-spell
    /// list, which is not ported. The value exists so callers can switch on the full enum now, and
    /// the case is named rather than silently absent.
    /// </para>
    /// </remarks>
    public ObstacleType Obstacle(int x, int y, int width = 1, int height = 1,
                                 bool checkOccupants = true, int ignoreCombatant = NoDude)
    {
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                if (!Fits(x + j, y + i, 1, 1))
                {
                    return ObstacleType.OffMap;
                }

                if (!IsPassable(x + j, y + i))
                {
                    return ObstacleType.Wall;
                }

                if (checkOccupants)
                {
                    int dude = OccupantAt(x + j, y + i, 1, 1);
                    if (dude != NoDude && dude != ignoreCombatant)
                    {
                        return ObstacleType.Occupied;
                    }
                }
            }
        }

        return ObstacleType.None;
    }

    /// <summary>
    /// Marks a combatant as standing on every square of its icon
    /// (<c>placeCombatant</c>, <c>Drawtile.cpp:1463</c>).
    /// </summary>
    /// <remarks>
    /// Squares outside the grid are skipped rather than rejected, so a partly-off-map placement
    /// writes what it can. That is what the original does; the callers that care test
    /// <see cref="Fits"/> first.
    /// </remarks>
    public void Place(int x, int y, int combatant, int width = 1, int height = 1) =>
        Write(occupants, x, y, combatant, width, height);

    /// <summary>
    /// Marks a dying combatant (<c>placeDyingCombatant</c>, <c>Drawtile.cpp:1492</c>).
    /// </summary>
    /// <remarks>
    /// A separate layer so a corpse and a live combatant can occupy one square. Only the live
    /// layer blocks movement.
    /// </remarks>
    public void PlaceDying(int x, int y, int combatant, int width = 1, int height = 1) =>
        Write(dying, x, y, combatant, width, height);

    /// <summary>Clears a combatant from every square of its icon.</summary>
    public void Remove(int x, int y, int width = 1, int height = 1) =>
        Write(occupants, x, y, NoDude, width, height);

    private void Write(short[] layer, int x, int y, int combatant, int width, int height)
    {
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                if (Contains(x + j, y + i))
                {
                    layer[((y + i) * Width) + x + j] = (short)combatant;
                }
            }
        }
    }

    /// <summary>
    /// The live combatant standing anywhere in a rectangle, or <see cref="NoDude"/>
    /// (<c>getCombatantInCell</c>, <c>Drawtile.cpp:1520</c>).
    /// </summary>
    public int OccupantAt(int x, int y, int width = 1, int height = 1, int ignoreCombatant = NoDude) =>
        Read(occupants, x, y, width, height, ignoreCombatant);

    /// <summary>
    /// The dying combatant in a rectangle, or <see cref="NoDude"/>
    /// (<c>getDeadCombatantInCell</c>, <c>Drawtile.cpp:1539</c>).
    /// </summary>
    public int DyingAt(int x, int y, int width = 1, int height = 1, int ignoreCombatant = NoDude) =>
        Read(dying, x, y, width, height, ignoreCombatant);

    private int Read(short[] layer, int x, int y, int width, int height, int ignoreCombatant)
    {
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                if (!Contains(x + j, y + i))
                {
                    continue;
                }

                int index = (((y + i) * Width) + x + j);
                int dude = layer[index];
                if (dude == NoDude || dude == ignoreCombatant)
                {
                    continue;
                }

                if (dude < CombatantCount)
                {
                    return dude;
                }

                // The original clears a stale index here rather than merely skipping it, so a
                // combatant removed from the list cannot keep blocking a square.
                layer[index] = NoDude;
            }
        }

        return NoDude;
    }

    /// <summary>
    /// Straight-line distance in squares, rounded to nearest
    /// (<c>Distance(sx, sy, dx, dy)</c>, <c>Drawtile.cpp:1777</c>).
    /// </summary>
    /// <remarks>
    /// Euclidean and rounded with <c>floor(d + 0.5)</c>, not Chebyshev or Manhattan — a diagonal
    /// neighbour is 1 away, but two diagonal steps are 3 rather than 2. Range checks throughout
    /// combat use this, so the rounding is not cosmetic.
    /// </remarks>
    public static int Distance(int sx, int sy, int dx, int dy)
    {
        int x = sx - dx;
        int y = sy - dy;
        return (int)Math.Floor(Math.Sqrt((x * x) + (y * y)) + 0.5);
    }

    /// <summary>
    /// Fills every empty square with the terrain's floor tile
    /// (the hole-filling loop at <c>Drawtile.cpp:2828</c>).
    /// </summary>
    /// <remarks>
    /// Runs after generation. Until it does, <see cref="IsPassable"/> reports the whole map
    /// impassable, because an empty square fails the <c>cell &gt;= 1</c> guard.
    /// </remarks>
    public void FillHoles()
    {
        int floor = Kind == CombatTerrainKind.Dungeon
            ? CombatTiles.DungeonEmptyTile
            : CombatTiles.WildernessEmptyTile;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (IsEmpty(x, y))
                {
                    SetTile(x, y, floor);
                }
            }
        }
    }
}
