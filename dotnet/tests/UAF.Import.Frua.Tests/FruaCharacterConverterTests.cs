using UAF.Import.Frua;
using UAF.Serialization;
using static UAF.Import.Frua.FruaMonsterTraits;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Turning a <c>MONST###.DAT</c> into a UAF monster or a UAF character.
/// </summary>
/// <remarks>
/// <c>HEIRS.DSN</c> is the witness because it ships one of each: three monsters and, in
/// <c>MONST109.DAT</c>, an NPC that takes the other branch.
/// </remarks>
public class FruaCharacterConverterTests
{
    private static IReadOnlyDictionary<int, FruaCharacter>? Creatures()
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
        return Directory.Exists(design) ? FruaCharacter.ReadAll(design) : null;
    }

    /// <summary>The three monsters take the monster path and keep their identity.</summary>
    [Fact]
    public void A_monster_keeps_its_name_armour_class_and_hit_dice()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        var source = creatures[101];
        var monster = FruaCharacterConverter.ToMonster(source);

        Assert.Equal("Khulzond", monster.Name);
        Assert.Equal(1, monster.ArmorClass);
        Assert.Equal(14f, monster.HitDice);
        Assert.Equal(1, monster.UseHitDice);
        Assert.Equal(source.Move, monster.Movement);
        Assert.Equal(source.Abilities.Intelligence, monster.Intelligence);
    }

    /// <summary>
    /// Both THAC0s undo the same <c>60 - x</c> storage the armour class does.
    /// </summary>
    /// <remarks>
    /// This is the check that catches a raw byte reaching the record: a stored THAC0 for a
    /// dangerous monster is a large number, and passing it through unchanged gives a creature
    /// that can never hit anything.
    /// </remarks>
    [Fact]
    public void Thac0_is_decoded_rather_than_carried_raw()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        foreach (var source in creatures.Values)
        {
            Assert.Equal(60 - source.Thac0Raw, source.Thac0);

            if (source.IsMonster)
            {
                Assert.Equal(source.Thac0, FruaCharacterConverter.ToMonster(source).Thac0);
            }

            // A THAC0 outside 1..30 would mean the offset is wrong, not that the design is odd.
            Assert.InRange(source.Thac0, 1, 30);
        }
    }

    /// <summary>
    /// A monster gets one attack per pair of attacks-per-two-rounds, never zero.
    /// </summary>
    [Fact]
    public void The_attack_list_repeats_one_attack()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        foreach (var source in creatures.Values.Where(c => c.IsMonster))
        {
            var monster = FruaCharacterConverter.ToMonster(source);

            Assert.Equal(Math.Max(source.AttacksPerTwoRounds / 2, 1), monster.Attacks.Count);
            Assert.NotEmpty(monster.Attacks);

            foreach (var attack in monster.Attacks)
            {
                Assert.Equal(source.DamageDiceSides, attack.Sides);
                Assert.Equal(source.DamageDiceCount, attack.Nbr);
                Assert.Equal(source.DamageBonus, attack.Bonus);
                Assert.Equal("attacks", attack.AttackMessage);
            }
        }
    }

    /// <summary>Morale is masked, and a stored zero becomes fifty rather than none.</summary>
    [Fact]
    public void Morale_is_masked_and_never_zero()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        foreach (var source in creatures.Values)
        {
            Assert.InRange(source.MoraleValue, 1, 127);
            Assert.Equal(source.MoraleValue,
                         source.IsMonster
                             ? FruaCharacterConverter.ToMonster(source).Morale
                             : FruaCharacterConverter.ToCharacter(source).Morale);
        }
    }

    /// <summary>Nothing imports as small, and the forced-large bit lands in the form flags.</summary>
    [Fact]
    public void Size_is_medium_or_large_and_forced_large_is_a_form_flag()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        foreach (var source in creatures.Values.Where(c => c.IsMonster))
        {
            var monster = FruaCharacterConverter.ToMonster(source);

            Assert.NotEqual((int)Size.Small, monster.Size);
            Assert.Equal(source.Size is >= 1 and <= 3 ? (int)Size.Medium : (int)Size.Large,
                         monster.Size);
            Assert.Equal(source.ForcedLarge,
                         ((Form)monster.FormType & Form.Large) != 0);
        }
    }

    /// <summary>
    /// The fourteen FRUA trait bits are spread across four unrelated engine fields.
    /// </summary>
    /// <remarks>
    /// Checked exhaustively against synthetic flag bytes rather than against the shipped monsters,
    /// which between them do not set every bit.
    /// </remarks>
    [Theory]
    // specAbFlags
    [InlineData(1, 0, 0u, 0u, 0u, (uint)MiscOptions.AffectedByDispelEvil)]
    [InlineData(2, 0, (uint)Form.Mammal, 0u, 0u, 0u)]
    [InlineData(4, 0, 0u, (uint)Penalty.DwarfArmorClass, 0u, 0u)]
    [InlineData(8, 0, 0u, (uint)Penalty.RangerDamage, 0u, 0u)]
    [InlineData(16, 0, (uint)Form.Snake, 0u, 0u, 0u)]
    [InlineData(32, 0, 0u, (uint)Penalty.GnomeArmorClass, 0u, 0u)]
    [InlineData(64, 0, (uint)Form.Animal, 0u, 0u, 0u)]
    [InlineData(128, 0, 0u, (uint)Penalty.DwarfThac0, 0u, 0u)]
    // specAbFlags2
    [InlineData(0, 1, (uint)Form.Giant, 0u, 0u, 0u)]
    [InlineData(0, 2, 0u, 0u, 0u, (uint)MiscOptions.CanBeHeldCharmed)]
    [InlineData(0, 4, 0u, (uint)Penalty.GnomeThac0, 0u, 0u)]
    [InlineData(0, 8, 0u, 0u, (uint)Immunity.Death, 0u)]
    [InlineData(0, 16, 0u, 0u, (uint)Immunity.Poison, 0u)]
    [InlineData(0, 32, 0u, 0u, (uint)Immunity.Vorpal, 0u)]
    [InlineData(0, 64, 0u, 0u, (uint)Immunity.Confusion, 0u)]
    public void Each_trait_bit_lands_in_its_own_field(
        int flags, int flags2, uint form, uint penalty, uint immunity, uint misc)
    {
        var monster = FruaCharacterConverter.ToMonster(
            Synthetic() with
            {
                SpecialAbilityFlags = (byte)flags,
                SpecialAbilityFlags2 = (byte)flags2,
            });

        Assert.Equal(form, monster.FormType);
        Assert.Equal(penalty, monster.PenaltyType);
        Assert.Equal(immunity, monster.ImmunityType);
        Assert.Equal(misc, monster.MiscOptionsType);
    }

    /// <summary>A monster's undead type is guessed from its name, not read.</summary>
    [Theory]
    [InlineData("Skeleton Warrior", "Skeleton")]
    [InlineData("giant ZOMBIE", "Zombie")]
    [InlineData("Ancient Lich", "Lich")]
    [InlineData("Bone Golem", "")]
    [InlineData("Khulzond", "")]
    // Capitalised here where the reference writes it lowercase -- see UndeadFromName.
    [InlineData("Barrow Wight", "Wight")]
    public void The_undead_type_comes_from_the_name(string name, string expected)
    {
        Assert.Equal(expected, FruaCharacterConverter.UndeadFromName(name));
        Assert.Equal(expected, FruaCharacterConverter.ToMonster(
            Synthetic() with { Name = name }).UndeadType);
    }

    /// <summary>The one non-monster in the design takes the character path.</summary>
    [Fact]
    public void The_npc_becomes_a_character_rather_than_a_monster()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        var source = creatures[109];
        Assert.False(source.IsMonster);

        var npc = FruaCharacterConverter.ToCharacter(source);

        Assert.Equal("xelez-dar", npc.Name);
        Assert.Equal(FruaCharacterConverter.NpcType, npc.Type);

        // Race 0 is Elf, which is what puts this record on the character path at all.
        Assert.Equal("Elf", npc.Race);
        Assert.Equal(source.ArmourClass, npc.ArmorClass);
        Assert.Equal(source.AdjustedHitPoints, npc.HitPoints);
        Assert.Equal(source.MaxHitPoints, npc.MaxHitPoints);
        Assert.Equal(source.Abilities.Strength, npc.Abilities.Strength);
        Assert.Equal(source.Abilities.Charisma, npc.Abilities.Charisma);
    }

    /// <summary>
    /// FRUA's alignment ordinals are permuted into the engine's.
    /// </summary>
    /// <remarks>
    /// The identity mapping would pass for lawful-good, true-neutral and chaotic-evil and corrupt
    /// the other six, so all nine are named.
    /// </remarks>
    [Theory]
    [InlineData(0, 0)]  // Lawful Good
    [InlineData(1, 3)]  // Lawful Neutral
    [InlineData(2, 6)]  // Lawful Evil
    [InlineData(3, 1)]  // Neutral Good
    [InlineData(4, 4)]  // True Neutral
    [InlineData(5, 7)]  // Neutral Evil
    [InlineData(6, 2)]  // Chaotic Good
    [InlineData(7, 5)]  // Chaotic Neutral
    [InlineData(8, 8)]  // Chaotic Evil
    public void Alignment_is_permuted(byte frua, int engine) =>
        Assert.Equal(engine,
                     FruaCharacterConverter.ToCharacter(
                         Synthetic() with { Alignment = frua }).Alignment);

    /// <summary>The status ordinals are permuted too.</summary>
    [Theory]
    [InlineData(0, 0)]  // Okay
    [InlineData(1, 6)]  // Animated
    [InlineData(2, 7)]  // Temporarily gone
    [InlineData(3, 8)]  // Running
    [InlineData(4, 1)]  // Unconscious
    [InlineData(5, 9)]  // Dying
    [InlineData(6, 2)]  // Dead
    [InlineData(7, 4)]  // Petrified
    [InlineData(8, 5)]  // Gone
    public void Status_is_permuted(byte frua, int engine) =>
        Assert.Equal(engine,
                     FruaCharacterConverter.ToCharacter(
                         Synthetic() with { Status = frua }).Status);

    /// <summary>Every baseclass gets a record, including the ones at level zero.</summary>
    [Fact]
    public void All_seven_baseclasses_are_present()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        var npc = FruaCharacterConverter.ToCharacter(creatures[109]);

        Assert.Equal(7, npc.BaseclassStats.Count);
        Assert.Equal(
            ["Fighter", "Cleric", "Ranger", "Paladin", "MagicUser", "Thief", "Druid"],
            npc.BaseclassStats.Select(b => b.BaseclassId));

        // Druid has no FRUA slot to read, so it is always zero.
        Assert.Equal(0, npc.BaseclassStats[^1].CurrentLevel);
    }

    /// <summary>
    /// The class levels come from the right slots, which the file orders differently.
    /// </summary>
    /// <remarks>
    /// The file's order is cleric, knight, fighter, paladin, ranger, mage, thief — so fighter is
    /// slot 2 and cleric is slot 0, not the other way round.
    /// </remarks>
    [Fact]
    public void The_class_levels_come_from_the_right_slots()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        var source = creatures[109];
        var npc = FruaCharacterConverter.ToCharacter(source);

        Assert.Equal(source.ClassLevels[2], npc.BaseclassStats[0].CurrentLevel);  // Fighter
        Assert.Equal(source.ClassLevels[0], npc.BaseclassStats[1].CurrentLevel);  // Cleric
        Assert.Equal(source.ClassLevels[4], npc.BaseclassStats[2].CurrentLevel);  // Ranger
        Assert.Equal(source.ClassLevels[3], npc.BaseclassStats[3].CurrentLevel);  // Paladin
        Assert.Equal(source.ClassLevels[5], npc.BaseclassStats[4].CurrentLevel);  // MagicUser
        Assert.Equal(source.ClassLevels[6], npc.BaseclassStats[5].CurrentLevel);  // Thief
    }

    /// <summary>
    /// Every creature in the design converts without throwing, down its own branch.
    /// </summary>
    [Fact]
    public void Every_shipped_creature_converts()
    {
        if (Creatures() is not { } creatures)
        {
            return;
        }

        int monsters = 0;
        int npcs = 0;

        foreach (var source in creatures.Values)
        {
            if (source.IsMonster)
            {
                Assert.False(string.IsNullOrEmpty(FruaCharacterConverter.ToMonster(source).Name));
                monsters++;
            }
            else
            {
                Assert.False(string.IsNullOrEmpty(FruaCharacterConverter.ToCharacter(source).Name));
                npcs++;
            }
        }

        Assert.Equal(3, monsters);
        Assert.Equal(1, npcs);
    }

    /// <summary>
    /// A blank creature, for the cases where a shipped one does not set the bit under test.
    /// </summary>
    private static FruaCharacter Synthetic() =>
        new(Name: "test", MonsterIndex: 1, Race: 6, CombatMode: 1,
            Experience: 0, Platinum: 0, Gems: 0, Jewelry: 0,
            Level: 1, ArmourClassRaw: 50, HitPoints: 8, AdjustedHitPoints: 8,
            Move: 12, AdjustedMove: 12, Morale: 0, AttacksPerTwoRounds: 2,
            DamageDiceCount: 1, DamageDiceSides: 6, DamageBonus: 0,
            SavingThrows: new byte[5], ClassLevels: new byte[7],
            ItemsCarried: new byte[16], ItemQuantities: new byte[16],
            CharClass: 2, Undead: 0, Gender: 0, Alignment: 4, Status: 0,
            Thac0Raw: 40, AdjustedThac0Raw: 40, AdjustedArmourClassRaw: 50,
            SizeRaw: 1, MaxHitPoints: 8, MaxCureDisease: 0, MagicResistance: 0,
            ReadyToTrain: 0, UniquePartyId: 8, IconId: 0,
            SpecialAbilityFlags: 0, SpecialAbilityFlags2: 0,
            Age: 20, ExperienceValue: 10, Encumbrance: 0,
            Abilities: new FruaAbilities(10, 10, 10, 10, 10, 10, 0),
            ClassLevelsPreDrain: new byte[7], ClassLevelsPreClassChange: new byte[7]);
}
