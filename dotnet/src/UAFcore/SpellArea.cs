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
}
