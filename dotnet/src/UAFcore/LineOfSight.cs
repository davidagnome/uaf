namespace UAFcore;

/// <summary>
/// Whether one combat square can see another (<c>IsLineOfSight</c>, <c>Drawtile.cpp:3460</c>).
/// </summary>
/// <remarks>
/// <para>
/// An octant-decomposed DDA walk, not a plain Bresenham line: at each step it tests the cells on
/// <b>both</b> sides of the line, so a sight line that slips diagonally between two walls is
/// blocked rather than allowed. That is the whole point of the two-sided test and it is why this
/// is not interchangeable with a stock line-drawing routine.
/// </para>
/// <para>
/// Terrain only — combatants never block sight. Monster placement's <c>V</c> rule and spell
/// targeting both go through here.
/// </para>
/// </remarks>
public static class LineOfSight
{
    /// <summary>
    /// Whether the straight line from one square to another is unobstructed.
    /// </summary>
    public static bool Between(CombatMap map, int x0, int y0, int x1, int y1)
    {
        ArgumentNullException.ThrowIfNull(map);

        int dx = x1 - x0;
        int dy = y1 - y0;

        // The circle in eight octants, chosen so the walk always steps along the longer axis.
        int octant = dx > 0 ? 0 : 4;
        if (dy > 0) { octant += 2; }
        if (Math.Abs(dy) > Math.Abs(dx)) { octant += 1; }

        // Transcribed from the switch at Drawtile.cpp:3468. Four of the eight walk from the far
        // end instead of the near one, which is how the routine stays symmetric.
        return octant switch
        {
            0 => Walk(map, x0, y0, 1, 0, 0, -1, dx, -dy),
            1 => Walk(map, x1, y1, 0, 1, -1, 0, -dy, dx),
            2 => Walk(map, x0, y0, 1, 0, 0, 1, dx, dy),
            3 => Walk(map, x0, y0, 0, 1, 1, 0, dy, dx),
            4 => Walk(map, x1, y1, 1, 0, 0, 1, -dx, -dy),
            5 => Walk(map, x1, y1, 0, 1, 1, 0, -dy, -dx),
            6 => Walk(map, x1, y1, 1, 0, 0, -1, -dx, dy),
            _ => Walk(map, x0, y0, 0, 1, -1, 0, dy, -dx),
        };
    }

    /// <summary>
    /// One octant's walk (<c>TestLineOfSight</c>, <c>Drawtile.cpp:3417</c>). True when nothing
    /// blocked.
    /// </summary>
    /// <remarks>
    /// <paramref name="c"/> is the DDA accumulator and doubles as the step count;
    /// <paramref name="dc"/> is the minor-axis increment. The <c>(i | c) != 0</c> guard skips the
    /// very first left-hand cell — the square the looker is standing on, which must not block its
    /// own view — and the <c>i != numStep - 1</c> guard skips the last right-hand cell, the
    /// target's own square.
    /// </remarks>
    private static bool Walk(CombatMap map, int x, int y,
                             int dx1, int dy1, int dx2, int dy2, int c, int dc)
    {
        int max = 2 * c;
        int steps = c;
        c += dc;
        dc *= 2;

        for (int i = 0; i < steps; i++)
        {
            if ((i | c) != 0 && Blocks(map, x, y))
            {
                return false;
            }

            x += dx1;
            y += dy1;

            if (i != steps - 1 && Blocks(map, x, y))
            {
                return false;
            }

            c += dc;
            if (c >= max)
            {
                x += dx2;
                y += dy2;
                c -= max;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a square stops sight (<c>TestLineOfSight(x, y)</c>, <c>Drawtile.cpp:3404</c>).
    /// </summary>
    /// <remarks>
    /// <b>Not the same predicate as <see cref="CombatMap.IsSeeThrough"/>.</b> This one bounds the
    /// tile index with <c>cell &lt; CurrentTileCount</c> where the visibility routine uses
    /// <c>cell &gt; CurrentTileCount</c> to reject — so the very last tile of each table counts as
    /// transparent here and opaque there. Unreachable in practice, because the last dungeon and
    /// wilderness tiles are both disabled and so are never placed, but the two are not
    /// interchangeable and should not be folded together.
    /// </remarks>
    private static bool Blocks(CombatMap map, int x, int y)
    {
        if (!map.Contains(x, y))
        {
            return false;
        }

        int cell = map.CellAt(x, y);
        var tiles = map.Tiles;
        return cell > 0 && cell < tiles.Length - 1 && !tiles[cell].SeeThrough;
    }
}
