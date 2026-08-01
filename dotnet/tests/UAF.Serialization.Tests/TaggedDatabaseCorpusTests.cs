namespace UAF.Serialization.Tests;

/// <summary>
/// Drives <see cref="TaggedDatabaseReader"/> across more than one design.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TaggedDatabaseTests"/> establishes the framing against <c>DefaultDesign</c>, which is
/// committed and always present. This adds the designs under <c>reference/</c> — gitignored, so
/// these return early without them — and every finding here is one <c>DefaultDesign</c> alone
/// cannot show, because its six databases are uniform where real designs are not.
/// </para>
/// <para>
/// The counts asserted for <c>DefaultDesign</c> were established independently, by decompressing
/// the files in Python before any C# existed, so agreeing with them is a cross-check rather than
/// this reader confirming itself.
/// </para>
/// </remarks>
public class TaggedDatabaseCorpusTests
{
    private static string? Design(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string path = Path.Combine([dir!.FullName, .. parts]);
        return Directory.Exists(path) ? path : null;
    }

    private static string? DefaultDesign() => Design("src", "UAFWinEd", "DefaultDesign.dsn");

    private static string? SomethingWild() => Design("reference", "SomethingWild.dsn");

    private static TaggedDatabaseHeader? Read(string design, TaggedDatabase database)
    {
        string path = Path.Combine(design, "Data", TaggedDatabaseReader.FileName(database));
        if (!File.Exists(path))
        {
            return null;
        }

        var header = TaggedDatabaseReader.Read(path, database, out _, out var stream);
        stream.Dispose();
        return header;
    }

    [Theory]
    [InlineData(TaggedDatabase.Ability, "AbilityV1", 6)]
    [InlineData(TaggedDatabase.Baseclass, "BaseclassV1", 7)]
    [InlineData(TaggedDatabase.Class, "ClassV1", 19)]
    [InlineData(TaggedDatabase.Race, "RaceV1", 6)]
    [InlineData(TaggedDatabase.SpellGroup, "SpGrpV1", 15)]
    [InlineData(TaggedDatabase.Trait, "TraitV1", 43)]
    public void The_reader_returns_the_tag_and_count_of_each_database(
        TaggedDatabase database, string tag, int count)
    {
        string? design = DefaultDesign();
        if (design is null) return;

        var header = Read(design, database);
        Assert.NotNull(header);

        Assert.Equal(tag, header.Tag);

        // Six abilities and seven baseclasses are decisive rather than merely plausible: they are
        // the AD&D ability scores and the seven experience fields CHARACTER::Serialize reads.
        Assert.Equal((uint)count, header.Count);
    }

    [Fact]
    public void Both_compression_types_are_in_circulation()
    {
        // DefaultDesign carries 1 throughout; SomethingWild carries 2. Pinning either is wrong, and
        // the porting plan asserted "every tagged database on disk carries 1" from DefaultDesign
        // alone until this test was written.
        string? classic = DefaultDesign();
        string? modern = SomethingWild();
        if (classic is null || modern is null) return;

        Assert.Equal((byte)1, Read(classic, TaggedDatabase.Baseclass)?.CompressType);
        Assert.Equal((byte)2, Read(modern, TaggedDatabase.Baseclass)?.CompressType);
    }

    [Fact]
    public void The_version_digit_varies_by_design_and_by_database()
    {
        // SomethingWild ships AbilityV2 and RaceV3 beside a BaseclassV1, so a reader that pins the
        // digit rejects real designs. The lexicographic gate accepts all of them.
        string? design = SomethingWild();
        if (design is null) return;

        Assert.Equal("AbilityV2", Read(design, TaggedDatabase.Ability)?.Tag);
        Assert.Equal("RaceV3", Read(design, TaggedDatabase.Race)?.Tag);
        Assert.Equal("BaseclassV1", Read(design, TaggedDatabase.Baseclass)?.Tag);

        Assert.All(new[] { TaggedDatabase.Ability, TaggedDatabase.Race, TaggedDatabase.Baseclass },
                   d => Assert.True(Read(design, d)!.Compressed));
    }

    [Fact]
    public void A_design_need_not_ship_every_database()
    {
        string? design = SomethingWild();
        if (design is null) return;

        Assert.Null(Read(design, TaggedDatabase.SpellGroup));
        Assert.Null(Read(design, TaggedDatabase.Trait));

        // ...and the ones it does ship still read.
        Assert.NotNull(Read(design, TaggedDatabase.Class));
    }

    [Fact]
    public void A_file_whose_tag_names_another_database_is_refused()
    {
        string? design = DefaultDesign();
        if (design is null) return;

        // races.dat opened as if it were baseclass.dat. Reading on would consume the LZW stream as
        // though it held baseclasses, which is far worse than stopping.
        string path = Path.Combine(design, "Data", "races.dat");
        Assert.Throws<InvalidDataException>(() =>
        {
            TaggedDatabaseReader.Read(path, TaggedDatabase.Baseclass, out _, out var s);
            s.Dispose();
        });
    }

    [Fact]
    public void The_container_tag_and_the_record_tag_are_independent_version_axes()
    {
        // Every design ships container tag "BaseclassV1", yet DefaultDesign's records are Bcd1 and
        // the reference designs' are Bcd5. Neither axis predicts the other, and a reader that
        // derives one from the other picks the wrong record layout.
        string? classic = DefaultDesign();
        string? modern = SomethingWild();
        if (classic is null || modern is null) return;

        Assert.Equal("BaseclassV1", Read(classic, TaggedDatabase.Baseclass)?.Tag);
        Assert.Equal("BaseclassV1", Read(modern, TaggedDatabase.Baseclass)?.Tag);

        Assert.Equal("Bcd1", FirstRecordTag(classic));
        Assert.Equal("Bcd5", FirstRecordTag(modern));
    }

    [Fact]
    public void The_engine_would_refuse_DefaultDesigns_baseclass_database()
    {
        // Bcd1 is below the intVer < 2 floor, where the engine shows "you must install a new one"
        // and shuts down (class.cpp:5734). The primary golden fixture is editor-only for this
        // database, so nothing validating the engine's levelling path can use it.
        string? design = DefaultDesign();
        if (design is null) return;

        Assert.Equal("Bcd1", FirstRecordTag(design));
        Assert.True(string.CompareOrdinal(FirstRecordTag(design), "Bcd2") < 0);
    }

    /// <summary>The tag opening the first record, which each record carries for itself.</summary>
    private static string FirstRecordTag(string design)
    {
        string path = Path.Combine(design, "Data",
                                   TaggedDatabaseReader.FileName(TaggedDatabase.Baseclass));
        TaggedDatabaseReader.Read(path, TaggedDatabase.Baseclass, out var body, out var stream);
        using (stream)
        {
            return body.ReadString();
        }
    }

    // ---- record bodies -------------------------------------------------------------------------

    private static BaseclassRecord? FirstRecord(string design)
    {
        string path = Path.Combine(design, "Data",
                                   TaggedDatabaseReader.FileName(TaggedDatabase.Baseclass));
        TaggedDatabaseReader.Read(path, TaggedDatabase.Baseclass, out var body, out var stream);
        using (stream)
        {
            return BaseclassRecordReader.Read(body);
        }
    }

    [Fact]
    public void A_baseclass_record_decodes_to_its_published_ability_and_experience_tables()
    {
        // The strongest check available without an oracle: SomethingWild's first baseclass is the
        // AD&D assassin, and both its ability ranges (Str 12-19, Int 11-18, Dex 12-19) and its
        // experience table (1501, 3001, 6001, 12001, 25001, 50001, ...) are published values. A
        // stream that had drifted by even two bytes would not reproduce them.
        string? design = SomethingWild();
        if (design is null) return;

        var record = FirstRecord(design);
        Assert.NotNull(record);

        Assert.Equal("assassin", record.Name);
        Assert.Equal("Bcd5", record.Tag);

        var strength = record.AbilityRequirements[0];
        Assert.Equal("Strength", strength.AbilityId);
        Assert.Equal((short)12, strength.Min);
        Assert.Equal((short)19, strength.Max);

        Assert.Equal(["Dwarf", "Elf", "Gnome", "Half-Elf", "Half-Orc", "Human"],
                     record.AllowedRaces);

        Assert.Equal<uint[]>([0, 1501, 3001, 6001, 12001, 25001, 50001, 100001, 200001, 300001],
                             [.. record.ExperienceLevels.Take(10)]);
    }

    [Fact]
    public void A_design_may_restrict_a_baseclass_to_its_own_invented_race()
    {
        // Ambassador's_Letter's first baseclass is a custom "ninja" allowed only to "Helmettiger" --
        // proof the race list is read as authored strings rather than matched against a fixed set.
        string? design = Design("reference", "Ambassador's_Letter");
        if (design is null) return;

        var record = FirstRecord(design);
        Assert.NotNull(record);

        Assert.Equal("ninja", record.Name);
        Assert.Equal(["Helmettiger"], record.AllowedRaces);
    }

    [Fact]
    public void A_record_below_the_engines_floor_is_refused_with_that_reason()
    {
        // DefaultDesign's Bcd1 records. The message has to distinguish "too old for the engine"
        // from "a shape this port has not done", because they need different responses.
        string? design = DefaultDesign();
        if (design is null) return;

        var error = Assert.Throws<InvalidDataException>(() => FirstRecord(design));
        Assert.Contains("Bcd1", error.Message, StringComparison.Ordinal);
        Assert.Contains("refuses", error.Message, StringComparison.Ordinal);
    }
}
