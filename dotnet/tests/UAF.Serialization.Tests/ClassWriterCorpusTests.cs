using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole <c>classes.dat</c> databases taken from shipped designs.
/// </summary>
/// <remarks>
/// The only one of the five databases that embeds an <c>ITEM_LIST</c>, so it is the only one whose
/// refusals can come from the item layer — a class starting with an item held by the pre-0.998101
/// numeric id cannot be written, for the same reason a monster carrying one cannot.
/// </remarks>
public class ClassWriterCorpusTests
{
    /// <summary>Every design in the corpus whose class records the reader accepts.</summary>
    public static TheoryData<string> Designs =>
    [
        "reference/SomethingWild.dsn",
        "reference/Case.dsn",
        "reference/ci-tier3",
    ];

    private static List<ClassRecord>? Read(string design)
    {
        if (DatabaseWriterCorpus.File(design, "classes.dat") is not { } path
            || DatabaseWriterCorpus.Version(design) is not { } version)
        {
            return null;
        }

        var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Class, out var body,
                                               out var stream);
        using (stream)
        {
            return ClassRecordReader.ReadAll(body, header.Count, version);
        }
    }

    private static byte[] Write(IReadOnlyList<ClassRecord> classes)
    {
        var stream = new MemoryStream();
        ClassRecordWriter.WriteFile(stream, classes);
        return stream.ToArray();
    }

    private static List<ClassRecord> ReadBack(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        var header = TaggedDatabaseReader.Read(stream, TaggedDatabase.Class, out var body);

        Assert.Equal(TaggedDatabaseWriter.Tag(TaggedDatabase.Class), header.Tag);
        Assert.True(header.Compressed);

        return ClassRecordReader.ReadAll(body, header.Count, ClassRecordWriter.WrittenVersion);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_real_class_round_trips(string design)
    {
        var classes = Read(design);
        if (classes is null)
        {
            return;
        }

        Assert.NotEmpty(classes);

        var read = ReadBack(Write(classes));
        Assert.Equal(classes.Count, read.Count);

        for (int i = 0; i < classes.Count; i++)
        {
            AssertSame(classes[i], read[i]);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_record_in_a_shipped_design_is_writable(string design)
    {
        // Without this the round trip could pass by having nothing to do.
        var classes = Read(design);
        if (classes is null)
        {
            return;
        }

        Assert.NotEmpty(classes);
        Assert.All(classes,
                   c => Assert.True(ClassRecordWriter.CanWrite(c, out string reason), reason));
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Saving_the_same_database_twice_produces_the_same_bytes(string design)
    {
        var classes = Read(design);
        if (classes is null)
        {
            return;
        }

        byte[] first = Write(classes);
        byte[] second = Write(ReadBack(first));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The premise: the corpus really carries classes, including a multiclass and a starting kit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The always-true half pins that <c>DefaultDesign</c>'s <c>classes.dat</c> exists and is the
    /// <c>CL1</c> shape the reader refuses, so a checkout with no <c>reference/</c> still asserts
    /// something. The rest names what the corpus reaches: the baseclass list and the starting
    /// equipment are the two structures a writer could drop entirely and still produce a file that
    /// reads back, so both have to be non-empty somewhere for the round trip to mean anything.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_corpus_really_carries_classes_with_baseclasses_and_equipment()
    {
        Assert.NotNull(DatabaseWriterCorpus.RepoRoot());

        Assert.NotNull(DatabaseWriterCorpus.File(DatabaseWriterCorpus.DefaultDesign,
                                                 "classes.dat"));
        Assert.Throws<InvalidDataException>(() => Read(DatabaseWriterCorpus.DefaultDesign));

        var classes = Read("reference/SomethingWild.dsn");
        if (classes is null)
        {
            return;
        }

        Assert.Equal(20, classes.Count);
        Assert.Equal("Assassin", classes[0].Name);

        Assert.Contains(classes, c => c.Baseclasses.Count > 1);          // a multiclass
        Assert.Contains(classes, c => c.HitDiceLevelBonuses.Count > 0);
        Assert.Contains(classes, c => c.SpecialAbilities.Pairs.Count > 0);
        Assert.All(classes,
                   c => Assert.Equal(MonsterLeafReaders.ReadySlotCount,
                                     c.StartingEquipment.Ready.Slots.Count));

        // What the corpus does NOT reach, stated rather than implied: no class in any design
        // starts with an item, so the round trip proves only that the ITEM_LIST is written at the
        // right size -- an empty count and twelve zeroed slots. The ITEM inside it is covered by
        // the monster and character writers, which share this leaf.
        Assert.All(classes, c => Assert.Empty(c.StartingEquipment.Items));
    }

    /// <summary>
    /// A shipped <c>classes.dat</c> comes back byte for byte.
    /// </summary>
    /// <remarks>
    /// Stronger than the round trip and the fixpoint, which compare this port against itself: this
    /// compares it against a file the reference wrote, so it also pins the string-interning order,
    /// which no field-by-field comparison can reach. It holds because a tagged database has no
    /// version stamp to upgrade — see <see cref="BaseclassWriterCorpusTests"/>'s equivalent.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Designs))]
    public void A_shipped_database_comes_back_byte_for_byte(string design)
    {
        if (DatabaseWriterCorpus.File(design, "classes.dat") is not { } path)
        {
            return;
        }

        Assert.Equal(File.ReadAllBytes(path), Write(Read(design)!));
    }

    /// <summary>
    /// A class starting with an item held by the legacy numeric id is refused.
    /// </summary>
    /// <remarks>
    /// The number indexes the item database by ordinal and the modern field is a name. Writing an
    /// empty <c>ITEM_ID</c> would leave the class starting with nothing, and the file would read
    /// back without complaint.
    /// </remarks>
    [Fact]
    public void A_class_holding_a_legacy_item_id_is_refused_with_a_reason()
    {
        var legacy = new ItemInstance(0, string.Empty, LegacyItemId: 7, 0, 1, 0, 0, 0, 0);
        var record = Minimal() with
        {
            StartingEquipment = new ItemList([legacy], ReadyItems.Empty),
        };

        Assert.False(ClassRecordWriter.CanWrite(record, out string reason));
        Assert.Contains("numeric id", reason, StringComparison.Ordinal);
    }

    /// <summary>A record below <c>CL5</c> is refused, matching the reader.</summary>
    [Fact]
    public void A_record_below_CL5_is_refused_with_a_reason()
    {
        var record = Minimal() with { Tag = "CL1" };

        Assert.False(ClassRecordWriter.CanWrite(record, out string reason));
        Assert.Contains("CL1", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hit-dice bonus keeps its two ids the right way round.
    /// </summary>
    /// <remarks>
    /// <c>HIT_DICE_LEVEL_BONUS</c> declares <c>ability</c> first and writes <c>baseclassID</c>
    /// first (<c>class.cpp:7507</c>). Both are strings, so transposing them silently swaps two
    /// plausible identifiers and no round trip through a single implementation would notice — this
    /// pins the wire order by giving the two fields values that cannot be confused.
    /// </remarks>
    [Fact]
    public void The_hit_dice_bonus_writes_its_baseclass_before_its_ability()
    {
        var record = Minimal() with
        {
            HitDiceLevelBonuses =
            [
                new("fighter", "Constitution",
                    [.. Enumerable.Range(1, ClassRecordReader.BonusValueCount)
                                  .Select(i => (byte)i)]),
            ],
        };

        var read = ReadBack(Write([record]))[0];

        Assert.Equal("fighter", read.HitDiceLevelBonuses[0].BaseclassId);
        Assert.Equal("Constitution", read.HitDiceLevelBonuses[0].Ability);
        Assert.Equal(record.HitDiceLevelBonuses[0].BonusValues,
                     read.HitDiceLevelBonuses[0].BonusValues);
    }

    private static ClassRecord Minimal() =>
        new(ClassRecordReader.SupportedTag, 0, "Fighter", [], new SpecabBlock([], [], []), [],
            DicePlusWriter.Empty, new ItemList([], ReadyItems.Empty), string.Empty);

    private static void AssertSame(ClassRecord expected, ClassRecord actual)
    {
        Assert.Equal(expected.Tag, actual.Tag);
        Assert.Equal(expected.PreSpellNameKey, actual.PreSpellNameKey);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Baseclasses, actual.Baseclasses);

        DatabaseWriterCorpus.AssertSameSpecabs(expected.SpecialAbilities, actual.SpecialAbilities);

        Assert.Equal(expected.HitDiceLevelBonuses.Count, actual.HitDiceLevelBonuses.Count);
        for (int i = 0; i < expected.HitDiceLevelBonuses.Count; i++)
        {
            Assert.Equal(expected.HitDiceLevelBonuses[i].BaseclassId,
                         actual.HitDiceLevelBonuses[i].BaseclassId);
            Assert.Equal(expected.HitDiceLevelBonuses[i].Ability,
                         actual.HitDiceLevelBonuses[i].Ability);
            Assert.Equal(expected.HitDiceLevelBonuses[i].BonusValues,
                         actual.HitDiceLevelBonuses[i].BonusValues);
        }

        DatabaseWriterCorpus.AssertSameDice(expected.StrengthBonusDice, actual.StrengthBonusDice);

        Assert.Equal(expected.StartingEquipment.Items, actual.StartingEquipment.Items);
        Assert.Equal(expected.StartingEquipment.Ready.Slots, actual.StartingEquipment.Ready.Slots);

        Assert.Equal(expected.HitDiceBaseclassId, actual.HitDiceBaseclassId);
    }
}
