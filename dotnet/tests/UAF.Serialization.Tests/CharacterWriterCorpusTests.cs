using UAF.Common;
using UAF.Serialization;

namespace UAF.Serialization.Tests;

/// <summary>
/// Round-trips the pre-generated character lists out of shipped designs' <c>game.dat</c>.
/// </summary>
/// <remarks>
/// The fourth record type the port can write, and the one with the most leaves under it. Unlike the
/// three databases there is no whole-file byte comparison to make here — a character list sits in
/// the middle of <c>GLOBAL_STATS</c>, which has no writer — so the claim is the round trip and the
/// write-read-write identity beneath it.
/// </remarks>
public class CharacterWriterCorpusTests
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

    private static List<CharacterRecord>? Read(string rel)
    {
        if (GameDat(rel) is not { } path)
        {
            return null;
        }

        using var fs = File.OpenRead(path);
        var cursor = GameDataReader.Open(fs);
        return [.. GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version).Characters];
    }

    private static byte[] WriteCompressed(IReadOnlyList<CharacterRecord> characters)
    {
        var stream = new MemoryStream();
        using (var car = CarArchiveWriter.Open(stream))
        {
            CharacterRecordWriter.WriteList(ArchiveWriteCursor.For(car), characters);
        }

        return stream.ToArray();
    }

    private static List<CharacterRecord> ReadBack(byte[] payload) =>
        CharacterReader.ReadList(ArchiveCursor.For(CarArchiveReader.Open(new MemoryStream(payload))),
                                 CharacterRecordWriter.WrittenVersion, ArchiveRole.Editor);

    public static TheoryData<string, int> Designs => new()
    {
        { "Case.dsn/Data", 6 },
        { "SomethingWild.dsn/Data", 23 },
    };

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_real_character_round_trips(string rel, int expectedCount)
    {
        var characters = Read(rel);
        if (characters is null)
        {
            return;
        }

        Assert.Equal(expectedCount, characters.Count);

        var read = ReadBack(WriteCompressed(characters));

        Assert.Equal(characters.Count, read.Count);
        for (int i = 0; i < characters.Count; i++)
        {
            AssertSameCharacter(characters[i], read[i]);
        }
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Writing_what_was_read_gives_the_same_bytes_the_second_time(string rel, int count)
    {
        // The assertion that catches a field which never went out at all: a byte the writer omits
        // is one the reader takes from somewhere else, so the second pass diverges.
        var characters = Read(rel);
        if (characters is null)
        {
            return;
        }

        byte[] first = WriteCompressed(characters);
        byte[] second = WriteCompressed(ReadBack(first));

        Assert.Equal(first, second);
        _ = count;
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_character_in_a_modern_design_is_writable(string rel, int count)
    {
        // Without this the round trip could pass by having nothing to do.
        var characters = Read(rel);
        if (characters is null)
        {
            return;
        }

        Assert.NotEmpty(characters);
        Assert.All(characters,
                   c => Assert.True(CharacterRecordWriter.CanWrite(c, out string reason), reason));
        _ = count;
    }

    [Fact]
    public void The_corpus_reaches_the_leaves_that_only_a_character_has()
    {
        // Named rather than assumed: the spellbook, the blockage list and the three tagged
        // adjustment lists have no other writer, so if every one of them were empty across the
        // whole corpus the round trip would prove nothing about them.
        var characters = Read("Case.dsn/Data");
        var wild = Read("SomethingWild.dsn/Data");
        if (characters is null || wild is null)
        {
            return;
        }

        var all = characters.Concat(wild).ToList();
        Assert.Equal(29, all.Count);

        // What the corpus does reach: all 29 carry baseclass stats and items, 20 carry special
        // abilities, and exactly one has spells in its book.
        Assert.All(all, c => Assert.NotEmpty(c.BaseclassStats));
        Assert.All(all, c => Assert.NotEmpty(c.Items.Items));
        Assert.Equal(20, all.Count(c => c.SpecialAbilities.Pairs.Count > 0));
        Assert.Equal(1, all.Count(c => c.SpellBook.Spells.Count > 0));

        // And what it does NOT reach, stated rather than implied. Six of the record's structures
        // are empty in every character of both designs, so the round trip proves only that each is
        // written at the right size; their contents are covered by unit tests alone. The empty
        // money sack is the same gap the monster corpus has.
        Assert.All(all, c => Assert.All(c.Money!.Coins, coin => Assert.Equal(0, coin)));
        Assert.All(all, c => Assert.Empty(c.Money!.Gems));
        Assert.All(all, c => Assert.Empty(c.SkillAdjustments));
        Assert.All(all, c => Assert.Empty(c.SpellAdjustments));
        Assert.All(all, c => Assert.Empty(c.Blockages));
        Assert.All(all, c => Assert.Empty(c.SpellEffects));
        Assert.All(all, c => Assert.Empty(c.Attributes));
    }

    [Fact]
    public void The_key_the_reader_used_to_drop_is_real_data()
    {
        // preSpellNamesKey was read and discarded until the writer needed it. It is non-zero on
        // every character in the corpus, so writing a zero there would have put 29 wrong keys into
        // a file -- which makes keeping it real work rather than tidiness.
        var characters = Read("Case.dsn/Data");
        var wild = Read("SomethingWild.dsn/Data");
        if (characters is null || wild is null)
        {
            return;
        }

        Assert.All(characters.Concat(wild), c => Assert.NotEqual(0, c.PreSpellNamesKey));
    }

    [Fact]
    public void The_opener_is_rewritten_as_the_constant()
    {
        // The reference writes CHARACTER_VERSION whatever the record was read as, so a character
        // whose opener was a legacy index comes back carrying the version instead.
        var characters = Read("Case.dsn/Data");
        if (characters is null)
        {
            return;
        }

        Assert.All(ReadBack(WriteCompressed(characters)),
                   c => Assert.Equal(unchecked((int)CharacterRecordWriter.CharacterVersion),
                                     c.CharacterVersion));
    }

    private static void AssertSameCharacter(CharacterRecord expected, CharacterRecord actual)
    {
        // CharacterVersion is deliberately excluded: the writer replaces it with the constant.
        Assert.Equal(expected.PreSpellNamesKey, actual.PreSpellNamesKey);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Race, actual.Race);
        Assert.Equal(expected.Gender, actual.Gender);
        Assert.Equal(expected.ClassId, actual.ClassId);
        Assert.Equal(expected.Alignment, actual.Alignment);
        Assert.Equal(expected.AllowInCombat, actual.AllowInCombat);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.UndeadType, actual.UndeadType);
        Assert.Equal(expected.CreatureSize, actual.CreatureSize);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.CharacterId, actual.CharacterId);
        Assert.Equal(expected.Thac0, actual.Thac0);
        Assert.Equal(expected.Morale, actual.Morale);
        Assert.Equal(expected.Encumbrance, actual.Encumbrance);
        Assert.Equal(expected.MaxEncumbrance, actual.MaxEncumbrance);
        Assert.Equal(expected.ArmorClass, actual.ArmorClass);
        Assert.Equal(expected.HitPoints, actual.HitPoints);
        Assert.Equal(expected.MaxHitPoints, actual.MaxHitPoints);
        Assert.Equal(expected.NumberOfHitDice, actual.NumberOfHitDice);
        Assert.Equal(expected.Age, actual.Age);
        Assert.Equal(expected.MaxAge, actual.MaxAge);
        Assert.Equal(expected.Birthday, actual.Birthday);
        Assert.Equal(expected.MaxCureDisease, actual.MaxCureDisease);
        Assert.Equal(expected.UnarmedDieSmall, actual.UnarmedDieSmall);
        Assert.Equal(expected.UnarmedNumberDieSmall, actual.UnarmedNumberDieSmall);
        Assert.Equal(expected.UnarmedBonus, actual.UnarmedBonus);
        Assert.Equal(expected.UnarmedDieLarge, actual.UnarmedDieLarge);
        Assert.Equal(expected.UnarmedNumberDieLarge, actual.UnarmedNumberDieLarge);
        Assert.Equal(expected.MaxMovement, actual.MaxMovement);
        Assert.Equal(expected.ReadyToTrain, actual.ReadyToTrain);
        Assert.Equal(expected.CanTradeItems, actual.CanTradeItems);
        Assert.Equal(expected.Abilities, actual.Abilities);
        Assert.Equal(expected.OpenDoors, actual.OpenDoors);
        Assert.Equal(expected.OpenMagicDoors, actual.OpenMagicDoors);
        Assert.Equal(expected.BendBarsLiftGates, actual.BendBarsLiftGates);
        Assert.Equal(expected.HitBonus, actual.HitBonus);
        Assert.Equal(expected.DamageBonus, actual.DamageBonus);
        Assert.Equal(expected.MagicResistance, actual.MagicResistance);
        Assert.Equal(expected.BaseclassStats, actual.BaseclassStats);
        Assert.Equal(expected.SkillAdjustments, actual.SkillAdjustments);
        Assert.Equal(expected.SpellAdjustments, actual.SpellAdjustments);
        Assert.Equal(expected.IsPreGenerated, actual.IsPreGenerated);
        Assert.Equal(expected.CanBeSaved, actual.CanBeSaved);
        Assert.Equal(expected.HasLayedOnHandsToday, actual.HasLayedOnHandsToday);
        Assert.Equal(expected.NumberOfAttacks, actual.NumberOfAttacks);
        Assert.Equal(expected.Icon, actual.Icon);
        Assert.Equal(expected.IconIndex, actual.IconIndex);
        Assert.Equal(expected.OriginalIndex, actual.OriginalIndex);
        Assert.Equal(expected.UniquePartyId, actual.UniquePartyId);
        Assert.Equal(expected.DisableTalkIfDead, actual.DisableTalkIfDead);
        Assert.Equal(expected.TalkEvent, actual.TalkEvent);
        Assert.Equal(expected.TalkLabel, actual.TalkLabel);
        Assert.Equal(expected.ExamineEvent, actual.ExamineEvent);
        Assert.Equal(expected.ExamineLabel, actual.ExamineLabel);
        Assert.Equal(expected.DetectingInvisible, actual.DetectingInvisible);
        Assert.Equal(expected.DetectingTraps, actual.DetectingTraps);
        Assert.Equal(expected.Blockages, actual.Blockages);
        Assert.Equal(expected.SmallPic, actual.SmallPic);
        Assert.Equal(expected.SpecialAbilities.Pairs, actual.SpecialAbilities.Pairs);
        Assert.Equal(expected.Attributes, actual.Attributes);

        Assert.Equal(expected.Money!.Coins, actual.Money!.Coins);
        Assert.Equal(expected.Money.Gems, actual.Money.Gems);
        Assert.Equal(expected.Money.Jewelry, actual.Money.Jewelry);

        Assert.Equal(expected.SpellBook.UseLimits, actual.SpellBook.UseLimits);
        Assert.Equal(expected.SpellBook.Spells, actual.SpellBook.Spells);

        Assert.Equal(expected.Items.Items, actual.Items.Items);
        Assert.Equal(expected.Items.Ready.Slots, actual.Items.Ready.Slots);
    }
}
