using System.Text;

namespace UAF.Serialization;

/// <summary>
/// Writes the framing shared by the six tagged databases — the inverse of
/// <see cref="TaggedDatabaseReader"/> (<c>ABILITY_DATA_TYPE::Serialize</c>, <c>class.cpp:4381</c>,
/// and its five siblings).
/// </summary>
/// <remarks>
/// <para>
/// <b>The tag a database is written with is not the tag it was read with.</b> Every storing branch
/// emits one literal — <c>RACE_DATA_TYPE</c> always writes <c>"RaceV3"</c> (<c>class.cpp:3466</c>)
/// however old the file it just loaded — so this is a table of literals rather than something
/// derived from a <see cref="TaggedDatabaseHeader"/>. Echoing the tag back would be the one
/// combination nothing reads: the record writers here only produce the newest record shape, and the
/// container tag is what selects the record shape for <c>races.dat</c>.
/// </para>
/// <para>
/// <b>Compression is not optional on this path.</b> The gate is <c>version &gt; "…V0"</c>
/// (<c>class.cpp:3494</c>) and every literal below is above its own <c>V0</c>, so the reference has
/// no reachable branch that writes one of these files uncompressed. <see cref="TaggedDatabaseReader"/>
/// still has the plain branch because a <c>V0</c> file would be legal to read; nothing has ever
/// produced one.
/// </para>
/// <para>
/// <b>Three readers touch the first few bytes and the order is load-bearing.</b> The tag goes out
/// through the plain <see cref="MfcArchiveWriter"/> while <c>m_compressType</c> is still 0, then
/// <see cref="CarArchiveWriter.Open"/> emits the compression-type byte in the clear, and only then
/// is the record count compressed. Writing the count before switching on compression puts it in the
/// wrong encoding and the reader takes the first four bytes of the LZW stream as the count instead.
/// </para>
/// <para>
/// <b>The count's C++ spelling differs between siblings and its bytes do not.</b> Ability, baseclass
/// and class write <c>car &lt;&lt; GetCount()</c> — a plain <c>int</c> — while race writes
/// <c>car.WriteCount()</c>. Under compression <c>CAR::WriteCount</c> delegates straight to
/// <c>operator&lt;&lt;(int)</c> (<c>class.cpp:11693</c>), so all six are four flat bytes and one code
/// path serves them. The distinction only exists on the uncompressed branch, which none of them
/// reaches.
/// </para>
/// </remarks>
public static class TaggedDatabaseWriter
{
    /// <summary>
    /// The tag each database's <b>storing</b> branch emits.
    /// </summary>
    /// <remarks>
    /// Not <see cref="TaggedDatabaseReader.Stem"/> plus a digit picked here: the digit is a literal
    /// in each <c>*_TYPE::Serialize</c> and the six do not agree on one. A design saved by the
    /// reference therefore comes back with <c>AbilityV2</c>, <c>BaseclassV1</c>, <c>ClassV1</c>,
    /// <c>RaceV3</c>, <c>SpGrpV3</c> and <c>TraitV1</c> side by side, which is exactly the mixture
    /// the shipped designs carry.
    /// </remarks>
    public static string Tag(TaggedDatabase database) => database switch
    {
        // "V2 uses ABILITY_ID instead of key" -- class.cpp:4384.
        TaggedDatabase.Ability => "AbilityV2",

        // class.cpp:7269. The one database whose storing tag is still V1.
        TaggedDatabase.Baseclass => "BaseclassV1",

        // class.cpp:8653.
        TaggedDatabase.Class => "ClassV1",

        // "V1 is compressed; V2 is non-keyed databases" -- class.cpp:3466.
        TaggedDatabase.Race => "RaceV3",

        // "Version 3 uses SPELLGROUP_ID" -- class.cpp:9557.
        TaggedDatabase.SpellGroup => "SpGrpV3",

        // class.cpp:9112.
        TaggedDatabase.Trait => "TraitV1",

        _ => throw new ArgumentOutOfRangeException(nameof(database), database, null),
    };

    /// <summary>
    /// Writes a whole tagged database: the tag, the compression byte, the record count, then
    /// whatever <paramref name="writeRecords"/> puts out.
    /// </summary>
    /// <param name="count">
    /// How many records follow. Passed separately rather than taken from the callback because the
    /// count precedes the records on the wire and the callback cannot be run twice — the compressed
    /// encoding interns strings as it goes, so a dry run would poison the string table.
    /// </param>
    /// <remarks>
    /// <b>The flush is the whole reason this exists as a helper.</b> <c>CAR::Close</c> writes the
    /// final partial LZW block and the terminator; without it the reader stops early on a short read
    /// and returns what it had, which looks like a truncated design rather than an unflushed writer.
    /// Every caller would otherwise have to remember the <c>using</c>.
    /// </remarks>
    public static void WriteFile(Stream stream, TaggedDatabase database, uint count,
                                 Action<IArchiveWriteCursor> writeRecords,
                                 Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(writeRecords);

        // Uncompressed: CAR delegates to the wrapped CArchive while m_compressType is still 0, so
        // this is an ordinary MFC counted string and the reader's plain half picks it up.
        var plain = new MfcArchiveWriter(stream, encoding);
        plain.WriteString(Tag(database));

        using var car = CarArchiveWriter.Open(stream, encoding);
        var body = ArchiveWriteCursor.For(car);

        body.WriteCount(count);
        writeRecords(body);
    }
}
