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
/// <b>Scope: the far corner squares 0 and 1, square 2, and the ten that are plain sequences of
/// passes (5–14).</b> Only squares 3 and 4 remain — 3 is likely square 2's mirror but has not been
/// read, and 4 carries 35 conditionals, the most of any.
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
    /// Draws a far corner square — 0 (two forward and two left) or 1 (two forward and two right).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from <c>RenderSquare0</c> and <c>RenderSquare1</c>'s five-distant-wall branches
    /// (<c>Viewport.cpp:2348,2486</c>). Two slots come off one sheet — <c>P</c> at the square's
    /// origin and <c>J</c> immediately right of it — and one of them is gated by an occlusion test.
    /// </para>
    /// <para>
    /// <b>The two squares are mirrors, and the mirroring swaps which slot is gated.</b> Square 0
    /// draws <c>J</c> unconditionally and gates <c>P</c>; square 1 draws <c>P</c> unconditionally
    /// and gates <c>J</c>. Assuming the same slot is conditional in both — the natural reading of
    /// "square 1 is square 0 mirrored" — draws the wrong sliver on one side of every corridor.
    /// </para>
    /// <para>
    /// The test itself is a disjunction of four unrelated questions, mirrored likewise: for square
    /// 0 it asks about slot 13 on the facing side, slot 0 on the left, slot 13 on the right, and
    /// whether slot 13 exists at all; square 1 asks the same with 14, right, and left. That last
    /// disjunct is why <see cref="ViewMap"/> leaves slots 13 and 14 unwrapped — on a torus it could
    /// never be true, and the sliver would vanish wherever a corridor crossed a map edge.
    /// </para>
    /// <para>
    /// The door draws only at the <i>unconditional</i> slot, never the gated one, and the overlay
    /// repeats the gate. Both follow the original exactly rather than any principle I can state.
    /// </para>
    /// </remarks>
    public void RenderFarSquare(Surface screen, ViewMap view, WallResolver resolver, Facing facing,
                                int square, int viewportX, int viewportY, Func<string, Surface?> art)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(art);

        if (square is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(square), "only squares 0 and 1");
        }

        // The seven-distant-wall layout is a different routine in the original, not this one with
        // a different constant.
        if (format.DistantWallCount != 5)
        {
            return;
        }

        var (originX, originY) = SquareOrigin(square);
        int x = originX + viewportX;
        int y = originY + viewportY;
        int pWidth = SlotWidth(DrawSlot.P);

        var left = (Facing)(((int)facing + 3) & 3);
        var right = (Facing)(((int)facing + 1) & 3);

        // Square 0 looks outward past slot 13 and inward via its own left face; square 1 mirrors
        // that onto 14 and its own right face.
        int outer = square == 0 ? 13 : 14;
        var ownFace = square == 0 ? left : right;
        var outerFace = square == 0 ? right : left;

        bool gateOpen = resolver.HasWall(view, outer, facing) ||
                        resolver.HasWall(view, square, ownFace) ||
                        resolver.HasWall(view, outer, outerFace) ||
                        !resolver.CellExists(view, outer);

        // Square 0 gates P and always draws J; square 1 is the other way round.
        bool gatesP = square == 0;

        DrawPair(WallLayer.Wall);

        if (resolver.DoorFirst(view, square, facing))
        {
            DrawDoor();
            DrawPair(WallLayer.Overlay);
        }
        else
        {
            DrawPair(WallLayer.Overlay);
            DrawDoor();
        }

        void DrawPair(WallLayer layer)
        {
            var sheet = Sheet(layer);
            if (sheet is null)
            {
                return;
            }

            if (!gatesP || gateOpen)
            {
                DrawSlotArt(screen, sheet, DrawSlot.P, x, y);
            }

            if (gatesP || gateOpen)
            {
                DrawSlotArt(screen, sheet, DrawSlot.J, x + pWidth, y);
            }
        }

        // The door lands on whichever slot is not gated, and is never repeated on the other.
        void DrawDoor()
        {
            var sheet = Sheet(WallLayer.Door);
            if (sheet is not null)
            {
                DrawSlotArt(screen, sheet, gatesP ? DrawSlot.J : DrawSlot.P,
                            gatesP ? x + pWidth : x, y);
            }
        }

        Surface? Sheet(WallLayer layer)
        {
            string? file = resolver.ArtFor(view, square, facing, layer);
            return file is null ? null : art(file);
        }
    }

    /// <summary>
    /// The occlusion test for a far corner square, exposed so each disjunct can be checked alone.
    /// </summary>
    public static bool FarSquareGateOpen(ViewMap view, WallResolver resolver, Facing facing,
                                         int square)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var left = (Facing)(((int)facing + 3) & 3);
        var right = (Facing)(((int)facing + 1) & 3);
        int outer = square == 0 ? 13 : 14;
        var ownFace = square == 0 ? left : right;
        var outerFace = square == 0 ? right : left;

        return resolver.HasWall(view, outer, facing) ||
               resolver.HasWall(view, square, ownFace) ||
               resolver.HasWall(view, outer, outerFace) ||
               !resolver.CellExists(view, outer);
    }

    /// <summary>
    /// Draws viewport square 2 — two forward and one left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from <c>RenderSquare2</c>'s five-distant-wall branch (<c>Viewport.cpp:2621</c>). Two
    /// slots off one sheet: <c>J</c> at the origin, gated, and <c>P</c> beside it, always.
    /// </para>
    /// <para>
    /// <b>Three irregularities, all transcribed rather than tidied.</b> The gate has only two
    /// disjuncts — a wall on the facing side of slot 0, or on the left side of slot 2 — with no
    /// <c>validCoords</c> term, unlike squares 0 and 1. <c>P</c> is offset by <b><c>J</c>'s</b>
    /// width, not its own. And the door draws its <c>J</c> slot at <c>pt + p_width</c>, a
    /// different place from where the <c>J</c> <i>wall</i> went and using the other slot's width
    /// to get there.
    /// </para>
    /// <para>
    /// That last one reads like a defect in the original — a door landing somewhere its own wall
    /// does not — but it is what the engine does, and designs have been authored against it for
    /// twenty years. Reproduced; if it ever needs changing that is a decision to take knowingly.
    /// </para>
    /// </remarks>
    public void RenderSquare2(Surface screen, ViewMap view, WallResolver resolver, Facing facing,
                              int viewportX, int viewportY, Func<string, Surface?> art)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(art);

        if (format.DistantWallCount != 5)
        {
            return;
        }

        var (originX, originY) = SquareOrigin(2);
        int x = originX + viewportX;
        int y = originY + viewportY;
        int pWidth = SlotWidth(DrawSlot.P);
        int jWidth = SlotWidth(DrawSlot.J);

        var left = (Facing)(((int)facing + 3) & 3);
        bool gateOpen = resolver.HasWall(view, 0, facing) || resolver.HasWall(view, 2, left);

        DrawPair(WallLayer.Wall);

        if (resolver.DoorFirst(view, 2, facing))
        {
            DrawDoor();
            DrawPair(WallLayer.Overlay);
        }
        else
        {
            DrawPair(WallLayer.Overlay);
            DrawDoor();
        }

        void DrawPair(WallLayer layer)
        {
            string? file = resolver.ArtFor(view, 2, facing, layer);
            var sheet = file is null ? null : art(file);
            if (sheet is null)
            {
                return;
            }

            if (gateOpen)
            {
                DrawSlotArt(screen, sheet, DrawSlot.J, x, y);
            }

            // Offset by J's width, not P's.
            DrawSlotArt(screen, sheet, DrawSlot.P, x + jWidth, y);
        }

        void DrawDoor()
        {
            string? file = resolver.ArtFor(view, 2, facing, WallLayer.Door);
            var sheet = file is null ? null : art(file);
            if (sheet is not null)
            {
                // p_width, for a J slot, ungated -- see the remarks.
                DrawSlotArt(screen, sheet, DrawSlot.J, x + pWidth, y);
            }
        }
    }

    /// <summary>Square 2's gate, exposed for testing. Two disjuncts, no validity term.</summary>
    public static bool Square2GateOpen(ViewMap view, WallResolver resolver, Facing facing)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var left = (Facing)(((int)facing + 3) & 3);
        return resolver.HasWall(view, 0, facing) || resolver.HasWall(view, 2, left);
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
    /// Only squares 1, 2, 3 and 4 are absent, and those carry 24 to 35 conditionals each. Two of
    /// the six that looked hard were not: square 12 is three plain passes despite its length, and
    /// squares 13 and 14 are single passes behind a layout gate rather than occlusion tests.
    /// Classifying by size would have left three easy squares unported.
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

            // The party's own cell. E is the 112-wide front wall -- the one filling the view when
            // you face a dead end -- with A and B the near side walls either side of it.
            [12] = [new(PassDirection.Front, DrawSlot.E), new(PassDirection.Left, DrawSlot.A),
                    new(PassDirection.Right, DrawSlot.B)],

            // The far outer cells. Single front pass each -- see SevenDistantWallOnly.
            [13] = [new(PassDirection.Front, DrawSlot.J)],
            [14] = [new(PassDirection.Front, DrawSlot.J)],
        };

    /// <summary>
    /// Squares that exist only in the seven-distant-wall layout.
    /// </summary>
    /// <remarks>
    /// <c>RenderSquare13</c> and <c>RenderSquare14</c> open with
    /// <c>if (WallCount == 5) return;</c> (<c>Viewport.cpp:3595</c>) — they are not occlusion
    /// tests but a layout gate, so in the five-wall layout these two squares draw nothing at all.
    /// That is the same <c>DistantWallCount</c> that decides whether a format carries 13 or 15
    /// viewport coordinates, and it is why: coordinates 13 and 14 only exist when these squares do.
    /// </remarks>
    public static readonly IReadOnlySet<int> SevenDistantWallOnly = new HashSet<int> { 13, 14 };

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

        // Squares 13 and 14 are absent from the narrower layout entirely.
        if (SevenDistantWallOnly.Contains(square) && format.DistantWallCount != 7)
        {
            return;
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

}
