using System.Text.Json;
using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Walks whole <c>monsters.dat</c> files: DefaultDesign against the C++ oracle, and three
/// LZW-compressed designs spanning 2.53 → 5.28.
/// </summary>
/// <remarks>
/// <c>MONSTER_DATA</c> is the only record type whose payload continues <b>past</b> its attribute
/// list — <c>myItems</c> and <c>money</c> follow it (<c>Monster.cpp:851</c>). A reader modelled on
/// <c>ITEM_DATA</c>, where the ASL is last, stops three structures early.
/// </remarks>
public class MonsterWalkTests
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

    private static string DefaultDesignMonsters() =>
        Path.Combine(RepoRoot().FullName, "src", "UAFWinEd", "DefaultDesign.dsn", "Data", "monsters.dat");

    private static string[]? OracleMonsterNames()
    {
        string path = Path.Combine(RepoRoot().FullName, "oracle", "golden", "DefaultDesign.json");
        if (!File.Exists(path)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return [.. doc.RootElement.GetProperty("monsterNames").EnumerateArray()
                      .Select(e => e.ValueKind == JsonValueKind.Object
                          ? e.GetProperty("name").GetString() ?? string.Empty
                          : e.GetString() ?? string.Empty)];
    }

    private static List<MonsterRecord> ReadDefaultDesign(out DesignVersion version, out FileStream fs)
    {
        fs = File.OpenRead(DefaultDesignMonsters());
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        version = header.Version;
        return MonsterRecordReader.ReadDatabase(
            new MfcArchiveReader(fs), header.Version, ArchiveRole.Editor);
    }

    [Fact]
    public void DefaultDesign_walks_to_exactly_the_end_of_the_file()
    {
        var monsters = ReadDefaultDesign(out _, out var fs);
        using (fs)
        {
            Assert.Equal(44, monsters.Count);
            Assert.Equal(fs.Length, fs.Position);
        }
    }

    [Fact]
    public void Every_monster_name_matches_the_oracle()
    {
        string[]? expected = OracleMonsterNames();
        if (expected is null) return;

        var monsters = ReadDefaultDesign(out _, out var fs);
        using (fs)
        {
            Assert.Equal(expected.Length, monsters.Count);
            Assert.Equal(expected, monsters.Select(m => m.Name));
        }
    }

    [Fact]
    public void Hit_dice_is_a_float_and_fractional_values_survive()
    {
        var monsters = ReadDefaultDesign(out _, out var fs);
        using (fs)
        {
            // Monster.h:410 declares Hit_Dice as float among longs. Same four bytes, so reading it
            // as an int never desynchronises -- it just yields nonsense, which no alignment check
            // catches. A kobold has a QUARTER hit die; as an int those bytes read as ~1.05e9.
            var kobold = monsters.First(m => m.Name == "Kobold");
            Assert.Equal(0.25f, kobold.HitDice);

            // Fractional hit dice are not a rounding artefact -- several monsters have them.
            Assert.Contains(monsters, m => m.HitDice > 0 && m.HitDice < 1);
            Assert.All(monsters, m => Assert.InRange(m.HitDice, 0f, 100f));
        }
    }

    [Fact]
    public void Record_continues_past_the_attribute_list()
    {
        var monsters = ReadDefaultDesign(out var version, out var fs);
        using (fs)
        {
            // Both gates are open at 0.915, so every record carries them.
            Assert.True(version > DesignVersion.V0693);
            Assert.True(version >= DesignVersion.V0906);

            Assert.All(monsters, m => Assert.NotNull(m.Items));
            Assert.All(monsters, m => Assert.NotNull(m.Money));

            // MAX_COIN_TYPES is a compile-time 10, not design data, so every sack has ten slots.
            Assert.All(monsters, m => Assert.Equal(
                MonsterLeafReaders.MaxCoinTypes, m.Money!.Coins.Count));

            // Twelve equipment slots, likewise fixed.
            Assert.All(monsters, m => Assert.Equal(
                MonsterLeafReaders.ReadySlotCount, m.Items!.Ready.Slots.Count));

            // At least one monster actually carries something -- otherwise this proves nothing.
            Assert.Contains(monsters, m => m.Items!.Items.Count > 0);
        }
    }

    public static TheoryData<string, double, int> CompressedDesigns => new()
    {
        { "dc-default/data-files", 5.28, 171 },
        { "SomethingWild.dsn/Data", 3.55, 195 },
        { "Case.dsn/Data", 2.53, 160 },
    };

    [Theory]
    [MemberData(nameof(CompressedDesigns))]
    public void Compressed_designs_walk_to_exhaustion(string rel, double version, int expectedCount)
    {
        string path = Path.Combine(RepoRoot().FullName, "reference", rel, "monsters.dat");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var car = CarArchiveReader.Open(fs);

        var monsters = MonsterRecordReader.ReadDatabase(car, header.Version, ArchiveRole.Editor);

        Assert.Equal(version, header.Version.Value, 6);
        Assert.Equal(expectedCount, monsters.Count);
        Assert.Throws<EndOfStreamException>(() => car.ReadByte());

        // Past VersionSpellNames, so classID is read as a name rather than defaulted to "Fighter".
        Assert.True(header.Version > DesignVersion.SpellNames);
        Assert.All(monsters, m => Assert.All(m.Name, ch => Assert.InRange(ch, ' ', '~')));
    }

    [Theory]
    [MemberData(nameof(CompressedDesigns))]
    public void Attack_details_read_spell_ids_as_strings(string rel, double version, int expectedCount)
    {
        string path = Path.Combine(RepoRoot().FullName, "reference", rel, "monsters.dat");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var header = DesignFileHeader.Read(fs, DesignFileKind.Database);
        fs.Seek(header.PayloadOffset, SeekOrigin.Begin);
        var monsters = MonsterRecordReader.ReadDatabase(
            CarArchiveReader.Open(fs), header.Version, ArchiveRole.Editor);

        var attacks = monsters.SelectMany(m => m.Attacks).ToList();
        Assert.NotEmpty(attacks);

        // SPELL_ID derives from CString, so this is a name. Reading it as an int would
        // desynchronise on the first monster that has one.
        Assert.All(attacks, a => Assert.All(a.SpellId, ch => Assert.InRange(ch, ' ', '~')));
        Assert.All(attacks, a => Assert.InRange(a.Sides, 0, 1000));

        _ = (version, expectedCount);
    }
}
