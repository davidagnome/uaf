using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole <c>baseclass.dat</c> databases taken from shipped designs.
/// </summary>
/// <remarks>
/// <para>
/// The longest record in a design folder: forty THAC0 bytes, forty hit-dice triples, a casting
/// table of 410 blitted bytes per school, and six lists whose four skill-adjustment families share
/// a field order and disagree on their table widths by up to 80 bytes an entry. A round trip is an
/// unusually strong check here because a tagged database has no per-record length — every record
/// after the first is found only by having consumed its predecessor exactly.
/// </para>
/// <para>
/// <b><c>DefaultDesign</c> cannot supply records for this one.</b> Its <c>Bcd1</c> records are
/// below the floor the engine itself refuses at, so the premise case pins that instead.
/// </para>
/// </remarks>
public class BaseclassWriterCorpusTests
{
    /// <summary>Every design in the corpus whose baseclass records the reader accepts.</summary>
    public static TheoryData<string> Designs =>
    [
        "reference/SomethingWild.dsn",
        "reference/Case.dsn",
        "reference/ci-tier3",
    ];

    private static List<BaseclassRecord>? Read(string design)
    {
        if (DatabaseWriterCorpus.File(design, "baseclass.dat") is not { } path)
        {
            return null;
        }

        var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Baseclass, out var body,
                                               out var stream);
        using (stream)
        {
            return BaseclassRecordReader.ReadAll(body, header.Count);
        }
    }

    private static byte[] Write(IReadOnlyList<BaseclassRecord> baseclasses)
    {
        var stream = new MemoryStream();
        BaseclassRecordWriter.WriteFile(stream, baseclasses);
        return stream.ToArray();
    }

    private static List<BaseclassRecord> ReadBack(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        var header = TaggedDatabaseReader.Read(stream, TaggedDatabase.Baseclass, out var body);

        Assert.Equal(TaggedDatabaseWriter.Tag(TaggedDatabase.Baseclass), header.Tag);
        Assert.True(header.Compressed);

        return BaseclassRecordReader.ReadAll(body, header.Count);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_real_baseclass_round_trips(string design)
    {
        var baseclasses = Read(design);
        if (baseclasses is null)
        {
            return;
        }

        Assert.NotEmpty(baseclasses);

        var read = ReadBack(Write(baseclasses));
        Assert.Equal(baseclasses.Count, read.Count);

        for (int i = 0; i < baseclasses.Count; i++)
        {
            AssertSame(baseclasses[i], read[i]);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_record_in_a_shipped_design_is_writable(string design)
    {
        // Without this the round trip could pass by having nothing to do.
        var baseclasses = Read(design);
        if (baseclasses is null)
        {
            return;
        }

        Assert.NotEmpty(baseclasses);
        Assert.All(baseclasses,
                   b => Assert.True(BaseclassRecordWriter.CanWrite(b, out string reason), reason));
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Saving_the_same_database_twice_produces_the_same_bytes(string design)
    {
        var baseclasses = Read(design);
        if (baseclasses is null)
        {
            return;
        }

        byte[] first = Write(baseclasses);
        byte[] second = Write(ReadBack(first));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The premise: the corpus really carries baseclasses, with the tables that make the round trip
    /// worth running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two halves, because only one design here is committed and it is unreadable.</b> The
    /// always-true half pins that <c>DefaultDesign</c>'s <c>baseclass.dat</c> exists and is the
    /// <c>Bcd1</c> shape the reader refuses — so a checkout with no <c>reference/</c> still asserts
    /// something rather than returning green from an empty file. The rest runs when
    /// <c>SomethingWild</c> is there and names what the corpus actually reaches: a record cannot
    /// prove the 50-byte and 80-byte adjustment tables are written at different widths if every one
    /// of them is empty.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_corpus_really_carries_baseclasses_with_tables_in_them()
    {
        Assert.NotNull(DatabaseWriterCorpus.RepoRoot());

        string? committed = DatabaseWriterCorpus.File(DatabaseWriterCorpus.DefaultDesign,
                                                      "baseclass.dat");
        Assert.NotNull(committed);
        Assert.Throws<InvalidDataException>(() => Read(DatabaseWriterCorpus.DefaultDesign));

        var baseclasses = Read("reference/SomethingWild.dsn");
        if (baseclasses is null)
        {
            return;
        }

        Assert.Equal(9, baseclasses.Count);
        Assert.Equal("assassin", baseclasses[0].Name);

        // Every record writes its two fixed tables, so these are the sizes the writer must not
        // get wrong -- and they are the ones with no length on the wire.
        Assert.All(baseclasses, b => Assert.Equal(40, b.Thac0.Length));
        Assert.All(baseclasses, b => Assert.Equal(40, b.HitDice.Count));

        // And the variable parts are really populated somewhere in the corpus, so a writer that
        // silently dropped one of them would be caught by the round trip rather than passing it.
        Assert.Contains(baseclasses, b => b.AbilityRequirements.Count > 0);
        Assert.Contains(baseclasses, b => b.AllowedRaces.Count > 0);
        Assert.Contains(baseclasses, b => b.ExperienceLevels.Count > 0);
        Assert.Contains(baseclasses, b => b.Casting.Count > 0);
        Assert.Contains(baseclasses, b => b.BonusSpells.Length > 0);
        Assert.Contains(baseclasses, b => b.Skills.Count > 0);
        Assert.Contains(baseclasses, b => b.BaseclassAdjustments.Count > 0);
        Assert.Contains(baseclasses, b => b.RaceAdjustments.Count > 0);
        Assert.Contains(baseclasses, b => b.BonusExperience.Count > 0);
        Assert.Contains(baseclasses, b => b.SpecialAbilities.Pairs.Count > 0);

        // What the corpus does NOT reach, stated rather than implied. Not one baseclass in any
        // design carries an ability-family or a script-family adjustment, so the round trip proves
        // only that both go out as an empty count. The ability family is exercised elsewhere --
        // nine of SomethingWild's twelve races carry one and it is the same writer -- but the
        // script family is written by nothing in the corpus and rests on its unit test alone.
        Assert.All(baseclasses, b => Assert.Empty(b.AbilityAdjustments));
        Assert.All(baseclasses, b => Assert.Empty(b.ScriptAdjustments));
    }

    /// <summary>
    /// A shipped <c>baseclass.dat</c> comes back byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stronger than the round trip and stronger than the fixpoint.</b> Those two compare this
    /// port against itself; this compares it against a file <i>the reference wrote</i>, so it also
    /// pins the container tag, the compression type, the LZW output and — the part no field-by-field
    /// comparison can reach — the order in which strings were interned.
    /// </para>
    /// <para>
    /// It holds here where <c>items.dat</c> and <c>monsters.dat</c> manage it only at 5.29, because
    /// a tagged database has no version stamp to upgrade: the record shape is chosen by the
    /// <c>Bcd5</c> tag, every corpus design already carries it, and the container tag this writes
    /// is the one they all shipped with.
    /// </para>
    /// <para>
    /// It also settles a question the round trip cannot: <see cref="BaseclassRecordReader"/> applies
    /// the <c>DAS</c> blank convention to several ids the reference writes verbatim, so an id
    /// stored as <c>"*"</c> would be read as empty and written back as empty. ci-tier3 has 78
    /// ability requirements whose id <i>is</i> empty; byte identity is what proves they were stored
    /// empty rather than as the sentinel.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Designs))]
    public void A_shipped_database_comes_back_byte_for_byte(string design)
    {
        if (DatabaseWriterCorpus.File(design, "baseclass.dat") is not { } path)
        {
            return;
        }

        Assert.Equal(File.ReadAllBytes(path), Write(Read(design)!));
    }

    /// <summary>
    /// A short fixed table is refused rather than written, because nothing on the wire records its
    /// length.
    /// </summary>
    /// <remarks>
    /// THAC0 and the hit-dice table are compile-time arrays in the reference. A 39-entry one
    /// produces a file whose next field starts four bytes early and whose every later record is
    /// nonsense — with no error until something far away fails to make sense.
    /// </remarks>
    [Fact]
    public void A_short_fixed_table_is_refused_with_a_reason()
    {
        var record = Minimal() with { Thac0 = new byte[39] };

        Assert.False(BaseclassRecordWriter.CanWrite(record, out string reason));
        Assert.Contains("39", reason, StringComparison.Ordinal);
        Assert.Contains("THAC0", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record below <c>Bcd5</c> is refused, matching the reader.
    /// </summary>
    /// <remarks>
    /// The older shapes are not read from the file at all: the editor rebuilds them from hard-coded
    /// AD&amp;D tables as it loads (<c>class.cpp:5860</c> onward). There is no storing branch for
    /// them and no honest way to write one back.
    /// </remarks>
    [Fact]
    public void A_record_below_Bcd5_is_refused_with_a_reason()
    {
        var record = Minimal() with { Tag = "Bcd1" };

        Assert.False(BaseclassRecordWriter.CanWrite(record, out string reason));
        Assert.Contains("Bcd1", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The four adjustment families keep their four different table widths across a round trip.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than taken from the corpus, because this is the failure the corpus is
    /// least likely to catch: ability and baseclass tables are both plausible runs of bytes, and a
    /// writer that used one width for both would still produce a file that read back — with the
    /// stream drifting by 30 bytes per entry from the first race adjustment onward.
    /// </remarks>
    [Fact]
    public void The_four_adjustment_families_survive_at_their_own_widths()
    {
        var record = Minimal() with
        {
            AbilityAdjustments = [new("PickPockets", "Dexterity", '%', Filled(50), "", "")],
            BaseclassAdjustments = [new("BackstabMultiplier", "thief", '+', Filled(80), "", "")],
            RaceAdjustments = [new("HearNoise", "Elf", '%', Filled(2), "", "")],
            ScriptAdjustments = [new("Turn", "", '\0', [], "undead", "TurnScript")],
        };

        var read = ReadBack(Write([record]))[0];

        DatabaseWriterCorpus.AssertSameAdjustments(record.AbilityAdjustments,
                                                   read.AbilityAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(record.BaseclassAdjustments,
                                                   read.BaseclassAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(record.RaceAdjustments, read.RaceAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(record.ScriptAdjustments,
                                                   read.ScriptAdjustments);
    }

    private static byte[] Filled(int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i + 1);
        }
        return bytes;
    }

    /// <summary>A record with every fixed table at its right size and nothing else in it.</summary>
    private static BaseclassRecord Minimal() =>
        new(BaseclassRecordReader.SupportedTag, 0, "fighter", [], [], [], 0x1ff,
            new byte[BaseclassRecordReader.Thac0Size], string.Empty, [], [],
            new SpecabBlock([], [], []),
            [.. Enumerable.Range(0, BaseclassRecordReader.Thac0Size)
                          .Select(_ => new HitDice(0, 0, 0))],
            [], [], [], [], [], []);

    private static void AssertSame(BaseclassRecord expected, BaseclassRecord actual)
    {
        Assert.Equal(expected.Tag, actual.Tag);
        Assert.Equal(expected.PreSpellNameKey, actual.PreSpellNameKey);
        Assert.Equal(expected.Name, actual.Name);

        DatabaseWriterCorpus.AssertSameRequirements(expected.AbilityRequirements,
                                                    actual.AbilityRequirements);
        Assert.Equal(expected.AllowedRaces, actual.AllowedRaces);
        Assert.Equal(expected.ExperienceLevels, actual.ExperienceLevels);
        Assert.Equal(expected.AllowedAlignments, actual.AllowedAlignments);
        Assert.Equal(expected.Thac0, actual.Thac0);
        Assert.Equal(expected.SpellBonusAbility, actual.SpellBonusAbility);
        Assert.Equal(expected.BonusSpells, actual.BonusSpells);

        Assert.Equal(expected.Casting.Count, actual.Casting.Count);
        for (int i = 0; i < expected.Casting.Count; i++)
        {
            Assert.Equal(expected.Casting[i].SchoolId, actual.Casting[i].SchoolId);
            Assert.Equal(expected.Casting[i].PrimeAbility, actual.Casting[i].PrimeAbility);
            Assert.Equal(expected.Casting[i].SpellsPerLevel, actual.Casting[i].SpellsPerLevel);
            Assert.Equal(expected.Casting[i].MaxSpellLevelByPrime,
                         actual.Casting[i].MaxSpellLevelByPrime);
            Assert.Equal(expected.Casting[i].MaxSpellsByPrime, actual.Casting[i].MaxSpellsByPrime);
        }

        DatabaseWriterCorpus.AssertSameSpecabs(expected.SpecialAbilities, actual.SpecialAbilities);

        Assert.Equal(expected.HitDice, actual.HitDice);       // no lists inside: value equality

        DatabaseWriterCorpus.AssertSameSkills(expected.Skills, actual.Skills);
        DatabaseWriterCorpus.AssertSameAdjustments(expected.AbilityAdjustments,
                                                   actual.AbilityAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(expected.BaseclassAdjustments,
                                                   actual.BaseclassAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(expected.RaceAdjustments,
                                                   actual.RaceAdjustments);
        DatabaseWriterCorpus.AssertSameAdjustments(expected.ScriptAdjustments,
                                                   actual.ScriptAdjustments);

        Assert.Equal(expected.BonusExperience.Count, actual.BonusExperience.Count);
        for (int i = 0; i < expected.BonusExperience.Count; i++)
        {
            Assert.Equal(expected.BonusExperience[i].AbilityId, actual.BonusExperience[i].AbilityId);
            Assert.Equal(expected.BonusExperience[i].BonusType, actual.BonusExperience[i].BonusType);
            Assert.Equal(expected.BonusExperience[i].Bonus, actual.BonusExperience[i].Bonus);
        }
    }
}
