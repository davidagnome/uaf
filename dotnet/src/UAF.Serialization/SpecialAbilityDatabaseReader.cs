using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// One of the game's special abilities, and the strings that define it.
/// </summary>
/// <param name="Name">The ability's name, which is how everything else refers to it.</param>
/// <param name="Strings">
/// Its own attribute list. <b>This is where the scripts live</b> — an entry whose flags say
/// <see cref="SpecialAbilityDatabaseReader.ScriptFlag"/> holds GPDL source, and the entry's key is
/// the script's name.
/// </param>
public sealed record SpecialAbilityDefinition(string Name, IReadOnlyList<AslEntry> Strings);

/// <summary>
/// Reads <c>specialAbilities.dat</c> (<c>A_SPECABILITY_DEF_L::Serialize</c>, <c>ASL.cpp:2256</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the database the <c>$RUN_*_SCRIPTS</c> family needs, and the last design file with
/// no reader.</b> A spell, item or character carries only ability <i>names</i> in its
/// <see cref="SpecabBlock"/>; the scripts those names stand for are here, and
/// <c>SPECIAL_ABILITIES::RunScripts</c> (<c>Specab.cpp:1876</c>) is what joins the two — it looks
/// each name up here, finds a string keyed by the script's name, and compiles its value.
/// </para>
/// <para>
/// <b>Its framing is unlike every other design file.</b> There is no magic sentinel and no version
/// <c>double</c>: the file opens with a <i>counted string</i> naming the format, and only then does
/// <c>car.Compress(true)</c> switch the archive to LZW — so the version is plain and everything
/// after it is compressed. Reading it means one plain string, then handing the same stream to
/// <see cref="CarArchiveReader"/> positioned exactly at the compression byte.
/// </para>
/// </remarks>
public static class SpecialAbilityDatabaseReader
{
    /// <summary>The format string the file opens with.</summary>
    public const string Version = "SpecAbVer01";

    /// <summary>
    /// The ASL map name each ability's strings are stored under.
    /// </summary>
    public const string MapName = "SPECIAL_ABILITIES_DB";

    /// <summary>
    /// An entry holding GPDL source (<c>SPECAB_SCRIPT</c>, <c>Specab.h:286</c>).
    /// </summary>
    /// <remarks>
    /// <b>These are values, not bits, and the reference compares them for equality.</b> Testing
    /// <c>flags &amp; 1</c> instead would also match <see cref="BinaryCodeFlag"/>, which is 5 — so
    /// an already-compiled script would read as uncompiled source. <c>RunScripts</c> writes
    /// <c>== SPECAB_SCRIPT</c> and this port does the same.
    /// </remarks>
    public const byte ScriptFlag = 1;

    /// <summary>A constant the ability carries — a message or a number (<c>SPECAB_CONSTANT</c>).</summary>
    public const byte ConstantFlag = 2;

    /// <summary>
    /// Source that has been compiled, with the entry rewritten to hold the bytecode
    /// (<c>SPECAB_BINARYCODE</c>).
    /// </summary>
    /// <remarks>
    /// <b>The reference caches in place.</b> After a successful compile it overwrites the entry's
    /// value with the binary and re-flags it, so the next run skips compilation entirely — and
    /// only entries at this flag are actually executed.
    /// </remarks>
    public const byte BinaryCodeFlag = 5;

    /// <summary>
    /// Source that failed to compile (<c>SPECAB_SCRIPTERROR</c>).
    /// </summary>
    /// <remarks>The error text replaces the source, so a broken script is never retried.</remarks>
    public const byte ScriptErrorFlag = 6;

    /// <summary>Reads the whole database from the start of a stream.</summary>
    /// <param name="stream">Positioned at the beginning of the file.</param>
    /// <param name="version">
    /// The design version, which the ASL reader needs. The file carries no version of its own
    /// beyond its format string, so this comes from <c>game.dat</c>.
    /// </param>
    public static List<SpecialAbilityDefinition> Read(Stream stream, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The format string is written BEFORE compression is switched on, so it is read plainly
        // and the stream is then left sitting on the compression byte.
        var plain = new MfcArchiveReader(stream);
        string stamp = plain.ReadString();

        if (stamp != Version)
        {
            throw new InvalidDataException(
                $"specialAbilities.dat should open with \"{Version}\"; this one has \"{stamp}\".");
        }

        var car = CarArchiveReader.Open(stream);
        var cursor = ArchiveCursor.For(car);

        int count = cursor.ReadInt32();
        var abilities = new List<SpecialAbilityDefinition>(Math.Max(count, 0));

        for (int i = 0; i < count; i++)
        {
            string name = RepairName(cursor.ReadString());
            abilities.Add(new SpecialAbilityDefinition(
                name, AslReader.Read(cursor, version, MapName)));
        }

        return abilities;
    }

    /// <summary>
    /// Repairs a name whose characters fell below the printable range.
    /// </summary>
    /// <remarks>
    /// <b>The reference does this on load and it is not a no-op.</b> Any character under
    /// <c>0x20</c> has <c>0x20</c> added to it (<c>ASL.cpp:2296</c>) — a fix-up for names an
    /// earlier version mangled, and one that changes what the ability is called. Skipping it would
    /// leave a name nothing else in the design can match.
    /// </remarks>
    public static string RepairName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!name.Any(c => c < 0x20))
        {
            return name;
        }

        return string.Create(name.Length, name, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                span[i] = source[i] < 0x20 ? (char)(source[i] + 0x20) : source[i];
            }
        });
    }

    /// <summary>
    /// The GPDL source an ability holds for a named script, or null when it has none.
    /// </summary>
    /// <remarks>
    /// The lookup <c>RunScripts</c> performs: find the string keyed by the script's name, and take
    /// it only if its flags mark it as a script.
    /// </remarks>
    public static string? Script(SpecialAbilityDefinition ability, string scriptName)
    {
        ArgumentNullException.ThrowIfNull(ability);

        foreach (var entry in ability.Strings)
        {
            if (string.Equals(entry.Key, scriptName, StringComparison.OrdinalIgnoreCase)
                && entry.Flags == ScriptFlag)
            {
                return entry.Value;
            }
        }

        return null;
    }
}
