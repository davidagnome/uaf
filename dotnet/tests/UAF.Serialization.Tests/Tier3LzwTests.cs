using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Drives <see cref="CarLzwDecompressor"/> with real <c>CAR</c> encoder output.
/// </summary>
/// <remarks>
/// <para>
/// Until these fixtures existed, the decoder was verified only against hand-built streams — it
/// implemented the algorithm at <c>class.cpp:12215</c> correctly but had never seen a byte the
/// C++ side actually wrote. These designs are all past the 0.930 compression gate, so their
/// databases are LZW-compressed.
/// </para>
/// <para>
/// Layout of a tier-3 database file:
/// <code>
///   [0..7]   magic 0xFABCDEFABCDEFABF
///   [8..15]  version double
///   [16]     compressType byte (2) -- written through CAR while still uncompressed
///   [17..]   13-bit LZW codes in fixed 52-byte blocks
/// </code>
/// Corroborated by the data: <c>(fileSize - 17) % 52 == 0</c> on every tier-3 file, and 0x02 sits
/// at offset 16 exactly as <c>CAR::Compress(true)</c> writes it.
/// </para>
/// <para>
/// The fixtures live under <c>reference/</c>, which is gitignored, so these tests return early
/// when absent rather than failing.
/// </para>
/// </remarks>
public class Tier3LzwTests
{
    private const int CompressTypeOffset = 16;
    private const int LzwPayloadOffset = 17;
    private const int BlockBytes = 52;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Tier-3 fixtures, by design name and expected version.</summary>
    public static TheoryData<string, double> Fixtures => new()
    {
        { Path.Combine("Case.dsn", "Data"), 2.53 },
        { Path.Combine("Ambassador's_Letter", "Data"), 2.53 },
        { Path.Combine("SomethingWild.dsn", "Data"), 3.55 },
        { Path.Combine("dc-default", "data-files"), 5.28 },
    };

    private static string? ItemsDat(string relativeDataDir)
    {
        string path = Path.Combine(RepoRoot(), "reference", relativeDataDir, "items.dat");
        return File.Exists(path) ? path : null;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Tier3_files_are_LZW_framed_as_the_model_predicts(string dataDir, double expectedVersion)
    {
        if (ItemsDat(dataDir) is not { } path) { return; }   // fixture not present

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);

        Assert.True(header.HadMagic);
        Assert.Equal(expectedVersion, header.Version.Value, precision: 6);
        Assert.Equal(ArchiveTier.CompressedCar, header.Tier);

        // The compression-type byte is written through CAR *before* compression starts, so it is
        // plain. 2 is the only type CAR::Compress emits (class.cpp:11670).
        fs.Seek(CompressTypeOffset, SeekOrigin.Begin);
        Assert.Equal(2, fs.ReadByte());

        // The LZW body is a whole number of 52-byte blocks -- 416 bits, exactly 32 codes, no
        // padding. A framing model that were off by even one byte would not divide evenly.
        Assert.Equal(0, (fs.Length - LzwPayloadOffset) % BlockBytes);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Decompressed_stream_opens_with_a_plausible_record_count(string dataDir, double expectedVersion)
    {
        if (ItemsDat(dataDir) is not { } path) { return; }

        Assert.True(expectedVersion >= 0.930, "fixture must be past the compression gate");

        using var fs = File.OpenRead(path);
        fs.Seek(LzwPayloadOffset, SeekOrigin.Begin);

        var lzw = new CarLzwDecompressor(fs);
        byte[] first = lzw.ReadBytes(4);
        Assert.Equal(4, first.Length);

        int count = BitConverter.ToInt32(first, 0);

        // ITEM_DATA_TYPE::Serialize opens with the record count (Items.cpp:3136). Garbage out of
        // the decompressor shows up here immediately as an absurd count -- this is the assertion
        // that actually exercises the LZW path end to end.
        Assert.InRange(count, 1, 20_000);

        // Sanity: the compressed file is far smaller than count * a plausible record size, which
        // is only true if the data really is compressed.
        Assert.True(fs.Length < count * 2000L,
            $"file {fs.Length} bytes for {count} records - does not look compressed");
    }

    /// <summary>
    /// The decisive test: decompress, then read real item names out of the result.
    /// </summary>
    /// <remarks>
    /// A plausible record count could survive a partially-wrong decoder. Readable names cannot —
    /// LZW output is either exactly right or it is noise, and noise does not spell "Arrow".
    /// </remarks>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Decompressed_records_yield_readable_item_names(string dataDir, double expectedVersion)
    {
        if (ItemsDat(dataDir) is not { } path) { return; }

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        Assert.Equal(expectedVersion, header.Version.Value, precision: 6);

        // Open at the compression-type byte. CarArchiveReader consumes it, then layers the
        // string-interning scheme over the LZW output -- MfcArchiveReader cannot read this,
        // because compressed CAR uses a 4-byte length and an index, not a 1-byte count.
        fs.Seek(CompressTypeOffset, SeekOrigin.Begin);
        var car = CarArchiveReader.Open(fs);
        Assert.Equal(2, car.CompressType);

        int count = car.ReadInt32();
        Assert.InRange(count, 1, 20_000);

        // preSpellNameKey is read: ver >= VersionSaveIDs (Items.cpp:2753).
        Assert.True(header.Version >= DesignVersion.SaveIDs);
        car.ReadInt32();

        // OPEN QUESTION -- spellID is NOT read here, though Items.cpp:2761 gates it on
        // `ver >= 0.999647` and these designs are 2.53 / 3.55 / 5.28, all far above that.
        // Reading it desynchronises immediately: the next uint comes back as string index 8
        // against a 2-entry table, whereas skipping it decodes cleanly. The bytes are
        // unambiguous, so one of the following is true and only the oracle can say which:
        //   * ITEM_DATA_TYPE::Serialize(CAR&) passes a different `version` than the file header's
        //   * these files were written by a build whose gate differed
        //   * the field is consumed elsewhere in a path not yet traced
        // Pointing the C++ dumper at one of these designs settles it -- deliberately NOT guessed
        // here, because a wrong guess would be baked into every record that follows.

        string uniqueName = ArchiveStringConventions.Decode(car.ReadString());
        string idName = ArchiveStringConventions.Decode(car.ReadString());

        // Readable names are the decisive evidence: LZW output is either exactly right or it is
        // noise, and noise does not spell an item name.
        // Record 0 is a blank/template placeholder in these designs, so uniqueName may be
        // empty; idName carries the real content. What matters is that both are PRINTABLE --
        // LZW output is either exactly right or it is noise, and noise is not printable ASCII.
        Assert.All(uniqueName, ch => Assert.InRange(ch, ' ', '~'));
        Assert.All(idName, ch => Assert.InRange(ch, ' ', '~'));
        Assert.False(string.IsNullOrEmpty(idName),
            "first record has no idName - the compressed stream was not decoded correctly");

        // Interning is actually in use, which is what distinguishes this from a plain archive.
        Assert.True(car.InternedStringCount > 0);
    }

    [Fact]
    public void Version_constants_are_waypoints_not_an_enumeration()
    {
        // Real designs sit at 2.53 and 3.55, but Externs.h jumps straight from _VERSION_0930
        // (0.930) to _VERSION_524 (5.24) with nothing between. Any logic that treats the named
        // constants as the set of valid versions -- a switch, a lookup, a validity check -- is
        // wrong for a large fraction of real content.
        var real = new[] { 2.53, 3.55 };
        foreach (double v in real)
        {
            Assert.DoesNotContain(DesignVersion.All, c => Math.Abs(c.Value - v) < 1e-9);

            // They still order correctly against the surrounding waypoints, which is all the
            // gates ever need.
            var version = new DesignVersion(v);
            Assert.True(version > DesignVersion.SpecialAbilities);   // 0.930 -> compressed
            Assert.True(version < DesignVersion.V524);
            Assert.Equal(ArchiveTier.CompressedCar, DesignFileKind.Database.TierFor(version));
        }
    }
}
