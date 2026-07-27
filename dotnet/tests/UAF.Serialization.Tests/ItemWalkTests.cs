using System.Text.Json;
using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks <b>every</b> record in <c>items.dat</c> end to end and diffs the names against the C++
/// oracle's dump.
/// </summary>
/// <remarks>
/// <para>
/// This is the test the whole serialization effort was working toward. Reading record 0 only
/// proves the leading fields are right; reading all 285 in sequence proves every field's
/// <i>width and gate</i> is right, because a single byte of drift anywhere renames every record
/// after it. Nothing short of a full walk can catch that — which is exactly how a mis-modelled
/// <c>SPELL_ID</c> survived three attempts while still producing readable output.
/// </para>
/// <para>
/// It also exercises <c>Specab</c> and <c>ASL</c> against real data at every record, since both
/// terminate each one.
/// </para>
/// </remarks>
public class ItemWalkTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!;
    }

    private static string ItemsDat() =>
        Path.Combine(RepoRoot().FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "items.dat");

    /// <summary>
    /// The oracle's item names as (uniqueName, idName), or null when the golden dump is absent.
    /// </summary>
    private static (string Unique, string Id)[]? OracleItemNames()
    {
        string path = Path.Combine(RepoRoot().FullName, "oracle", "golden", "DefaultDesign.json");
        if (!File.Exists(path)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return [.. doc.RootElement.GetProperty("itemNames").EnumerateArray()
                      .Select(e => (e.GetProperty("uniqueName").GetString() ?? string.Empty,
                                    e.GetProperty("idName").GetString() ?? string.Empty))];
    }

    private static ItemDatabase WalkAll(out DesignVersion version)
    {
        using var fs = File.OpenRead(ItemsDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        version = header.Version;

        var ar = new MfcArchiveReader(fs);
        return ItemRecordReader.ReadDatabase(ar, version, ArchiveRole.Editor);
    }

    [Fact]
    public void All_285_records_read_to_completion()
    {
        var db = WalkAll(out _);
        var records = db.Items;

        Assert.Equal(285, records.Count);

        // Read after the records, so its contents prove the walk ended where it should.
        Assert.Equal(["None", "Bow", "CrossBow"], db.AmmoTypes);

        // Every name printable and non-empty. Drift shows up here as binary before it shows up
        // as a diff, and this way the failure names the record that broke.
        for (int i = 0; i < records.Count; i++)
        {
            string name = records[i].Names.UniqueName;
            Assert.False(string.IsNullOrEmpty(name), $"record {i} has an empty name");
            Assert.All(name, ch => Assert.InRange(ch, ' ', '~'));
        }
    }

    [Fact]
    public void Every_item_name_matches_the_oracle()
    {
        var expected = OracleItemNames();
        if (expected is null) return;              // golden dump not produced yet

        var records = WalkAll(out _).Items;

        Assert.Equal(expected.Length, records.Count);

        // Both names, every record. This is the assertion the port is ultimately judged by: it
        // compares against what the reference implementation itself reported, not against a
        // plausibility heuristic.
        Assert.Equal(expected, records.Select(r => (r.Names.UniqueName, r.Names.IdName)));
    }

    [Fact]
    public void Stream_is_fully_consumed_with_nothing_left_over()
    {
        // The strongest single assertion available without the oracle: if any field were the
        // wrong width, the walk would end early or run off the end. Landing exactly on EOF after
        // 285 records is hard to achieve by accident.
        using var fs = File.OpenRead(ItemsDat());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        var ar = new MfcArchiveReader(fs);
        ItemRecordReader.ReadDatabase(ar, header.Version, ArchiveRole.Editor);

        Assert.Equal(fs.Length, fs.Position);
    }

    [Fact]
    public void Legacy_specab_path_is_the_one_this_fixture_exercises()
    {
        var records = WalkAll(out var version).Items;

        // DefaultDesign is below the 0.920 gate, so its records carry the legacy conversion form
        // rather than an A_CStringPAIR_L. Asserted so that a fixture change cannot quietly move
        // the coverage to the other branch and leave this one untested.
        Assert.True(SpecabReader.UsesLegacyConversion(version));
        Assert.All(records, r => Assert.Empty(r.Tail.SpecialAbilities.Pairs));

        // Above the ordinal-array gate, so slots rather than bare ordinals.
        Assert.True(version >= SpecabReader.OrdinalArrayGate);
        Assert.All(records, r => Assert.Empty(r.Tail.SpecialAbilities.LegacyOrdinals));
    }

    [Fact]
    public void Every_record_ends_with_a_well_formed_attribute_list()
    {
        var records = WalkAll(out var version).Items;

        Assert.True(AslReader.IsPresent(version));

        // The ASL map name is verified inside the reader, so reaching here at all means all 285
        // markers matched. What is worth checking beyond that is that keys are sane.
        foreach (var entry in records.SelectMany(r => r.Tail.Attributes))
        {
            Assert.NotEmpty(entry.Key);
            Assert.All(entry.Key, ch => Assert.InRange(ch, ' ', '~'));
        }
    }
}
