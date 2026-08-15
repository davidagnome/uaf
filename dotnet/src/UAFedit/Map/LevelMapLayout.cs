using UAFcore;

namespace UAFedit.Map;

/// <summary>One cell that falls inside the viewport: where it is on screen, and which cell it is.</summary>
/// <param name="Column">The tile column, which may be outside the level once the torus repeats.</param>
/// <param name="Row">The tile row, likewise.</param>
/// <param name="X">The level coordinate the tile shows, wrapped.</param>
/// <param name="Y">The level coordinate the tile shows, wrapped.</param>
/// <param name="Left">Screen position of the cell's left edge, in device pixels.</param>
/// <param name="Top">Screen position of the cell's top edge, in device pixels.</param>
public readonly record struct VisibleCell(
    int Column, int Row, int X, int Y, double Left, double Top);

/// <summary>A square of the level, by coordinate.</summary>
/// <remarks>
/// A struct so that "no selection" can be spelt <c>null</c> without a second flag, which is what a
/// bindable selection property wants.
/// </remarks>
public readonly record struct MapPoint(int X, int Y);

/// <summary>Where a point on screen lands: a cell, the side of it, and the offset inside it.</summary>
/// <param name="OffsetX">
/// Position inside the cell in <i>unzoomed</i> cell pixels, which is what
/// <see cref="MapCellGeometry.SideAt(int, int)"/> expects.
/// </param>
public readonly record struct MapHit(int X, int Y, Facing Side, int OffsetX, int OffsetY);

/// <summary>
/// The map's viewport: which cells are on screen, where they land, and what a click hits.
/// </summary>
/// <remarks>
/// <para>
/// The scrolling half of <c>CDlgPartialMapPicture::RenderDib</c> and of
/// <c>CUAFWinEdView::MouseToMap</c> / <c>MapToMouse</c> / <c>SetSBData</c>
/// (<c>UAFWinEdView.cpp:485-520</c>, <c>:1918</c>). Immutable so the control can hold one and
/// replace it with <c>with</c>; nothing here touches a window.
/// </para>
/// <para>
/// <b>Scroll is measured in cells, not pixels</b>, because <c>xCurrentScroll</c> is — the original
/// scrolls by whole cells and its page is five of them (<c>UAFWinEdView.cpp:60</c>). Fractional
/// values are allowed here so a trackpad can scroll smoothly; every derived quantity is a
/// multiplication by <see cref="CellWidth"/>, so nothing else changes.
/// </para>
/// <para>
/// <b>Zoom is new.</b> The original's cell is <c>SQUARESIZE</c> pixels and there is no way to
/// change it short of editing <c>config.txt</c>. A 16-pixel cell on a 2025 display is very small,
/// so the layout scales; the geometry stays in its own integer pixel space and the scale is applied
/// once, at the boundary, which is why <see cref="MapHit.OffsetX"/> comes back unzoomed.
/// </para>
/// <para>
/// The original also subtracts 2 from the mouse position for the picture control's border
/// (<c>UAFWinEdView.cpp:495</c>). That is a Win32 client-area detail, not map geometry, and is not
/// reproduced.
/// </para>
/// </remarks>
public sealed record LevelMapLayout(int Width, int Height, MapCellGeometry Geometry)
{
    /// <summary><c>LINE_SIZE</c> — cells per scrollbar arrow click (<c>UAFWinEdView.cpp:59</c>).</summary>
    public const int LineSize = 1;

    /// <summary><c>PAGE_SIZE</c> — cells per page (<c>UAFWinEdView.cpp:60</c>).</summary>
    public const int PageSize = 5;

    /// <summary>The smallest zoom the view offers.</summary>
    public const double MinZoom = 0.5;

    /// <summary>The largest zoom the view offers.</summary>
    public const double MaxZoom = 8.0;

    private readonly double zoom = 1.0;

    /// <summary>Screen pixels per geometry pixel. Clamped rather than rejected.</summary>
    public double Zoom
    {
        get => zoom;
        init => zoom = Math.Clamp(double.IsFinite(value) ? value : 1.0, MinZoom, MaxZoom);
    }

    /// <summary>Leftmost visible column, in cells.</summary>
    public double ScrollX { get; init; }

    /// <summary>Topmost visible row, in cells.</summary>
    public double ScrollY { get; init; }

    /// <summary>
    /// Whether the level repeats past its edges.
    /// </summary>
    /// <remarks>
    /// <c>m_TileMap</c> (<c>UAFWinEd.cpp:63</c>), which the original defaults to <c>FALSE</c> and
    /// whose own comment says the tiling was added because switching to a smaller level left the
    /// previous one's walls on screen. It is defaulted to true here for a different reason: a level
    /// really is a torus to the party, and an author drawing a corridor that runs off the east edge
    /// wants to see it arrive at the west.
    /// </remarks>
    public bool Tile { get; init; } = true;

    /// <summary>A cell's width on screen.</summary>
    public double CellWidth => Geometry.SquareWidth * Zoom;

    /// <summary>A cell's height on screen.</summary>
    public double CellHeight => Geometry.SquareHeight * Zoom;

    /// <summary>The whole level's width on screen, tiling ignored.</summary>
    public double ContentWidth => Width * CellWidth;

    /// <summary>The whole level's height on screen, tiling ignored.</summary>
    public double ContentHeight => Height * CellHeight;

    /// <summary>
    /// The scroll position clamped to something that shows map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A level smaller than the viewport scrolls not at all, which is what
    /// <c>max(ActualMapWidth-MapBufferWidth, 0)</c> comes to (<c>UAFWinEdView.cpp:1938</c>). The
    /// original's dungeon path then rounds that limit up to a multiple of <c>PAGE_SIZE</c> — so it
    /// can be scrolled up to four cells past the end of the map, showing blank or, with tiling on,
    /// the start of the next copy. Not reproduced: the rounding exists to make the Win32 scrollbar's
    /// page arithmetic come out, and there is no such scrollbar here.
    /// </para>
    /// <para>
    /// With <see cref="Tile"/> on there is no clamp at all in either direction. The torus has no
    /// end to run off, and stopping at one would be an edge the map does not have.
    /// </para>
    /// </remarks>
    public (double X, double Y) ClampScroll(double x, double y,
                                            double viewportWidth, double viewportHeight)
    {
        if (Tile)
        {
            return (Finite(x), Finite(y));
        }

        double maxX = Math.Max(Width - (viewportWidth / CellWidth), 0);
        double maxY = Math.Max(Height - (viewportHeight / CellHeight), 0);

        return (Math.Clamp(Finite(x), 0, maxX), Math.Clamp(Finite(y), 0, maxY));

        static double Finite(double v) => double.IsFinite(v) ? v : 0;
    }

    /// <summary>
    /// Every cell that touches a viewport of the given size, in row-major order.
    /// </summary>
    /// <remarks>
    /// The partly-visible cell at each edge is included, which the original's whole-cell scrolling
    /// never had to think about. Callers clip.
    /// </remarks>
    public IEnumerable<VisibleCell> Visible(double viewportWidth, double viewportHeight)
    {
        if (Width <= 0 || Height <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            yield break;
        }

        int firstColumn = (int)Math.Floor(ScrollX);
        int firstRow = (int)Math.Floor(ScrollY);
        int columns = (int)Math.Ceiling((viewportWidth / CellWidth) + (ScrollX - firstColumn));
        int rows = (int)Math.Ceiling((viewportHeight / CellHeight) + (ScrollY - firstRow));

        for (int row = firstRow; row <= firstRow + rows; row++)
        {
            for (int column = firstColumn; column <= firstColumn + columns; column++)
            {
                if (!Tile && (column < 0 || column >= Width || row < 0 || row >= Height))
                {
                    continue;
                }

                yield return new VisibleCell(
                    column, row,
                    ViewMap.Wrap(column, Width), ViewMap.Wrap(row, Height),
                    (column - ScrollX) * CellWidth, (row - ScrollY) * CellHeight);
            }
        }
    }

    /// <summary>Where a cell's top-left corner sits on screen, tiling ignored.</summary>
    /// <remarks>
    /// The inverse of <see cref="HitTest"/> for the cell part, and the equivalent of
    /// <c>MapToMouse</c> minus its half-cell centring.
    /// </remarks>
    public (double Left, double Top) Origin(int x, int y) =>
        ((x - ScrollX) * CellWidth, (y - ScrollY) * CellHeight);

    /// <summary>
    /// Which cell and which of its sides a screen point falls on.
    /// </summary>
    /// <remarks>
    /// <b>Returns a hit for any point, including one outside the level</b>, because the map is a
    /// torus and a point past the east edge is a real square on the west. <c>MouseToMap</c> instead
    /// returns false past <c>area_width</c> (<c>UAFWinEdView.cpp:502</c>) — it has to, since it
    /// scrolls a non-tiled map into blank space. With tiling off the caller should bounds-check
    /// <see cref="Contains"/> first.
    /// </remarks>
    public MapHit HitTest(double screenX, double screenY)
    {
        double cellX = ScrollX + (screenX / CellWidth);
        double cellY = ScrollY + (screenY / CellHeight);

        int column = (int)Math.Floor(cellX);
        int row = (int)Math.Floor(cellY);

        // Back into the geometry's own integer pixel space, where the diagonals are defined.
        int offsetX = (int)((cellX - column) * Geometry.SquareWidth);
        int offsetY = (int)((cellY - row) * Geometry.SquareHeight);

        return new MapHit(
            ViewMap.Wrap(column, Math.Max(Width, 1)),
            ViewMap.Wrap(row, Math.Max(Height, 1)),
            Geometry.SideAt(offsetX, offsetY),
            offsetX, offsetY);
    }

    /// <summary>Whether a coordinate is inside the level's declared extent.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>
    /// The scroll position that brings a cell into view, moving as little as possible.
    /// </summary>
    /// <remarks>
    /// The original has no equivalent — it moves the party with the arrow keys and lets the square
    /// walk off screen. Added because a level chooser or a search result has to be able to say
    /// "show me this square".
    /// </remarks>
    public LevelMapLayout ScrollToShow(int x, int y, double viewportWidth, double viewportHeight)
    {
        double columns = viewportWidth / CellWidth;
        double rows = viewportHeight / CellHeight;

        double sx = Math.Min(ScrollX, x);
        sx = Math.Max(sx, x + 1 - columns);

        double sy = Math.Min(ScrollY, y);
        sy = Math.Max(sy, y + 1 - rows);

        var (clampedX, clampedY) = ClampScroll(sx, sy, viewportWidth, viewportHeight);
        return this with { ScrollX = clampedX, ScrollY = clampedY };
    }
}
