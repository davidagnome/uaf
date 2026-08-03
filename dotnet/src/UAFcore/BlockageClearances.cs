using UAF.Serialization;

namespace UAFcore;

/// <summary>What a party can clear about a wall (<c>BlockageStats::flags</c>, <c>Char.h:61</c>).</summary>
public enum Clearable
{
    /// <summary>A locked door the party has opened.</summary>
    Locked = 0,

    /// <summary>A secret door the party has found.</summary>
    Secret = 1,

    /// <summary>A magically sealed way the party has dispelled.</summary>
    Spelled = 2,
}

/// <summary>
/// Which blockages the party has got past (<c>BLOCKAGE_STATUS</c>, <c>Char.h:98</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a list of clearances, not of blockages.</b> The name says otherwise and so does the
/// header comment beside the struct, but every accessor reads the other way: <c>IsSecret</c>
/// returns <b>TRUE</b> for a cell that is not in the list — "not found means party has not cleared
/// secret bit for this spot yet" (<c>Char.cpp:574</c>). An empty list is a dungeon where nothing
/// has been opened, and a port that read it as "these are the walls in the way" would have the
/// whole map inverted.
/// </para>
/// <para>
/// <b>Every bit starts at 1 and is set to 0 on clearing.</b> A new entry is created with
/// <c>0xFFFF</c> and then the one bit for the one facing is cleared, so a record's presence means
/// only that <i>something</i> about that cell has been dealt with.
/// </para>
/// </remarks>
public sealed class BlockageClearances
{
    /// <summary>What a cell's flags are before the party has cleared anything.</summary>
    public const ushort AllBlocked = 0xFFFF;

    private readonly Dictionary<(int Level, int X, int Y), ushort> cells = [];

    /// <summary>
    /// Which four-bit group a facing occupies.
    /// </summary>
    /// <remarks>
    /// <b>The bit groups are ordered North, South, East, West; the facings are North, East,
    /// South, West.</b> The two are transposed for East and South (<c>Char.h:53</c> against
    /// <c>Externs.h:1039</c>), so indexing the flags by the facing value swaps them — a secret
    /// door found to the east opens one to the south and stays shut. Nothing about either
    /// declaration hints at the other.
    /// </remarks>
    public static int GroupOf(Facing facing) => facing switch
    {
        Facing.North => 0,
        Facing.South => 1,
        Facing.East => 2,
        Facing.West => 3,
        _ => 0,
    };

    private static int BitFor(Facing facing, Clearable what) =>
        (GroupOf(facing) * 4) + (int)what;

    /// <summary>The raw flags for a cell — <see cref="AllBlocked"/> when it has no record.</summary>
    public ushort FlagsAt(int level, int x, int y) =>
        cells.TryGetValue((level, x, y), out ushort flags) ? flags : AllBlocked;

    /// <summary>
    /// Whether a blockage is still in the party's way (<c>IsSecret</c>, <c>IsLocked</c>,
    /// <c>IsSpelled</c>).
    /// </summary>
    public bool IsBlocked(int level, int x, int y, Facing facing, Clearable what) =>
        (FlagsAt(level, x, y) & (1 << BitFor(facing, what))) != 0;

    /// <summary>
    /// Records that the party got past one blockage (<c>ClearSecret</c> and its siblings).
    /// </summary>
    /// <remarks>
    /// Clearing one facing leaves the other three — and the other two kinds on this facing —
    /// exactly as they were, which is why the entry is created full rather than empty.
    /// </remarks>
    public void Clear(int level, int x, int y, Facing facing, Clearable what)
    {
        var key = (level, x, y);
        ushort flags = cells.TryGetValue(key, out ushort found) ? found : AllBlocked;

        cells[key] = (ushort)(flags & ~(1 << BitFor(facing, what)));
    }

    /// <summary>How many cells have had something cleared.</summary>
    public int Count => cells.Count;

    /// <summary>
    /// The savegame's shape, in a stable order.
    /// </summary>
    /// <remarks>
    /// The reference's own list is in insertion order, which nothing depends on; sorting makes a
    /// written save reproducible from the same play, which is worth more than matching an order
    /// no reader looks at.
    /// </remarks>
    public List<BlockageData> ToRecords() =>
        [.. cells.OrderBy(e => e.Key.Level).ThenBy(e => e.Key.Y).ThenBy(e => e.Key.X)
                 .Select(e => new BlockageData(e.Key.Level, e.Key.X, e.Key.Y, e.Value))];

    /// <summary>Rebuilds from a savegame's records.</summary>
    public static BlockageClearances FromRecords(IReadOnlyList<BlockageData> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var clearances = new BlockageClearances();
        foreach (var record in records)
        {
            clearances.cells[(record.Level, record.X, record.Y)] = record.Stats;
        }
        return clearances;
    }
}
