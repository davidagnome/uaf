using UAF.Media;
using UAF.Serialization;
using UAFcore;

namespace UAFedit.Map;

/// <summary>
/// What the map is being edited for, which decides what the middle of each cell shows
/// (<c>UAFWinEd/UAFWinEd.h:80</c>).
/// </summary>
/// <remarks>
/// The walls are drawn in every mode; only the centre block and the blockage marks change. The
/// original's <c>BLOCKAGE_MODE</c> is missing here on purpose — it is the overland map's mode and
/// <c>DrawSquare</c>, the dungeon path, treats reaching it as a bug and rewrites it to
/// <c>WALL_MODE</c> (<c>DlgPicture.cpp:1197</c>).
/// </remarks>
public enum MapDisplayMode
{
    /// <summary>Walls and blockages — the editor's default and its main screen.</summary>
    Walls = 0,

    /// <summary>A white block on every square carrying an event.</summary>
    Events = 1,

    /// <summary>Every square's zone, in the zone's colour.</summary>
    Zones = 2,

    /// <summary>The four per-side backdrop slots, drawn where the blockage marks go.</summary>
    Backgrounds = 3,

    /// <summary>A white block on the design's start square.</summary>
    StartLocation = 4,

    /// <summary>A coloured block on each of the level's eight entry points.</summary>
    EntryPoints = 5,
}

/// <summary>What a mark means, so a test can talk about the drawing without counting rectangles.</summary>
public enum MapMarkKind
{
    /// <summary>The whole cell's backdrop.</summary>
    Fill,

    /// <summary>One of the four grid dots.</summary>
    Corner,

    /// <summary>A wall dash.</summary>
    Wall,

    /// <summary>A blockage mark.</summary>
    Blockage,

    /// <summary>A per-side backdrop slot, in <see cref="MapDisplayMode.Backgrounds"/>.</summary>
    Background,

    /// <summary>The centre block, in whichever mode fills it.</summary>
    Center,

    /// <summary>The selected square's facing arrow.</summary>
    Arrow,
}

/// <summary>One filled rectangle, positioned relative to its cell's top-left corner.</summary>
public readonly record struct MapMark(SurfaceRect Rect, MapColor Color, MapMarkKind Kind);

/// <summary>
/// Turns a cell into the list of filled rectangles that draw it.
/// </summary>
/// <remarks>
/// <para>
/// <c>CDlgPartialMapPicture::RenderDib</c> and <c>DrawSquare</c>
/// (<c>UAFWinEd/DlgPicture.cpp:955-1200</c>). The original draws straight into a DIB through
/// <c>FillRect</c>; this yields the rectangles instead, so the order, the overdraw and the choice
/// of colour can all be asserted without a device.
/// </para>
/// <para>
/// <b>The order is load-bearing because the marks overlap.</b> The cell fill covers everything, the
/// corner dots sit on top of it, and the wall dashes sit on top of the dots — a cell walled on all
/// four sides has its corners painted over. Emitting the walls first would leave four white pixels
/// showing through at each corner, which is a different map.
/// </para>
/// <para>
/// <b>Not ported: the access overlay.</b> <c>DrawSquare</c> can fill the centre block with a
/// three-bit reachability colour when <c>m_accessMode</c> is on (<c>DlgPicture.cpp:1131</c>), which
/// comes from <c>levelAccess.ComputeAccess</c> running the level's <c>$ACCESSPARAMS</c> script over
/// the whole grid. That is a solver, not a drawing decision, and it belongs outside this class when
/// it arrives.
/// </para>
/// </remarks>
public sealed class LevelMapPainter(MapCellGeometry geometry, MapPalette palette)
{
    /// <summary>The palette slot the empty cell is filled with — black.</summary>
    public const int EmptyCellColor = 0;

    /// <summary>
    /// The palette slot the cell is filled with in zone and entry-point modes — dark red.
    /// </summary>
    /// <remarks>
    /// <c>RenderDib</c> switches the backdrop for exactly those two modes
    /// (<c>DlgPicture.cpp:978</c>). Both draw their information as a coloured centre block, and
    /// against black the darker zone colours would be unreadable.
    /// </remarks>
    public const int ModalCellColor = 7;

    /// <summary>The palette slot the grid dots and the event/start markers use — white.</summary>
    public const int MarkerColor = 15;

    /// <summary>
    /// The order the original walks a cell's sides in: north, <b>south</b>, east, west.
    /// </summary>
    /// <remarks>
    /// <c>DrawSquare</c>'s four <c>if</c> blocks are written against <c>spot.wall[0]</c>…<c>[3]</c>,
    /// so this is the storage order rather than a compass. It only shows where two sides' marks
    /// touch, which the default geometry avoids, but a config that widened the dashes would notice.
    /// </remarks>
    public static IReadOnlyList<Facing> DrawOrder { get; } =
        [Facing.North, Facing.South, Facing.East, Facing.West];

    private readonly MapCellGeometry geometry =
        geometry ?? throw new ArgumentNullException(nameof(geometry));

    private readonly MapPalette palette =
        palette ?? throw new ArgumentNullException(nameof(palette));

    /// <summary>The geometry this painter positions marks with.</summary>
    public MapCellGeometry Geometry => geometry;

    /// <summary>The palette this painter colours marks from.</summary>
    public MapPalette Palette => palette;

    /// <summary>What the centre of each cell shows.</summary>
    public MapDisplayMode Mode { get; init; } = MapDisplayMode.Walls;

    /// <summary>
    /// Every rectangle that draws one cell, in the order the original fills them.
    /// </summary>
    /// <param name="wallSets">
    /// The design's wall table, consulted only for door art. Pass an empty list to draw every wall
    /// solid.
    /// </param>
    public IEnumerable<MapMark> Marks(LevelMapCell cell, IReadOnlyList<WallSetSlot> wallSets)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(wallSets);

        int fill = Mode is MapDisplayMode.Zones or MapDisplayMode.EntryPoints
            ? ModalCellColor
            : EmptyCellColor;

        yield return new MapMark(geometry.Square, palette.Backdrop(fill), MapMarkKind.Fill);

        foreach (var corner in geometry.Corners())
        {
            yield return new MapMark(corner, palette.Backdrop(MarkerColor), MapMarkKind.Corner);
        }

        foreach (var facing in DrawOrder)
        {
            foreach (var mark in WallMarks(cell, facing, wallSets))
            {
                yield return mark;
            }
        }

        foreach (var mark in CenterMarks(cell))
        {
            yield return mark;
        }
    }

    /// <summary>
    /// The dashes drawing one side's wall, or nothing when the side has none.
    /// </summary>
    /// <remarks>
    /// Two dashes rather than three when the wall set names door art — see
    /// <see cref="LevelMapCell.HasDoorGap"/>. The gap is the door.
    /// </remarks>
    public IEnumerable<MapMark> WallMarks(LevelMapCell cell, Facing facing,
                                          IReadOnlyList<WallSetSlot> wallSets)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(wallSets);

        int index = cell.Side(facing).WallIndex;
        if (index <= MapPalette.NoWall)
        {
            yield break;
        }

        var color = palette.Wall(index);

        yield return new MapMark(geometry.SegmentRect(facing, MapSegment.Start), color,
                                 MapMarkKind.Wall);
        yield return new MapMark(geometry.SegmentRect(facing, MapSegment.End), color,
                                 MapMarkKind.Wall);

        if (!cell.HasDoorGap(facing, wallSets))
        {
            yield return new MapMark(geometry.SegmentRect(facing, MapSegment.Middle), color,
                                     MapMarkKind.Wall);
        }
    }

    /// <summary>
    /// The marks the current mode puts inside the cell's walls.
    /// </summary>
    /// <remarks>
    /// <see cref="MapDisplayMode.Walls"/> and <see cref="MapDisplayMode.Backgrounds"/> both draw
    /// per-side marks in the obstruction slots and the other four draw a single centre block, which
    /// is why the two families are not split into separate methods: they compete for the same
    /// pixels and only one mode ever runs.
    /// </remarks>
    public IEnumerable<MapMark> CenterMarks(LevelMapCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        switch (Mode)
        {
            case MapDisplayMode.Walls:
                foreach (var facing in DrawOrder)
                {
                    var side = cell.Side(facing);

                    // Open is the only blockage that draws nothing. Every "secret" variant does
                    // draw, which is the point -- the author has to be able to see a secret door.
                    if (side.Blockage == BlockageType.Open)
                    {
                        continue;
                    }

                    yield return new MapMark(
                        geometry.SegmentRect(facing, MapSegment.Obstruction),
                        palette.Obstruction(side.Blockage),
                        MapMarkKind.Blockage);
                }
                break;

            case MapDisplayMode.Backgrounds:
                foreach (var facing in DrawOrder)
                {
                    // Slot 0 draws nothing, unlike the zone block below -- backgrounds share the
                    // wall table's "0 means none" convention and zones do not.
                    byte background = cell.Side(facing).Background;
                    if (background == 0)
                    {
                        continue;
                    }

                    yield return new MapMark(
                        geometry.SegmentRect(facing, MapSegment.Obstruction),
                        palette.Backdrop(background),
                        MapMarkKind.Background);
                }
                break;

            case MapDisplayMode.Events when cell.HasEvent:
                yield return new MapMark(geometry.CenterRect(), palette.Backdrop(MarkerColor),
                                         MapMarkKind.Center);
                break;

            case MapDisplayMode.Zones:
                // Unconditional: zone 0 is a real zone and draws in slot 0's black, which against
                // the dark-red fill of this mode is still visible.
                yield return new MapMark(geometry.CenterRect(), palette.Zone(cell.Zone),
                                         MapMarkKind.Center);
                break;

            case MapDisplayMode.StartLocation when cell.IsStartLocation:
                yield return new MapMark(geometry.CenterRect(), palette.Backdrop(MarkerColor),
                                         MapMarkKind.Center);
                break;

            case MapDisplayMode.EntryPoints when cell.IsEntryPoint:
                yield return new MapMark(geometry.CenterRect(),
                                         palette.EntryPoint(cell.EntryPointIndex),
                                         MapMarkKind.Center);
                break;
        }
    }

    /// <summary>
    /// The marker drawn on the selected square.
    /// </summary>
    /// <remarks>
    /// The original blits a directional arrow cut from the editor's map art
    /// (<c>DlgPicture.cpp:1028</c>) at the centre block's offset, in the 8×8 <c>ArrowSize</c> rather
    /// than the block's 6×6 — so the arrow overhangs the block on two sides. The rectangle is
    /// reproduced; what fills it is the control's business, since this port has no map art and has
    /// to draw the four directions itself.
    /// </remarks>
    public MapMark SelectionMark() =>
        new(geometry.ArrowRect(), palette.Backdrop(MarkerColor), MapMarkKind.Arrow);
}
