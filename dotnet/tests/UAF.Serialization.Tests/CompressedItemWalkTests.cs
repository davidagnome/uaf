using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks whole LZW-compressed <c>items.dat</c> files from three real designs spanning 2.53 → 5.28.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ItemWalkTests"/> covers the uncompressed DefaultDesign, which is the only fixture the
/// C++ oracle can dump. These three cover everything that fixture cannot reach: LZW decompression
/// driven for a whole file, the string-intern table maintained across hundreds of records, the
/// <b>modern</b> <c>Specab</c> branch (all three are above the 0.920 gate, where DefaultDesign is
/// below it), and the post-<c>VersionSpellNames</c> baseclass list rather than the legacy bitmask.
/// </para>
/// <para>
/// There is no golden dump to diff against here, so the assertions lean on two properties that are
/// very hard to satisfy by accident: the ammo-type list decodes correctly <i>after</i> every
/// record, and the LZW stream is exhausted at exactly that point.
/// </para>
/// <para>
/// The fixtures live under <c>reference/</c>, which is gitignored, so each returns early when
/// absent. CI fetches the 5.28 one.
/// </para>
/// </remarks>
public class CompressedItemWalkTests
{
    public static TheoryData<string, double, int, string[]> Designs => new()
    {
        // folder under reference/, version, record count, ammo types
        { "dc-default/data-files", 5.28, 562, ["None", "Bow", "CrossBow"] },
        { "SomethingWild.dsn/Data", 3.55, 551, ["Bow", "CrossBow"] },
        { "Case.dsn/Data", 2.53, 479, ["None", "Bow", "CrossBow"] },
    };

    private static string? ItemsDat(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        string path = Path.Combine(dir!.FullName, "reference", rel, "items.dat");
        return File.Exists(path) ? path : null;
    }

    private static ItemDatabase Walk(string path, out DesignVersion version, out CarArchiveReader car)
    {
        var fs = File.OpenRead(path);
        try
        {
            var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
            version = header.Version;
            fs.Seek(header.PayloadOffset, SeekOrigin.Begin);

            car = CarArchiveReader.Open(fs);
            Assert.Equal(2, car.CompressType);          // 2 == LZW; 0/1 would not exercise it
            return ItemRecordReader.ReadDatabase(car, version, ArchiveRole.Editor);
        }
        finally
        {
            // Deliberately not disposed inside the try: the CAR reader is handed back so callers
            // can check for stream exhaustion. Held open for the duration of the test method.
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Whole_compressed_database_walks_to_exactly_the_end(
        string rel, double expectedVersion, int expectedCount, string[] expectedAmmo)
    {
        string? path = ItemsDat(rel);
        if (path is null) return;

        var db = Walk(path, out var version, out var car);

        Assert.Equal(expectedVersion, version.Value, 6);
        Assert.Equal(expectedCount, db.Items.Count);

        // Read after every record, so decoding these correctly means no record drifted by a byte.
        Assert.Equal(expectedAmmo, db.AmmoTypes);

        // And nothing is left: the LZW stream ends exactly where the walk does.
        Assert.Throws<EndOfStreamException>(() => car.ReadByte());
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Names_survive_the_intern_table_across_every_record(
        string rel, double expectedVersion, int expectedCount, string[] expectedAmmo)
    {
        string? path = ItemsDat(rel);
        if (path is null) return;

        var db = Walk(path, out _, out _);

        // A mismanaged intern table shows up as a name from the wrong record rather than as
        // garbage, so printability alone is too weak here -- check the names are distinct enough
        // to rule out the table handing back the same entry repeatedly.
        Assert.All(db.Items, i => Assert.NotEmpty(i.Names.UniqueName));
        Assert.True(db.Items.Select(i => i.Names.UniqueName).Distinct().Count() > expectedCount / 2,
                    "suspiciously few distinct names for a healthy string table");

        // The '|' qualifier convention (Items.cpp:2451) appears across all three designs.
        Assert.Contains(db.Items, i => i.Names.UniqueName.Contains('|'));

        _ = (expectedVersion, expectedAmmo);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Modern_specab_branch_is_the_one_these_fixtures_exercise(
        string rel, double expectedVersion, int expectedCount, string[] expectedAmmo)
    {
        string? path = ItemsDat(rel);
        if (path is null) return;

        var db = Walk(path, out var version, out _);

        // Above 0.920, so A_CStringPAIR_L rather than the legacy conversion form. This is the
        // branch DefaultDesign cannot reach, which is the whole reason these fixtures are here.
        Assert.False(SpecabReader.UsesLegacyConversion(version));
        Assert.All(db.Items, i => Assert.Empty(i.Tail.SpecialAbilities.LegacySlots));
        Assert.All(db.Items, i => Assert.Empty(i.Tail.SpecialAbilities.LegacyOrdinals));

        // Real content, not just empty lists: most records carry named abilities.
        int withPairs = db.Items.Count(i => i.Tail.SpecialAbilities.Pairs.Count > 0);
        Assert.True(withPairs > db.Items.Count / 2, $"only {withPairs} records had specab pairs");

        // Printable, but NOT necessarily non-empty -- see below.
        Assert.All(db.Items.SelectMany(i => i.Tail.SpecialAbilities.Pairs), p =>
        {
            Assert.All(p.Key, ch => Assert.InRange(ch, ' ', '~'));
            Assert.All(p.Value, ch => Assert.InRange(ch, ' ', '~'));
        });

        _ = (expectedVersion, expectedCount, expectedAmmo);
    }

    [Fact]
    public void Empty_pairs_occur_in_real_designs_and_must_be_tolerated()
    {
        string? path = ItemsDat("dc-default/data-files");
        if (path is null) return;

        var db = Walk(path, out _, out _);
        var pairs = db.Items.SelectMany(i => i.Tail.SpecialAbilities.Pairs).ToList();

        // A handful of pairs have both an empty key and an empty value. That is genuine data, not
        // drift -- the walk still consumes the file to exactly its last byte either way.
        //
        // It matters because the two sibling structures disagree about this. A_ASLENTRY_L::Update
        // explicitly refuses an empty key (ASL.cpp:1311), which makes "keys are never empty" a
        // tempting invariant; but A_CStringPAIR_L::Serialize (ASL.cpp:1875) just reads and inserts
        // whatever is on the wire. A reader that validates non-empty keys here rejects designs the
        // reference loads without complaint.
        Assert.Contains(pairs, p => p.Key.Length == 0);
        Assert.True(pairs.Count(p => p.Key.Length == 0) < pairs.Count / 10,
                    "empty keys should be rare; a large share suggests misalignment, not data");

        // These strings are read verbatim, so the "*" blank sentinel is NOT decoded here -- an
        // empty key means a zero-length string on disk, not a sentinel.
        Assert.DoesNotContain(pairs, p => p.Key == ArchiveStringConventions.ArchiveBlank);
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Baseclass_list_is_read_as_strings_not_ordinals(
        string rel, double expectedVersion, int expectedCount, string[] expectedAmmo)
    {
        string? path = ItemsDat(rel);
        if (path is null) return;

        var db = Walk(path, out var version, out _);

        // Above VersionSpellNames, so the record carries a counted list of BASECLASS_ID -- which
        // derives from CString. Reading those as ints would desynchronise immediately, so the walk
        // completing is most of the proof; this pins the values themselves.
        Assert.False(ItemRecordReader.UsesLegacyUsability(ArchiveRole.Editor, version));

        var allBaseclasses = db.Items.SelectMany(i => i.Tail.UsableByBaseclass).Distinct().ToList();
        Assert.NotEmpty(allBaseclasses);
        Assert.Contains("fighter", allBaseclasses);
        Assert.All(allBaseclasses, b => Assert.All(b, ch => Assert.InRange(ch, ' ', '~')));

        _ = (expectedVersion, expectedCount, expectedAmmo);
    }

    [Fact]
    public void Compressed_asl_blocks_are_present_but_empty_in_these_designs()
    {
        string? path = ItemsDat("dc-default/data-files");
        if (path is null) return;

        var db = Walk(path, out _, out _);

        // Every record's ASL was located and its map name matched -- AslReader throws otherwise,
        // and matching "ITEM_DATA_ATTRIBUTES" 562 times running cannot happen by chance. So the
        // compressed reader is genuinely driving AslReader with a live intern table.
        //
        // But every block has a count of zero, so the key/flags/value loop -- and with it the
        // compressed-only key fixup -- is still not exercised on real data. The non-empty
        // compressed ASLs live in game.dat (GLOBAL_STATS_ATTRIBUTES, four entries), so closing
        // that last gap waits on a game.dat record walk. Asserted rather than left implicit so
        // this stays visible instead of looking like coverage it is not.
        Assert.All(db.Items, i => Assert.Empty(i.Tail.Attributes));
    }

    [Fact]
    public void Specab_pairs_carry_named_ability_data()
    {
        string? path = ItemsDat("dc-default/data-files");
        if (path is null) return;

        var db = Walk(path, out _, out _);

        var awlPike = db.Items.First(i => i.Names.UniqueName == "Awl Pike");
        Assert.Equal(
            [new SpecabPair("item_WeaponType", "piercing")],
            awlPike.Tail.SpecialAbilities.Pairs);
    }
}
