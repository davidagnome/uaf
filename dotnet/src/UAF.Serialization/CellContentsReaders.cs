using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// One cell's per-facing overrides: five override types × four facings, as raw bytes.
/// </summary>
/// <remarks>
/// <c>CELL_OVERRIDE</c> (<c>GlobalData.h:560</c>) is
/// <c>unsigned char m_overrides[NUM_OVERRIDE_TYPE][4]</c> — 5 × 4 = <b>20 bytes, and no
/// padding</b>, since every member is a char. It is never serialized field by field; a whole row
/// of them is blitted at once, so the size has to be exactly right or the row after it lands
/// mid-struct.
/// </remarks>
public sealed record CellOverride(byte[] Values)
{
    /// <summary><c>NUM_OVERRIDE_TYPE</c> (<c>GlobalData.h:476</c>).</summary>
    public const int OverrideTypes = 5;

    public const int Facings = 4;

    /// <summary><c>sizeof(CELL_OVERRIDE)</c>.</summary>
    public const int Size = OverrideTypes * Facings;

    /// <summary>
    /// The override for a type and facing, or null when unset.
    /// </summary>
    /// <remarks>
    /// 255 is the "no override" sentinel — <c>Clear()</c> memsets the struct to <c>0xFF</c>, and
    /// <c>GetMapOverride</c> returns -1 for it (<c>GlobalData.cpp:2418</c>). A reader that treated
    /// it as a value would override every wall in the level with index 255.
    /// </remarks>
    public byte? this[int overrideType, int facing]
    {
        get
        {
            byte value = Values[(overrideType * Facings) + facing];
            return value == 255 ? null : value;
        }
    }
}

/// <summary>One map row's overrides (<c>ROW_OVERRIDES</c>, <c>GlobalData.h:566</c>).</summary>
public sealed record RowOverrides(int Column, IReadOnlyList<CellOverride> Cells);

/// <summary>
/// A level's wall/door/background/overlay/blockage overrides
/// (<c>WALL_OVERRIDES::Serialize</c>, <c>GlobalData.cpp:2597</c>).
/// </summary>
/// <remarks>
/// Sparse by row: the count is how many entries follow, and each is prefixed by its row number,
/// with -1 meaning the row is absent and carries no payload.
/// </remarks>
/// <param name="EntryCount">
/// How many entries the count field declared, which is <b>not</b> <c>Rows.Count</c> when any of
/// them was an absent <c>-1</c>. Kept because a writer has to emit the same number, and because
/// the position of an absent row among the present ones is the one thing this shape cannot
/// recover — see <see cref="CellContentsWriters.CanWrite"/>.
/// </param>
public sealed record WallOverrides(IReadOnlyDictionary<int, RowOverrides> Rows, int EntryCount);

/// <summary>One item lying in a map cell (<c>CELL_ITEM</c>, <c>GlobalData.h:491</c>).</summary>
public sealed record CellItem(
    string ItemId, int Charges, int Quantity, int Identified, int Paid, int Type, byte Cursed);

/// <summary>The items in one cell of a column (<c>CELL_ROW_CONTENTS</c>).</summary>
public sealed record CellRowContents(int Row, IReadOnlyList<CellItem> Items);

/// <summary>One column of cells that hold something (<c>CELL_COLUMN_CONTENTS</c>).</summary>
public sealed record CellColumnContents(int Column, IReadOnlyList<CellRowContents> Rows);

/// <summary>
/// What is lying on the floor across a level (<c>CELL_LEVEL_CONTENTS</c>,
/// <c>GlobalData.cpp:2957</c>).
/// </summary>
public sealed record CellLevelContents(IReadOnlyList<CellColumnContents> Columns);

/// <summary>
/// Reads the two 5.x tables that hang off <c>LEVEL_STATS</c>: per-cell overrides and per-cell
/// item contents.
/// </summary>
/// <remarks>
/// <para>
/// Both are gated on <c>_CELL_CONTENTS_VERSION</c> (<c>GlobalData.cpp:3265</c>) and appear only in
/// 5.0-and-later designs — which is why <c>dc-default</c> at 5.28 previously stopped there. They
/// are sparse three-level structures: a count, then entries carrying their own coordinate, so a
/// mostly-empty level costs almost nothing.
/// </para>
/// <para>
/// <b>The override table is where the viewport's <c>GetMapOverride</c> reads from</b>
/// (<c>Viewport.cpp:1127</c>), and an override <i>wins</i> over the cell's own wall index. Until
/// now <c>UAFcore</c>'s wall resolver has had to note that as an accepted gap; with this ported
/// the data is available to close it.
/// </para>
/// </remarks>
public static class CellContentsReaders
{
    /// <summary>
    /// Reads <c>WALL_OVERRIDES</c>.
    /// </summary>
    /// <remarks>
    /// The row loop runs <c>m_numRow</c> times regardless of how many rows are present — the
    /// count is the number of <i>entries</i>, not the map height, and an absent row still costs
    /// its 4-byte -1. Reading it as "one entry per map row" would work only for a level with no
    /// gaps.
    /// </remarks>
    public static WallOverrides ReadWallOverrides(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int rowCount = ar.ReadInt32();
        var rows = new Dictionary<int, RowOverrides>();

        for (int i = 0; i < rowCount; i++)
        {
            int rowNumber = ar.ReadInt32();
            if (rowNumber < 0)
            {
                continue;
            }

            int columnCount = ar.ReadInt32();
            var cells = new List<CellOverride>(Math.Max(columnCount, 0));

            // One blit of columnCount * 20 bytes, not a loop of structs. Splitting it here is
            // presentation only; the bytes consumed are identical.
            var raw = ar.ReadBytes(checked(columnCount * CellOverride.Size));
            for (int c = 0; c < columnCount; c++)
            {
                cells.Add(new CellOverride(raw[(c * CellOverride.Size)..
                                               ((c + 1) * CellOverride.Size)]));
            }

            rows[rowNumber] = new RowOverrides(columnCount, cells);
        }

        return new WallOverrides(rows, rowCount);
    }

    /// <summary>Reads <c>CELL_LEVEL_CONTENTS</c>.</summary>
    public static CellLevelContents ReadCellContents(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int columnCount = ar.ReadInt32();
        var columns = new List<CellColumnContents>(Math.Max(columnCount, 0));

        for (int i = 0; i < columnCount; i++)
        {
            int column = ar.ReadInt32();
            int rowCount = ar.ReadInt32();
            var rows = new List<CellRowContents>(Math.Max(rowCount, 0));

            for (int r = 0; r < rowCount; r++)
            {
                int row = ar.ReadInt32();
                int itemCount = ar.ReadInt32();

                // The original returns early on zero rather than allocating, which changes nothing
                // about the bytes.
                var items = new List<CellItem>(Math.Max(itemCount, 0));
                for (int n = 0; n < itemCount; n++)
                {
                    items.Add(ReadCellItem(ar));
                }

                rows.Add(new CellRowContents(row, items));
            }

            columns.Add(new CellColumnContents(column, rows));
        }

        return new CellLevelContents(columns);
    }

    /// <summary>
    /// Reads one <c>CELL_ITEM</c> (<c>GlobalData.cpp</c>, <c>CELL_ITEM::Serialize</c>).
    /// </summary>
    /// <remarks>
    /// <c>m_type</c> is an enum written through an <c>int</c> temporary, so four bytes — but
    /// <c>m_cursed</c> is a <c>BYTE</c> and is one. The pair is the usual trap: adjacent fields
    /// with no shared width, and the narrower one last where a reader is least likely to check.
    /// </remarks>
    public static CellItem ReadCellItem(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        // ITEM_ID derives from CString (Externs.h:1378), so it is an ordinary counted string.
        string itemId = ar.ReadString();

        return new CellItem(itemId,
                            ar.ReadInt32(),      // charges
                            ar.ReadInt32(),      // qty
                            ar.ReadInt32(),      // identified
                            ar.ReadInt32(),      // paid
                            ar.ReadInt32(),      // type, an enum via an int temporary
                            ar.ReadByte());      // cursed, a BYTE
    }
}
