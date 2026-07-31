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
/// <b>Scope: square 0 only.</b> The original has a hand-written routine per viewport square, each
/// with its own occlusion tests and door/overlay ordering, and they are not variations on a
/// template — <c>RenderSquare0</c> alone consults four different neighbour cells before deciding
/// whether to draw one sliver. This ports the primitive and the first square faithfully; the rest
/// are the same shape of work and are listed in the class remarks rather than guessed at.
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
