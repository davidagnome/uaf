namespace UAF.Data;

/// <summary>
/// What one entry of a special ability is, decided by how its name is bracketed
/// (<c>DecodeSpecAbObject</c>, <c>UAFWinEd/ItemDB.cpp:3202</c>).
/// </summary>
public enum SpecialAbilityEntryKind
{
    /// <summary>An undecorated name: a plain string value.</summary>
    Constant,

    /// <summary><c>[name]</c> — GPDL source, and the reason this file exists.</summary>
    Script,

    /// <summary><c>(name)</c> — a named parameter the scripts read.</summary>
    Variable,

    /// <summary><c>&lt;name&gt;</c> — an integer table.</summary>
    IntegerTable,
}

/// <summary>One named entry of a special ability.</summary>
public sealed record SpecialAbilityEntry(string Name, string Value, SpecialAbilityEntryKind Kind);

/// <summary>
/// One special ability — a named bag of scripts, parameters and constants.
/// </summary>
public sealed record SpecialAbility(string Name, IReadOnlyList<SpecialAbilityEntry> Entries)
{
    /// <summary>
    /// An entry by name, or null.
    /// </summary>
    /// <remarks>
    /// <b>Case-sensitive</b>, because the reference stores these in an <c>A_ASLENTRY_L</c> keyed by
    /// a <c>CString</c> and looks them up with <c>Find</c>, which is <c>strcmp</c>.
    /// </remarks>
    public SpecialAbilityEntry? Find(string name) =>
        Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    /// <summary>The GPDL source of a named script, or null when there is none.</summary>
    public string? Script(string name) =>
        Find(name) is { Kind: SpecialAbilityEntryKind.Script } entry ? entry.Value : null;
}

/// <summary>
/// A design's <c>specialAbilities.txt</c> — where its GPDL scripts live.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the file the port has been missing every time a hook came up.</b> Turning undead,
/// <c>WHO_TRIES</c>'s <c>Attempt</c> veto, combat placement, scripted teleporter destinations and
/// two of the logic block's input types all resolve through <c>RunGlobalScript</c>
/// (<c>Shared/Specab.cpp:2097</c>), which looks its source up here by ability name and then by
/// script name. Shipped designs carry a great deal of it — 182 abilities in
/// <c>SomethingWild</c>, 441 in <c>dc-default</c>, 507 in the editor's default design — and
/// <c>SomethingWild</c> defines <c>$EVENT_WhoTries_Attempt</c> outright.
/// </para>
/// <para>
/// <b>The format is a line-oriented object file, not the archive format.</b> Objects run from
/// <c>\(BEGIN)</c> to <c>\(END)</c>; inside one, each logical line is <c>name = value</c>, and a
/// line beginning <c>-</c> continues the previous one. The bracketing of the name gives its kind.
/// </para>
/// <para>
/// <b>The comment marker is <c>\\</c>, not <c>//</c></b> — <c>IsComment</c>
/// (<c>ItemDB.cpp:3116</c>) tests for two backslashes. The file's own header block uses <c>//</c>
/// and survives only because it sits before the first <c>\(BEGIN)</c> and the loader starts at
/// object 1 (<c>ItemDB.cpp:3173</c>). A <c>//</c> line <i>inside</i> an object is not a comment and
/// would be parsed as data.
/// </para>
/// <para>
/// <b>Splitting is on the first <c>=</c> with no escape handling.</b> The general config splitter
/// honours <c>\</c> escapes; this decoder does not — it is a plain <c>Find('=')</c>. So a script
/// whose first line contains an <c>=</c> before its opening brace is split in the wrong place,
/// which is a real constraint on how a design may write one.
/// </para>
/// </remarks>
public static class SpecialAbilitiesFile
{
    /// <summary>The first line every such file must carry.</summary>
    public const string Header = "// Special Abilities database file";

    /// <summary>
    /// Parses the whole file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Everything before the first <c>\(BEGIN)</c> is discarded</b>, header and all, because the
    /// reference enumerates objects from 1 and object 0 is whatever preceded the first delimiter.
    /// </para>
    /// <para>
    /// <b>An object with no <c>name</c> entry is dropped</b> and the rest of the file is still
    /// read; the reference logs a semantic error and carries on. <b>An object left unterminated at
    /// end of file, or by the next <c>\(BEGIN)</c>, is kept</b> — every opener starts a new object
    /// number and each object's lines are decoded on their own, so a missing closer costs nothing.
    /// The editor's own <c>DefaultDesign</c> relies on it: 182 <c>\(BEGIN)</c> against 181
    /// <c>\(END)</c>. An object whose name repeats an
    /// earlier one replaces nothing here — both are returned, and it is the lookup that takes the
    /// first, matching <c>InsertAbility</c>'s behaviour of handing back the existing entry.
    /// </para>
    /// </remarks>
    public static List<SpecialAbility> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var abilities = new List<SpecialAbility>();
        List<SpecialAbilityEntry>? entries = null;
        string? name = null;

        foreach (string logical in LogicalLines(lines))
        {
            if (logical.StartsWith("\\(BEGIN)", StringComparison.OrdinalIgnoreCase))
            {
                // Flush first: an opener also closes whatever came before it. The reference gives
                // every \(BEGIN) a new object number and decodes each object's lines on their own,
                // so an object missing its \(END) is still read -- it simply runs to the next
                // opener. Discarding it here loses an ability the reference loads.
                Flush(abilities, entries, name);

                entries = [];
                name = null;
                continue;
            }

            if (logical.StartsWith("\\(END)", StringComparison.OrdinalIgnoreCase))
            {
                Flush(abilities, entries, name);
                entries = null;
                name = null;
                continue;
            }

            if (entries is null || Split(logical) is not var (key, value))
            {
                continue;
            }

            if (string.Equals(key, "name", StringComparison.Ordinal))
            {
                // The reference pulls `name` out of the bag and deletes it, so it is the ability's
                // identity rather than one of its entries.
                name = value;
                continue;
            }

            entries.Add(Entry(key, value));
        }

        // ...and one left open at end of file is kept for the same reason.
        Flush(abilities, entries, name);

        return abilities;
    }

    /// <summary>Records a finished object, if it got far enough to have a name.</summary>
    private static void Flush(List<SpecialAbility> abilities,
                              List<SpecialAbilityEntry>? entries, string? name)
    {
        if (entries is not null && !string.IsNullOrEmpty(name))
        {
            abilities.Add(new SpecialAbility(name, entries));
        }
    }

    /// <summary>Parses a file from disk, or returns an empty list when it is absent.</summary>
    /// <remarks>
    /// A design without one is ordinary — it simply has no scripts of its own, and every hook falls
    /// back on the built-in defaults.
    /// </remarks>
    public static List<SpecialAbility> Load(string path) =>
        File.Exists(path) ? Parse(File.ReadLines(path)) : [];

    /// <summary>
    /// Joins continuation lines, as <c>GetCompleteLine</c> does
    /// (<c>UAFWinEd/ItemDB.cpp:3084</c>).
    /// </summary>
    /// <remarks>
    /// <b>Continuations are joined with CRLF and lose their leading <c>-</c>.</b> The line endings
    /// matter: what comes out is GPDL source that the compiler sees with real newlines in it, so
    /// joining with spaces would merge a <c>//</c> comment into the following statement.
    /// </remarks>
    private static IEnumerable<string> LogicalLines(IEnumerable<string> lines)
    {
        string? pending = null;

        foreach (string raw in lines)
        {
            if (IsComment(raw))
            {
                continue;
            }

            if (raw.StartsWith('-'))
            {
                // A continuation with nothing to continue is dropped rather than starting a line.
                pending = pending is null ? null : pending + "\r\n" + raw[1..];
                continue;
            }

            if (pending is not null)
            {
                yield return pending;
            }

            pending = raw;
        }

        if (pending is not null)
        {
            yield return pending;
        }
    }

    /// <summary><c>IsComment</c> (<c>ItemDB.cpp:3116</c>) — two backslashes, not two slashes.</summary>
    public static bool IsComment(string line) => line.StartsWith("\\\\", StringComparison.Ordinal);

    /// <summary>Splits on the first <c>=</c>, trimming both halves. Null when there is none.</summary>
    private static (string Key, string Value)? Split(string line)
    {
        int at = line.IndexOf('=');
        if (at < 0)
        {
            return null;
        }

        return (line[..at].Trim(), line[(at + 1)..].Trim());
    }

    /// <summary>
    /// Writes the whole file back, as <see cref="Parse"/> would read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this is not: a rewrite of the file that was read.</b> Comments, blank lines, the
    /// order of entries within an object and any original spacing are not carried in
    /// <see cref="SpecialAbility"/>, so they are gone. What is preserved is everything the
    /// reference reads — the abilities, their names, their entries and each entry's kind — and
    /// <see cref="Parse"/> of this output equals the abilities that went in.
    /// </para>
    /// <para>
    /// <b>Newlines inside a value become continuation lines</b>, which is the only way this format
    /// carries a multi-line value: the reader joins a run of <c>-</c>-prefixed lines with CRLF and
    /// strips the dash. That matters most for scripts, whose values are GPDL source — joining with
    /// anything else would merge a <c>//</c> comment into the next statement.
    /// </para>
    /// <para>
    /// <b>Trailing and leading whitespace in a value does not survive</b>, because the reader
    /// trims what it splits off — a script written with a final newline comes back without one.
    /// That is the reference's behaviour, not a defect here, and it is idempotent: a value read
    /// once is already trimmed, so writing and re-reading it changes nothing.
    /// </para>
    /// <para>
    /// <b>One shape cannot survive and is refused rather than silently mangled:</b> a key
    /// containing <c>=</c>, which would be split in the wrong place on the way back. A value line
    /// beginning with <c>-</c> is <i>not</i> such a case — see <c>Lines</c>.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> Format(IEnumerable<SpecialAbility> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        yield return Header;

        foreach (var ability in abilities)
        {
            yield return "\\(BEGIN)";

            // The reference pulls `name` out of the bag on the way in, so it goes back first --
            // an object with no name is dropped by the reader.
            foreach (string line in Lines("name", ability.Name))
            {
                yield return line;
            }

            foreach (var entry in ability.Entries)
            {
                foreach (string line in Lines(Key(entry), entry.Value))
                {
                    yield return line;
                }
            }

            yield return "\\(END)";
        }
    }

    /// <summary>Writes the file to disk.</summary>
    /// <remarks>
    /// CRLF line endings, matching what the reference writes and what the continuation join
    /// already puts inside multi-line values.
    /// </remarks>
    public static void Save(string path, IEnumerable<SpecialAbility> abilities) =>
        File.WriteAllText(path, string.Concat(Format(abilities).Select(l => l + "\r\n")));

    /// <summary>One entry as its <c>key = value</c> line, plus a continuation per extra line.</summary>
    private static IEnumerable<string> Lines(string key, string value)
    {
        if (key.Contains('=', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{key}' contains '=', so it would be split in the wrong place when read back.",
                nameof(key));
        }

        string[] parts = value.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        yield return $"{key} = {parts[0]}";

        // Every continuation gets exactly one '-', including a line that already starts with one.
        // The reader strips a single dash, so a table entry of -3 goes out as "--3" and comes back
        // as "-3" -- Case.dsn's <DexInit> is one, and it is the reason this is a plain prefix
        // rather than a refusal.
        foreach (string continued in parts.Skip(1))
        {
            yield return "-" + continued;
        }
    }

    /// <summary>Puts an entry's kind back into its key, as the bracketing <see cref="Entry"/> reads.</summary>
    private static string Key(SpecialAbilityEntry entry) => entry.Kind switch
    {
        SpecialAbilityEntryKind.Script => $"[{entry.Name}]",
        SpecialAbilityEntryKind.Variable => $"({entry.Name})",
        SpecialAbilityEntryKind.IntegerTable => $"<{entry.Name}>",
        _ => entry.Name,
    };

    /// <summary>Reads the name's bracketing to decide the entry's kind.</summary>
    private static SpecialAbilityEntry Entry(string key, string value)
    {
        (char open, char close, SpecialAbilityEntryKind kind)[] forms =
        [
            ('[', ']', SpecialAbilityEntryKind.Script),
            ('(', ')', SpecialAbilityEntryKind.Variable),
            ('<', '>', SpecialAbilityEntryKind.IntegerTable),
        ];

        foreach (var (open, close, kind) in forms)
        {
            // Three characters minimum, so "[]" is not a script -- it is a constant named "[]".
            if (key.Length >= 3 && key[0] == open && key[^1] == close)
            {
                return new SpecialAbilityEntry(key[1..^1], value, kind);
            }
        }

        return new SpecialAbilityEntry(key, value, SpecialAbilityEntryKind.Constant);
    }
}
