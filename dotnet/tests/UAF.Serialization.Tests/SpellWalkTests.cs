using System.Text.Json;
using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks whole <c>spells.dat</c> files: the uncompressed DefaultDesign against the C++ oracle, and
/// three LZW-compressed designs spanning 2.53 → 5.28.
/// </summary>
/// <remarks>
/// <para>
/// <c>SPELL_DATA</c> is the largest record type in the format. It is also the only one where both
/// class-mask branches are live in the fixtures: DefaultDesign is 0.915 — above the 0.910 gate that
/// introduced <c>castMask</c>, below the 0.998101 that replaced it with a name list — so the
/// legacy bitmask conversion is exercised by the very design the oracle can dump.
/// </para>
/// <para>
/// The compressed fixtures are gitignored; those tests return early when absent.
/// </para>
/// </remarks>
public class SpellWalkTests
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

    private static string DefaultDesignSpells() =>
        Path.Combine(RepoRoot().FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "spells.dat");

    private static string[]? OracleSpellNames()
    {
        string path = Path.Combine(RepoRoot().FullName, "oracle", "golden", "DefaultDesign.json");
        if (!File.Exists(path)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return [.. doc.RootElement.GetProperty("spellNames").EnumerateArray()
                      .Select(e => e.ValueKind == JsonValueKind.Object
                          ? e.GetProperty("name").GetString() ?? string.Empty
                          : e.GetString() ?? string.Empty)];
    }

    [Fact]
    public void DefaultDesign_walks_to_exactly_the_end_of_the_file()
    {
        using var fs = File.OpenRead(DefaultDesignSpells());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        var spells = SpellRecordReader.ReadDatabase(
            new MfcArchiveReader(fs), header.Version, ArchiveRole.Editor);

        Assert.Equal(117, spells.Count);

        // The whole-file assertion: any wrong field width anywhere in any of the 117 records ends
        // the walk early or runs off the end.
        Assert.Equal(fs.Length, fs.Position);
    }

    [Fact]
    public void Every_spell_name_matches_the_oracle()
    {
        string[]? expected = OracleSpellNames();
        if (expected is null) return;

        using var fs = File.OpenRead(DefaultDesignSpells());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        var spells = SpellRecordReader.ReadDatabase(
            new MfcArchiveReader(fs), header.Version, ArchiveRole.Editor);

        Assert.Equal(expected.Length, spells.Count);
        Assert.Equal(expected, spells.Select(s => s.Name));
    }

    [Fact]
    public void Legacy_cast_mask_expands_into_baseclass_names()
    {
        using var fs = File.OpenRead(DefaultDesignSpells());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        var spells = SpellRecordReader.ReadDatabase(
            new MfcArchiveReader(fs), header.Version, ArchiveRole.Editor);

        // 0.915 sits between the two gates, so this fixture takes the bitmask branch.
        Assert.True(header.Version >= DesignVersion.V0910);
        Assert.True(header.Version < DesignVersion.SpellNames);

        // Below 0.930 the mask is not trusted: magic-user and cleric are forced apart, so no spell
        // can end up castable by both (Spell.cpp:3927).
        Assert.True(header.Version < DesignVersion.V0930);
        Assert.All(spells, s => Assert.False(
            s.AllowedBaseclasses.Contains("magicUser") && s.AllowedBaseclasses.Contains("cleric"),
            $"'{s.Name}' came out castable by both magicUser and cleric"));

        // Every name produced must be one of the seven the mask can expand to.
        string[] known = [.. ClassFlags.InSerializedOrder.Select(f => f.Name)];
        Assert.All(spells.SelectMany(s => s.AllowedBaseclasses), b => Assert.Contains(b, known));

        // And the school follows the same mask.
        Assert.All(spells, s => Assert.Contains(s.SchoolId, (string[])["Magic User", "Cleric"]));
    }

    public static TheoryData<string, double, int> CompressedDesigns => new()
    {
        { "dc-default/data-files", 5.28, 423 },
        { "SomethingWild.dsn/Data", 3.55, 377 },
        { "Case.dsn/Data", 2.53, 318 },
    };

    [Theory]
    [MemberData(nameof(CompressedDesigns))]
    public void Compressed_designs_walk_to_exhaustion(string rel, double version, int expectedCount)
    {
        string path = Path.Combine(RepoRoot().FullName, "reference", rel, "spells.dat");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var car = CarArchiveReader.Open(fs);

        var spells = SpellRecordReader.ReadDatabase(car, header.Version, ArchiveRole.Editor);

        Assert.Equal(version, header.Version.Value, 6);
        Assert.Equal(expectedCount, spells.Count);
        Assert.Throws<EndOfStreamException>(() => car.ReadByte());

        // These are past VersionSpellNames, so they take the NAME branch rather than the bitmask
        // one -- the opposite of DefaultDesign, which is why both fixtures are needed.
        Assert.True(header.Version > DesignVersion.SpellNames);
        Assert.Contains(spells, s => s.SchoolId == "Druid");     // impossible via the legacy mask
    }

    [Theory]
    [MemberData(nameof(CompressedDesigns))]
    public void Effects_carry_dice_expressions_and_scripts(string rel, double version, int expectedCount)
    {
        string path = Path.Combine(RepoRoot().FullName, "reference", rel, "spells.dat");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var spells = SpellRecordReader.ReadDatabase(
            CarArchiveReader.Open(fs), header.Version, ArchiveRole.Editor);

        // Six DICEPLUS parameters per spell above 0.999432: Duration, P1, P2, P3, P4, P5.
        Assert.All(spells, s => Assert.Equal(6, s.Parameters.Count));
        Assert.All(spells.SelectMany(s => s.Parameters), d => Assert.Equal("DP2", d.Tag));

        // A good fraction carry effects, and each effect's trailing changeData is present -- that
        // field sits OUTSIDE the storing/loading branch (Spell.cpp:273) and is easy to miss.
        var effects = spells.SelectMany(s => s.Effects).ToList();
        Assert.NotEmpty(effects);
        Assert.All(effects, e => Assert.NotNull(e.ChangeData));
        Assert.Contains(effects, e => e.IndexKey.StartsWith('$'));

        _ = (version, expectedCount);
    }
}
