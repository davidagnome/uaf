using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole <c>races.dat</c> databases taken from shipped designs.
/// </summary>
/// <remarks>
/// <para>
/// The only one of the five whose record carries an ASL block, and the only place in the codebase
/// that reaches <c>CAR::DeSerialize</c> — the third ASL entry point, whose count is a 32-bit
/// <c>int</c> where both <c>Serialize</c> twins use a <c>WORD</c>. Writing the 16-bit form here
/// desynchronises the five counted lists that follow it, which is what makes a whole-file round
/// trip worth more than a unit test of the record.
/// </para>
/// <para>
/// <b>The container tag is a version axis of its own and the writer changes it.</b> A record is
/// written back inside a <c>RaceV3</c> container whatever it arrived in, because <c>RaceV3</c> is
/// what the reference writes and it is the only tag under which <c>preSpellNameKey</c> is read.
/// </para>
/// </remarks>
public class RaceWriterCorpusTests
{
    /// <summary>Every design in the corpus whose race container the reader accepts.</summary>
    public static TheoryData<string> Designs =>
    [
        "reference/SomethingWild.dsn",
        "reference/Case.dsn",
        "reference/ci-tier3",
    ];

    private static List<RaceRecord>? Read(string design)
    {
        if (DatabaseWriterCorpus.File(design, "races.dat") is not { } path
            || DatabaseWriterCorpus.Version(design) is not { } version)
        {
            return null;
        }

        var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Race, out var body,
                                               out var stream);
        using (stream)
        {
            return RaceRecordReader.ReadAll(body, header.Count, header.Tag, version);
        }
    }

    private static byte[] Write(IReadOnlyList<RaceRecord> races)
    {
        var stream = new MemoryStream();
        RaceRecordWriter.WriteFile(stream, races);
        return stream.ToArray();
    }

    private static List<RaceRecord> ReadBack(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        var header = TaggedDatabaseReader.Read(stream, TaggedDatabase.Race, out var body);

        Assert.Equal(TaggedDatabaseWriter.Tag(TaggedDatabase.Race), header.Tag);
        Assert.True(header.Compressed);

        return RaceRecordReader.ReadAll(body, header.Count, header.Tag,
                                        RaceRecordWriter.WrittenVersion);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_real_race_round_trips(string design)
    {
        var races = Read(design);
        if (races is null)
        {
            return;
        }

        Assert.NotEmpty(races);

        var read = ReadBack(Write(races));
        Assert.Equal(races.Count, read.Count);

        for (int i = 0; i < races.Count; i++)
        {
            AssertSame(races[i], read[i]);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_record_in_a_shipped_design_is_writable(string design)
    {
        // Without this the round trip could pass by having nothing to do.
        var races = Read(design);
        if (races is null)
        {
            return;
        }

        Assert.NotEmpty(races);
        Assert.All(races, r => Assert.True(RaceRecordWriter.CanWrite(r, out string reason), reason));
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Saving_the_same_database_twice_produces_the_same_bytes(string design)
    {
        var races = Read(design);
        if (races is null)
        {
            return;
        }

        byte[] first = Write(races);
        byte[] second = Write(ReadBack(first));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The premise: the corpus really carries races, with dice and attributes on them.
    /// </summary>
    /// <remarks>
    /// The always-true half pins that <c>DefaultDesign</c>'s <c>races.dat</c> exists and is the
    /// <c>RaceV1</c> container the reader refuses — the one shape where the editor and the engine
    /// read different bytes from the same record. The rest names what the corpus reaches: five dice
    /// expressions and an ASL block per race, and the ASL is the structure whose count width this
    /// file exists to pin.
    /// </remarks>
    [Fact]
    public void The_corpus_really_carries_races_with_dice_and_attributes()
    {
        Assert.NotNull(DatabaseWriterCorpus.RepoRoot());

        Assert.NotNull(DatabaseWriterCorpus.File(DatabaseWriterCorpus.DefaultDesign, "races.dat"));
        var refusal = Assert.Throws<InvalidDataException>(
            () => Read(DatabaseWriterCorpus.DefaultDesign));
        Assert.Contains("RaceV1", refusal.Message, StringComparison.Ordinal);

        var races = Read("reference/SomethingWild.dsn");
        if (races is null)
        {
            return;
        }

        Assert.Equal(12, races.Count);
        Assert.Equal("Dwarf", races[0].Name);

        // Every race rolls its physical ranges from text expressions, which is the only DICEPLUS
        // form that can be written back.
        Assert.All(races, r => Assert.Equal(DicePlusReader.TagText, r.Weight.Tag));
        Assert.All(races, r => Assert.Equal(DicePlusReader.TagText, r.BaseMovement.Tag));

        // The ASL block and the five skill lists are what a writer could silently drop.
        Assert.Contains(races, r => r.Attributes.Count > 0);
        Assert.Contains(races, r => r.AbilityRequirements.Count > 0);
        Assert.Contains(races, r => r.Skills.Count > 0);
        Assert.Contains(races, r => r.AbilityAdjustments.Count > 0);
        Assert.Contains(races, r => r.BaseclassAdjustments.Count > 0);
        Assert.Contains(races, r => r.RaceAdjustments.Count > 0);
        Assert.Contains(races, r => r.SpecialAbilities.Pairs.Count > 0);

        // What the corpus does NOT reach, stated rather than implied: no race carries a
        // script-family adjustment, and no baseclass in the corpus does either, so that family's
        // three-string shape rests on its unit test alone.
        Assert.All(races, r => Assert.Empty(r.ScriptAdjustments));
    }

    /// <summary>
    /// A shipped <c>races.dat</c> comes back byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stronger than the round trip and the fixpoint, which compare this port against itself. It
    /// matters most here because of the ASL: the compressed reader applies a one-way fix-up to
    /// every key below <c>0x20</c> (<c>ASL.cpp:1236</c>), so a design carrying such a key could
    /// round-trip through this port perfectly and still differ from what the reference wrote.
    /// Byte identity is what says no corpus design has one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Designs))]
    public void A_shipped_database_comes_back_byte_for_byte(string design)
    {
        if (DatabaseWriterCorpus.File(design, "races.dat") is not { } path)
        {
            return;
        }

        Assert.Equal(File.ReadAllBytes(path), Write(Read(design)!));
    }

    /// <summary>
    /// The attribute block survives, which pins its 32-bit count.
    /// </summary>
    /// <remarks>
    /// Built by hand and given more than one entry on purpose. With a 16-bit count the block itself
    /// still reads back — the two extra bytes come out of the first following list's count instead
    /// — so what catches the mistake is the <b>skill list after it</b>, which is why this record
    /// carries one.
    /// </remarks>
    [Fact]
    public void The_attribute_block_and_the_list_after_it_both_survive()
    {
        var record = Minimal() with
        {
            Attributes =
            [
                new("$SYS$MaxLevel", 5, "20"),
                new("Nightvision", 4, "60"),
            ],
            Skills = [new("HearNoise", 25)],
        };

        var read = ReadBack(Write([record]))[0];

        Assert.Equal(record.Attributes, read.Attributes);
        Assert.Equal(record.Skills, read.Skills);
    }

    /// <summary>
    /// The five flags are four-byte <c>BOOL</c>s and are not always 0 or 1.
    /// </summary>
    /// <remarks>
    /// <c>m_findSecretDoor</c> holds 5 or 2 (<c>class.cpp:3104</c>), so a writer that narrowed
    /// these to a boolean would lose the value as well as the width. Distinct values here so a
    /// transposition would show up too.
    /// </remarks>
    [Fact]
    public void The_five_flags_keep_their_values_and_their_order()
    {
        var record = Minimal() with
        {
            CanChangeClass = 1,
            DwarfResistance = 2,
            GnomeResistance = 3,
            FindSecretDoor = 5,
            FindSecretDoorSearching = 4,
        };

        var read = ReadBack(Write([record]))[0];

        Assert.Equal(1, read.CanChangeClass);
        Assert.Equal(2, read.DwarfResistance);
        Assert.Equal(3, read.GnomeResistance);
        Assert.Equal(5, read.FindSecretDoor);
        Assert.Equal(4, read.FindSecretDoorSearching);
    }

    /// <summary>A race whose physical ranges are still in a numeric dice form is refused.</summary>
    [Fact]
    public void A_race_with_legacy_dice_is_refused_with_a_reason()
    {
        var legacy = new DicePlus(DicePlusReader.TagLegacy, string.Empty, string.Empty,
                                  1, 6, 0, 0, 0, 0, []);
        var record = Minimal() with { Height = legacy };

        Assert.False(RaceRecordWriter.CanWrite(record, out string reason));
        Assert.Contains("height", reason, StringComparison.Ordinal);
        Assert.Contains("DP0", reason, StringComparison.Ordinal);
    }

    private static RaceRecord Minimal() =>
        new(0, "Dwarf", DicePlusWriter.Empty, DicePlusWriter.Empty, DicePlusWriter.Empty,
            DicePlusWriter.Empty, [], DicePlusWriter.Empty, 0, 0, 0, 0, 0,
            [], [], [], [], [], [], new SpecabBlock([], [], []));

    private static void AssertSame(RaceRecord expected, RaceRecord actual)
    {
        Assert.Equal(expected.PreSpellNameKey, actual.PreSpellNameKey);
        Assert.Equal(expected.Name, actual.Name);

        DatabaseWriterCorpus.AssertSameDice(expected.Weight, actual.Weight);
        DatabaseWriterCorpus.AssertSameDice(expected.Height, actual.Height);
        DatabaseWriterCorpus.AssertSameDice(expected.Age, actual.Age);
        DatabaseWriterCorpus.AssertSameDice(expected.MaxAge, actual.MaxAge);
        DatabaseWriterCorpus.AssertSameDice(expected.BaseMovement, actual.BaseMovement);

        DatabaseWriterCorpus.AssertSameRequirements(expected.AbilityRequirements,
                                                    actual.AbilityRequirements);

        Assert.Equal(expected.CanChangeClass, actual.CanChangeClass);
        Assert.Equal(expected.DwarfResistance, actual.DwarfResistance);
        Assert.Equal(expected.GnomeResistance, actual.GnomeResistance);
        Assert.Equal(expected.FindSecretDoor, actual.FindSecretDoor);
        Assert.Equal(expected.FindSecretDoorSearching, actual.FindSecretDoorSearching);

        Assert.Equal(expected.Attributes, actual.Attributes);

        DatabaseWriterCorpus.AssertSameSkills(expected.Skills, actual.Skills);
        DatabaseWriterCorpus.AssertSameAdjustments(expected.AbilityAdjustments,
                                                   actual.AbilityAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(expected.BaseclassAdjustments,
                                                   actual.BaseclassAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(expected.RaceAdjustments,
                                                   actual.RaceAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(expected.ScriptAdjustments,
                                                   actual.ScriptAdjustments);

        DatabaseWriterCorpus.AssertSameSpecabs(expected.SpecialAbilities, actual.SpecialAbilities);
    }
}
