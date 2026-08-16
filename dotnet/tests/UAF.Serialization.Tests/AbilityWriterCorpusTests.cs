using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole <c>ability.dat</c> databases taken from shipped designs.
/// </summary>
/// <remarks>
/// The first of the five design databases that had a reader and no writer. It is also the only one
/// of the five whose records <c>DefaultDesign</c> can supply — its <c>baseclass.dat</c>,
/// <c>classes.dat</c> and <c>races.dat</c> are all in shapes the readers refuse, and it ships no
/// binary <c>specialAbilities.dat</c> at all — so this file is the one that runs non-vacuously on a
/// checkout with no <c>reference/</c>.
/// </remarks>
public class AbilityWriterCorpusTests
{
    /// <summary>Every design in the corpus that ships an ability database.</summary>
    public static TheoryData<string> Designs =>
    [
        DatabaseWriterCorpus.DefaultDesign,
        "reference/SomethingWild.dsn",
        "reference/Case.dsn",
        "reference/ci-tier3",
    ];

    private static List<AbilityRecord>? Read(string design)
    {
        if (DatabaseWriterCorpus.File(design, "ability.dat") is not { } path
            || DatabaseWriterCorpus.Version(design) is not { } version)
        {
            return null;
        }

        var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Ability, out var body,
                                               out var stream);
        using (stream)
        {
            return AbilityRecordReader.ReadAll(body, header.Count, version);
        }
    }

    private static byte[] Write(IReadOnlyList<AbilityRecord> abilities)
    {
        var stream = new MemoryStream();
        AbilityRecordWriter.WriteFile(stream, abilities);
        return stream.ToArray();
    }

    private static List<AbilityRecord> ReadBack(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        var header = TaggedDatabaseReader.Read(stream, TaggedDatabase.Ability, out var body);

        Assert.Equal(TaggedDatabaseWriter.Tag(TaggedDatabase.Ability), header.Tag);
        Assert.True(header.Compressed);

        return AbilityRecordReader.ReadAll(body, header.Count, AbilityRecordWriter.WrittenVersion);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_real_ability_round_trips(string design)
    {
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        Assert.NotEmpty(abilities);

        var read = ReadBack(Write(abilities));
        Assert.Equal(abilities.Count, read.Count);

        for (int i = 0; i < abilities.Count; i++)
        {
            Assert.Equal(abilities[i].Name, read[i].Name);
            Assert.Equal(abilities[i].Abbreviation, read[i].Abbreviation);
            DatabaseWriterCorpus.AssertSameDice(abilities[i].Roll, read[i].Roll);
            DatabaseWriterCorpus.AssertSameSpecabs(abilities[i].SpecialAbilities,
                                                   read[i].SpecialAbilities);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_record_in_a_shipped_design_is_writable(string design)
    {
        // Without this the round trip could pass by having nothing to do.
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        Assert.NotEmpty(abilities);
        Assert.All(abilities,
                   a => Assert.True(AbilityRecordWriter.CanWrite(a, out string reason), reason));
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Saving_the_same_database_twice_produces_the_same_bytes(string design)
    {
        // A save must not churn: the port's own output, read and written again, has to be
        // byte-identical to what it wrote the first time. This is the strongest claim available
        // without a reference build, because the compressed encoding interns strings as it goes --
        // a writer that emitted the same fields in a different order would still fail here.
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        byte[] first = Write(abilities);
        byte[] second = Write(ReadBack(first));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The premise: the committed design is on disk and its abilities are real.
    /// </summary>
    /// <remarks>
    /// <b>This is what stops the file passing while proving nothing.</b> Three of the four designs
    /// above live under the gitignored <c>reference/</c> and every case over them returns early
    /// when they are absent. <c>DefaultDesign</c> is committed, so this always has something to
    /// bite on — and it is a 0.915 design whose ability records take the pre-<c>SpellNames</c>
    /// reader fork, which is the fork the writer deliberately does not reproduce.
    /// </remarks>
    [Fact]
    public void The_committed_design_really_supplies_abilities_to_round_trip()
    {
        Assert.NotNull(DatabaseWriterCorpus.RepoRoot());
        Assert.NotNull(DatabaseWriterCorpus.File(DatabaseWriterCorpus.DefaultDesign,
                                                 "ability.dat"));

        var version = DatabaseWriterCorpus.Version(DatabaseWriterCorpus.DefaultDesign);
        Assert.NotNull(version);
        Assert.True(version < DesignVersion.SpellNames,
                    $"DefaultDesign is {version}; the pre-SpellNames fork is no longer exercised");

        var abilities = Read(DatabaseWriterCorpus.DefaultDesign);
        Assert.NotNull(abilities);

        // The six a character sheet shows, all named and all with dice to roll from.
        Assert.True(abilities.Count >= 6, $"only {abilities.Count} abilities");
        Assert.All(abilities, a => Assert.NotEmpty(a.Name));
        Assert.All(abilities, a => Assert.Equal(DicePlusReader.TagText, a.Roll.Tag));
    }

    /// <summary>
    /// A shipped <c>ability.dat</c> from a modern design comes back byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stronger than the round trip and stronger than the fixpoint</b>, which compare this port
    /// against itself: this compares it against a file the reference wrote, so it pins the
    /// container tag, the compression type, the LZW output and the order strings were interned in.
    /// </para>
    /// <para>
    /// <b><c>DefaultDesign</c> is excluded and cannot pass.</b> It ships <c>AbilityV1</c> at
    /// compression type 1 and a design version of 0.915, so saving it is a genuine upgrade — the
    /// pre-<c>SpellNames</c> key is dropped and a special-abilities block appears. That is the
    /// subject of <see cref="An_old_container_is_written_back_in_the_modern_framing"/> instead.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("reference/SomethingWild.dsn")]
    [InlineData("reference/Case.dsn")]
    [InlineData("reference/ci-tier3")]
    public void A_shipped_database_comes_back_byte_for_byte(string design)
    {
        if (DatabaseWriterCorpus.File(design, "ability.dat") is not { } path)
        {
            return;
        }

        Assert.Equal(File.ReadAllBytes(path), Write(Read(design)!));
    }

    /// <summary>
    /// The container is upgraded to <c>AbilityV2</c> however old the file it came from.
    /// </summary>
    /// <remarks>
    /// <c>DefaultDesign</c> ships <c>AbilityV1</c> and compression type 1; the reference's storing
    /// branch writes <c>AbilityV2</c> unconditionally (<c>class.cpp:4384</c>) and
    /// <c>CAR::Compress</c> always emits type 2. So a saved design differs from the shipped one in
    /// its first few bytes by design, and pinning that is the difference between an upgrade and a
    /// bug.
    /// </remarks>
    [Fact]
    public void An_old_container_is_written_back_in_the_modern_framing()
    {
        var abilities = Read(DatabaseWriterCorpus.DefaultDesign);
        if (abilities is null)
        {
            return;
        }

        string? path = DatabaseWriterCorpus.File(DatabaseWriterCorpus.DefaultDesign, "ability.dat");
        TaggedDatabaseReader.Read(path!, TaggedDatabase.Ability, out _, out var shipped);
        using (shipped) { }

        var stream = new MemoryStream(Write(abilities));
        var header = TaggedDatabaseReader.Read(stream, TaggedDatabase.Ability, out _);

        Assert.Equal("AbilityV2", header.Tag);
        Assert.Equal(CarArchiveWriter.CompressType, header.CompressType);
    }

    /// <summary>
    /// A record still carrying a legacy dice form is refused rather than written empty.
    /// </summary>
    /// <remarks>
    /// The whole record survives a <c>DP0</c> read, so nothing stops a writer emitting it as
    /// <c>DP2</c> with an empty expression — a file that reads back cleanly with every ability
    /// rolling nothing. The refusal has to name the dice, because that is the only clue to what
    /// would have been lost.
    /// </remarks>
    [Fact]
    public void An_ability_with_legacy_dice_is_refused_with_a_reason()
    {
        var legacy = new DicePlus(DicePlusReader.TagPacked, string.Empty, string.Empty,
                                  3, 6, 0, 3, 18, 0, []);
        var ability = new AbilityRecord("Strength", "Str", legacy,
                                        new SpecabBlock([], [], []));

        Assert.False(AbilityRecordWriter.CanWrite(ability, out string reason));
        Assert.Contains("DP1", reason, StringComparison.Ordinal);

        var thrown = Assert.Throws<NotSupportedException>(
            () => AbilityRecordWriter.WriteFile(new MemoryStream(), [ability]));
        Assert.Contains("Strength", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty name and abbreviation survive, which is what the blank sentinel is for.
    /// </summary>
    /// <remarks>
    /// The reference substitutes <c>"*"</c> by hand around the two writes (<c>class.cpp:4003</c>).
    /// Skipping it writes a zero-length <c>CString</c> the reference's own reader will not restore,
    /// and nothing in a round trip through this port alone would show it.
    /// </remarks>
    [Fact]
    public void An_ability_with_no_abbreviation_keeps_it_empty()
    {
        var ability = new AbilityRecord(string.Empty, string.Empty, DicePlusWriter.Empty,
                                        new SpecabBlock([], [], []));

        var read = ReadBack(Write([ability]));

        Assert.Equal(string.Empty, read[0].Name);
        Assert.Equal(string.Empty, read[0].Abbreviation);
    }
}
