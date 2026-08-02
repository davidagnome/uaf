namespace UAFcore;

/// <summary>
/// The squares an area spell covers (<c>GetMapTilesInRectangle</c> and its callers,
/// <c>Drawtile.cpp:4646</c>).
/// </summary>
/// <remarks>
/// <para>
/// The rectangle is the primitive; the circle is a square pruned by distance, and the cone and the
/// two lines are built the same way. Its own header comment gives the convention — <b>"Width is
/// normal to casting direction; Height is parallel"</b> — and notes that it was "totally rewritten
/// to center the rectangle and rotate it for the various directions", with the older
/// corner-anchored version left commented out beneath.
/// </para>
/// <para>
/// <b>How it works.</b> Two half-planes through the target, one across the casting direction and
/// one along it, intersected. Rather than test every square, the reference floods outward from the
/// target four-connected, which is equivalent because the intersection of two slabs is convex.
/// Everything is done in quarter-square integers so that an even width can straddle the target
/// without rounding.
/// </para>
/// </remarks>
public static class SpellArea
{
    /// <summary>
    /// The four-connected steps the flood takes (<c>deltax</c>/<c>deltay</c>,
    /// <c>Drawtile.cpp:4643</c>). North, east, south, west — the order the output comes out in.
    /// </summary>
    private static ReadOnlySpan<int> StepX => [0, 1, 0, -1];

    /// <inheritdoc cref="StepX"/>
    private static ReadOnlySpan<int> StepY => [-1, 0, 1, 0];

    /// <summary>
    /// The squares of a rectangle centred on <paramref name="x0"/>, <paramref name="y0"/> and
    /// rotated to face (<paramref name="dirX"/>, <paramref name="dirY"/>).
    /// </summary>
    /// <param name="width">Across the casting direction — but see the remarks on diagonals.</param>
    /// <param name="height">Along it.</param>
    /// <param name="forceNonZero">
    /// Clamps a width or height below one up to one. <c>AreaSquare</c> passes true; the circle
    /// passes false, so a zero radius yields nothing.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The target square is always included, tested against nothing.</b> The flood seeds with it
    /// and marks it visited before the loop, so it is in the result even when the width and height
    /// are one — and even when it lies outside the map, which the reference does not check.
    /// </para>
    /// <para>
    /// <b>An even width straddles the target one square off centre.</b> The target's coordinates
    /// are scaled by four and nudged by one (<c>targetX += (x0&lt;0) ? -1 : 1</c>), putting the
    /// centre a quarter-square past the square's own position. A width of 2 facing east therefore
    /// covers the target's row and the one below it, not the one above.
    /// </para>
    /// <para>
    /// <b>On a diagonal, width and height swap meaning.</b> The two tests use
    /// <c>(dirY, dirX)</c> and <c>(dirX, −dirY)</c>, which are the direction vector reflected
    /// rather than rotated. Reflection happens to give the perpendicular for the four cardinal
    /// directions — the only case where the header comment holds — but for a diagonal the vector
    /// paired with <paramref name="width"/> is the direction itself, so width measures extent
    /// <i>along</i> the cast and height measures it across. The code contradicts its own comment
    /// there. Reproduced, because a design's area spells were tuned against what ships.
    /// </para>
    /// </remarks>
    public static List<(int X, int Y)> Rectangle(int x0, int y0, int dirX, int dirY,
                                                 int width, int height, bool forceNonZero,
                                                 int mapWidth, int mapHeight)
    {
        var cells = new List<(int X, int Y)>();

        if (forceNonZero)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
        }

        if (width <= 0 || height <= 0)
        {
            return cells;
        }

        if (dirX == 0 && dirY == 0)
        {
            dirX = 1;
        }

        long d2 = ((long)dirX * dirX) + ((long)dirY * dirY);

        // Quarter-square coordinates, nudged off the square's own corner so an even extent can
        // straddle the target.
        long targetX = ((long)x0 * 4) + (x0 < 0 ? -1 : 1);
        long targetY = ((long)y0 * 4) + (y0 < 0 ? -1 : 1);

        long cp = -((dirX * targetY) + (dirY * targetX));
        long cn = (dirY * targetY) - (dirX * targetX);

        // Squared half-extents, in the same quarter-square units.
        long alongLimit = d2 * (height * 2L) * (height * 2L);
        long acrossLimit = d2 * (width * 2L) * (width * 2L);

        long stepScaleX = 4L * dirX;
        long stepScaleY = 4L * dirY;

        var seen = new HashSet<(int, int)> { (x0, y0) };
        cells.Add((x0, y0));

        for (int i = 0; i < cells.Count; i++)
        {
            var (cx, cy) = cells[i];

            for (int step = 0; step < 4; step++)
            {
                int x = cx + StepX[step];
                int y = cy + StepY[step];

                if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight || !seen.Add((x, y)))
                {
                    continue;
                }

                long across = (stepScaleX * y) + (stepScaleY * x) + cp;
                if (across * across >= acrossLimit)
                {
                    continue;
                }

                long along = (stepScaleX * x) - (stepScaleY * y) + cn;
                if (along * along >= alongLimit)
                {
                    continue;
                }

                cells.Add((x, y));
            }
        }

        return cells;
    }

    /// <summary>
    /// The squares of a circle of <paramref name="radius"/> centred on a square
    /// (<c>GetMapTilesInCircle</c>, <c>Drawtile.cpp:4812</c>).
    /// </summary>
    /// <remarks>
    /// <b>A square, then pruned by distance.</b> The side is <c>radius * 2 | 1</c> — doubled and
    /// then forced odd, the reference's own comment explaining that this leaves <c>radius</c> on
    /// both sides of the target. So a radius of 2 gives a 5×5 square before pruning, not 4×4.
    /// <para>
    /// Facing is east (1, 0) because, as the reference notes, direction does not matter for a
    /// circle — and with an odd side the quarter-square nudge cancels out, so the square really is
    /// centred.
    /// </para>
    /// <para>
    /// The prune uses <see cref="CombatMap.Distance"/>, which is Euclidean rounded to nearest — so
    /// the corners of the square do come off, and the shape really is round. <b>The combatant
    /// version prunes by a different distance</b>: <c>GetCombatantsInCircle</c> calls the
    /// footprint-aware overload (<c>Drawtile.cpp:1699</c>), which walks a large monster's icon
    /// inwards to its nearest occupied square first. A big monster is therefore caught by a circle
    /// that its top-left corner alone would fall outside of.
    /// </para>
    /// </remarks>
    public static List<(int X, int Y)> Circle(int x0, int y0, int radius,
                                              int mapWidth, int mapHeight)
    {
        if (radius < 0)
        {
            return [];
        }

        int side = (radius * 2) | 1;
        var square = Rectangle(x0, y0, dirX: 1, dirY: 0, side, side,
                               forceNonZero: false, mapWidth, mapHeight);

        return [.. square.Where(c => CombatMap.Distance(x0, y0, c.X, c.Y) <= radius)];
    }

    /// <summary>
    /// Which combatants stand on any of a set of squares
    /// (<c>GetCombatantsInRectangle</c> → <c>GetCombatantsInSquare</c>, <c>Drawtile.cpp:4614</c>).
    /// </summary>
    /// <remarks>
    /// Read off the grid's occupancy layer rather than by scanning combatants, so a large
    /// monster is caught by any square of its footprint and is reported once.
    /// </remarks>
    public static List<int> CombatantsIn(CombatMap map, IEnumerable<(int X, int Y)> squares)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(squares);

        var found = new List<int>();

        foreach (var (x, y) in squares)
        {
            int dude = map.OccupantAt(x, y);
            if (dude != CombatMap.NoDude && !found.Contains(dude))
            {
                found.Add(dude);
            }
        }

        return found;
    }

    /// <summary>
    /// Whether a point falls in a triangle (<c>IsPointInTriangle</c>, <c>Drawtile.cpp:4375</c>).
    /// </summary>
    /// <param name="onLine">
    /// Which edges count as inside, as bit flags: 1 for the <c>p2</c>–<c>p3</c> edge, 2 for
    /// <c>p1</c>–<c>p3</c>, 4 for <c>p1</c>–<c>p2</c>. The cone asks for all three (7) when
    /// collecting squares and only the far edge (1) when testing a combatant directly.
    /// </param>
    /// <remarks>
    /// Barycentric, kept in the unnormalised form the reference uses — a 2011 comment records the
    /// change to "compute everything scaled by b0", avoiding a division. A degenerate triangle
    /// (zero area) contains nothing at all, including its own vertices.
    /// </remarks>
    public static bool IsPointInTriangle(double x, double y,
                                         double x1, double y1, double x2, double y2,
                                         double x3, double y3, int onLine)
    {
        double b0 = ((x2 - x1) * (y3 - y1)) - ((x3 - x1) * (y2 - y1));
        if (b0 == 0)
        {
            return false;
        }

        double b1 = ((y3 - y1) * (x - x1)) - ((x3 - x1) * (y - y1));
        double b2 = ((y1 - y2) * (x - x1)) - ((x1 - x2) * (y - y1));

        if (b0 < 0)
        {
            b0 = -b0;
            b1 = -b1;
            b2 = -b2;
        }

        double b3 = b0 - b1 - b2;

        bool inFar = b3 > 0 || (b3 == 0 && (onLine & 1) != 0);
        bool inSideA = b1 > 0 || (b1 == 0 && (onLine & 2) != 0);
        bool inSideB = b2 > 0 || (b2 == 0 && (onLine & 4) != 0);

        return inFar && inSideA && inSideB;
    }

    /// <summary>
    /// The squares of a cone (<c>GetCombatantsAndTilesInCone</c>, <c>Drawtile.cpp:5083</c>).
    /// </summary>
    /// <param name="casterX">Where the cast comes from; it sets the direction and nothing else.</param>
    /// <param name="targetX">The cone's apex — the chosen square, not the caster.</param>
    /// <param name="length">How far past the apex it reaches.</param>
    /// <param name="width">How wide it is at that far end.</param>
    /// <remarks>
    /// <para>
    /// <b>The cone is a triangle whose apex is the target, not the caster.</b> The reference's own
    /// diagram shows the caster off to one side of the apex: <c>C-----T ------&gt;L</c>, with the base
    /// A–B standing across the far point L. So a cone cast at an adjacent square starts there and
    /// spreads beyond it; the squares between caster and target are not in it.
    /// </para>
    /// <para>
    /// Points are tested against the triangle rather than the triangle being rasterised, over the
    /// bounding box of its three corners — the reference explains it chose this because
    /// "the Point-In-Triangle test is slow". All three edges count as inside.
    /// </para>
    /// <para>
    /// <b>A cone cast on the caster's own square produces nothing.</b> The direction comes from
    /// <c>sinT = (Ty-Cy)/D</c> with <c>D</c> the caster-to-target distance, so a zero distance
    /// divides by zero. The reference has no guard and gets a NaN triangle that contains nothing;
    /// this returns empty explicitly, which is the same result without the arithmetic.
    /// </para>
    /// <para>
    /// The far point is placed at <c>length - 0.000001</c> rather than <c>length</c>. The epsilon
    /// pulls it just inside, which keeps the last row of squares off the boundary — where, with all
    /// three edges counting as inside, they would otherwise be included.
    /// </para>
    /// </remarks>
    public static List<(int X, int Y)> Cone(int casterX, int casterY, int targetX, int targetY,
                                            int length, int width, bool forceNonZero,
                                            int mapWidth, int mapHeight)
    {
        var cells = new List<(int X, int Y)>();

        if (forceNonZero)
        {
            length = Math.Max(1, length);
            width = Math.Max(1, width);
        }

        if (length <= 0 || width <= 0)
        {
            return cells;
        }

        width = Math.Min(width, Math.Min(mapWidth, mapHeight));
        length = Math.Min(length, Math.Min(mapWidth, mapHeight));

        double dx = targetX - casterX;
        double dy = targetY - casterY;
        double d = Math.Sqrt((dx * dx) + (dy * dy));

        if (d == 0)
        {
            return cells;
        }

        double sin = dy / d;
        double cos = dx / d;

        double lx = targetX + ((length - 0.000001) * cos);
        double ly = targetY + ((length - 0.000001) * sin);
        double ax = lx - (width / 2.0 * sin);
        double ay = ly + (width / 2.0 * cos);
        double bx = lx + (width / 2.0 * sin);
        double by = ly - (width / 2.0 * cos);

        int left = Math.Clamp((int)Math.Min(targetX, Math.Min(ax, bx)), 0, mapWidth - 1);
        int right = Math.Clamp((int)Math.Max(targetX, Math.Max(ax, bx)), 0, mapWidth - 1);
        int top = Math.Clamp((int)Math.Min(targetY, Math.Min(ay, by)), 0, mapHeight - 1);
        int bottom = Math.Clamp((int)Math.Max(targetY, Math.Max(ay, by)), 0, mapHeight - 1);

        // Column-major, as the reference's loops are: x outermost, y inner.
        for (int x = left; x <= right; x++)
        {
            for (int y = top; y <= bottom; y++)
            {
                if (IsPointInTriangle(x, y, targetX, targetY, ax, ay, bx, by, onLine: 7))
                {
                    cells.Add((x, y));
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// The squares a line passes through (<c>GetMapTilesInLine</c>, <c>Drawtile.cpp:5540</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A line is always exactly one square thick.</b> The width-taking overloads
    /// (<c>Drawtile.cpp:5574</c>, <c>:5588</c>) test it for being positive and then drop it — they
    /// call the two-point version without passing it on. The directional overload has the widening
    /// loop written out and commented, under the note "need the following only if line width can be
    /// greater than 1". Since a spell's line width comes from <c>MaxTargets</c>, that field is a
    /// zero test and nothing more for these two shapes.
    /// </para>
    /// <para>
    /// <b>Bresenham is run in pixels, not squares.</b> Both ends are converted with
    /// <c>TerrainToWorldCoord</c> (×48) and each step converted back, so the line is drawn at
    /// forty-eight times the resolution and then quantised — which picks up squares a
    /// square-resolution Bresenham would step over. The conversion takes each square's <b>top-left
    /// corner</b>: the <c>+ COMBAT_TILE_WIDTH/2</c> that would have centred it is commented out at
    /// both ends, so the line runs corner to corner and leans up and left of where it looks.
    /// </para>
    /// <para>
    /// The walk stops at the first square outside the map rather than skipping it — the callback
    /// returns false there, which terminates the line.
    /// </para>
    /// </remarks>
    public static List<(int X, int Y)> Line(int startX, int startY, int endX, int endY,
                                            int mapWidth, int mapHeight)
    {
        var cells = new List<(int X, int Y)>();
        var seen = new HashSet<(int, int)>();

        int x0 = startX * CombatMap.TileWidth;
        int y0 = startY * CombatMap.TileHeight;
        int x1 = endX * CombatMap.TileWidth;
        int y1 = endY * CombatMap.TileHeight;

        foreach (var (px, py) in Bresenham(x0, y0, x1, y1))
        {
            int tx = px / CombatMap.TileWidth;
            int ty = py / CombatMap.TileHeight;

            if (tx < 0 || tx >= mapWidth || ty < 0 || ty >= mapHeight)
            {
                break;
            }

            if (seen.Add((tx, ty)))
            {
                cells.Add((tx, ty));
            }
        }

        return cells;
    }

    /// <summary>
    /// The Bresenham walk the line shapes use (<c>Line2DSpell</c>, <c>Drawtile.cpp:3341</c>).
    /// </summary>
    /// <remarks>
    /// Steps along whichever axis is longer and carries the error term on the other. Both endpoints
    /// are visited. The reference threads a <c>speed</c> through this to skip points; it is a
    /// constant 1 in every spell path, so nothing is skipped.
    /// </remarks>
    private static IEnumerable<(int X, int Y)> Bresenham(int x0, int y0, int x1, int y1)
    {
        int x = x0;
        int y = y0;
        int dx = x1 - x0;
        int dy = y1 - y0;
        int sx = Math.Sign(dx);
        int sy = Math.Sign(dy);

        dx = Math.Abs(dx);
        dy = Math.Abs(dy);
        int ax = 2 * dx;
        int ay = 2 * dy;

        if (dy > dx)
        {
            for (int decision = ax - dy; ; y += sy, decision += ax)
            {
                yield return (x, y);

                if (y == y1)
                {
                    break;
                }

                if (decision >= 0)
                {
                    decision -= ay;
                    x += sx;
                }
            }
        }
        else
        {
            for (int decision = ay - dx; ; x += sx, decision += ay)
            {
                yield return (x, y);

                if (x == x1)
                {
                    break;
                }

                if (decision >= 0)
                {
                    decision -= ax;
                    y += sy;
                }
            }
        }
    }
}
