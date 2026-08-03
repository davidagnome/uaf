namespace UAF.Serialization;

/// <summary>
/// Writes a level's wall overrides and cell contents — the inverses of
/// <see cref="CellContentsReaders"/>.
/// </summary>
/// <remarks>
/// Both tables are empty in every shipped design, so the round trip proves only that each is
/// written at the right size. That is worth stating rather than implying: their contents have unit
/// coverage alone, the same gap the money sack has.
/// </remarks>
public static class CellContentsWriters
{
    /// <summary>Writes a <c>WALL_OVERRIDES</c> (<c>GlobalData.cpp:2597</c>).</summary>
    /// <remarks>
    /// <para>
    /// <b>The table is sparse and the gaps are positional.</b> A count, then that many entries,
    /// each prefixed by its row number — with <c>-1</c> meaning "absent, no payload". A savegame's
    /// <c>LEVEL_STATS</c> really does carry one with sixteen entries of which three are present, so
    /// the absent ones have to be written back where they were; an earlier revision kept only the
    /// present rows and had to refuse such a table entirely.
    /// </para>
    /// <para>
    /// A row's cells go out as one blit of <c>columnCount × 20</c> bytes, matching how they are
    /// read — the reference writes the whole array in one call rather than per struct.
    /// </para>
    /// </remarks>
    public static void WriteWallOverrides(IArchiveWriteCursor ar, WallOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(overrides);

        ar.WriteInt32(overrides.Entries.Count);

        foreach (var entry in overrides.Entries)
        {
            ar.WriteInt32(entry.RowNumber);
            if (entry.Row is not { } row)
            {
                continue;                            // a bare -1
            }

            ar.WriteInt32(row.Column);

            foreach (var cell in row.Cells)
            {
                if (cell.Values.Length != CellOverride.Size)
                {
                    throw new ArgumentException(
                        $"a CELL_OVERRIDE is {CellOverride.Size} bytes, not {cell.Values.Length}.",
                        nameof(overrides));
                }
                ar.WriteBytes(cell.Values);
            }
        }
    }

    /// <summary>Writes a <c>CELL_LEVEL_CONTENTS</c>.</summary>
    public static void WriteCellContents(IArchiveWriteCursor ar, CellLevelContents contents)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(contents);

        ar.WriteInt32(contents.Columns.Count);
        foreach (var column in contents.Columns)
        {
            ar.WriteInt32(column.Column);
            ar.WriteInt32(column.Rows.Count);

            foreach (var row in column.Rows)
            {
                ar.WriteInt32(row.Row);
                ar.WriteInt32(row.Items.Count);

                foreach (var item in row.Items)
                {
                    WriteCellItem(ar, item);
                }
            }
        }
    }

    /// <summary>Writes one <c>CELL_ITEM</c>.</summary>
    /// <remarks>
    /// <c>m_type</c> is an enum written through an <c>int</c> temporary — four bytes — but
    /// <c>m_cursed</c> is a <c>BYTE</c>. Adjacent fields with no shared width, and the narrow one
    /// last where a writer is least likely to check.
    /// </remarks>
    public static void WriteCellItem(IArchiveWriteCursor ar, CellItem item)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(item);

        ar.WriteString(item.ItemId);                 // verbatim: an ITEM_ID
        ar.WriteInt32(item.Charges);
        ar.WriteInt32(item.Quantity);
        ar.WriteInt32(item.Identified);
        ar.WriteInt32(item.Paid);
        ar.WriteInt32(item.Type);
        ar.WriteByte(item.Cursed);                   // BYTE
    }
}
