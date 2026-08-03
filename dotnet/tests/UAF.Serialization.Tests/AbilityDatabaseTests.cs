using UAF.Common;

namespace UAF.Serialization.Tests;

/// <summary>
/// Reads <c>ability.dat</c> — the seventh tagged database, and the last one that had no reader.
/// </summary>
/// <remarks>
/// Its file name was already in <see cref="TaggedDatabaseReader.FileName"/> and nothing ever
/// opened it, which stayed invisible until the character generator needed the dice a strength
/// score is rolled from.
/// </remarks>
public class AbilityDatabaseTests
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

    /// <summary>Every design in the corpus that ships an ability database.</summary>
    public static TheoryData<string> Designs => new()
    {
        "src/UAFWinEd/DefaultDesign.dsn",
        "reference/SomethingWild.dsn",
        "reference/ci-tier3",
    };

    private static List<AbilityRecord>? Read(string design)
    {
        if (RepoRoot() is not { } root)
        {
            return null;
        }

        string path = Path.Combine(root.FullName,
            design.Replace('/', Path.DirectorySeparatorChar), "Data",
            TaggedDatabaseReader.FileName(TaggedDatabase.Ability));

        if (!File.Exists(path))
        {
            return null;
        }

        // The design's own version, off game.dat beside the database. A tagged header carries a
        // record tag and a count and no version, so the version has to come from the design --
        // and it decides both the specab gate and whether an editor stream carries the old key.
        string gameDat = Path.Combine(Path.GetDirectoryName(path)!, "game.dat");
        if (!File.Exists(gameDat))
        {
            return null;
        }

        DesignVersion version;
        using (var game = File.OpenRead(gameDat))
        {
            version = GameDataReader.Open(game).Version;
        }

        var header = TaggedDatabaseReader.Read(path, TaggedDatabase.Ability, out var body,
                                               out var stream);
        using (stream)
        {
            return AbilityRecordReader.ReadAll(body, header.Count, version);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void A_designs_abilities_read_whole(string design)
    {
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        // The six a character sheet shows. A design may add more, but never fewer -- the
        // generator names all six by hand.
        Assert.True(abilities.Count >= 6,
                    $"{design} has {abilities.Count} abilities; six are named by the generator");

        Assert.All(abilities, a => Assert.NotEmpty(a.Name));
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void The_six_the_generator_names_are_all_there(string design)
    {
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        string[] wanted =
            ["Strength", "Intelligence", "Wisdom", "Dexterity", "Constitution", "Charisma"];

        foreach (string name in wanted)
        {
            Assert.Contains(abilities,
                            a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_ability_carries_dice_to_roll_a_score_from(string design)
    {
        var abilities = Read(design);
        if (abilities is null)
        {
            return;
        }

        // Without these the 0.870-and-later path has nothing to roll, and a new character's
        // scores would all come out at zero.
        Assert.All(abilities, a => Assert.NotNull(a.Roll));
    }

    [Fact]
    public void An_unknown_record_version_is_refused_rather_than_guessed()
    {
        // One tag exists. The reference logs and returns a failure; there is no second shape to
        // fall back to, so guessing would mean reading a record that is not there.
        var bytes = new MemoryStream();
        var writer = new MfcArchiveWriter(bytes);
        writer.WriteString("Abd9");
        bytes.Position = 0;

        var cursor = ArchiveCursor.For(new MfcArchiveReader(bytes));

        var thrown = Assert.Throws<InvalidDataException>(
            () => AbilityRecordReader.Read(cursor, DesignVersion.SpellNames));

        Assert.Contains("Abd9", thrown.Message);
    }
}
