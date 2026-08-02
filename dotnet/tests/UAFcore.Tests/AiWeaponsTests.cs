using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers what the AI is told a combatant can attack with.</summary>
public class AiWeaponsTests
{
    private static ItemInstance Carried(string itemId) =>
        new(Key: 1, itemId, LegacyItemId: 0, ReadyLocation: 0, Quantity: 1, Identified: 1,
            Charges: 1, Cursed: 0, Paid: 0);

    private static ItemRecord Item(WeaponClass weapon = WeaponClass.HandCutting,
                                   uint slot = 0, int range = 1, int count = 1, int sides = 8,
                                   int bonus = 0, string spellId = "") =>
        new(new ItemNames(0, spellId, "id", "name", string.Empty, string.Empty, string.Empty),
            HitArt: null, MissileArt: null,
            new ItemScalars(string.Empty, 0, 0, 0, 0, 0, 1, 0),
            new ItemCombat(slot, 1, sides, count, bonus, sides, count, bonus, 1, 0, 0),
            new ItemTail((int)weapon, 0, 0, [], range, 0, 0, string.Empty, string.Empty, 0, 0,
                         null, 0, 0, null!, []));

    private static MonsterRecord Monster(params AttackDetails[] attacks) =>
        new(0, "orc", null, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            Intelligence: 8, ArmorClass: 6, Movement: 9, HitDice: 1, UseHitDice: 1,
            HitDiceBonus: 0, Thac0: 19, Attacks: attacks,
            MagicResistance: 0, Size: 1, ClassId: string.Empty, Morale: 50, ExperienceValue: 15,
            FormType: 0, PenaltyType: 0, ImmunityType: 0, MiscOptionsType: 0,
            UndeadType: string.Empty, SpecialAbilities: null!, Attributes: [],
            Items: null, Money: null);

    private static AttackDetails Attack(int count, int sides, int bonus = 0) =>
        new(sides, count, bonus, string.Empty, string.Empty, 0, 0, 0);

    private static Combatant Orc() =>
        new(0, isFriendly: false, new CombatantIcon(1, 1), "orc");

    // ---- damage estimates ----------------------------------------------------------------------

    [Fact]
    public void A_weapons_damage_estimate_is_ten_times_its_true_average()
    {
        // 1d8 averages 4.5, so 45. 2d6+1 averages 8, so 80.
        Assert.Equal(45, AiWeapons.WeaponDamage(count: 1, sides: 8, bonus: 0));
        Assert.Equal(80, AiWeapons.WeaponDamage(count: 2, sides: 6, bonus: 1));
    }

    [Fact]
    public void A_natural_attacks_estimate_has_its_dice_operands_transposed()
    {
        // 5 * ((1 + nbr) * sides + 2 * bonus), where the weapon path puts the 1 + on the sides.
        // 1d8 comes out at 80 against a true 45.
        Assert.Equal(80, AiWeapons.AttackDamage(count: 1, sides: 8, bonus: 0));
        Assert.NotEqual(AiWeapons.WeaponDamage(1, 8, 0), AiWeapons.AttackDamage(1, 8, 0));
    }

    [Fact]
    public void The_transposition_overrates_few_large_dice_and_underrates_many_small_ones()
    {
        // 1d8 and 3d2 both average 4.5, so both should be 45. The estimate says 80 and 40.
        Assert.Equal(45, AiWeapons.WeaponDamage(1, 8, 0));
        Assert.Equal(45, AiWeapons.WeaponDamage(3, 2, 0));

        Assert.Equal(80, AiWeapons.AttackDamage(1, 8, 0));
        Assert.Equal(40, AiWeapons.AttackDamage(3, 2, 0));
    }

    [Fact]
    public void The_bonus_term_is_right_in_both_only_the_dice_are_swapped()
    {
        // The outer 5 makes 2 * bonus into 10 * bonus, matching the weapon path's own field.
        Assert.Equal(AiWeapons.AttackDamage(1, 8, 0) + 20, AiWeapons.AttackDamage(1, 8, 2));
        Assert.Equal(AiWeapons.WeaponDamage(1, 8, 0) + 20, AiWeapons.WeaponDamage(1, 8, 2));
    }

    // ---- carried weapons -----------------------------------------------------------------------

    [Fact]
    public void Only_items_readied_in_the_weapon_hand_count()
    {
        var orc = Orc();
        orc.Items.Add(Carried("sword"));
        orc.Items.Add(Carried("boots"));

        var weapons = AiWeapons.For(orc, null,
                                    id => id == "sword" ? Item() : Item(slot: 9));

        Assert.Single(weapons);
        Assert.Equal(WeaponClass.HandCutting, weapons[0].Class);
    }

    [Fact]
    public void A_weapon_carries_its_reach_and_its_damage_estimate()
    {
        var orc = Orc();
        orc.Items.Add(Carried("bow"));

        var weapons = AiWeapons.For(orc, null,
                                    _ => Item(WeaponClass.Bow, range: 12, count: 1, sides: 6));

        Assert.Equal(12, weapons[0].Range);
        Assert.Equal(AiWeapons.WeaponDamage(1, 6, 0), weapons[0].AverageDamage);
    }

    [Fact]
    public void A_spell_item_is_marked_only_when_it_names_a_spell()
    {
        var orc = Orc();
        orc.Items.Add(Carried("wand"));

        var withSpell = AiWeapons.For(orc, null,
                                      _ => Item(WeaponClass.SpellCaster, spellId: "sleep"));
        var without = AiWeapons.For(orc, null, _ => Item(WeaponClass.SpellCaster));

        Assert.True(withSpell[0].HasSpell);
        Assert.False(without[0].HasSpell);
    }

    [Fact]
    public void A_combatant_with_no_item_database_has_no_weapons()
    {
        var orc = Orc();
        orc.Items.Add(Carried("sword"));

        Assert.Empty(AiWeapons.For(orc, null));
    }

    // ---- natural attacks -----------------------------------------------------------------------

    [Fact]
    public void A_monsters_attacks_are_counted_and_the_best_reported()
    {
        var (count, best) = AiWeapons.NaturalAttacks(
            Monster(Attack(1, 4), Attack(1, 8), Attack(2, 3)));

        Assert.Equal(3, count);
        Assert.Equal(AiWeapons.AttackDamage(1, 8, 0), best);
    }

    [Fact]
    public void A_monster_with_no_record_or_no_attacks_has_none()
    {
        Assert.Equal((0, 0), AiWeapons.NaturalAttacks(null));
        Assert.Equal((0, 0), AiWeapons.NaturalAttacks(Monster()));
    }
}
