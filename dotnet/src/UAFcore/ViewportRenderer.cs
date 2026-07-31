using UAF.Media;

namespace UAFcore;

/// <summary>
/// A wall slot to draw, <c>DRAW_A_WALL</c>…<c>DRAW_P_WALL</c> (<c>Shared/Viewport.h:274</c>).
/// </summary>
/// <remarks>
/// <b>One-based.</b> <c>WallFormatMgr::GetWidth</c> does <c>Type--</c> before indexing
/// (<c>Viewport.h:254</c>), so <c>A</c> is 1 and addresses slot rect 0. Using the value directly as
/// an index shifts every wall to its neighbour's rectangle, which at these sizes means drawing a
/// 32-pixel side wall where a 112-pixel front wall belongs.
/// </remarks>
public enum DrawSlot
{
    A = 1, B = 2, C = 3, D = 4, E = 5, F = 6, G = 7, H = 8,
    I = 9, J = 10, K = 11, L = 12, M = 13, N = 14, O = 15, P = 16,
}

/// <summary>
/// Composites the corridor view: the port of <c>BltSurface</c> and the <c>RenderSquare</c> family
/// (<c>Shared/Viewport.cpp:1441</c>, <c>:2335</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope: square 0, plus the seven squares that are plain sequences of passes
/// (5, 6, 7, 8, 9, 10, 11).</b> The original has a hand-written routine per viewport square and
/// they are not variations on a template — square 0 alone consults four different neighbour cells
/// before deciding whether to draw one sliver. The seven remaining all carry occlusion tests of
/// their own and need porting individually; extrapolating them from these would be a guess.
/// </para>
/// <para>
/// <b>The blit is 1:1.</b> <c>BltSurface</c> takes the source rectangle's own width and height for
/// the destination (<c>Viewport.cpp:1449-1456</c>), so nothing is scaled at draw time — the
/// perspective is entirely in how the artist drew the sheet and which rectangle the format
/// selects. A renderer that scaled to fit would blur art that was designed to land pixel-exact.
/// </para>
/// </remarks>
public sealed class ViewportRenderer(WallFormat format)
{
    private readonly WallFormat format = format ?? throw new ArgumentNullException(nameof(format));

    /// <summary>Where a square's art is anchored, before the per-slot offset.</summary>
    /// <remarks><c>GetSquareOrigin</c> (<c>Viewport.h:242</c>), indexed by viewport slot.</remarks>
    public (int X, int Y) SquareOrigin(int square) =>
        square >= 0 && square < format.ViewportCoords.Count
            ? format.ViewportCoords[square]
            : (0, 0);

    /// <summary>The source rectangle a draw slot cuts from the wall sheet.</summary>
    public SurfaceRect SlotRect(DrawSlot slot) => format.SlotRects[(int)slot - 1];

    /// <summary>The offset added to a square's origin for this slot.</summary>
    public (int X, int Y) SlotOffset(DrawSlot slot) => format.SlotOffsets[(int)slot - 1];

    /// <summary>The width of a slot's rectangle, which is also its drawn width.</summary>
    public int SlotWidth(DrawSlot slot) => SlotRect(slot).Width;

    /// <summary>
    /// Blits one slot of a wall sheet to the screen — <c>BltSurface</c>.
    /// </summary>
    /// <remarks>
    /// Keyed, because wall art declares its transparent colour the way all this engine's art does:
    /// by the top-left pixel. A wall sheet blitted opaque fills the corridor with the sheet's
    /// background instead of showing the backdrop through the gaps.
    /// </remarks>
    public void DrawSlotArt(Surface screen, Surface sheet, DrawSlot slot, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(sheet);

        var source = SlotRect(slot);
        if (!source.TryClipTo(sheet.Bounds, out var clipped))
        {
            return;
        }

        var (ox, oy) = SlotOffset(slot);
        Blitter.BlitTransparent(screen, x + ox, y + oy, sheet, clipped);
    }

    /// <summary>
    /// Draws viewport square 0 — the far left square, two forward and two left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from <c>RenderSquare0</c>'s five-distant-wall branch (<c>Viewport.cpp:2348</c>). Two
    /// slots come off the same sheet: <c>P</c> at the square's origin and <c>J</c> immediately to
    /// its right, <c>J</c> always and <c>P</c> only when something further left would hide the seam.
    /// </para>
    /// <para>
    /// <b>The occlusion test is a disjunction of four unrelated questions</b> — is there a wall on
    /// the facing side of slot 13, or on the left side of slot 0, or on the right side of slot 13,
    /// or is slot 13 off the map entirely. That last one is why <see cref="ViewMap"/> leaves slots
    /// 13 and 14 unwrapped: on a torus the question could never be yes, and the sliver would go
    /// missing wherever the corridor crossed an edge.
    /// </para>
    /// </remarks>
    public void RenderSquare0(Surface screen, Surface? wallSheet, ViewMap view,
                              WallResolver resolver, Facing facing, int viewportX, int viewportY)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(resolver);

        if (wallSheet is null || format.DistantWallCount != 5)
        {
            // The seven-distant-wall layout is a different routine in the original, not this one
            // with a different constant.
            return;
        }

        var (originX, originY) = SquareOrigin(0);
        int x = originX + viewportX;
        int y = originY + viewportY;

        var left = (Facing)(((int)facing + 3) & 3);
        var right = (Facing)(((int)facing + 1) & 3);

        if (ShouldDrawFarSliver(view, resolver, facing, left, right))
        {
            DrawSlotArt(screen, wallSheet, DrawSlot.P, x, y);
        }

        DrawSlotArt(screen, wallSheet, DrawSlot.J, x + SlotWidth(DrawSlot.P), y);
    }

    /// <summary>Which wall of a cell a pass draws, relative to the way the party faces.</summary>
    public enum PassDirection
    {
        Front,
        Left,
        Right,
    }

    /// <summary>One draw pass: a cell face, and the slot its art is cut from.</summary>
    public readonly record struct SquarePass(PassDirection Direction, DrawSlot Slot);

    /// <summary>
    /// The squares whose routine is a plain sequence of passes at one origin, and what those
    /// passes are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off <c>RenderSquare5</c>, <c>6</c>, <c>7</c>, <c>8</c>, <c>9</c>, <c>10</c> and
    /// <c>11</c>. Each pass is the same three-layer draw — wall, then door and overlay in the wall
    /// set's own order — at the square's origin, differing only in which face of the cell it asks
    /// for and which rectangle it cuts.
    /// </para>
    /// <para>
    /// <b>None of it is derivable.</b> Square 10 uses D and square 11 uses C; squares 7, 8 and 9
    /// all draw the front face from <c>H</c> but at different origins, and their side passes use
    /// N, O, F and G with no pattern relating slot to direction. The table is transcribed.
    /// </para>
    /// <para>
    /// The eight squares absent from this table are the ones with occlusion tests — they consult
    /// neighbouring cells before deciding what to draw, and each needs porting individually.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<int, SquarePass[]> SquarePasses =
        new Dictionary<int, SquarePass[]>
        {
            [5] = [new(PassDirection.Front, DrawSlot.M)],
            [6] = [new(PassDirection.Front, DrawSlot.L)],
            [7] = [new(PassDirection.Front, DrawSlot.H), new(PassDirection.Left, DrawSlot.N)],
            [8] = [new(PassDirection.Front, DrawSlot.H), new(PassDirection.Right, DrawSlot.O)],
            [9] = [new(PassDirection.Front, DrawSlot.H), new(PassDirection.Left, DrawSlot.F),
                   new(PassDirection.Right, DrawSlot.G)],
            [10] = [new(PassDirection.Front, DrawSlot.D)],
            [11] = [new(PassDirection.Front, DrawSlot.C)],
        };

    /// <summary>
    /// Draws one of the <see cref="SquarePasses"/> squares: each pass in order, and within a pass
    /// the wall, then the door and overlay in the order the wall set asks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The door/overlay order is per wall set, not global.</b> <c>RenderDoorBeforeOverlay</c>
    /// returns <c>WallSets[slot].doorFirst</c> (<c>Viewport.cpp:1299</c>), so two walls in the same
    /// view can disagree about it — and since it is read per pass, the front and left faces of one
    /// cell can disagree too. Getting it wrong hides a door behind its own frame, which looks like
    /// bad art rather than a bug.
    /// </para>
    /// <para>
    /// All three layers of a pass land at the same point with the same draw slot; only the source
    /// sheet differs.
    /// </para>
    /// </remarks>
    public void RenderSquare(Surface screen, ViewMap view, WallResolver resolver, Facing facing,
                             int square, int viewportX, int viewportY, Func<string, Surface?> art)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(art);

        if (!SquarePasses.TryGetValue(square, out var passes))
        {
            throw new ArgumentOutOfRangeException(nameof(square),
                $"square {square} needs its own routine; it is not a plain sequence of passes");
        }

        var (originX, originY) = SquareOrigin(square);
        int x = originX + viewportX;
        int y = originY + viewportY;

        foreach (var pass in passes)
        {
            var face = pass.Direction switch
            {
                PassDirection.Left => (Facing)(((int)facing + 3) & 3),
                PassDirection.Right => (Facing)(((int)facing + 1) & 3),
                _ => facing,
            };

            Draw(WallLayer.Wall, face, pass.Slot);

            if (resolver.DoorFirst(view, square, face))
            {
                Draw(WallLayer.Door, face, pass.Slot);
                Draw(WallLayer.Overlay, face, pass.Slot);
            }
            else
            {
                Draw(WallLayer.Overlay, face, pass.Slot);
                Draw(WallLayer.Door, face, pass.Slot);
            }
        }

        void Draw(WallLayer layer, Facing face, DrawSlot slot)
        {
            string? file = resolver.ArtFor(view, square, face, layer);
            if (file is null)
            {
                return;
            }

            var sheet = art(file);
            if (sheet is not null)
            {
                DrawSlotArt(screen, sheet, slot, x, y);
            }
        }
    }

    /// <summary>
    /// <c>RenderSquare0</c>'s occlusion test, named so it can be checked on its own.
    /// </summary>
    public static bool ShouldDrawFarSliver(ViewMap view, WallResolver resolver,
                                           Facing facing, Facing left, Facing right) =>
        resolver.HasWall(view, 13, facing) ||
        resolver.HasWall(view, 0, left) ||
        resolver.HasWall(view, 13, right) ||
        !resolver.CellExists(view, 13);
}
