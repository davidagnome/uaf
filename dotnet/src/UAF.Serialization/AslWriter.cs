using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes the attribute/string list block that terminates most records
/// (<c>ASL.cpp:1386</c> and <c>:1489</c>).
/// </summary>
/// <remarks>
/// <para>
/// The first record-level writer, chosen because ASL is a leaf every other record ends with — items,
/// spells, monsters, characters, events, globals. Nothing above it can be written until this can.
/// See <see cref="AslReader"/> for the format; this documents only what differs when writing.
/// </para>
/// <para>
/// <b>Only the uncompressed path.</b> The compressed one interns strings across the whole stream
/// and applies a key fixup on read (<see cref="AslReader.FixUpCompressedKey"/>) — and that fixup is
/// <i>not invertible</i>: it maps every character below <c>0x20</c> up by <c>0x20</c>, so a key
/// read as <c>'%'</c> could have been written as <c>'%'</c> or as <c>0x05</c>. A compressed writer
/// therefore cannot be derived from the reader alone; it needs the pre-fixup key, which only the
/// producing code knows.
/// </para>
/// </remarks>
public static class AslWriter
{
    /// <summary>
    /// Writes an ASL block as <c>Serialize</c> does: <b>every entry, whatever its flags</b>.
    /// </summary>
    /// <param name="version">
    /// Below <see cref="AslReader.MinimumVersion"/> nothing is written at all — not the name, not
    /// the count, not a byte. A writer that emits an empty block instead produces a file the
    /// reference cannot read.
    /// </param>
    /// <remarks>
    /// This is what a <b>design file</b> contains. For a savegame use <see cref="Save"/>.
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, DesignVersion version, string mapName,
                             IEnumerable<AslEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(entries);

        WriteBlock(ar, version, mapName, [.. entries], wideCount: false);
    }

    /// <summary>
    /// Writes an ASL block as <c>Save</c> does: <b>only entries without
    /// <see cref="AslFlags.ReadOnly"/></b> (<c>ASL.cpp:1489</c>).
    /// </summary>
    /// <remarks>
    /// <b>The count must be of the filtered set, not the whole one.</b> The reference walks the
    /// list twice for exactly this reason, and its own <c>ASSERT(count==0)</c> after the second
    /// walk is there to catch the two disagreeing. Counting everything and writing some produces a
    /// file that reads back cleanly with silently missing attributes — the failure mode that
    /// prompted the assert.
    /// </remarks>
    public static void Save(IArchiveWriteCursor ar, DesignVersion version, string mapName,
                            IEnumerable<AslEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(entries);

        WriteBlock(ar, version, mapName,
                   [.. entries.Where(AslReader.IsSavedInSavegame)], wideCount: false);
    }

    /// <summary>
    /// Writes the block as <c>CAR::DeSerialize</c>'s twin reads it — <b>with a 32-bit count</b>
    /// (<c>class.cpp:12117</c>).
    /// </summary>
    /// <remarks>
    /// The two <c>Serialize</c> paths agree on a 16-bit count, which makes "the count is a WORD"
    /// look like a property of the format. It is not: this third entry point reads an <c>int</c>,
    /// and it is what <c>races.dat</c> uses. Writing the wrong width desynchronises everything
    /// after the block by two bytes.
    /// </remarks>
    public static void WriteDeSerialized(IArchiveWriteCursor ar, DesignVersion version,
                                         string mapName, IEnumerable<AslEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(entries);

        WriteBlock(ar, version, mapName, [.. entries], wideCount: true);
    }

    private static void WriteBlock(IArchiveWriteCursor ar, DesignVersion version, string mapName,
                                   IReadOnlyList<AslEntry> entries, bool wideCount)
    {
        if (!AslReader.IsPresent(version))
        {
            return;
        }

        // The map name is a sync marker rather than a label, so it is written verbatim -- not
        // through the DAS blank convention, which would turn an empty name into "*".
        ar.WriteString(mapName);

        if (wideCount)
        {
            ar.WriteInt32(entries.Count);
        }
        else
        {
            ar.WriteUInt16(checked((ushort)entries.Count));
        }

        foreach (var entry in entries)
        {
            ar.WriteString(entry.Key);
            ar.WriteByte(entry.Flags);
            ar.WriteString(entry.Value);
        }
    }
}
