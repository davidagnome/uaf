namespace UAF.Serialization.Tests;

/// <summary>
/// The tagged databases — <c>ability</c>, <c>baseclass</c>, <c>classes</c>, <c>races</c>,
/// <c>spellgroups</c>, <c>traits</c>.
/// </summary>
/// <remarks>
/// <para>
/// A fourth framing, unlike anything else in the format (<c>class.cpp:3489</c>):
/// </para>
/// <code>
///   car &gt;&gt; version;                       // a STRING tag, e.g. "RaceV1" -- no version double
///   if (version &gt; "RaceV0")               // LEXICOGRAPHIC comparison gates compression
///       car.Compress(true);
///   count = car.ReadCount();
///   for (count) data.Serialize(car, version);   // records take the STRING version
/// </code>
/// <para>
/// Three things are unique here: the version is a string, the compression gate is a string
/// comparison rather than a numeric one, and the <c>DesignVersion</c> machinery does not apply
/// at all — these files carry their own schema version in the tag suffix.
/// </para>
/// </remarks>
public class TaggedDatabaseTests
{
    private static string DataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data");
    }

    public static TheoryData<string, string> Databases => new()
    {
        { "ability.dat", "AbilityV1" },
        { "baseclass.dat", "BaseclassV1" },
        { "classes.dat", "ClassV1" },
        { "races.dat", "RaceV1" },
        { "spellgroups.dat", "SpGrpV1" },
        { "traits.dat", "TraitV1" },
    };

    [Theory]
    [MemberData(nameof(Databases))]
    public void Tagged_database_opens_with_a_counted_type_tag(string fileName, string expectedTag)
    {
        using var fs = File.OpenRead(Path.Combine(DataDir(), fileName));

        // The tag is read while still uncompressed, as an ordinary MFC counted string.
        var plain = new MfcArchiveReader(fs);
        Assert.Equal(expectedTag, plain.ReadString());

        // No magic prologue and no version double -- this framing has neither.
        fs.Seek(0, SeekOrigin.Begin);
        Assert.NotEqual(DesignFileHeader.Magic, new MfcArchiveReader(fs).ReadUInt64());
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void DefaultDesigns_databases_all_carry_compression_type_1(
        string fileName, string expectedTag)
    {
        using var fs = File.OpenRead(Path.Combine(DataDir(), fileName));
        new MfcArchiveReader(fs).ReadString();          // consume the tag

        var car = CarArchiveReader.Open(fs);

        // CAR::Compress(true) always WRITES 2 (class.cpp:11670), yet these six carry 1 -- an older
        // variant. It is not cosmetic: the C++ string reader gates its embedded-NUL check on
        // `m_compressType > 1`, so type-1 streams intern NUL-bearing strings that type-2 streams
        // deliberately do not, and getting it wrong shifts every later string-table index.
        //
        // This is a fact about THIS design, not about the format. An earlier revision of this test
        // was named "Compression_type_is_1_not_2" and the porting plan said "every tagged database
        // on disk carries 1"; SomethingWild's all carry 2. See TaggedDatabaseCorpusTests.
        Assert.Equal(1, car.CompressType);
        _ = expectedTag;
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void Record_count_decompresses_to_a_plausible_value(string fileName, string expectedTag)
    {
        using var fs = File.OpenRead(Path.Combine(DataDir(), fileName));
        new MfcArchiveReader(fs).ReadString();
        var car = CarArchiveReader.Open(fs);

        // Compression is enabled BEFORE the count is read, so the count comes out of the LZW
        // stream, not the file directly.
        int count = car.ReadInt32();

        Assert.InRange(count, 1, 500);
        _ = expectedTag;
    }

    [Fact]
    public void Compression_gate_is_a_string_comparison()
    {
        // class.cpp:3495 -- `if (version > "RaceV0") car.Compress(true);`. Ordinal string
        // comparison, not a numeric version test, so V1/V2/V3 compress and V0 does not.
        // Modelling this with DesignVersion would be a category error: these files have no
        // version double at all.
        Assert.True(string.CompareOrdinal("RaceV1", "RaceV0") > 0);
        Assert.True(string.CompareOrdinal("RaceV3", "RaceV0") > 0);
        Assert.False(string.CompareOrdinal("RaceV0", "RaceV0") > 0);
    }
}
