using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// Writes a design's databases back to the folder they were read from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The write is atomic per file.</b> Each database goes to a temporary file beside the real one
/// and is moved over it only once it is complete. An editor that truncated <c>items.dat</c> and
/// then threw halfway through would leave the design unopenable by anything, including itself, and
/// a design is somebody's work — the reference overwrites in place and has no such protection.
/// </para>
/// <para>
/// <b>Nothing here reads the design.</b> A save takes the records the editor holds, not the ones
/// on disk: the panes own the edited state (see <c>DatabaseEditorViewModel</c>), and re-reading
/// would either discard their edits or need them merged back. The design folder is a path.
/// </para>
/// <para>
/// <b>Three of a design's files, not all of them.</b> Levels, <c>game.dat</c> and
/// <c>specialAbilities.txt</c> are not written here — the first two because no pane edits them
/// yet, the last because it is a text format with no writer at all. See docs/PORTING-PLAN.md
/// section 7 Phase 5.
/// </para>
/// </remarks>
public static class DesignSaver
{
    /// <summary>The <c>Data</c> folder of a design root.</summary>
    public static string DataDirectory(string root) =>
        Path.Combine(root ?? throw new ArgumentNullException(nameof(root)), "Data");

    /// <summary>Writes <c>items.dat</c>.</summary>
    /// <remarks>
    /// Takes the whole <see cref="ItemDatabase"/> rather than the record list: the trailing
    /// ammo-type list is part of the file, and a launcher whose family is missing from it finds no
    /// ammunition.
    /// </remarks>
    public static void SaveItems(string root, ItemDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        WriteAtomically(
            Path.Combine(DataDirectory(root), "items.dat"),
            stream => DesignFileWriter.Write(
                stream, ItemRecordWriter.WrittenVersion,
                ar => ItemRecordWriter.WriteDatabase(ar, database.Items, database.AmmoTypes)));
    }

    /// <summary>Writes <c>monsters.dat</c>.</summary>
    public static void SaveMonsters(string root, IReadOnlyList<MonsterRecord> monsters)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        WriteAtomically(
            Path.Combine(DataDirectory(root), "monsters.dat"),
            stream => DesignFileWriter.Write(
                stream, MonsterRecordWriter.WrittenVersion,
                ar => MonsterRecordWriter.WriteDatabase(ar, monsters)));
    }

    /// <summary>Writes <c>spells.dat</c>.</summary>
    public static void SaveSpells(string root, IReadOnlyList<SpellRecord> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);

        WriteAtomically(
            Path.Combine(DataDirectory(root), "spells.dat"),
            stream => DesignFileWriter.Write(
                stream, SpellRecordWriter.WrittenVersion,
                ar => SpellRecordWriter.WriteDatabase(ar, spells)));
    }

    /// <summary>
    /// Builds the whole file in memory, then puts it in place in one move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The payload is built before the destination is touched at all.</b> Every one of these
    /// writers refuses records it cannot reproduce faithfully — a pre-0.998101 item, a monster
    /// attack carrying a numeric spell id — and those refusals are thrown from the middle of the
    /// write. Building into memory first means a refusal costs nothing: the file on disk has not
    /// been opened, let alone truncated.
    /// </para>
    /// <para>
    /// <see cref="File.Move(string, string, bool)"/> rather than
    /// <see cref="File.Replace(string, string, string?)"/>: Replace requires the destination to
    /// exist, and a design may legitimately be missing a database it has never had.
    /// </para>
    /// </remarks>
    private static void WriteAtomically(string path, Action<Stream> write)
    {
        var payload = new MemoryStream();
        write(payload);

        string directory = Path.GetDirectoryName(path)
                           ?? throw new ArgumentException($"'{path}' has no directory.", nameof(path));
        Directory.CreateDirectory(directory);

        string staging = Path.Combine(directory, $".{Path.GetFileName(path)}.saving");
        try
        {
            File.WriteAllBytes(staging, payload.ToArray());
            File.Move(staging, path, overwrite: true);
        }
        catch
        {
            // A staging file left behind would be picked up by the next directory listing as if it
            // were part of the design.
            try
            {
                File.Delete(staging);
            }
            catch (IOException)
            {
                // Nothing useful to do about it; the original failure is the one worth reporting.
            }

            throw;
        }
    }
}
