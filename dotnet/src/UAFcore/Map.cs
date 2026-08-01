using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// What a cell's blockage byte means (<c>BlockageType</c>, <c>Shared/Level.h:29</c>).
/// </summary>
/// <remarks>
/// Sixteen values, and their grouping is not obvious from the names. The "secret" variants are
/// <i>passable</i> once found and the false door is not — <c>FalseDoorBlk</c> is documented in the
/// header as "secret + blocked". So a passability test cannot simply be
/// <c>value != OpenBlk</c>; it has to name the cases.
/// </remarks>
public enum BlockageType : byte
{
    Open = 0,
    OpenSecret = 1,
    Blocked = 2,
    FalseDoor = 3,
    Locked = 4,
    LockedSecret = 5,
    LockedWizard = 6,
    LockedWizardSecret = 7,
    LockedKey1 = 8,
    LockedKey2 = 9,
    LockedKey3 = 10,
    LockedKey4 = 11,
    LockedKey5 = 12,
    LockedKey6 = 13,
    LockedKey7 = 14,
    LockedKey8 = 15,
}

/// <summary>
/// A level's cell grid, wrapped for lookup by coordinate and direction.
/// </summary>
/// <remarks>
/// <see cref="LevelFile"/> stores cells as a flat array; this is the indexing and passability
/// logic that belongs to the engine rather than to the reader.
/// </remarks>
public sealed class Map(byte width, byte height, IReadOnlyList<AreaMapCell> cells)
{
    private readonly IReadOnlyList<AreaMapCell> cells =
        cells ?? throw new ArgumentNullException(nameof(cells));

    public int Width { get; } = width;

    public int Height { get; } = height;

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>
    /// Wraps a coordinate onto the map's torus.
    /// </summary>
    /// <remarks>
    /// A level has no edges. Both the viewport (<c>Viewport.cpp:4105</c>) and movement
    /// (<c>Party.cpp:1735</c>) take the coordinate modulo the map extent, so walking off the east
    /// side arrives at the west. Only walls stop a party.
    /// </remarks>
    public (int X, int Y) Wrap(int x, int y) =>
        (ViewMap.Wrap(x, Width), ViewMap.Wrap(y, Height));

    /// <summary>The cells each viewport slot shows from a position and facing.</summary>
    public ViewMap View(int x, int y, Facing facing) =>
        ViewMap.For(x, y, facing, Width, Height);

    /// <summary>The cell at a coordinate, or null when outside the map.</summary>
    /// <remarks>
    /// Row-major: <c>ReadAreaMap</c> fills <c>width × height</c> cells in a single loop, so the
    /// stride is the width.
    /// </remarks>
    public AreaMapCell? At(int x, int y) =>
        Contains(x, y) ? cells[(y * Width) + x] : null;

    /// <summary>The blockage on one face of a cell.</summary>
    public BlockageType Blockage(int x, int y, Facing facing)
    {
        // Not cell.Blockage[(int)facing]: the array is stored north, south, east, west, so it has
        // to be permuted. See AreaMapCell.BlockageAt.
        var cell = At(x, y);
        return cell?.BlockageAt((int)facing) is { } raw
            ? (BlockageType)raw
            : BlockageType.Blocked;
    }

    /// <summary>
    /// Whether the party can leave <paramref name="x"/>,<paramref name="y"/> in a direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the cell being left is consulted, not the one being entered. That mirrors how the data
    /// is authored — each cell carries the blockage of its own four faces — but it does mean a
    /// design with inconsistent facing pairs behaves asymmetrically, which the original also
    /// allowed.
    /// </para>
    /// <para>
    /// Locked and keyed doors count as blocked here because the party has no keys and no
    /// unlocking rules yet. When those arrive this becomes a question for the rules layer rather
    /// than the map, which is why the blockage type is returned rather than a bare bool.
    /// </para>
    /// </remarks>
    public bool CanLeave(int x, int y, Facing facing) => Blockage(x, y, facing) switch
    {
        // Secret passages are open once found; nothing here tracks "found" yet, so they are
        // treated as open. That is the permissive choice, and it is deliberate: the alternative
        // walls a party into a room with no way to discover the exit.
        BlockageType.Open or BlockageType.OpenSecret => true,
        _ => false,
    };
}
