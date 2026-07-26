using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Tier 2 — <c>CAR</c> without compression.
/// </summary>
/// <remarks>
/// <para>
/// <c>CAR</c>'s every operator begins <c>if (m_compressType == 0) { ar &lt;&lt; ...; }</c>, delegating
/// straight through to the wrapped <c>CArchive</c> (<c>class.cpp:11722</c>, <c>:11940</c>,
/// <c>:11707</c>). The string-interning table and the LZW layer are reached only on the
/// <c>else</c> branch, which requires <c>Compress(true)</c> to have set a non-zero type.
/// </para>
/// <para>
/// So tier 2 is <b>byte-identical to tier 1</b> at the primitive level: the same
/// <see cref="MfcArchiveReader"/> reads both, and only the payload offset differs. Only tier 3
/// needs <see cref="CarLzwDecompressor"/>.
/// </para>
/// </remarks>
public class CarTier2Tests
{
    private static string DataFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data", name);
    }

    /// <summary>
    /// The database payloads open with an int32 record count (<c>ITEM_DATA_TYPE::Serialize</c>,
    /// <c>Items.cpp:3136</c> — <c>ar &gt;&gt; temp;</c> then that many records).
    /// </summary>
    [Theory]
    [InlineData("items.dat", 285)]
    [InlineData("monsters.dat", 44)]
    [InlineData("spells.dat", 117)]
    public void Database_payload_opens_with_its_record_count(string fileName, int expectedCount)
    {
        using var fs = File.OpenRead(DataFile(fileName));
        var header = DesignFileHeader.Read(fs, DesignFileKind.Items);
        Assert.Equal(ArchiveTier.UncompressedCar, header.Tier);

        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        int count = new MfcArchiveReader(fs).ReadInt32();

        Assert.Equal(expectedCount, count);

        // Corroboration that the count is real rather than misread noise: the implied record
        // size must be sane. A misaligned read gives either a huge number (tiny record size) or
        // a tiny one (absurd record size).
        double bytesPerRecord = (double)fs.Length / count;
        Assert.InRange(bytesPerRecord, 100, 20_000);
    }

    /// <summary>
    /// These three numbers are also emitted by the C++ oracle's <c>-dumpjson</c> mode under
    /// <c>counts.items</c> / <c>counts.monsters</c> / <c>counts.spells</c>. When the golden file
    /// lands they can be diffed directly — this is the first end-to-end oracle agreement point,
    /// so keep the names aligned with the dumper's keys.
    /// </summary>
    [Fact]
    public void Record_counts_match_the_keys_the_oracle_dumper_emits()
    {
        var counts = new Dictionary<string, int>();
        foreach (var (file, key) in new[]
                 {
                     ("items.dat", "items"),
                     ("monsters.dat", "monsters"),
                     ("spells.dat", "spells"),
                 })
        {
            using var fs = File.OpenRead(DataFile(file));
            var header = DesignFileHeader.Read(fs, DesignFileKind.Items);
            fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
            counts[key] = new MfcArchiveReader(fs).ReadInt32();
        }

        Assert.Equal(285, counts["items"]);
        Assert.Equal(44, counts["monsters"]);
        Assert.Equal(117, counts["spells"]);
    }

    /// <summary>
    /// Tier 2 must not route through the LZW decoder. Guards against a future refactor that
    /// "simplifies" by treating every CAR file as compressed.
    /// </summary>
    [Fact]
    public void Tier_2_does_not_use_the_compression_path()
    {
        Assert.Equal(ArchiveTier.UncompressedCar,
            DesignFileKind.Items.TierFor(new DesignVersion(0.915025)));
        Assert.NotEqual(ArchiveTier.CompressedCar,
            DesignFileKind.Items.TierFor(new DesignVersion(0.915025)));
    }
}
