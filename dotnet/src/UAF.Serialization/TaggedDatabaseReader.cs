using System.Text;

namespace UAF.Serialization;

/// <summary>
/// The six databases that identify themselves with a string tag rather than a magic prologue.
/// </summary>
/// <remarks>
/// Each value is the tag's stem; a file carries the stem followed by a version digit. <b>The digit
/// varies by design and by database</b> — <c>DefaultDesign</c> is <c>V1</c> throughout, while
/// <c>SomethingWild</c> ships <c>AbilityV2</c> and <c>RaceV3</c> beside a <c>BaseclassV1</c>. Each
/// loader accepts its own range (<c>RaceV0</c>…<c>RaceV3</c>, <c>class.cpp:3493</c>), so a reader
/// must not pin the digit.
/// </remarks>
public enum TaggedDatabase
{
    Ability,
    Baseclass,
    Class,
    Race,
    SpellGroup,
    Trait,
}

/// <summary>A tagged database's framing: its version tag, compression, and record count.</summary>
/// <param name="Tag">The literal read from the file, e.g. <c>"BaseclassV1"</c>.</param>
/// <param name="Compressed">
/// Whether the payload is LZW. Decided by a <b>lexicographic</b> comparison of the tag, not a
/// numeric one — see <see cref="TaggedDatabaseReader"/>.
/// </param>
/// <param name="CompressType">
/// The compression-type byte, present only when <paramref name="Compressed"/>. <b>Every shipped
/// file carries 1, not the 2 the writer emits</b>, and the difference changes how strings are
/// interned.
/// </param>
/// <param name="Count">How many records follow.</param>
public sealed record TaggedDatabaseHeader(string Tag, bool Compressed, byte? CompressType,
                                          uint Count);

/// <summary>
/// Reads the framing shared by <c>ability.dat</c>, <c>baseclass.dat</c>, <c>classes.dat</c>,
/// <c>races.dat</c>, <c>spellgroups.dat</c> and <c>traits.dat</c>
/// (<c>class.cpp:3489</c> and its five siblings).
/// </summary>
/// <remarks>
/// <para>
/// This is the plan's "Shape 2", and it shares nothing with the magic-prologue families but the
/// folder it lives in. There is no <c>0xFABCDEFABCDEFABF</c>, no version <c>double</c>, and the
/// <c>DesignVersion</c> machinery does not apply at all — modelling these files with it is a
/// category error.
/// </para>
/// <para>
/// <b>The version is a string and the compression gate is a string comparison.</b>
/// <c>if (version &gt; "RaceV0") car.Compress(true)</c> — lexicographic, so <c>"RaceV1"</c> through
/// <c>"RaceV9"</c> compress and only the hypothetical <c>V0</c> does not. Comparing a parsed digit
/// instead happens to agree today and would diverge the moment a tag reached two digits.
/// </para>
/// <para>
/// <b>The tag is read uncompressed and the count is read compressed.</b> Between them sits the
/// compression-type byte. So three different readers touch the first few bytes of these files, and
/// getting the order wrong reads the count out of the LZW dictionary.
/// </para>
/// <para>
/// <b>Both compression types are in circulation and a reader must handle either.</b>
/// <c>CAR::Compress(true)</c> always writes 2 (<c>class.cpp:11670</c>), yet <c>DefaultDesign</c>'s
/// six databases all carry <b>1</b> — an older variant — while <c>SomethingWild</c>'s carry 2. It
/// is not cosmetic: the string reader gates its embedded-NUL check on <c>m_compressType &gt; 1</c>
/// (<c>class.cpp:11975</c>), so type-1 streams <i>intern</i> NUL-bearing strings that type-2
/// streams skip, and every later string-table index shifts if that is wrong.
/// <see cref="CarArchiveReader"/> already honours this.
/// </para>
/// </remarks>
public static class TaggedDatabaseReader
{
    /// <summary>The tag stem each database writes.</summary>
    /// <remarks>
    /// <b>Not derivable from the file name.</b> <c>classes.dat</c> is tagged <c>"ClassV1"</c> and
    /// <c>spellgroups.dat</c> is <c>"SpGrpV1"</c> — the stems are abbreviations the writer chose,
    /// so they are transcribed rather than generated.
    /// </remarks>
    public static string Stem(TaggedDatabase database) => database switch
    {
        TaggedDatabase.Ability => "Ability",
        TaggedDatabase.Baseclass => "Baseclass",
        TaggedDatabase.Class => "Class",
        TaggedDatabase.Race => "Race",
        TaggedDatabase.SpellGroup => "SpGrp",
        TaggedDatabase.Trait => "Trait",
        _ => throw new ArgumentOutOfRangeException(nameof(database), database, null),
    };

    /// <summary>The file each database lives in.</summary>
    public static string FileName(TaggedDatabase database) => database switch
    {
        TaggedDatabase.Ability => "ability.dat",
        TaggedDatabase.Baseclass => "baseclass.dat",

        // Plural, where the tag is singular.
        TaggedDatabase.Class => "classes.dat",
        TaggedDatabase.Race => "races.dat",
        TaggedDatabase.SpellGroup => "spellgroups.dat",
        TaggedDatabase.Trait => "traits.dat",
        _ => throw new ArgumentOutOfRangeException(nameof(database), database, null),
    };

    /// <summary>
    /// Reads the tag, opens the payload, and reads the record count.
    /// </summary>
    /// <param name="stream">Positioned at the start of the file.</param>
    /// <param name="database">Which database is expected; the tag is checked against its stem.</param>
    /// <param name="body">
    /// The cursor the records are read from — the LZW stream when compressed, the plain archive
    /// otherwise. Positioned immediately after the count.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The tag does not name <paramref name="database"/>. The reference logs "Unknown … data
    /// version" and returns an error rather than aborting; this throws, because in a port a
    /// mismatched tag means the file or the reader is wrong and carrying on would read noise.
    /// </exception>
    public static TaggedDatabaseHeader Read(Stream stream, TaggedDatabase database,
                                            out IArchiveCursor body, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The tag comes through the plain reader: CAR delegates to the wrapped CArchive while
        // m_compressType is still 0, so this is an ordinary MFC counted string.
        var plain = new MfcArchiveReader(stream, encoding);
        string tag = plain.ReadString();

        string stem = Stem(database);
        if (!tag.StartsWith(stem, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"expected a {stem}V* tag, found '{tag}' -- this is not {FileName(database)}.");
        }

        // Lexicographic, as the reference's CString comparison is.
        bool compressed = string.CompareOrdinal(tag, stem + "V0") > 0;
        if (!compressed)
        {
            body = ArchiveCursor.For(plain);
            return new TaggedDatabaseHeader(tag, false, null, body.ReadCount());
        }

        var car = CarArchiveReader.Open(stream, encoding);
        body = ArchiveCursor.For(car);
        return new TaggedDatabaseHeader(tag, true, car.CompressType, body.ReadCount());
    }

    /// <inheritdoc cref="Read(Stream, TaggedDatabase, out IArchiveCursor, Encoding?)"/>
    public static TaggedDatabaseHeader Read(string path, TaggedDatabase database,
                                            out IArchiveCursor body, out Stream stream)
    {
        ArgumentNullException.ThrowIfNull(path);

        stream = File.OpenRead(path);
        return Read(stream, database, out body);
    }
}
