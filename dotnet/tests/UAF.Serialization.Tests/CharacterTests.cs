using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads the pre-generated character list out of <c>game.dat</c>.
/// </summary>
/// <remarks>
/// <c>CHARACTER</c> is the largest record in the format and the last structure blocking the rest
/// of <c>GLOBAL_STATS</c>. Reaching it at all means the entire preceding record — prefix, ASL, art
/// slots, sounds, keys, special items and quests — was consumed exactly.
/// </remarks>
public class CharacterTests
{
    private static string? GameDat(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        string path = Path.Combine(dir!.FullName, "reference", rel, "game.dat");
        return File.Exists(path) ? path : null;
    }

    private static GlobalStatsPrefix Read(string path)
    {
        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        return GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version);
    }

    public static TheoryData<string, int> Designs => new()
    {
        { "Case.dsn/Data", 6 },
        { "SomethingWild.dsn/Data", 23 },
        { "dc-default/data-files", 0 },
    };

    [Theory]
    [MemberData(nameof(Designs))]
    public void Character_list_reads_with_coherent_values(string rel, int expectedCount)
    {
        string? path = GameDat(rel);
        if (path is null) return;

        var g = Read(path);
        Assert.Equal(expectedCount, g.Characters.Count);

        foreach (var c in g.Characters)
        {
            // Names, races and classes are all strings; a width error anywhere earlier in this
            // 100-field record turns them into binary.
            Assert.NotEmpty(c.Name);
            Assert.All(c.Name, ch => Assert.InRange(ch, ' ', '~'));
            Assert.All(c.Race, ch => Assert.InRange(ch, ' ', '~'));

            // Semantic sanity: hit points within a plausible range, and current never above max.
            Assert.InRange(c.HitPoints, 0, 10000);
            Assert.True(c.HitPoints <= c.MaxHitPoints,
                        $"'{c.Name}' has {c.HitPoints}/{c.MaxHitPoints} hit points");

            // The seven ability scores are AD&D 3..25. This is the assertion that would catch the
            // 0.999702 BYTE/int width fork being applied to the wrong side.
            Assert.InRange(c.Abilities.Strength, 0, 25);
            Assert.InRange(c.Abilities.Intelligence, 0, 25);
            Assert.InRange(c.Abilities.Charisma, 0, 25);
        }
    }

    [Fact]
    public void Multiclass_characters_have_one_baseclass_entry_per_class()
    {
        string? path = GameDat("Case.dsn/Data");
        if (path is null) return;

        var g = Read(path);

        // Internal cross-check: the class name is a single string, the baseclass stats are a
        // counted list read much later in the record. That a "Cleric/Fighter" has exactly two
        // entries -- and single-class characters exactly one -- ties the two together.
        foreach (var c in g.Characters)
        {
            int slashes = c.ClassId.Count(ch => ch == '/');
            Assert.Equal(slashes + 1, c.BaseclassStats.Count);
        }

        Assert.Contains(g.Characters, c => c.ClassId.Contains('/'));
    }

    [Fact]
    public void Known_characters_decode_field_by_field()
    {
        string? path = GameDat("Case.dsn/Data");
        if (path is null) return;

        var g = Read(path);
        var sherlas = g.Characters.First(c => c.Name == "Sherlas of Hemlock");

        Assert.Equal("Half-Elf", sherlas.Race);
        Assert.Equal("Fighter", sherlas.ClassId);
        Assert.Equal(27, sherlas.HitPoints);
        Assert.Equal(27, sherlas.MaxHitPoints);
        Assert.Equal(16, sherlas.Abilities.Strength);
        Assert.Equal(19, sherlas.Abilities.Intelligence);
        Assert.Equal(5, sherlas.Items.Items.Count);

        // NbrAttacks is a float and nbrHitDice a double -- both sit among ints, so reading either
        // at the wrong width gives a plausible-looking record with nonsense numbers.
        Assert.Equal(1f, sherlas.NumberOfAttacks);
        Assert.InRange(sherlas.NumberOfHitDice, 0, 100);
    }
}
