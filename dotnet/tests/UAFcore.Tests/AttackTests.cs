using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers damage dice, ammunition consumption and attack resolution
/// (<c>GetDamageDice</c>, <c>WpnConsumesAmmoAtRange</c>, <c>giveCharacterDamage</c>).
/// </summary>
public class AttackTests
{
    private static CombatMap OpenMap()
    {
        var map = new CombatMap(25, 25);
        map.FillHoles();
        map.CombatantCount = 8;
        return map;
    }

    private static Combatant Fighter(int index, bool friendly, int x, int y) =>
        new(index, friendly, new CombatantIcon(1, 1), $"c{index}")
        {
            X = x,
            Y = y,
            Kind = friendly ? CombatantKind.Character : CombatantKind.Monster,
            AvailableAttacks = 1,
        };

    /// <summary>A roller that returns a fixed sequence, so an attack has a known outcome.</summary>
    private static Func<int, int> Rolls(params int[] values)
    {
        int at = 0;
        return _ => values[Math.Min(at++, values.Length - 1)];
    }

    // ---- damage dice -----------------------------------------------------------------------

    [Fact]
    public void A_weapon_rolls_different_dice_by_target_size()
    {
        var sword = new WeaponDamage(
            AgainstSmall: new DamageRoll(1, 8, 0),
            AgainstLarge: new DamageRoll(1, 12, 0));

        Assert.Equal(new DamageRoll(1, 8, 0), DamageDice.ForWeapon(sword, targetIsLarge: false));
        Assert.Equal(new DamageRoll(1, 12, 0), DamageDice.ForWeapon(sword, targetIsLarge: true));
    }

    [Fact]
    public void A_weapons_to_hit_bonus_is_added_to_its_damage_as_well()
    {
        // One field doing two jobs: a +1 sword both lands more often and hits harder.
        var magicSword = new WeaponDamage(
            AgainstSmall: new DamageRoll(1, 8, 0),
            AgainstLarge: new DamageRoll(1, 12, 0),
            AttackBonus: 1);

        var roll = DamageDice.ForWeapon(magicSword, targetIsLarge: false, damageBonus: 2);
        Assert.Equal(3, roll.Bonus);        // 0 size bonus + 2 wielder + 1 attack bonus
    }

    [Fact]
    public void Unarmed_damage_drops_the_unarmed_bonus_against_large_targets()
    {
        // No comment and no obvious reason in the reference: the large branch uses the adjusted
        // bonus alone and silently discards the combatant's own unarmed bonus.
        var fists = new UnarmedDamage(CountSmall: 1, SidesSmall: 2, BonusSmall: 3,
                                      CountLarge: 1, SidesLarge: 3);

        Assert.Equal(new DamageRoll(1, 2, 4),
                     DamageDice.ForUnarmed(fists, targetIsLarge: false, damageBonus: 1));

        Assert.Equal(new DamageRoll(1, 3, 1),
                     DamageDice.ForUnarmed(fists, targetIsLarge: true, damageBonus: 1));
    }

    [Fact]
    public void A_monster_rolls_its_attacks_in_order_as_its_allowance_drains()
    {
        // Which attack is being made is inferred from what is left, not tracked: index is
        // totalAttacks - availableAttacks.
        var claws = new List<DamageRoll>
        {
            new(1, 4, 0), new(1, 4, 0), new(2, 6, 1),
        };
        var fists = new UnarmedDamage(1, 2, 0, 1, 2);

        Assert.Equal(new DamageRoll(1, 4, 0),
                     DamageDice.ForMonster(claws, 3, 3, fists));
        Assert.Equal(new DamageRoll(1, 4, 0),
                     DamageDice.ForMonster(claws, 3, 2, fists));
        Assert.Equal(new DamageRoll(2, 6, 1),
                     DamageDice.ForMonster(claws, 3, 1, fists));
    }

    [Fact]
    public void A_monsters_own_attack_takes_no_adjusted_damage_bonus()
    {
        // Unlike every other branch. The same monster falling back to unarmed dice does get it,
        // which is what makes this worth pinning.
        var claws = new List<DamageRoll> { new(1, 6, 2) };
        var fists = new UnarmedDamage(1, 2, 0, 1, 3);

        Assert.Equal(2, DamageDice.ForMonster(claws, 1, 1, fists, damageBonus: 5).Bonus);
        Assert.Equal(5, DamageDice.ForMonster([], 1, 1, fists, damageBonus: 5).Bonus);
    }

    [Fact]
    public void A_monster_with_no_attacks_falls_back_to_the_large_dice_whatever_the_target()
    {
        var fists = new UnarmedDamage(CountSmall: 1, SidesSmall: 2, BonusSmall: 9,
                                      CountLarge: 1, SidesLarge: 3);

        var roll = DamageDice.ForMonster([], 1, 1, fists);
        Assert.Equal(1, roll.Count);
        Assert.Equal(3, roll.Sides);        // the large dice
    }

    [Fact]
    public void An_out_of_range_attack_index_falls_back_to_the_first()
    {
        var claws = new List<DamageRoll> { new(1, 4, 0), new(1, 6, 0) };
        var fists = new UnarmedDamage(1, 2, 0, 1, 2);

        // availableAttacks above the total, so the index goes negative.
        Assert.Equal(new DamageRoll(1, 4, 0), DamageDice.ForMonster(claws, 2, 5, fists));
        // ...and past the end.
        Assert.Equal(new DamageRoll(1, 4, 0), DamageDice.ForMonster(claws, 9, 0, fists));
    }

    // ---- ammunition ------------------------------------------------------------------------

    [Theory]
    [InlineData(WeaponClass.Bow, 5, true)]
    [InlineData(WeaponClass.Crossbow, 5, true)]
    [InlineData(WeaponClass.SlingNoAmmo, 5, false)]
    [InlineData(WeaponClass.HandBlunt, 1, false)]
    [InlineData(WeaponClass.HandThrow, 1, false)]    // stabbing with it keeps it
    [InlineData(WeaponClass.HandThrow, 3, true)]     // throwing it does not
    [InlineData(WeaponClass.Throw, 1, false)]
    [InlineData(WeaponClass.Throw, 3, true)]
    public void Ammunition_is_spent_by_class_and_distance(WeaponClass cls, int range,
                                                          bool expected)
    {
        Assert.Equal(expected, WeaponRange.ConsumesAmmoAt(cls, range));
    }

    [Theory]
    [InlineData(WeaponClass.Bow, false)]
    [InlineData(WeaponClass.Crossbow, false)]
    [InlineData(WeaponClass.HandThrow, true)]
    [InlineData(WeaponClass.Throw, true)]
    [InlineData(WeaponClass.SpellCaster, true)]
    [InlineData(WeaponClass.HandBlunt, false)]
    public void Some_weapons_are_their_own_ammunition(WeaponClass cls, bool expected)
    {
        // The two questions are different: a wand has no quiver and spends itself; a bow is the
        // mirror image.
        Assert.Equal(expected, WeaponRange.ConsumesSelfAsAmmo(cls));
    }

    // ---- resolution ------------------------------------------------------------------------

    [Fact]
    public void A_hit_rolls_damage_and_spends_the_attack()
    {
        var attacker = Fighter(0, true, 5, 5);
        var target = Fighter(1, false, 6, 5);
        var sword = new ReadiedWeapon(WeaponClass.HandCutting, Range: 1);

        // d20 = 15, then d8 = 6.
        var result = Attack.Resolve(attacker, target, OpenMap(), Rolls(15, 6),
                                    new DamageRoll(1, 8, 1), sword,
                                    attackerThac0: 18, targetArmorClass: 5, strengthBonus: 2);

        Assert.True(result.Happened);
        Assert.True(result.Hit);
        Assert.Equal(13, result.TargetNumber);      // 18 - 5
        Assert.Equal(9, result.Damage);             // 6 + 1 bonus + 2 strength
        Assert.Equal(0, attacker.AvailableAttacks);
        Assert.Equal(target.Index, attacker.LastAttacked);
        Assert.Equal(attacker.Index, target.LastAttacker);
    }

    [Fact]
    public void A_miss_does_no_damage_but_still_costs_the_attack()
    {
        var attacker = Fighter(0, true, 5, 5);
        var target = Fighter(1, false, 6, 5);
        var sword = new ReadiedWeapon(WeaponClass.HandCutting, Range: 1);

        var result = Attack.Resolve(attacker, target, OpenMap(), Rolls(2),
                                    new DamageRoll(1, 8, 0), sword,
                                    attackerThac0: 18, targetArmorClass: 5);

        Assert.True(result.Happened);
        Assert.False(result.Hit);
        Assert.Equal(0, result.Damage);
        Assert.Equal(0, attacker.AvailableAttacks);
    }

    [Fact]
    public void Equalling_the_target_number_hits()
    {
        var attacker = Fighter(0, true, 5, 5);
        var target = Fighter(1, false, 6, 5);
        var sword = new ReadiedWeapon(WeaponClass.HandCutting, Range: 1);

        var result = Attack.Resolve(attacker, target, OpenMap(), Rolls(13, 4),
                                    new DamageRoll(1, 8, 0), sword,
                                    attackerThac0: 18, targetArmorClass: 5);

        Assert.True(result.Hit);
    }

    [Fact]
    public void An_attack_that_is_not_allowed_is_refused_before_any_dice_are_rolled()
    {
        var attacker = Fighter(0, true, 5, 5);
        var target = Fighter(1, false, 20, 5);          // far out of a sword's reach
        var sword = new ReadiedWeapon(WeaponClass.HandCutting, Range: 1);

        var result = Attack.Resolve(attacker, target, OpenMap(),
                                    _ => throw new InvalidOperationException("rolled anyway"),
                                    new DamageRoll(1, 8, 0), sword);

        Assert.False(result.Happened);
        Assert.Equal(AttackRefusal.OutOfWeaponRange, result.Refusal);
        Assert.Equal(1, attacker.AvailableAttacks);     // and the attack was not spent
    }

    [Fact]
    public void An_arrow_that_misses_is_still_gone()
    {
        // Ammunition is spent on the swing, not on the hit.
        var attacker = Fighter(0, true, 5, 5);
        var target = Fighter(1, false, 10, 5);
        var bow = new ReadiedWeapon(WeaponClass.Bow, Range: 12, AmmoClass: "arrow");
        var arrows = new ReadiedAmmo(WeaponClass.Ammo, 12, "arrow");

        var result = Attack.Resolve(attacker, target, OpenMap(), Rolls(1),
                                    new DamageRoll(1, 6, 0), bow, arrows);

        Assert.False(result.Hit);
        Assert.True(result.AmmoSpent);
        Assert.False(result.WeaponSpent);
    }

    [Fact]
    public void A_thrown_dagger_is_spent_but_a_stabbing_one_is_not()
    {
        var dagger = new ReadiedWeapon(WeaponClass.HandThrow, Range: 6);

        var thrower = Fighter(0, true, 5, 5);
        var far = Fighter(1, false, 9, 5);
        var thrown = Attack.Resolve(thrower, far, OpenMap(), Rolls(10, 3),
                                    new DamageRoll(1, 4, 0), dagger);
        Assert.True(thrown.AmmoSpent);
        Assert.True(thrown.WeaponSpent);

        var stabber = Fighter(2, true, 5, 5);
        var near = Fighter(3, false, 6, 5);
        var stab = Attack.Resolve(stabber, near, OpenMap(), Rolls(10, 3),
                                  new DamageRoll(1, 4, 0), dagger);
        Assert.False(stab.AmmoSpent);
        Assert.False(stab.WeaponSpent);
    }

    // ---- applying damage -------------------------------------------------------------------

    [Theory]
    [InlineData(10, 3, 7, CharacterStatus.Okay)]
    [InlineData(10, 10, 0, CharacterStatus.Unconscious)]   // exactly zero is unconscious
    [InlineData(10, 15, -5, CharacterStatus.Dying)]        // -1..-9 is dying
    [InlineData(10, 20, -10, CharacterStatus.Dead)]
    [InlineData(10, 500, -10, CharacterStatus.Dead)]       // clamped at the floor
    public void Hit_points_fall_through_three_bands(int start, int damage, int expectedHp,
                                                    CharacterStatus expectedStatus)
    {
        var c = Fighter(0, true, 5, 5);
        int hp = Attack.ApplyDamage(c, start, damage);

        Assert.Equal(expectedHp, hp);
        Assert.Equal(expectedStatus, c.Status);
    }

    [Fact]
    public void Dead_at_zero_collapses_the_bands()
    {
        var c = Fighter(0, true, 5, 5);
        int hp = Attack.ApplyDamage(c, 10, 10, deadAtZero: true);

        Assert.Equal(0, hp);
        Assert.Equal(CharacterStatus.Dead, c.Status);
        Assert.True(c.TurnIsDone);
    }

    [Fact]
    public void A_combatant_that_is_already_out_takes_no_damage_at_all()
    {
        // Damage only lands on okay, running, unconscious, animated or dying. Anything else keeps
        // its hit points unchanged -- a caller assuming otherwise kills things twice.
        foreach (var status in new[]
                 {
                     CharacterStatus.Dead, CharacterStatus.Fled, CharacterStatus.Gone,
                     CharacterStatus.TempGone, CharacterStatus.Petrified,
                 })
        {
            var c = Fighter(0, true, 5, 5);
            c.Status = status;

            Assert.Equal(10, Attack.ApplyDamage(c, 10, 999));
            Assert.Equal(status, c.Status);
        }
    }

    [Fact]
    public void An_unconscious_or_dying_combatant_can_still_be_finished_off()
    {
        var dying = Fighter(0, true, 5, 5);
        dying.Status = CharacterStatus.Dying;

        Assert.Equal(-10, Attack.ApplyDamage(dying, -5, 8));
        Assert.Equal(CharacterStatus.Dead, dying.Status);
    }

    [Fact]
    public void Healing_cannot_take_a_combatant_past_its_maximum()
    {
        // Negative damage heals, and the same clamp bounds it.
        var c = Fighter(0, true, 5, 5);
        Assert.Equal(12, Attack.ApplyDamage(c, 8, -20, maxHitPoints: 12));
        Assert.Equal(CharacterStatus.Okay, c.Status);
    }
}
