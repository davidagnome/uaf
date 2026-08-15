namespace UAF.Scripting;

/// <summary>
/// What the line-of-sight walks need to know about a map.
/// </summary>
/// <remarks>
/// <b>Two questions, not one, because the reference's two algorithms disagree about the answer off
/// the map.</b> <see cref="GpdlLineOfSight.IsClear"/> treats a square outside the map as clear;
/// <see cref="GpdlLineOfSight.HasVisibility"/> treats it as blocked. A single "can I see through
/// this?" predicate could not express both.
/// </remarks>
public interface IGpdlSightMap
{
    /// <summary>Whether a square is on the map at all (<c>ValidCoords</c>).</summary>
    bool Contains(int x, int y);

    /// <summary>
    /// Whether the terrain in an on-map square can be seen through.
    /// </summary>
    /// <remarks>
    /// The reference's <c>tile_invisible</c>, <b>which reads backwards</b> — the flag is
    /// non-zero for a tile you <i>can</i> see through. An empty square (terrain index 0) is not a
    /// tile at all; see the two callers for what each makes of that.
    /// </remarks>
    bool SeeThrough(int x, int y);

    /// <summary>
    /// Whether a square holds terrain at all — <c>cell &gt; 0</c> and within the tile table.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SeeThrough"/> because an index outside the tile table is neither
    /// see-through nor opaque, and the two algorithms disagree about it too.
    /// </remarks>
    bool HasTerrain(int x, int y);
}

/// <summary>
/// The two line-of-sight walks (<c>UAFWin/Drawtile.cpp:3460</c> and <c>:3509</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The engine has two, they use different algorithms, and they do not agree.</b>
/// <see cref="IsClear"/> is an octant-decomposed walk testing the pair of squares the line passes
/// between; <see cref="HasVisibility"/> is a plain Bresenham line testing one square at a time.
/// <c>$IsLineOfSight</c> calls the first and <c>$VisualDistance</c> the second, so a script can get
/// two different answers about the same pair of points.
/// </para>
/// <para>
/// <b>Neither considers combatants.</b> Both look only at terrain, so a wall of allies blocks
/// nothing.
/// </para>
/// </remarks>
public static class GpdlLineOfSight
{
    /// <summary>
    /// Whether the octant walk finds the line clear (<c>IsLineOfSight</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The line is decomposed into one of eight octants so the walk always advances along its
    /// major axis with a non-negative drift along the minor one — four of the eight start from the
    /// far endpoint to arrange that. Each step tests <b>both</b> squares the line passes between,
    /// which is why a diagonal through a doorway's corner is blocked.
    /// </para>
    /// <para>
    /// <b>Off the map counts as CLEAR here</b>, and so does an empty square or a terrain index
    /// past the tile table — the reference's per-square test starts <c>flags = 0</c> and only sets
    /// it for a square it can positively identify as opaque. <see cref="HasVisibility"/> takes the
    /// opposite view of every one of those cases.
    /// </para>
    /// </remarks>
    public static bool IsClear(IGpdlSightMap map, int x0, int y0, int x1, int y1)
    {
        ArgumentNullException.ThrowIfNull(map);

        int dx = x1 - x0;
        int dy = y1 - y0;

        // Eight octants: which half of x, which half of y, and which axis is the major one.
        int octant = (dx > 0 ? 0 : 4) + (dy > 0 ? 2 : 0) + (Math.Abs(dy) > Math.Abs(dx) ? 1 : 0);

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
        } == 0;
    }

    /// <summary>
    /// One octant's walk (<c>TestLineOfSight</c>, the eight-argument form).
    /// </summary>
    /// <param name="majorX">Step along the major axis.</param>
    /// <param name="minorX">Step along the minor axis, taken when the error term overflows.</param>
    /// <param name="count">How far along the major axis, which is also the number of steps.</param>
    /// <param name="drift">How far along the minor axis over the whole line.</param>
    /// <returns>Non-zero once anything opaque is found.</returns>
    /// <remarks>
    /// <b>The <c>(i | c) != 0</c> guard in the reference is dead code.</b> It is a <i>bitwise</i>
    /// or of the loop counter and the error term, so it is false only when both are zero — and the
    /// error term starts at <c>count + drift</c>, which is positive whenever the loop runs at all.
    /// Transcribed anyway: it costs nothing and removing it would hide that the original had it.
    /// </remarks>
    private static int Walk(IGpdlSightMap map, int x, int y,
                            int majorX, int majorY, int minorX, int minorY,
                            int count, int drift)
    {
        int flags = 0;
        int limit = 2 * count;
        int steps = count;

        int error = count + drift;
        int step = drift * 2;

        for (int i = 0; i < steps && flags == 0; i++)
        {
            // The square on one side of the line...
            if ((i | error) != 0)
            {
                flags |= Opaque(map, x, y);
            }

            x += majorX;
            y += majorY;

            // ...and the one on the other, except past the far end.
            if (i != steps - 1)
            {
                flags |= Opaque(map, x, y);
            }

            error += step;

            if (error >= limit)
            {
                x += minorX;
                y += minorY;
                error -= limit;
            }
        }

        return flags;
    }

    /// <summary>
    /// One square, as the octant walk sees it.
    /// </summary>
    /// <remarks>
    /// <b>Only a square positively identified as opaque blocks.</b> Off the map, empty, or a
    /// terrain index past the tile table all read as clear.
    /// </remarks>
    private static int Opaque(IGpdlSightMap map, int x, int y) =>
        map.Contains(x, y) && map.HasTerrain(x, y) && !map.SeeThrough(x, y) ? 1 : 0;

    /// <summary>
    /// Whether the Bresenham walk finds the line clear (<c>HaveLineOfSight</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both endpoints are skipped</b>, so a combatant standing in a wall can still see out and
    /// be seen. Every square between them must be see-through.
    /// </para>
    /// <para>
    /// <b>Off the map counts as BLOCKED here</b>, the opposite of <see cref="IsClear"/> — and so
    /// does an empty square, since <c>HaveVisibility</c> requires a terrain index of at least one.
    /// A fight on a map with blank squares between the combatants is therefore fully sighted by one
    /// algorithm and fully blind by the other.
    /// </para>
    /// </remarks>
    public static bool HasVisibility(IGpdlSightMap map, int x0, int y0, int x1, int y1)
    {
        ArgumentNullException.ThrowIfNull(map);

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

        // Whichever axis is longer is the one stepped every iteration.
        if (dy > dx)
        {
            for (int error = ax - dy; ; y += sy, error += ax)
            {
                if (!AtEnd(x, y, x0, y0, x1, y1) && !Visible(map, x, y))
                {
                    return false;
                }

                if (y == y1)
                {
                    return true;
                }

                if (error >= 0)
                {
                    error -= ay;
                    x += sx;
                }
            }
        }

        for (int error = ay - dx; ; x += sx, error += ay)
        {
            if (!AtEnd(x, y, x0, y0, x1, y1) && !Visible(map, x, y))
            {
                return false;
            }

            if (x == x1)
            {
                return true;
            }

            if (error >= 0)
            {
                error -= ax;
                y += sy;
            }
        }
    }

    private static bool AtEnd(int x, int y, int x0, int y0, int x1, int y1) =>
        (x == x0 && y == y0) || (x == x1 && y == y1);

    /// <summary>One square, as the Bresenham walk sees it (<c>HaveVisibility</c>).</summary>
    private static bool Visible(IGpdlSightMap map, int x, int y) =>
        map.Contains(x, y) && map.HasTerrain(x, y) && map.SeeThrough(x, y);

    /// <summary>
    /// What <c>$VisualDistance</c> answers when it cannot see.
    /// </summary>
    /// <remarks>
    /// <b>A very large number, not a negative one</b> — so a script comparing "is this closer
    /// than N?" answers no for an unseen target, which is the useful default, and one adding
    /// distances up gets nonsense.
    /// </remarks>
    public const int NotVisible = 999999;

    /// <summary>
    /// The distance between two squares, or <see cref="NotVisible"/> when the line is blocked.
    /// </summary>
    /// <remarks>
    /// <b>Truncated, not rounded.</b> The reference assigns a <c>float</c> square root to an
    /// <c>int</c>, so a diagonal of two squares (2.83) reads as 2.
    /// </remarks>
    public static int Distance(IGpdlSightMap map, int x0, int y0, int x1, int y1) =>
        HasVisibility(map, x0, y0, x1, y1)
            ? (int)Math.Sqrt(((x0 - x1) * (x0 - x1)) + ((y0 - y1) * (y0 - y1)))
            : NotVisible;
}
