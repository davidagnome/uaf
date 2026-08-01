using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers <see cref="ToHit"/>.</summary>
public class ToHitTests
{
    [Fact]
    public void The_target_number_is_thac0_minus_the_armour_class()
    {
        // A THAC0 of 20 against an unarmoured 10 needs a 10.
        Assert.Equal(10, ToHit.TargetNumber(attackerThac0: 20, targetArmorClass: 10));

        // Better armour is a lower number, and subtracting it raises the target.
        Assert.Equal(22, ToHit.TargetNumber(attackerThac0: 20, targetArmorClass: -2));
    }

    [Fact]
    public void Bonuses_lower_the_target_because_they_are_subtracted()
    {
        Assert.Equal(7, ToHit.TargetNumber(20, 10, environmentalBonus: 2, weaponBonus: 1));
    }

    [Fact]
    public void Equalling_the_target_hits()
    {
        // The test is >=, so a roll exactly on the number lands.
        Assert.True(ToHit.Hits(roll: 10, targetNumber: 10));
        Assert.False(ToHit.Hits(roll: 9, targetNumber: 10));
    }

    [Fact]
    public void There_is_no_natural_twenty_rule()
    {
        // A 20 is just a high roll here. The special treatment a 20 gets elsewhere is a vorpal
        // special ability, not an automatic hit -- so an impossible target stays impossible.
        Assert.False(ToHit.Hits(roll: 20, targetNumber: 21));

        // ...and a 1 is not an automatic miss either.
        Assert.True(ToHit.Hits(roll: 1, targetNumber: 0));
    }

    [Fact]
    public void An_absurdly_favourable_attack_collapses_to_zero_rather_than_the_floor()
    {
        // The reference tests `< MIN_THAC0` and then assigns 0, not the constant -- so the result
        // is "any roll hits" rather than -500. Clamping to the floor would look tidier and be wrong.
        int target = ToHit.TargetNumber(attackerThac0: 0, targetArmorClass: 600);

        Assert.Equal(0, target);
        Assert.True(ToHit.Hits(roll: 1, targetNumber: target));
    }

    [Fact]
    public void A_target_just_above_the_floor_is_kept_as_it_is()
    {
        // -500 itself is not below the floor, so it survives.
        Assert.Equal(ToHit.MinimumThac0, ToHit.TargetNumber(0, -ToHit.MinimumThac0));
    }

    [Fact]
    public void Resolve_puts_the_whole_chain_together()
    {
        // A level-10 fighter (THAC0 11) against plate mail (AC 3) needs an 8.
        Assert.True(ToHit.Resolve(roll: 8, attackerThac0: 11, targetArmorClass: 3));
        Assert.False(ToHit.Resolve(roll: 7, attackerThac0: 11, targetArmorClass: 3));

        // ...and a magic sword makes it a 7.
        Assert.True(ToHit.Resolve(roll: 7, attackerThac0: 11, targetArmorClass: 3, weaponBonus: 1));
    }

    [Fact]
    public void Damage_adds_the_weapon_and_strength_bonuses()
    {
        Assert.Equal(9, ToHit.Damage(rolledDice: 6, weaponBonus: 1, strengthBonus: 2));
        Assert.Equal(6, ToHit.Damage(rolledDice: 6));
    }

    [Fact]
    public void Damage_floors_at_one_rather_than_zero()
    {
        // `if (m_damage <= 0) m_damage = 1` -- an attack that lands always does something, however
        // large the penalty.
        Assert.Equal(1, ToHit.Damage(rolledDice: 1, strengthBonus: -4));
        Assert.Equal(1, ToHit.Damage(rolledDice: 1, strengthBonus: -99));
        Assert.Equal(1, ToHit.Damage(rolledDice: 0));
    }

    [Fact]
    public void A_missile_carries_no_strength_behind_it()
    {
        // The caller passes zero for a missile -- this pins the arithmetic that follows from it.
        Assert.Equal(7, ToHit.Damage(rolledDice: 6, weaponBonus: 1, strengthBonus: 0));
    }
}
