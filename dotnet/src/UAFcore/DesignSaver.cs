using UAF.Data;
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
    /// Writes <c>game.dat</c> — the design's globals and its global event list.
    /// </summary>
    /// <remarks>
    /// <b>The events go back with it, which is why this takes a <see cref="GameData"/> rather than
    /// a <c>GlobalStatsPrefix</c>.</b> The prefix does not carry them — the reader hands each body
    /// to a callback as it passes — so a save built from the prefix alone would write a design
    /// whose global events had all vanished, and vanished quietly, since a count of zero is a
    /// perfectly valid file. See <see cref="DesignGlobals.Read"/>.
    /// </remarks>
    public static void SaveGameData(string root, GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        WriteAtomically(
            Path.Combine(DataDirectory(root), "game.dat"),
            stream => DesignFileWriter.Write(
                stream, GlobalStatsWriter.WrittenVersion,
                ar => GlobalStatsWriter.Write(
                    ar, data.Global, [.. data.Events.Select(e => (e.Type, e.Body))])));
    }

    /// <summary>
    /// Writes <c>specialAbilities.dat</c> — the binary database beside the text file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A design saved at 5.26 that has no <c>specialAbilities.dat</c> will not load.</b> This
    /// was found by running the real <c>UAFWinEd.exe</c> over a design the port had saved: its log
    /// says "Unable to open special abilities db file … error 2" and then aborts the whole load
    /// with "Failed to load design data file", right after <c>globalData</c>. Dropping a
    /// <c>.dat</c> in made the same design load. The template ships only the <c>.txt</c>, so
    /// nothing in the port had ever produced one.
    /// </para>
    /// <para>
    /// <b>The two files carry the same abilities in different shapes</b> — the <c>.txt</c> is the
    /// line-oriented object file the scripts live in, the <c>.dat</c> an ASL database — so both are
    /// written from the same list rather than one being derived on load.
    /// </para>
    /// </remarks>
    public static void SaveSpecialAbilityDatabase(string root,
                                                  IReadOnlyList<SpecialAbility> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        WriteAtomically(
            Path.Combine(DataDirectory(root), "specialAbilities.dat"),
            stream => SpecialAbilityDatabaseWriter.WriteFile(
                stream, [.. abilities.Select(AsDefinition)]));
    }

    /// <summary>One ability in the binary database's shape.</summary>
    /// <remarks>
    /// The entry kinds are ASL flag values: <c>SPECAB_SCRIPT</c> is 1 and <c>SPECAB_CONSTANT</c> 2
    /// (<c>Specab.h:286</c>). A variable or an integer table is neither — both are stored as
    /// constants, which is what the text file's own reader treats them as once the brackets have
    /// been stripped.
    /// </remarks>
    private static SpecialAbilityDefinition AsDefinition(SpecialAbility ability) =>
        new(ability.Name,
            [.. ability.Entries.Select(e => new AslEntry(
                e.Name,
                e.Kind == SpecialAbilityEntryKind.Script
                    ? SpecialAbilityDatabaseReader.ScriptFlag
                    : SpecialAbilityDatabaseReader.ConstantFlag,
                e.Value))]);

    /// <summary>
    /// Writes <c>specialAbilities.txt</c> — the design's GPDL scripts.
    /// </summary>
    /// <remarks>
    /// <b>The <c>.txt</c>, not the <c>.dat</c>.</b> A design has both and they are different
    /// formats: <c>LoadedDesign.SpecialAbilities</c> reads this one, which is the line-oriented
    /// object file the scripts actually live in, while <c>specialAbilities.dat</c> is a binary
    /// database with a writer of its own. Nothing in the editor edits the binary one.
    /// </remarks>
    public static void SaveSpecialAbilities(string root, IReadOnlyList<SpecialAbility> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        WriteAtomically(
            Path.Combine(DataDirectory(root), "specialAbilities.txt"),
            stream =>
            {
                // CRLF, matching what the reference writes and what the continuation join already
                // puts inside a multi-line value. Left open: the caller owns the stream.
                var writer = new StreamWriter(stream, leaveOpen: true) { NewLine = "\r\n" };

                foreach (string line in SpecialAbilitiesFile.Format(abilities))
                {
                    writer.WriteLine(line);
                }

                writer.Flush();
            });
    }

    /// <summary>
    /// Writes one <c>.lvl</c> file.
    /// </summary>
    /// <param name="fileName">
    /// The level's file name as the design lists it — a level's number is not its position, so the
    /// name has to be carried rather than derived. <c>Case.dsn</c>'s tenth file is
    /// <c>Level255.lvl</c>.
    /// </param>
    /// <remarks>
    /// <b>Level files carry their own framing and are never compressed</b>, even in a design whose
    /// databases are: the compression decision is per file kind, so this does not go through
    /// <c>DesignFileWriter</c>.
    /// </remarks>
    public static void SaveLevel(string root, string fileName, LevelFile level)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        WriteAtomically(
            Path.Combine(DataDirectory(root), Path.GetFileName(fileName)),
            stream => LevelFileWriter.WriteFile(stream, level));
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
