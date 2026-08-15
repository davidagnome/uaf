using UAF.Data;
using UAF.Media;
using UAFcore;

namespace UAFedit.Map;

/// <summary>
/// Which of the four marks along one side of a cell is meant.
/// </summary>
/// <remarks>
/// The three wall segments are drawn in the wall set's colour; the obstruction sits just inside
/// them and is drawn in the blockage's. <see cref="Middle"/> is the interesting one — see
/// <see cref="LevelMapCell.HasDoorGap"/>.
/// </remarks>
public enum MapSegment
{
    /// <summary>The wall segment nearest the cell's top-left corner.</summary>
    Start = 0,

    /// <summary>The middle wall segment, which a door leaves out.</summary>
    Middle = 1,

    /// <summary>The wall segment nearest the cell's bottom-right corner.</summary>
    End = 2,

    /// <summary>The blockage mark, drawn inside the wall.</summary>
    Obstruction = 3,
}

/// <summary>
/// Where every mark of a map cell is drawn, relative to the cell's top-left corner.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>WallOffsetRects[17]</c> together with the six size rectangles beside it
/// (<c>UAFWinEd/DlgPicture.cpp:746-841</c>), and it is the whole of the 2-D map's layout. The
/// original's default cell is <c>SQUARESIZE</c> = 16 pixels square (<c>Shared/Viewport.h:139</c>):
/// three 4×2 dashes along each horizontal side, three 2×4 along each vertical one, a fatter
/// blockage mark just inside, and a 6×6 block in the middle.
/// </para>
/// <para>
/// <b>Only the left and top of each offset rectangle are used.</b> <c>TranslateRect</c>
/// (<c>Viewport.h:160</c>) takes a position and a <i>separate</i> size rectangle, so the right and
/// bottom of the entries in <c>WallOffsetRects</c> are dead weight — they happen to agree with the
/// sizes for the wall segments and do not for the arrow, which is 8×8 drawn from the 6×6 centre
/// block's corner. Storing offsets as points rather than rectangles removes the chance of reading
/// the wrong pair.
/// </para>
/// <para>
/// <b>The table is grouped north, SOUTH, east, west — the storage order of
/// <see cref="UAF.Serialization.AreaMapCell.Walls"/>, not compass order.</b> The original's
/// comments spell out that grouping, and <c>DrawSquare</c> indexes it with the <i>raw array
/// subscript</i> <c>spot.wall[i]</c> rather than through <c>walls(dir)</c>, so the two permutations
/// cancel and nobody had to think about it. They do not cancel here, because this port asks for a
/// side by <see cref="Facing"/> — hence <see cref="GroupOf"/>. Reading the table as N/E/S/W draws
/// every east wall along the bottom edge and every south wall down the right, which looks like a
/// plausible map right up until it is compared with the one the design's author drew.
/// </para>
/// </remarks>
public sealed class MapCellGeometry
{
    /// <summary><c>SQUARESIZE</c> (<c>Shared/Viewport.h:139</c>).</summary>
    public const int DefaultSquareSize = 16;

    /// <summary>Entries in <c>WallOffsetRects</c>: four sides of four, plus the centre.</summary>
    public const int OffsetCount = 17;

    /// <summary>The index of the centre entry, which is not part of any side.</summary>
    public const int CenterOffset = 16;

    private readonly (int X, int Y)[] offsets;

    private MapCellGeometry((int X, int Y)[] offsets, SurfaceRect square, SurfaceRect block,
                            SurfaceRect arrow, SurfaceRect horizontalWall,
                            SurfaceRect horizontalObstruction, SurfaceRect verticalWall,
                            SurfaceRect verticalObstruction)
    {
        this.offsets = offsets;
        Square = square;
        Block = block;
        Arrow = arrow;
        HorizontalWall = horizontalWall;
        HorizontalObstruction = horizontalObstruction;
        VerticalWall = verticalWall;
        VerticalObstruction = verticalObstruction;
    }

    /// <summary>The whole cell (<c>MapSquareSize</c>, <c>MAP_OUTER_SQUARE_SIZE</c>).</summary>
    public SurfaceRect Square { get; }

    /// <summary>The centre block (<c>MapBlockSize</c>, <c>MAP_INNER_SQUARE_SIZE</c>).</summary>
    public SurfaceRect Block { get; }

    /// <summary>
    /// The party arrow (<c>ArrowSize</c>). Drawn from the centre block's corner, and larger than it.
    /// </summary>
    public SurfaceRect Arrow { get; }

    /// <summary>A wall dash on the north or south side (<c>H_WallSize</c>).</summary>
    public SurfaceRect HorizontalWall { get; }

    /// <summary>A blockage mark on the north or south side (<c>HO_WallSize</c>).</summary>
    public SurfaceRect HorizontalObstruction { get; }

    /// <summary>A wall dash on the east or west side (<c>V_WallSize</c>).</summary>
    public SurfaceRect VerticalWall { get; }

    /// <summary>A blockage mark on the east or west side (<c>VO_WallSize</c>).</summary>
    public SurfaceRect VerticalObstruction { get; }

    /// <summary>The cell's width in pixels at zoom 1.</summary>
    public int SquareWidth => Square.Width;

    /// <summary>The cell's height in pixels at zoom 1.</summary>
    public int SquareHeight => Square.Height;

    /// <summary>
    /// The grid dot at each corner of a cell — a fixed 2×2 (<c>DlgPicture.cpp:874</c>).
    /// </summary>
    /// <remarks>
    /// A function-local <c>static RECT cornerRect = {0,0,2,2}</c>, and the one rectangle in the
    /// whole map that <c>config.txt</c> cannot override. It does not scale with
    /// <see cref="Square"/> either, so a design that enlarged its cells gets the same small dots.
    /// </remarks>
    public const int CornerSize = 2;

    /// <summary>The four corner dots of a cell, in the original's drawing order.</summary>
    public IEnumerable<SurfaceRect> Corners()
    {
        yield return SurfaceRect.FromBounds(0, 0, CornerSize, CornerSize);
        yield return SurfaceRect.FromBounds(SquareWidth - CornerSize, 0, CornerSize, CornerSize);
        yield return SurfaceRect.FromBounds(0, SquareHeight - CornerSize, CornerSize, CornerSize);
        yield return SurfaceRect.FromBounds(SquareWidth - CornerSize, SquareHeight - CornerSize,
                                            CornerSize, CornerSize);
    }

    /// <summary>
    /// The offset-table group a side occupies: north 0, <b>south 1, east 2</b>, west 3.
    /// </summary>
    /// <remarks>
    /// Identical to <c>AREA_MAP_DATA</c>'s <c>{0,2,1,3}</c> wall permutation, and for the same
    /// reason — the table was written to sit beside the storage array, not beside a compass.
    /// </remarks>
    public static int GroupOf(Facing side) => ((int)side & 3) switch
    {
        0 => 0,   // north
        1 => 2,   // east
        2 => 1,   // south
        _ => 3,   // west
    };

    /// <summary>The index into the offset table for one mark.</summary>
    public static int OffsetIndex(Facing side, MapSegment segment) =>
        (GroupOf(side) * 4) + (int)segment;

    /// <summary>Whether a side runs along the top or bottom of the cell.</summary>
    public static bool IsHorizontal(Facing side) =>
        ((int)side & 3) is 0 or 2;

    /// <summary>Where one mark of a cell is drawn, relative to the cell's top-left corner.</summary>
    public SurfaceRect SegmentRect(Facing side, MapSegment segment)
    {
        var (x, y) = offsets[OffsetIndex(side, segment)];
        var size = segment == MapSegment.Obstruction
            ? (IsHorizontal(side) ? HorizontalObstruction : VerticalObstruction)
            : (IsHorizontal(side) ? HorizontalWall : VerticalWall);

        return SurfaceRect.FromBounds(x, y, size.Width, size.Height);
    }

    /// <summary>The centre block, relative to the cell's top-left corner.</summary>
    public SurfaceRect CenterRect()
    {
        var (x, y) = offsets[CenterOffset];
        return SurfaceRect.FromBounds(x, y, Block.Width, Block.Height);
    }

    /// <summary>The party arrow, relative to the cell's top-left corner.</summary>
    /// <remarks>Shares the centre block's offset and not its size — see the type remarks.</remarks>
    public SurfaceRect ArrowRect()
    {
        var (x, y) = offsets[CenterOffset];
        return SurfaceRect.FromBounds(x, y, Arrow.Width, Arrow.Height);
    }

    /// <summary>
    /// Which side of a cell a click at <paramref name="dx"/>,<paramref name="dy"/> inside it
    /// selects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CUAFWinEdView::MousePointToWall</c> (<c>UAFWinEdView.cpp:565</c>), which is written as a
    /// single branchless expression the author was visibly pleased with — the comment says so. It
    /// divides the cell along both diagonals and returns the triangle: top is north, right east,
    /// bottom south, left west. Spelled out here rather than transcribed, because the original
    /// relies on <c>UINT</c> underflow putting a borrow into bit 8 and bit 9, which C# would give
    /// too but which no reader should have to verify.
    /// </para>
    /// <para>
    /// <b>The two diagonals do not treat their boundaries the same way, and neither is an
    /// accident.</b> The main diagonal (<c>dx == dy</c>) belongs to the north/east half, because the
    /// live expression says <c>dy-dx-1</c> where the commented-out first version said <c>dy-dx</c>
    /// — "fixed the case where dx==dy". Without the <c>-1</c> both halves of the second term vanish
    /// on the diagonal and every point along it comes back north, including the ones past the
    /// centre that plainly want east. The anti-diagonal (<c>dx + dy == squareSize</c>) is the
    /// opposite: it is tested with a strict <c>&gt;</c> above and a strict <c>&lt;</c> below, so the
    /// line itself falls to north above and south below. Both boundaries are one pixel wide and
    /// wrong in a way nobody notices until an author reports that clicking a corner edits the wrong
    /// wall.
    /// </para>
    /// <para>
    /// Only consulted when the editor's <c>m_KwikKlik</c> is on; with it off the original ignores
    /// the position entirely and edits whichever side the party faces.
    /// </para>
    /// </remarks>
    public Facing SideAt(int dx, int dy) => SideAt(dx, dy, SquareWidth, SquareHeight);

    /// <inheritdoc cref="SideAt(int, int)"/>
    /// <remarks>
    /// The original has one <c>SQUARESIZE</c> because the cell is square in every shipped config.
    /// <c>MAP_OUTER_SQUARE_SIZE</c> can override it to something that is not, so the vertical
    /// coordinate is scaled onto the width before the diagonals are tested. That is this port's
    /// addition and it reduces exactly to the original whenever the cell is square.
    /// </remarks>
    public static Facing SideAt(int dx, int dy, int squareWidth, int squareHeight)
    {
        int y = squareHeight == squareWidth
            ? dy
            : (int)Math.Round(dy * (double)squareWidth / Math.Max(squareHeight, 1));

        // The high bit of the original's result: strictly below the main diagonal is the
        // south/west half.
        bool below = y > dx;

        // The low bit, and the halves disagree about the anti-diagonal itself on purpose.
        bool outward = below ? dx + y < squareWidth : dx + y > squareWidth;

        return (below, outward) switch
        {
            (false, false) => Facing.North,
            (false, true) => Facing.East,
            (true, true) => Facing.West,
            (true, false) => Facing.South,
        };
    }

    /// <summary>The built-in layout: the table as it is declared in the source.</summary>
    public static MapCellGeometry Default { get; } = new(
        [
            // North: three dashes along the top, then the blockage mark just below them.
            (2, 0), (6, 0), (10, 0), (5, 2),
            // South.
            (2, 14), (6, 14), (10, 14), (5, 12),
            // East.
            (14, 2), (14, 6), (14, 10), (12, 5),
            // West.
            (0, 2), (0, 6), (0, 10), (2, 5),
            // Centre.
            (4, 4),
        ],
        square: SurfaceRect.FromSize(DefaultSquareSize, DefaultSquareSize),
        block: SurfaceRect.FromSize(6, 6),
        arrow: SurfaceRect.FromSize(8, 8),
        horizontalWall: SurfaceRect.FromSize(4, 2),
        horizontalObstruction: SurfaceRect.FromSize(6, 2),
        verticalWall: SurfaceRect.FromSize(2, 4),
        verticalObstruction: SurfaceRect.FromSize(2, 6));

    /// <summary>
    /// The layout a design's <c>config.txt</c> asks for, falling back per key to
    /// <see cref="Default"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ConfigureArtCoords</c> (<c>Shared/Viewport.cpp:734-798</c>), the whole block of which sits
    /// inside <c>#ifdef UAFEDITOR</c> — these keys exist only for the editor, and the engine never
    /// reads them. No shipped design declares any of them, so this is a path that has probably never
    /// run; it is ported because the alternative is silently ignoring a design that did.
    /// </para>
    /// <para>
    /// The keys are named for the side, so their order in the file is north, south, east, west —
    /// storage order again, matching the table they fill.
    /// </para>
    /// </remarks>
    public static MapCellGeometry FromConfig(DesignConfig? config)
    {
        if (config is null)
        {
            return Default;
        }

        string[] keys =
        [
            "NORTH_OFFSET_1", "NORTH_OFFSET_2", "NORTH_OFFSET_3", "NORTH_OFFSET_4",
            "SOUTH_OFFSET_1", "SOUTH_OFFSET_2", "SOUTH_OFFSET_3", "SOUTH_OFFSET_4",
            "EAST_OFFSET_1", "EAST_OFFSET_2", "EAST_OFFSET_3", "EAST_OFFSET_4",
            "WEST_OFFSET_1", "WEST_OFFSET_2", "WEST_OFFSET_3", "WEST_OFFSET_4",
            "CENTER_OFFSET",
        ];

        var offsets = new (int X, int Y)[OffsetCount];
        for (int i = 0; i < OffsetCount; i++)
        {
            offsets[i] = config.TryGetRect(keys[i], out int left, out int top, out _, out _,
                                           consume: false)
                ? (left, top)
                : Default.offsets[i];
        }

        return new MapCellGeometry(
            offsets,
            Size(config, "MAP_OUTER_SQUARE_SIZE", Default.Square),
            Size(config, "MAP_INNER_SQUARE_SIZE", Default.Block),
            Size(config, "ARROW_SIZE", Default.Arrow),
            Size(config, "HORZ_SIZE", Default.HorizontalWall),
            Size(config, "HORZOFFSET_SIZE", Default.HorizontalObstruction),
            Size(config, "VERT_SIZE", Default.VerticalWall),
            Size(config, "VERTOFFSET_SIZE", Default.VerticalObstruction));

        static SurfaceRect Size(DesignConfig config, string key, SurfaceRect fallback) =>
            config.TryGetRect(key, out int l, out int t, out int r, out int b, consume: false)
                ? new SurfaceRect(l, t, r, b)
                : fallback;
    }
}
