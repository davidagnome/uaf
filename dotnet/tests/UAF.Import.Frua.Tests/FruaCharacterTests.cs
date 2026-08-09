using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Reading a <c>MONST###.DAT</c> (<c>ImportUACCH</c>, <c>UAFWinEd/UAImport.cpp:6038</c>).
/// </summary>
public class FruaCharacterTests
{
    private static string? Heirs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string design = Path.Combine(dir.FullName, "reference", "Unlimited Adventures -ENG",
                                     "DESIGNS", "UA", "HEIRS.DSN");
        return Directory.Exists(design) ? design : null;
    }

    private static byte[] Synthetic()
    {
        var b = new byte[FruaCharacter.FileLength];
        FruaGameData.TextEncoding.GetBytes("Grick").CopyTo(b, 96);
        b[88] = 6;      // race: monster
        b[95] = 1;      // combat mode
        b[137] = 9;     // level
        b[179] = 55;    // stored AC -> 5
        b[184] = 40;    // hp
        b[395] = 44;    // adjusted hp
        b[397] = 77;    // monster index
        return b;
    }

    [Fact]
    public void The_record_reads_its_identifying_fields()
    {
        var c = FruaCharacter.Read(Synthetic());

        Assert.Equal("Grick", c.Name);
        Assert.Equal(77, c.MonsterIndex);
        Assert.Equal(9, c.Level);
        Assert.Equal(40, c.HitPoints);
        Assert.Equal(44, c.AdjustedHitPoints);
        Assert.Equal(5, c.SavingThrows.Count);
        Assert.Equal(7, c.ClassLevels.Count);
        Assert.Equal(16, c.ItemsCarried.Count);
    }

    /// <summary>Armour class is stored subtracted from 60.</summary>
    [Theory]
    [InlineData(59, 1)]
    [InlineData(50, 10)]
    [InlineData(60, 0)]
    public void Armour_class_is_stored_inverted(byte stored, int expected)
    {
        var b = Synthetic();
        b[179] = stored;

        Assert.Equal(expected, FruaCharacter.Read(b).ArmourClass);
    }

    /// <summary>
    /// Only race 6 with combat mode 1 is a monster; everything else imports as an NPC.
    /// </summary>
    [Theory]
    [InlineData(6, 1, true)]
    [InlineData(6, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(5, 1, false)]
    public void A_monster_is_race_six_in_combat_mode_one(byte race, byte mode, bool monster)
    {
        var b = Synthetic();
        b[88] = race;
        b[95] = mode;

        Assert.Equal(monster, FruaCharacter.Read(b).IsMonster);
    }

    [Fact]
    public void A_short_record_is_refused()
    {
        var thrown = Assert.Throws<InvalidDataException>(
            () => FruaCharacter.Read(new byte[400]));

        Assert.Contains("432", thrown.Message, StringComparison.Ordinal);
    }

    // ---- the real DOS monsters -------------------------------------------------------------

    /// <summary>
    /// Every shipped record's <c>monsterIndex</c> agrees with the number in its filename.
    /// </summary>
    /// <remarks>
    /// <b>This is the assertion that validates the whole 432-byte layout.</b> The field sits at
    /// offset 397, past 150 fields and several alignment gaps that MSVC inserts and the declaration
    /// does not show. Its agreeing with the filename — four times, for sparse numbers like 101 and
    /// 109 — cannot happen from a layout that is wrong anywhere before it.
    /// </remarks>
    [Fact]
    public void Each_records_index_matches_its_filename()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        var monsters = FruaCharacter.ReadAll(design);

        Assert.Equal(4, monsters.Count);

        foreach (var (number, monster) in monsters)
        {
            Assert.Equal(number, monster.MonsterIndex);
            Assert.False(string.IsNullOrWhiteSpace(monster.Name),
                         $"MONST{number} has no name");
        }
    }

    /// <summary>The four monsters of <c>HEIRS.DSN</c>, by name.</summary>
    [Fact]
    public void The_shipped_monsters_read_with_real_names()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        var monsters = FruaCharacter.ReadAll(design);

        Assert.Equal("Khulzond", monsters[101].Name);
        Assert.Equal("mordroka", monsters[102].Name);
        Assert.Equal("keremish", monsters[108].Name);
        Assert.Equal("xelez-dar", monsters[109].Name);

        // AC 1 for the three heavyweights, from a stored 59.
        Assert.Equal(1, monsters[101].ArmourClass);
        Assert.Equal(14, monsters[101].Level);
        Assert.Equal(63, monsters[101].HitPoints);
    }

    /// <summary>
    /// One of the four is not a monster at all.
    /// </summary>
    /// <remarks>
    /// <c>xelez-dar</c> has race 0, so the reference sends it down <c>ProcessNpcCchData</c> and it
    /// becomes a character rather than a monster-database entry. A file called <c>MONST109.DAT</c>
    /// holding an NPC is exactly the kind of thing a name-based assumption would get wrong.
    /// </remarks>
    [Fact]
    public void A_MONST_file_does_not_always_hold_a_monster()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        var monsters = FruaCharacter.ReadAll(design);

        Assert.True(monsters[101].IsMonster);
        Assert.True(monsters[102].IsMonster);
        Assert.True(monsters[108].IsMonster);
        Assert.False(monsters[109].IsMonster);

        // And its adjusted hit points differ from its rolled ones, as a con bonus would do.
        Assert.Equal(27, monsters[109].HitPoints);
        Assert.Equal(34, monsters[109].AdjustedHitPoints);
    }

    /// <summary>Every shipped file is 450 bytes, of which the reference reads 432.</summary>
    [Fact]
    public void The_files_are_longer_than_the_struct_that_reads_them()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        foreach (var (name, path) in FruaFiles.Index(design))
        {
            if (name.StartsWith("MONST", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(FruaCharacter.FileLength, new FileInfo(path).Length);
            }
        }

        Assert.Equal(450, FruaCharacter.FileLength);
        Assert.Equal(432, FruaCharacter.Length);
    }
}
