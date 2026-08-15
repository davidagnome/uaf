using UAF.Serialization;
using UAFcore;

namespace UAFedit.Map;

/// <summary>
/// Everything the map draws about one side of one cell.
/// </summary>
/// <param name="WallIndex">
/// The wall-set slot, or <see cref="MapPalette.NoWall"/> when the side carries no wall. Not an
/// index into the design's wall table until it has been range-checked — see
/// <see cref="LevelMapCell.HasDoorGap"/>.
/// </param>
/// <param name="Blockage">What stops the party leaving through this side.</param>
/// <param name="Background">
/// The backdrop slot for this side. Stored in compass order, unlike walls and blockage.
/// </param>
public readonly record struct LevelMapSide(int WallIndex, BlockageType Blockage, byte Background);

/// <summary>
/// One cell as the map view needs it: its four sides, and the three per-cell facts the modes draw.
/// </summary>
public sealed record LevelMapCell(
    int X, int Y,
    LevelMapSide North, LevelMapSide East, LevelMapSide South, LevelMapSide West,
    byte Zone, bool HasEvent, bool IsEntryPoint, int EntryPointIndex, bool IsStartLocation)
{
    /// <summary>The side facing a direction.</summary>
    public LevelMapSide Side(Facing facing) => ((int)facing & 3) switch
    {
        0 => North,
        1 => East,
        2 => South,
        _ => West,
    };

    /// <summary>
    /// Whether a side's middle wall dash is left out to show a doorway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the entire representation of a door on the 2-D map.</b> <c>DrawSquare</c>
    /// (<c>DlgPicture.cpp:1053</c>) draws the first and third dashes unconditionally and the middle
    /// one only <c>if (WallSets[wall].doorSurface &lt;= 0)</c> — so a wall set with door art is
    /// drawn as a wall with a gap in it, in the wall's own colour, and nothing else marks it. There
    /// is no door colour and no door glyph.
    /// </para>
    /// <para>
    /// <b>The test is on the loaded surface, not on the filename</b>, which matters: a wall set
    /// naming a door file the design does not ship has <c>doorSurface</c> of -1 and draws solid.
    /// This port has no surfaces at this layer and asks whether the slot names a door at all, so an
    /// editor built on it shows the gap where the original would show a solid wall. That is the
    /// better answer for an editor — the author wrote a door and wants to see it — but it is a
    /// difference, and it is the reason this is a method on the model rather than a flag baked into
    /// <see cref="LevelMapSide"/>.
    /// </para>
    /// </remarks>
    public bool HasDoorGap(Facing facing, IReadOnlyList<WallSetSlot> wallSets)
    {
        ArgumentNullException.ThrowIfNull(wallSets);

        int index = Side(facing).WallIndex;
        return index > MapPalette.NoWall
            && index < wallSets.Count
            && !string.IsNullOrWhiteSpace(wallSets[index].DoorFile);
    }
}

/// <summary>
/// A level's grid, answered per cell in the terms the 2-D map draws in.
/// </summary>
/// <remarks>
/// <para>
/// The pure half of the map view: <see cref="LevelMapView"/> is a drawing shell over this, and
/// everything worth being sure of — which side is which, what the wall index is, where the torus
/// wraps — is decided here where a test can reach it.
/// </para>
/// <para>
/// <b>The editor and the engine do not resolve a wall the same way, and the difference is real
/// rather than a porting gap.</b> <c>getWallSurface</c> (<c>Viewport.cpp:1065</c>) consults
/// <c>LEVEL_STATS::GetMapOverride</c> and lets a 5.x per-cell override <i>win</i> over the cell's
/// own wall index; <c>DrawSquare</c> reads <c>spot.wall[i]</c> and stops. So the engine can show a
/// wall the editor's map does not, and vice versa. The overrides are exposed here through
/// <see cref="ShowOverrides"/>, defaulting off to match the editor, because they are runtime state
/// a script wrote and not something the author drew.
/// </para>
/// <para>
/// Two smaller divergences in the same direction: the engine hides a wall whose blockage is a
/// <i>found</i> secret door (<c>haveSecretDoor</c>, <c>Level.cpp:1822</c>), which needs party state
/// the editor has none of; and it clamps a wall index at or above <c>MAX_WALLSETS</c> to nothing,
/// where the editor draws it in whatever colour slot the palette holds.
/// </para>
/// </remarks>
public sealed class LevelMapModel
{
    private readonly IReadOnlyList<AreaMapCell> cells;
    private readonly LevelStats? stats;

    /// <summary>
    /// Builds a model over a level that has been read whole.
    /// </summary>
    /// <param name="stats">
    /// The level's <c>LEVEL_STATS</c> from <c>game.dat</c>, when the caller has it. It carries the
    /// entry points and the override tables, none of which live in the <c>.lvl</c> file.
    /// </param>
    /// <param name="startLocation">
    /// The design's start square, when it is on this level. Drawn only in
    /// <see cref="MapDisplayMode.StartLocation"/>.
    /// </param>
    public LevelMapModel(LevelFile level, LevelStats? stats = null,
                         (int X, int Y)? startLocation = null)
    {
        ArgumentNullException.ThrowIfNull(level);

        Level = level;
        this.stats = stats;
        cells = level.Cells;
        Width = level.Width;
        Height = level.Height;
        StartLocation = startLocation;

        // A level whose grid is shorter than its declared extent is not a shape the reader can
        // produce -- it allocates width * height -- but the model is also constructed from
        // hand-built cells in tests, and a short grid there would fault deep inside a draw.
        if (cells.Count < Width * Height)
        {
            throw new ArgumentException(
                $"level declares {Width}x{Height} but carries {cells.Count} cells", nameof(level));
        }
    }

    /// <summary>The level as read, for the wall table and anything else a caller needs.</summary>
    public LevelFile Level { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The design's start square when it lies on this level.</summary>
    public (int X, int Y)? StartLocation { get; }

    /// <summary>The design's wall sets, which is where door art is named.</summary>
    public IReadOnlyList<WallSetSlot> WallSets => Level.WallSets;

    /// <summary>
    /// Whether to apply the 5.x per-cell override tables. Off by default — see the type remarks.
    /// </summary>
    public bool ShowOverrides { get; init; }

    /// <summary>
    /// Whether the design's global ASL carries <c>UseWallIndex</c>
    /// (<c>Level.cpp:3098</c>).
    /// </summary>
    /// <remarks>
    /// Only meaningful with <see cref="ShowOverrides"/> on. Without the flag, a wall override's
    /// stored value is an <i>ordinal</i> and one is subtracted to reach the index
    /// (<c>GlobalData.cpp:2378</c>); with it, the stored value already is the index. It is a
    /// design-wide switch that lives in the global ASL rather than in the level, so the caller has
    /// to supply it.
    /// </remarks>
    public bool UseWallIndex { get; init; }

    /// <inheritdoc cref="UseWallIndex"/>
    /// <remarks>The same switch for door, background and overlay overrides.</remarks>
    public bool UseDoorAndOverlayIndex { get; init; }

    /// <summary>
    /// Wraps a coordinate onto the level's torus.
    /// </summary>
    /// <remarks>
    /// The map view tiles rather than clamps, for the reason the original's own comment gives
    /// (<c>DlgPicture.cpp:889</c>): switching to a smaller level used to leave the previous level's
    /// walls on screen, and drawing the torus was the cheapest fix. It also matches the engine,
    /// where a level genuinely has no edges (<c>Viewport.cpp:4105</c>).
    /// </remarks>
    public (int X, int Y) Wrap(int x, int y) =>
        (ViewMap.Wrap(x, Math.Max(Width, 1)), ViewMap.Wrap(y, Math.Max(Height, 1)));

    /// <summary>The raw cell at a coordinate, wrapped onto the torus.</summary>
    public AreaMapCell CellAt(int x, int y)
    {
        var (wx, wy) = Wrap(x, y);
        return cells[(wy * Width) + wx];
    }

    /// <summary>Everything the map draws about one cell, at wrapped coordinates.</summary>
    public LevelMapCell At(int x, int y)
    {
        var (wx, wy) = Wrap(x, y);
        var cell = cells[(wy * Width) + wx];

        int entryPoint = EntryPointAt(wx, wy);

        return new LevelMapCell(
            wx, wy,
            Side(cell, wx, wy, Facing.North),
            Side(cell, wx, wy, Facing.East),
            Side(cell, wx, wy, Facing.South),
            Side(cell, wx, wy, Facing.West),
            cell.Zone,
            cell.EventExists,
            entryPoint >= 0,
            entryPoint,
            StartLocation is { } start && start.X == wx && start.Y == wy);
    }

    /// <summary>
    /// The entry-point slot standing on a square, or -1.
    /// </summary>
    /// <remarks>
    /// <b>The first match wins and the search stops</b> (<c>DlgPicture.cpp:967</c>), so a square
    /// carrying two entry points is drawn in the lower one's colour. The table is a fixed eight and
    /// unused slots are (0,0), which means <b>square 0,0 is reported as an entry point on nearly
    /// every level</b> — the original does the same, and an author looking at a red dot in the
    /// corner of every map is seeing the table's padding rather than their own data.
    /// </remarks>
    public int EntryPointAt(int x, int y)
    {
        if (stats?.EntryPoints is not { } points)
        {
            return -1;
        }

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].X == x && points[i].Y == y)
            {
                return i;
            }
        }

        return -1;
    }

    private LevelMapSide Side(AreaMapCell cell, int x, int y, Facing facing)
    {
        // WallAt and BlockageAt do the north/SOUTH/east/west permutation; the background accessors
        // must not, because the four background bytes really are stored in compass order.
        int wall = cell.WallAt((int)facing);
        var blockage = (BlockageType)(cell.BlockageAt((int)facing) ?? (byte)BlockageType.Blocked);

        byte background = ((int)facing & 3) switch
        {
            0 => cell.NorthBg,
            1 => cell.EastBg,
            2 => cell.SouthBg,
            _ => cell.WestBg,
        };

        if (ShowOverrides && stats?.Overrides is { } overrides)
        {
            if (Override(overrides, OverrideType.Wall, x, y, facing, UseWallIndex) is { } w)
            {
                wall = w;
            }

            if (Override(overrides, OverrideType.Blockage, x, y, facing, useIndex: true) is { } b)
            {
                blockage = (BlockageType)b;
            }

            if (Override(overrides, OverrideType.Background, x, y, facing,
                         UseDoorAndOverlayIndex) is { } bg)
            {
                background = (byte)Math.Clamp(bg, 0, 255);
            }
        }

        return new LevelMapSide(wall, blockage, background);
    }

    /// <summary><c>OVERRIDE_TYPE</c>'s user ordinals (<c>GlobalData.h:469</c>).</summary>
    private enum OverrideType
    {
        Wall = 0,
        Door = 1,
        Background = 2,
        Overlay = 3,
        Blockage = 4,
    }

    /// <summary>
    /// One override, converted from the stored ordinal to an index.
    /// </summary>
    /// <remarks>
    /// <c>GetMapOverride</c> returns <c>n - adj</c> where <c>adj</c> is 1 unless the design set the
    /// matching global flag. <see cref="OverrideType.Blockage"/> has no <c>_INDEX</c> form at all
    /// and is never adjusted, which is why it is passed <c>useIndex: true</c> rather than a flag of
    /// its own.
    /// </remarks>
    private static int? Override(WallOverrides overrides, OverrideType kind, int x, int y,
                                 Facing facing, bool useIndex)
    {
        if (overrides.At((int)kind, x, y, (int)facing & 3) is not { } value)
        {
            return null;
        }

        return useIndex ? value : value - 1;
    }
}
