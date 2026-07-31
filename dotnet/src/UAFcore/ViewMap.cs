namespace UAFcore;

/// <summary>
/// Which map cell each viewport slot shows, for a party at a position and facing.
/// </summary>
/// <remarks>
/// <para>
/// A direct port of <c>DetermineView</c> (<c>Shared/Viewport.cpp:4040</c>). This is the geometry
/// of the Gold Box corridor view: fifteen cells arranged two squares deep and three wide either
/// side, expressed relative to the party rather than in map coordinates.
/// </para>
/// <para>
/// <b>The map is a torus.</b> Slots 0–12 are wrapped with <c>% area_width</c> and
/// <c>% area_height</c> (<c>Viewport.cpp:4105</c>), so a corridor running off the east edge is
/// visibly continuous with the west edge. Movement wraps identically
/// (<c>Party.cpp:1735</c>), which means a map has no edges at all — only walls.
/// </para>
/// <para>
/// <b>Slots 13 and 14 are deliberately <i>not</i> wrapped</b>, with the original commenting "it is
/// significant if they exceed the map boundaries so don't wrap them". They are the far
/// left-and-right cells used by the occlusion tests, which ask <c>!validCoords(viewMap[13])</c> —
/// that is, "is there no cell there at all". Wrapping them would make the test always false and
/// silently change which walls get drawn.
/// </para>
/// </remarks>
public sealed class ViewMap
{
    /// <summary>Slots 0–12, which wrap.</summary>
    public const int WrappedSlots = 13;

    /// <summary>Total slots including the two unwrapped outer ones.</summary>
    public const int TotalSlots = 15;

    /// <summary><c>deltaX</c> / <c>deltaY</c> (<c>Shared/Globals.cpp:582-583</c>), N/E/S/W.</summary>
    private static readonly int[] DeltaX = [0, 1, 0, -1];
    private static readonly int[] DeltaY = [-1, 0, 1, 0];

    private readonly (int X, int Y)[] cells = new (int, int)[TotalSlots];

    /// <summary>
    /// The cell a slot shows. Slots 13 and 14 may be outside the map, which is meaningful.
    /// </summary>
    public (int X, int Y) this[int slot] => cells[slot];

    /// <summary>Builds the view for a party at <paramref name="x"/>,<paramref name="y"/>.</summary>
    public static ViewMap For(int x, int y, Facing facing, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int front = (int)facing & 3;
        int left = (front + 3) & 3;
        int right = (front + 1) & 3;

        int fx = DeltaX[front], fy = DeltaY[front];
        int lx = DeltaX[left], ly = DeltaY[left];
        int rx = DeltaX[right], ry = DeltaY[right];

        var view = new ViewMap();
        var c = view.cells;

        // Transcribed in the original's own order, comments included, because the slot index is
        // load-bearing everywhere downstream -- RenderSquare0 asks for viewMap[13] by number.
        c[0] = (x + ((fx + lx) * 2), y + ((fy + ly) * 2));   // forward 2, left 2
        c[1] = (x + ((fx + rx) * 2), y + ((fy + ry) * 2));   // forward 2, right 2
        c[2] = (x + fx + fx + lx, y + fy + fy + ly);         // forward 2, left 1
        c[3] = (x + fx + fx + rx, y + fy + fy + ry);         // forward 2, right 1
        c[4] = (x + fx + fx, y + fy + fy);                   // forward 2
        c[5] = (x + fx + lx + lx, y + fy + ly + ly);         // forward 1, left 2
        c[6] = (x + fx + rx + rx, y + fy + ry + ry);         // forward 1, right 2
        c[7] = (x + fx + lx, y + fy + ly);                   // forward 1, left 1
        c[8] = (x + fx + rx, y + fy + ry);                   // forward 1, right 1
        c[9] = (x + fx, y + fy);                             // forward 1
        c[10] = (x + lx, y + ly);                            // left 1
        c[11] = (x + rx, y + ry);                            // right 1
        c[12] = (x, y);                                      // here

        // Forward 2 and three to the side. Left unwrapped on purpose -- see the remarks.
        c[13] = (x + fx + fx + lx + lx + lx, y + fy + fy + ly + ly + ly);
        c[14] = (x + fx + fx + rx + rx + rx, y + fy + fy + ry + ry + ry);

        for (int i = 0; i < WrappedSlots; i++)
        {
            c[i] = (Wrap(c[i].X, width), Wrap(c[i].Y, height));
        }

        return view;
    }

    /// <summary>
    /// Wraps a coordinate onto the torus, handling negatives.
    /// </summary>
    /// <remarks>
    /// The original adds the extent before taking the remainder, because C's <c>%</c> keeps the
    /// sign of the dividend and a bare <c>-1 % width</c> is <c>-1</c>, not <c>width - 1</c>. C#
    /// behaves the same way, so the same correction is needed rather than being an artefact of the
    /// original's style.
    /// </remarks>
    public static int Wrap(int value, int extent) => ((value % extent) + extent) % extent;
}
