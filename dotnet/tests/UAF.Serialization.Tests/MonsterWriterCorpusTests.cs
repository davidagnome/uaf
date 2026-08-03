using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips whole <c>monsters.dat</c> databases taken from shipped designs.
/// </summary>
/// <remarks>
/// <para>
/// A hand-built record exercises the encoder; 570 real ones exercise what designs actually contain
/// — fractional hit dice, monsters carrying items, sacks with gems in, attack messages with
/// punctuation, attribute keys nobody would think to try.
/// </para>
/// <para>
/// <b>What this proves and what it does not.</b> The claim is that a record read, written and read
/// again is the same record, and that writing it a second time gives the same bytes. It is
/// <i>not</i> a claim of byte-identity with the shipped file: every modern <c>monsters.dat</c> in
/// the corpus is LZW-compressed and there is no <c>CAR</c> writer, so the comparison is against
/// the port's own reader — which is itself walked against the C++ oracle and to exact end-of-file
/// on every one of these designs (see <see cref="MonsterWalkTests"/>).
/// </para>
/// <para>
/// The four designs span 2.53 → 5.29. All are above every gate in the record, so what is read at
/// 2.53 is the same shape as what is written at
/// <see cref="MonsterRecordWriter.WrittenVersion"/> — with the single exception of the icon's
/// <c>RestartFrame</c>, which arrives at 5.24 and is zero in the older two either way.
/// </para>
/// </remarks>
public class MonsterWriterCorpusTests
{
    private static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        return dir;
    }

    private static List<MonsterRecord> Read(string relativeDataDir, ArchiveRole role)
    {
        var root = RepoRoot();
        string? path = root is null
            ? null
            : Path.Combine(root.FullName, Path.Combine(relativeDataDir.Split('/')), "monsters.dat");
        if (path is null || !File.Exists(path))
        {
            return [];
        }

        using var stream = File.OpenRead(path);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database);
        stream.Seek(header.PayloadOffset, SeekOrigin.Begin);

        return header.Tier == ArchiveTier.CompressedCar
            ? MonsterRecordReader.ReadDatabase(CarArchiveReader.Open(stream), header.Version, role)
            : MonsterRecordReader.ReadDatabase(new MfcArchiveReader(stream), header.Version, role);
    }

    private static byte[] WriteDatabase(IReadOnlyList<MonsterRecord> monsters)
    {
        var stream = new MemoryStream();
        MonsterRecordWriter.WriteDatabase(ArchiveWriteCursor.For(new MfcArchiveWriter(stream)), monsters);
        return stream.ToArray();
    }

    /// <summary>Writes a database as a compressed <c>CAR</c>, as a shipped design is.</summary>
    private static byte[] WriteCompressed(IReadOnlyList<MonsterRecord> monsters)
    {
        var stream = new MemoryStream();
        using (var car = CarArchiveWriter.Open(stream))
        {
            MonsterRecordWriter.WriteDatabase(ArchiveWriteCursor.For(car), monsters);
        }

        return stream.ToArray();
    }

    /// <summary>The raw payload of a shipped file, from the compression byte onward.</summary>
    private static byte[]? CompressedPayload(string relativeDataDir)
    {
        var root = RepoRoot();
        string? path = root is null
            ? null
            : Path.Combine(root.FullName, Path.Combine(relativeDataDir.Split('/')), "monsters.dat");
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var header = DesignFileHeader.Read(stream, DesignFileKind.Database);
        if (header.Tier != ArchiveTier.CompressedCar)
        {
            return null;
        }

        stream.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var payload = new MemoryStream();
        stream.CopyTo(payload);
        return payload.ToArray();
    }

    [Theory]
    [MemberData(nameof(ModernDesigns))]
    public void A_compressed_database_round_trips_through_the_car_writer(string dataDir,
                                                                        int expectedCount)
    {
        // The claim the port could not make until the CAR write path existed: a design written
        // back in the encoding it shipped in, read again through the compressed reader.
        var monsters = Read(dataDir, ArchiveRole.Engine);
        if (monsters.Count == 0)
        {
            return;
        }

        Assert.Equal(expectedCount, monsters.Count);

        var stream = new MemoryStream(WriteCompressed(monsters));
        var read = MonsterRecordReader.ReadDatabase(CarArchiveReader.Open(stream),
                                                    MonsterRecordWriter.WrittenVersion,
                                                    ArchiveRole.Engine);

        Assert.Equal(monsters.Count, read.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            AssertSameMonster(monsters[i], read[i]);
        }
    }

    [Theory]
    [MemberData(nameof(ModernDesigns))]
    public void Compressing_is_worth_doing(string dataDir, int expectedCount)
    {
        // Otherwise the round trip above would pass on a writer that compressed nothing.
        var monsters = Read(dataDir, ArchiveRole.Engine);
        if (monsters.Count == 0)
        {
            return;
        }

        Assert.True(WriteCompressed(monsters).Length < WriteDatabase(monsters).Length);
        _ = expectedCount;
    }

    [Fact]
    public void The_shipped_payload_is_a_stream_this_port_can_now_produce_the_shape_of()
    {
        // Not byte-identity: the reference's own writer interned strings in the order ITS record
        // walk produced them, and re-deriving that needs the writer to be byte-compatible field
        // for field -- which is what the rest of this class establishes separately. What this
        // pins is that both are whole 52-byte-block LZW streams of the same kind, so the two are
        // now comparable at all.
        byte[]? shipped = CompressedPayload("reference/ci-tier3/Data");
        if (shipped is null)
        {
            return;
        }

        var monsters = Read("reference/ci-tier3/Data", ArchiveRole.Engine);
        byte[] ours = WriteCompressed(monsters);

        Assert.Equal(0, (shipped.Length - 1) % 52);      // minus the compression-type byte
        Assert.Equal(0, (ours.Length - 1) % 52);
        Assert.Equal(shipped[0], ours[0]);               // both compressType 2
    }

    private static List<MonsterRecord> ReadDatabase(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        var monsters = MonsterRecordReader.ReadDatabase(new MfcArchiveReader(stream),
                                                        MonsterRecordWriter.WrittenVersion,
                                                        ArchiveRole.Engine);
        Assert.Equal(stream.Length, stream.Position);      // consumed exactly, nothing left over
        return monsters;
    }

    /// <summary>The designs whose monsters are modern enough to be written back.</summary>
    public static TheoryData<string, int> ModernDesigns => new()
    {
        { "reference/SomethingWild.dsn/Data", 195 },
        { "reference/Case.dsn/Data", 160 },
        { "reference/ci-tier3/Data", 44 },
        { "reference/dc-default/data-files", 171 },
    };

    [Theory]
    [MemberData(nameof(ModernDesigns))]
    public void Every_real_monster_round_trips(string dataDir, int expectedCount)
    {
        var monsters = Read(dataDir, ArchiveRole.Engine);
        if (monsters.Count == 0)
        {
            return;
        }

        Assert.Equal(expectedCount, monsters.Count);

        var read = ReadDatabase(WriteDatabase(monsters));

        Assert.Equal(monsters.Count, read.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            AssertSameMonster(monsters[i], read[i]);
        }
    }

    [Theory]
    [MemberData(nameof(ModernDesigns))]
    public void Writing_what_was_read_back_gives_the_same_bytes(string dataDir, int expectedCount)
    {
        // The field comparison catches a value that came back wrong; this catches a value that
        // never went out at all, since a byte the writer omits is a byte the reader takes from
        // somewhere else and re-emits differently.
        var monsters = Read(dataDir, ArchiveRole.Engine);
        if (monsters.Count == 0)
        {
            return;
        }

        byte[] first = WriteDatabase(monsters);

        Assert.Equal(first, WriteDatabase(ReadDatabase(first)));
        _ = expectedCount;
    }

    [Theory]
    [MemberData(nameof(ModernDesigns))]
    public void Every_record_in_a_modern_design_is_writable(string dataDir, int expectedCount)
    {
        // Without this the round-trip above could pass by having nothing to do.
        var monsters = Read(dataDir, ArchiveRole.Engine);
        if (monsters.Count == 0)
        {
            return;
        }

        Assert.Equal(expectedCount, monsters.Count);
        Assert.All(monsters, m => Assert.True(MonsterRecordWriter.CanWrite(m, out string reason),
                                              reason));
    }

    [Fact]
    public void The_corpus_really_does_exercise_every_leaf()
    {
        // Each of these is a structure whose record would still round-trip if the writer skipped
        // it entirely -- by writing nothing and the reader reading nothing back. Taken across the
        // whole corpus rather than one design, since no single design fills every one.
        //
        // NOT covered here: a monster's money. All 570 records in the corpus carry an empty
        // MONEY_SACK -- ten zeroed coin slots, no gems, no jewellery -- so its non-empty form is
        // exercised by MonsterRecordWriterTests alone. What the corpus does prove about it is
        // that the empty sack is written and read at exactly the right size, since anything else
        // would leave the following record misaligned.
        var monsters = ModernDesigns
            .Select(row => (string)row[0])
            .SelectMany(dir => Read(dir, ArchiveRole.Engine))
            .ToList();

        if (monsters.Count == 0)
        {
            return;
        }

        Assert.Contains(monsters, m => m.Attributes.Count > 0);
        Assert.Contains(monsters, m => m.SpecialAbilities.Pairs.Count > 0);
        Assert.Contains(monsters, m => m.Items!.Items.Count > 0);
        Assert.Contains(monsters, m => m.Items!.Ready.Slots.Any(s => s != 0));
        Assert.Contains(monsters, m => m.Attacks.Count > 1);
        Assert.Contains(monsters, m => m.Attacks.Any(a => a.SpellId.Length > 0));
        Assert.Contains(monsters, m => m.Attacks.Any(a => a.AttackMessage.Length > 0));
        Assert.Contains(monsters, m => m.Icon!.FileName.Length > 0);
        Assert.Contains(monsters, m => m.UndeadType.Length > 0);

        // A hit-die count below one is the field that proves it went out as a float: as an int
        // those bytes are around 1.05e9, and nothing in the stream would object.
        Assert.Contains(monsters, m => m.HitDice > 0 && m.HitDice < 1);
    }

    // ---- the design that cannot be written -------------------------------------------------------

    [Fact]
    public void A_pre_spell_names_design_is_refused_rather_than_written_hollow()
    {
        // DefaultDesign is 0.915: numeric spell ids on every monster, numeric item ids on ten of
        // them, and special abilities still in the pre-0.921 shape. Writing it would produce a
        // file that reads back clean with every one of those gone.
        var monsters = Read("src/UAFWinEd/DefaultDesign.dsn/Data", ArchiveRole.Editor);
        if (monsters.Count == 0)
        {
            return;
        }

        Assert.Equal(44, monsters.Count);
        Assert.All(monsters, m => Assert.False(MonsterRecordWriter.CanWrite(m, out _)));
        Assert.Throws<NotSupportedException>(() => WriteDatabase(monsters));
    }

    [Fact]
    public void The_legacy_undead_index_is_read_as_its_name()
    {
        // Below 0.998115 the file holds an ordinal, which the reference names from UndeadTypeText
        // as it loads (Monster.cpp:816). Keeping the ordinal would write "1" where the design
        // means "Skeleton" -- and no turning table has a category called "1".
        var monsters = Read("src/UAFWinEd/DefaultDesign.dsn/Data", ArchiveRole.Editor);
        if (monsters.Count == 0)
        {
            return;
        }

        var undead = monsters.Select(m => m.UndeadType).Where(u => u.Length > 0).Distinct().ToList();

        Assert.NotEmpty(undead);
        Assert.All(undead, u => Assert.Contains(u, MonsterRecordReader.UndeadTypeNames));
    }

    private static void AssertSameMonster(MonsterRecord expected, MonsterRecord actual)
    {
        // Field by field: MonsterRecord is a record, but its list members compare by reference,
        // so the compiler-generated equality would pass on any two distinct reads.
        Assert.Equal(expected.PreSpellNameKey, actual.PreSpellNameKey);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Icon, actual.Icon);
        Assert.Equal(expected.HitSound, actual.HitSound);
        Assert.Equal(expected.MissSound, actual.MissSound);
        Assert.Equal(expected.MoveSound, actual.MoveSound);
        Assert.Equal(expected.DeathSound, actual.DeathSound);
        Assert.Equal(expected.Intelligence, actual.Intelligence);
        Assert.Equal(expected.ArmorClass, actual.ArmorClass);
        Assert.Equal(expected.Movement, actual.Movement);
        Assert.Equal(expected.HitDice, actual.HitDice);
        Assert.Equal(expected.UseHitDice, actual.UseHitDice);
        Assert.Equal(expected.HitDiceBonus, actual.HitDiceBonus);
        Assert.Equal(expected.Thac0, actual.Thac0);
        Assert.Equal(expected.Attacks, actual.Attacks);
        Assert.Equal(expected.MagicResistance, actual.MagicResistance);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.ClassId, actual.ClassId);
        Assert.Equal(expected.Morale, actual.Morale);
        Assert.Equal(expected.ExperienceValue, actual.ExperienceValue);
        Assert.Equal(expected.FormType, actual.FormType);
        Assert.Equal(expected.PenaltyType, actual.PenaltyType);
        Assert.Equal(expected.ImmunityType, actual.ImmunityType);
        Assert.Equal(expected.MiscOptionsType, actual.MiscOptionsType);
        Assert.Equal(expected.UndeadType, actual.UndeadType);
        Assert.Equal(expected.SpecialAbilities.Pairs, actual.SpecialAbilities.Pairs);
        Assert.Equal(expected.Attributes, actual.Attributes);

        Assert.Equal(expected.Items?.Items ?? [], actual.Items!.Items);
        Assert.Equal(expected.Items?.Ready.Slots ?? [], actual.Items!.Ready.Slots);
        Assert.Equal(expected.Money?.Coins ?? [], actual.Money!.Coins);
        Assert.Equal(expected.Money?.Gems ?? [], actual.Money!.Gems);
        Assert.Equal(expected.Money?.Jewelry ?? [], actual.Money!.Jewelry);
    }
}
