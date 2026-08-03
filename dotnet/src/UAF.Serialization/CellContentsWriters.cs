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
    /// <summary>
    /// Whether a wall-override table can be written, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// <b>An absent row loses its position.</b> The table is a count followed by that many
    /// entries, each prefixed by a row number, and <c>-1</c> means "absent, no payload" — but the
    /// reader keeps only the rows that were present, in a dictionary, so where the <c>-1</c>s sat
    /// among them is gone. Writing them all at the end would produce a stream that reads back to
    /// the same rows and not to the same bytes, so it is refused instead. No shipped design has a
    /// non-empty table at all, which is why this has never had to be resolved.
    /// </remarks>
    public static bool CanWrite(WallOverrides overrides, out string reason)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        if (overrides.Rows.Count != overrides.EntryCount)
        {
            reason = $"a WALL_OVERRIDES table declared {overrides.EntryCount} entries but only " +
                     $"{overrides.Rows.Count} were present; the absent ones are -1 placeholders " +
                     "whose position among the rest this port does not record.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes a <c>WALL_OVERRIDES</c> (<c>GlobalData.cpp:2597</c>).</summary>
    /// <exception cref="NotSupportedException">
    /// When the table holds absent rows — see <see cref="CanWrite"/>.
    /// </exception>
    /// <remarks>
    /// A row's cells go out as one blit of <c>columnCount × 20</c> bytes, matching how they are
    /// read — the reference writes the whole array in one call rather than per struct.
    /// </remarks>
    public static void WriteWallOverrides(IArchiveWriteCursor ar, WallOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(overrides);

        if (!CanWrite(overrides, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteInt32(overrides.EntryCount);

        foreach ((int rowNumber, var row) in overrides.Rows.OrderBy(r => r.Key))
        {
            ar.WriteInt32(rowNumber);
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
