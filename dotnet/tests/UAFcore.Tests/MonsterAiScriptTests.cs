using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the monster AI's priority ordering, as the shipped script expresses it.</summary>
public class MonsterAiScriptTests
{
    private static Combatant Monster(CharacterStatus status = CharacterStatus.Okay) =>
        new(0, isFriendly: false, new CombatantIcon(1, 1), "orc") { Status = status };

    private static Combatant Hero(CharacterStatus status = CharacterStatus.Okay) =>
        new(1, isFriendly: true, new CombatantIcon(1, 1), "hero") { Status = status };

    private static AiAction Action(AiActionType type, int damage = 0, int distance = 1,
                                   WeaponClass weapon = WeaponClass.NotWeapon) =>
        new(type, Target: 1, weapon, damage, distance);

    // ---- who is worth attacking ----------------------------------------------------------------

    [Fact]
    public void An_enemy_in_good_health_is_worth_attacking()
    {
        Assert.True(MonsterAiScript.IsWorthAttacking(Monster(), Hero()));
    }

    [Fact]
    public void The_casters_own_side_is_refused_whatever_its_condition()
    {
        // Friendly? ?EXIT comes first, so it short-circuits everything after it.
        var ally = new Combatant(2, isFriendly: false, new CombatantIcon(1, 1), "other orc");

        Assert.False(MonsterAiScript.IsWorthAttacking(Monster(), ally));
    }

    [Theory]
    [InlineData(CharacterStatus.Gone)]
    [InlineData(CharacterStatus.Dead)]
    [InlineData(CharacterStatus.Petrified)]
    public void The_gone_the_dead_and_the_petrified_are_left_alone(CharacterStatus status)
    {
        Assert.False(MonsterAiScript.IsWorthAttacking(Monster(), Hero(status)));
    }

    [Fact]
    public void Whether_the_dying_are_attacked_is_the_one_difference_between_the_two_scripts()
    {
        // 1.01 added Dying? to FGDP?; 0.999785 lacks it, so an older design's monsters keep
        // hitting somebody who is bleeding out.
        var dying = Hero(CharacterStatus.Dying);

        Assert.False(MonsterAiScript.IsWorthAttacking(Monster(), dying));
        Assert.True(MonsterAiScript.IsWorthAttacking(Monster(), dying, attacksTheDying: true));
    }

    [Fact]
    public void An_unconscious_target_is_still_attacked()
    {
        // Not in the filter at all -- only Gone, Dead, Dying and Petrified are named.
        Assert.True(MonsterAiScript.IsWorthAttacking(Monster(),
                                                     Hero(CharacterStatus.Unconscious)));
    }

    // ---- the distance measure ------------------------------------------------------------------

    /// <summary>The script's own unit: 4 * (dx^2 + dy^2) between the nearest footprint edges.</summary>
    private static int D22(int squares) => 4 * squares * squares;

    [Fact]
    public void Distance_is_doubled_and_squared_between_the_nearest_edges()
    {
        var a = new Combatant(0, false, new CombatantIcon(1, 1), "a") { X = 0, Y = 0 };
        var b = new Combatant(1, true, new CombatantIcon(1, 1), "b") { X = 3, Y = 0 };

        Assert.Equal(D22(3), MonsterAiScript.DistanceBetween(a, b));
    }

    [Fact]
    public void Touching_footprints_are_no_distance_apart()
    {
        var big = new Combatant(0, false, new CombatantIcon(2, 2), "big") { X = 0, Y = 0 };
        var next = new Combatant(1, true, new CombatantIcon(1, 1), "next") { X = 1, Y = 1 };

        Assert.Equal(0, MonsterAiScript.DistanceBetween(big, next));
    }

    [Fact]
    public void A_large_monster_measures_from_its_nearest_edge()
    {
        // A 2x2 at the origin reaches x=1, so a target at x=4 is three squares off its edge, not
        // four off its corner.
        var big = new Combatant(0, false, new CombatantIcon(2, 2), "big") { X = 0, Y = 0 };
        var far = new Combatant(1, true, new CombatantIcon(1, 1), "far") { X = 4, Y = 0 };

        Assert.Equal(D22(3), MonsterAiScript.DistanceBetween(big, far));
    }

    [Fact]
    public void A_reach_of_one_is_melee_and_anything_longer_is_ranged()
    {
        // Nine is (2*1+1)^2 -- exactly a reach of one -- and the test excludes it.
        Assert.False(MonsterAiScript.IsRangedWeapon(1));
        Assert.True(MonsterAiScript.IsRangedWeapon(2));
    }

    [Fact]
    public void A_weapons_reach_uses_a_different_transform_from_a_distance()
    {
        // A distance is (2d)^2; a range is (2r+1)^2 -- half a square longer before squaring. They
        // are compared directly anyway, and the half-square is what makes a reach of r cover a
        // distance of exactly r.
        Assert.Equal(9, MonsterAiScript.WeaponRange22(1));
        Assert.Equal(25, MonsterAiScript.WeaponRange22(2));
        Assert.Equal(4, D22(1));
    }

    [Fact]
    public void A_reach_of_r_covers_a_distance_of_exactly_r()
    {
        for (int r = 1; r <= 6; r++)
        {
            Assert.True(MonsterAiScript.WeaponRange22(r) >= D22(r),
                        $"reach {r} should cover distance {r}");
            Assert.True(MonsterAiScript.WeaponRange22(r) < D22(r + 1),
                        $"reach {r} should not cover distance {r + 1}");
        }
    }

    [Fact]
    public void A_reach_above_ninety_is_effectively_unlimited_rather_than_its_square()
    {
        // The clamp comes first, so the formula's 32761 is never produced.
        Assert.Equal(MonsterAiScript.UnlimitedRange,
                     MonsterAiScript.WeaponRange22(MonsterAiScript.UnlimitedRangeAbove + 1));
        Assert.Equal(181 * 181, MonsterAiScript.WeaponRange22(90));
    }

    // ---- the filters ---------------------------------------------------------------------------

    [Fact]
    public void A_ranged_weapon_will_not_shoot_an_adjacent_target()
    {
        // TooNear? is `C:Distance 5 <` in distance22 units, so it only holds at one square. A
        // monster with a bow refuses to shoot somebody standing next to it and nothing else.
        var adjacent = Action(AiActionType.RangedWeapon, distance: D22(1));
        var twoAway = Action(AiActionType.RangedWeapon, distance: D22(2));
        var far = Action(AiActionType.RangedWeapon, distance: D22(9));
        int range = D22(5);

        Assert.False(MonsterAiScript.Survives(Monster(), Hero(), adjacent, range));
        Assert.True(MonsterAiScript.Survives(Monster(), Hero(), twoAway, range));
        Assert.False(MonsterAiScript.Survives(Monster(), Hero(), far, range));
    }

    [Fact]
    public void Judo_reaches_the_adjacent_squares_and_no_further()
    {
        // NotAdjacent? is `C:Distance 8 >`, which holds from two squares. A diagonal neighbour has
        // a gap of one on both axes, giving exactly 8 -- inside the reach.
        var beside = Action(AiActionType.Judo, distance: D22(1));
        var diagonal = Action(AiActionType.Judo, distance: 4 * (1 + 1));
        var twoAway = Action(AiActionType.Judo, distance: D22(2));

        Assert.True(MonsterAiScript.Survives(Monster(), Hero(), beside, D22(1)));
        Assert.True(MonsterAiScript.Survives(Monster(), Hero(), diagonal, D22(1)));
        Assert.False(MonsterAiScript.Survives(Monster(), Hero(), twoAway, D22(1)));
    }

    [Fact]
    public void Advancing_ignores_range_entirely()
    {
        // AdvanceFilter checks only the target's condition -- the whole point is to close ground.
        var far = Action(AiActionType.Advance, distance: D22(20));

        Assert.True(MonsterAiScript.Survives(Monster(), Hero(), far, D22(1)));
        Assert.False(MonsterAiScript.Survives(Monster(), Hero(CharacterStatus.Dead), far, D22(1)));
    }

    [Fact]
    public void A_melee_weapon_is_refused_beyond_its_reach()
    {
        var beyond = Action(AiActionType.MeleeWeapon, distance: D22(2));

        Assert.False(MonsterAiScript.Survives(Monster(), Hero(), beyond, D22(1)));
        Assert.True(MonsterAiScript.Survives(Monster(), Hero(),
                                             Action(AiActionType.MeleeWeapon, distance: D22(1)),
                                             D22(1)));
    }

    // ---- the ordering --------------------------------------------------------------------------

    [Fact]
    public void A_spell_caster_item_beats_everything()
    {
        var wand = Action(AiActionType.SpellCaster, weapon: WeaponClass.SpellCaster);
        var bow = Action(AiActionType.RangedWeapon, damage: 99);

        Assert.True(MonsterAiScript.Compare(wand, bow) > 0);
        Assert.True(MonsterAiScript.Compare(bow, wand) < 0);
    }

    [Fact]
    public void A_spell_like_ability_comes_next()
    {
        var breath = Action(AiActionType.SpellLikeAbility,
                            weapon: WeaponClass.SpellLikeAbility);
        var bow = Action(AiActionType.RangedWeapon, damage: 99);
        var wand = Action(AiActionType.SpellCaster, weapon: WeaponClass.SpellCaster);

        Assert.True(MonsterAiScript.Compare(breath, bow) > 0);
        Assert.True(MonsterAiScript.Compare(breath, wand) < 0);
    }

    [Fact]
    public void A_ranged_weapon_beats_a_melee_one()
    {
        var bow = Action(AiActionType.RangedWeapon, damage: 3);
        var sword = Action(AiActionType.MeleeWeapon, damage: 30);

        Assert.True(MonsterAiScript.Compare(bow, sword) > 0);
    }

    [Fact]
    public void Between_two_ranged_weapons_the_harder_hitting_one_wins()
    {
        var strong = Action(AiActionType.RangedWeapon, damage: 12);
        var weak = Action(AiActionType.RangedWeapon, damage: 4);

        Assert.True(MonsterAiScript.Compare(strong, weak) > 0);
        Assert.Equal(0, MonsterAiScript.Compare(strong, strong));
    }

    [Fact]
    public void A_melee_weapon_beats_unarmed_and_advancing()
    {
        var sword = Action(AiActionType.MeleeWeapon, damage: 5);

        Assert.True(MonsterAiScript.Compare(sword, Action(AiActionType.Judo)) > 0);
        Assert.True(MonsterAiScript.Compare(sword, Action(AiActionType.Advance)) > 0);
    }

    [Fact]
    public void Between_two_melee_weapons_the_harder_hitting_one_wins()
    {
        var axe = Action(AiActionType.MeleeWeapon, damage: 9);
        var dagger = Action(AiActionType.MeleeWeapon, damage: 3);

        Assert.True(MonsterAiScript.Compare(axe, dagger) > 0);
    }

    [Fact]
    public void Unarmed_beats_advancing()
    {
        Assert.True(MonsterAiScript.Compare(Action(AiActionType.Judo),
                                            Action(AiActionType.Advance)) > 0);
    }

    [Fact]
    public void Between_two_advances_the_closer_target_wins()
    {
        var near = Action(AiActionType.Advance, distance: 3);
        var far = Action(AiActionType.Advance, distance: 12);

        Assert.True(MonsterAiScript.Compare(near, far) > 0);
    }

    [Fact]
    public void Two_actions_the_script_cannot_tell_apart_rank_equal()
    {
        // "The only action left is to guard" -- the script falls off the end returning zero.
        Assert.Equal(0, MonsterAiScript.Compare(Action(AiActionType.Unknown),
                                                Action(AiActionType.Unknown)));
    }

    [Fact]
    public void The_comparison_is_antisymmetric_across_the_whole_ordering()
    {
        var actions = new[]
        {
            Action(AiActionType.SpellCaster, weapon: WeaponClass.SpellCaster),
            Action(AiActionType.SpellLikeAbility, weapon: WeaponClass.SpellLikeAbility),
            Action(AiActionType.RangedWeapon, damage: 5, distance: 36),
            Action(AiActionType.MeleeWeapon, damage: 7),
            Action(AiActionType.Judo),
            Action(AiActionType.Advance, distance: 16),
        };

        foreach (var a in actions)
        {
            foreach (var b in actions)
            {
                Assert.Equal(Math.Sign(MonsterAiScript.Compare(a, b)),
                             -Math.Sign(MonsterAiScript.Compare(b, a)));
            }
        }
    }

    // ---- ranking -------------------------------------------------------------------------------

    [Fact]
    public void Ranking_puts_the_scripts_whole_order_in_place()
    {
        var ranked = MonsterAiScript.Rank(
        [
            Action(AiActionType.Advance, distance: 16),
            Action(AiActionType.Judo),
            Action(AiActionType.MeleeWeapon, damage: 7),
            Action(AiActionType.RangedWeapon, damage: 5, distance: 36),
            Action(AiActionType.SpellLikeAbility, weapon: WeaponClass.SpellLikeAbility),
            Action(AiActionType.SpellCaster, weapon: WeaponClass.SpellCaster),
        ]);

        Assert.Equal(
        [
            AiActionType.SpellCaster, AiActionType.SpellLikeAbility, AiActionType.RangedWeapon,
            AiActionType.MeleeWeapon, AiActionType.Judo, AiActionType.Advance,
        ],
        ranked.Select(r => r.Type));
    }

    [Fact]
    public void Ranking_an_empty_list_yields_an_empty_list()
    {
        Assert.Empty(MonsterAiScript.Rank([]));
    }
}
