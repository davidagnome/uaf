using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers target selection and attack validity
/// (<c>GetCurrTarget</c>, <c>IsValidTarget</c>, <c>canAttack</c>).
/// </summary>
public class TargetingTests
{
    private static CombatMap OpenMap()
    {
        var map = new CombatMap(25, 25);
        map.FillHoles();
        return map;
    }

    private static Combatant Make(int index, bool friendly, int x, int y,
                                  CombatantKind kind = CombatantKind.Character)
    {
        var c = new Combatant(index, friendly, new CombatantIcon(1, 1), $"c{index}")
        {
            X = x,
            Y = y,
            Kind = kind,
            AvailableAttacks = 1,
        };
        return c;
    }

    /// <summary>An attacker at (5,5) and an enemy monster adjacent to it.</summary>
    private static (Combatant Attacker, Combatant Target, List<Combatant> All) Pair()
    {
        var attacker = Make(0, friendly: true, 5, 5);
        var target = Make(1, friendly: false, 6, 5, CombatantKind.Monster);
        return (attacker, target, [attacker, target]);
    }

    private static ReadiedWeapon Sword => new(WeaponClass.HandCutting, Range: 1);

    private static ReadiedWeapon Bow => new(WeaponClass.Bow, Range: 10, AmmoClass: "arrow");

    private static ReadiedAmmo Arrows => new(WeaponClass.Ammo, Quantity: 12, AmmoClass: "arrow");

    // ---- range rules -----------------------------------------------------------------------

    [Theory]
    [InlineData(WeaponClass.HandCutting, 1, 1, true)]    // adjacent, in range
    [InlineData(WeaponClass.HandCutting, 1, 2, false)]   // past its range
    [InlineData(WeaponClass.HandThrow, 5, 1, true)]      // hand-throw covers adjacent...
    [InlineData(WeaponClass.HandThrow, 5, 4, true)]      // ...and distance
    [InlineData(WeaponClass.Bow, 10, 1, false)]          // a bow cannot be used adjacent
    [InlineData(WeaponClass.Bow, 10, 2, true)]
    [InlineData(WeaponClass.Bow, 10, 11, false)]
    [InlineData(WeaponClass.Throw, 6, 1, false)]         // Throw has a minimum too
    [InlineData(WeaponClass.Throw, 6, 3, true)]
    [InlineData(WeaponClass.SlingNoAmmo, 8, 1, false)]
    [InlineData(WeaponClass.NotWeapon, 5, 1, false)]
    [InlineData(WeaponClass.Ammo, 5, 3, false)]
    public void Each_weapon_class_has_its_own_reach(WeaponClass cls, int range, int distance,
                                                    bool expected)
    {
        // The ranged classes have a MINIMUM of 2 as well as a maximum -- a bow is useless in your
        // face. The hand classes have no minimum, which is what lets HandThrow cover both.
        Assert.Equal(expected, WeaponRange.CanAttackAt(new ReadiedWeapon(cls, range), distance));
    }

    [Fact]
    public void A_spell_item_that_targets_its_caster_reaches_only_itself()
    {
        var selfSpell = new ReadiedWeapon(WeaponClass.SpellCaster, Range: 9,
                                          CastsSelfTargetingSpell: true);
        Assert.True(WeaponRange.CanAttackAt(selfSpell, 0));
        Assert.False(WeaponRange.CanAttackAt(selfSpell, 1));

        var ordinary = new ReadiedWeapon(WeaponClass.SpellCaster, Range: 9);
        Assert.True(WeaponRange.CanAttackAt(ordinary, 5));
        Assert.False(WeaponRange.CanAttackAt(ordinary, 10));
    }

    // ---- canAttack -------------------------------------------------------------------------

    [Fact]
    public void An_adjacent_enemy_can_be_attacked_with_a_sword()
    {
        var (attacker, target, _) = Pair();
        Assert.Equal(AttackRefusal.None,
                     Targeting.CanAttack(attacker, target, OpenMap(), Sword));
    }

    [Fact]
    public void A_combatant_with_no_attacks_left_cannot_attack()
    {
        var (attacker, target, _) = Pair();
        attacker.AvailableAttacks = 0;

        Assert.Equal(AttackRefusal.NoAttacksLeft,
                     Targeting.CanAttack(attacker, target, OpenMap(), Sword));

        // ...unless something grants extra ones.
        Assert.Equal(AttackRefusal.None,
                     Targeting.CanAttack(attacker, target, OpenMap(), Sword,
                                         additionalAttacks: 1));
    }

    [Fact]
    public void A_part_attack_cannot_be_spent_two_rounds_running()
    {
        // Half an attack banked from last round is not enough to swing again this one.
        var (attacker, target, _) = Pair();
        attacker.AvailableAttacks = 0.5;
        attacker.LastAttackRound = 4;

        Assert.Equal(AttackRefusal.PartialAttackTooSoon,
                     Targeting.CanAttack(attacker, target, OpenMap(), Sword, currentRound: 5));

        // A round later it is allowed.
        Assert.Equal(AttackRefusal.None,
                     Targeting.CanAttack(attacker, target, OpenMap(), Sword, currentRound: 6));
    }

    [Fact]
    public void Attacking_yourself_needs_saying_so()
    {
        var attacker = Make(0, friendly: true, 5, 5);
        var map = OpenMap();

        Assert.Equal(AttackRefusal.CannotAttackSelf,
                     Targeting.CanAttack(attacker, attacker, map, Sword));
        Assert.Equal(AttackRefusal.None,
                     Targeting.CanAttack(attacker, attacker, map, Sword, canAttackSelf: true));
    }

    [Fact]
    public void An_auto_combatant_never_turns_on_its_own_side()
    {
        var attacker = Make(0, friendly: false, 5, 5, CombatantKind.Monster);
        attacker.IsAuto = true;
        var ally = Make(1, friendly: false, 6, 5, CombatantKind.Monster);

        Assert.Equal(AttackRefusal.SameSideAutoCombatant,
                     Targeting.CanAttack(attacker, ally, OpenMap(), Sword));
    }

    [Fact]
    public void A_player_cannot_strike_a_party_character_but_can_strike_an_npc()
    {
        var attacker = Make(0, friendly: true, 5, 5);
        var partyMember = Make(1, friendly: true, 6, 5, CombatantKind.Character);
        var npc = Make(2, friendly: true, 6, 5, CombatantKind.Npc);
        var map = OpenMap();

        Assert.Equal(AttackRefusal.SameSidePlayerCharacter,
                     Targeting.CanAttack(attacker, partyMember, map, Sword));

        // An NPC on your own side is the one same-side target that is allowed.
        Assert.Equal(AttackRefusal.None, Targeting.CanAttack(attacker, npc, map, Sword));
    }

    [Fact]
    public void Natural_attacks_are_melee_only()
    {
        // With no weapon the reference refuses any distance above 1 outright, with no range table
        // involved at all.
        var attacker = Make(0, friendly: true, 5, 5);
        var adjacent = Make(1, friendly: false, 6, 5, CombatantKind.Monster);
        var distant = Make(2, friendly: false, 10, 5, CombatantKind.Monster);
        var map = OpenMap();

        Assert.Equal(AttackRefusal.None, Targeting.CanAttack(attacker, adjacent, map));
        Assert.Equal(AttackRefusal.OutOfWeaponRange, Targeting.CanAttack(attacker, distant, map));
    }

    [Fact]
    public void An_empty_weapon_stack_cannot_attack()
    {
        var (attacker, target, _) = Pair();
        Assert.Equal(AttackRefusal.WeaponStackEmpty,
                     Targeting.CanAttack(attacker, target, OpenMap(),
                                         Sword with { Quantity = 0 }));
    }

    [Fact]
    public void A_bow_needs_matching_ammunition_in_the_quiver()
    {
        var attacker = Make(0, friendly: true, 5, 5);
        var target = Make(1, friendly: false, 10, 5, CombatantKind.Monster);
        var map = OpenMap();

        Assert.Equal(AttackRefusal.NoAmmoReadied,
                     Targeting.CanAttack(attacker, target, map, Bow));

        Assert.Equal(AttackRefusal.WrongAmmoClass,
                     Targeting.CanAttack(attacker, target, map, Bow,
                                         Arrows with { AmmoClass = "bolt" }));

        Assert.Equal(AttackRefusal.AmmoStackEmpty,
                     Targeting.CanAttack(attacker, target, map, Bow,
                                         Arrows with { Quantity = 0 }));

        Assert.Equal(AttackRefusal.None,
                     Targeting.CanAttack(attacker, target, map, Bow, Arrows));
    }

    [Fact]
    public void An_item_with_no_ammunition_class_cannot_feed_a_bow()
    {
        // An empty ammo class means the item takes no ammunition at all -- a wand or potion.
        var attacker = Make(0, friendly: true, 5, 5);
        var target = Make(1, friendly: false, 10, 5, CombatantKind.Monster);

        Assert.Equal(AttackRefusal.WrongAmmoClass,
                     Targeting.CanAttack(attacker, target, OpenMap(),
                                         Bow with { AmmoClass = "" },
                                         Arrows with { AmmoClass = "" }));
    }

    [Fact]
    public void A_wall_between_the_two_stops_the_attack()
    {
        var attacker = Make(0, friendly: true, 5, 5);
        var target = Make(1, friendly: false, 10, 5, CombatantKind.Monster);
        var map = OpenMap();

        Assert.Equal(AttackRefusal.None,
                     Targeting.CanAttack(attacker, target, map, Bow, Arrows));

        for (int y = 0; y < 25; y++)
        {
            map.SetTile(7, y, 1);
        }

        Assert.Equal(AttackRefusal.NoLineOfSight,
                     Targeting.CanAttack(attacker, target, map, Bow, Arrows));
    }

    [Fact]
    public void Invisibility_only_protects_at_a_distance()
    {
        var attacker = Make(0, friendly: true, 5, 5);
        var far = Make(1, friendly: false, 10, 5, CombatantKind.Monster);
        var near = Make(2, friendly: false, 6, 5, CombatantKind.Monster);
        far.IsInvisible = true;
        near.IsInvisible = true;
        var map = OpenMap();

        Assert.Equal(AttackRefusal.TargetInvisible,
                     Targeting.CanAttack(attacker, far, map, Bow, Arrows));

        // Adjacent, an invisible target can still be found.
        Assert.Equal(AttackRefusal.None, Targeting.CanAttack(attacker, near, map, Sword));

        // And detecting invisibility restores the ranged attack.
        attacker.DetectsInvisible = true;
        Assert.Equal(AttackRefusal.None, Targeting.CanAttack(attacker, far, map, Bow, Arrows));
    }

    [Fact]
    public void The_selective_invisibilities_depend_on_what_the_attacker_is()
    {
        var target = Make(1, friendly: false, 10, 5, CombatantKind.Monster);
        target.IsInvisibleToUndead = true;
        var map = OpenMap();

        var undead = Make(0, friendly: true, 5, 5);
        undead.IsUndead = true;
        Assert.Equal(AttackRefusal.TargetInvisible,
                     Targeting.CanAttack(undead, target, map, Bow, Arrows));

        var living = Make(0, friendly: true, 5, 5);
        Assert.Equal(AttackRefusal.None,
                     Targeting.CanAttack(living, target, map, Bow, Arrows));
    }

    // ---- current target --------------------------------------------------------------------

    [Fact]
    public void A_combatant_with_no_target_reports_none()
    {
        var (attacker, _, all) = Pair();
        Assert.Equal(CombatMap.NoDude, Targeting.CurrentTarget(attacker, all));
    }

    [Fact]
    public void A_live_target_is_returned_unchanged()
    {
        var (attacker, target, all) = Pair();
        attacker.Target = target.Index;
        Assert.Equal(target.Index, Targeting.CurrentTarget(attacker, all));
    }

    [Fact]
    public void A_target_that_has_left_the_map_is_dropped()
    {
        var (attacker, target, all) = Pair();
        attacker.Target = target.Index;
        target.Status = CharacterStatus.Dead;

        Assert.Equal(CombatMap.NoDude, Targeting.CurrentTarget(attacker, all));
        Assert.Equal(CombatMap.NoDude, attacker.Target);
    }

    [Fact]
    public void Asking_without_updating_leaves_a_stale_target_in_place()
    {
        // The reference distinguishes asking from acting: animation code wants to know who the
        // target was without clearing it.
        var (attacker, target, all) = Pair();
        attacker.Target = target.Index;
        target.Status = CharacterStatus.Dead;

        Assert.Equal(target.Index, Targeting.CurrentTarget(attacker, all, updateTarget: false));
        Assert.Equal(target.Index, attacker.Target);
    }

    [Fact]
    public void A_replacement_target_can_be_supplied_when_the_old_one_goes()
    {
        var attacker = Make(0, friendly: true, 5, 5);
        var dead = Make(1, friendly: false, 6, 5, CombatantKind.Monster);
        var spare = Make(2, friendly: false, 7, 5, CombatantKind.Monster);
        dead.Status = CharacterStatus.Dead;
        attacker.Target = dead.Index;

        int result = Targeting.CurrentTarget(attacker, [attacker, dead, spare],
                                             onTargetLost: _ => spare.Index);

        Assert.Equal(spare.Index, result);
        Assert.Equal(spare.Index, attacker.Target);
    }

    [Fact]
    public void An_unconscious_target_can_be_kept_when_the_caller_allows_it()
    {
        var (attacker, target, all) = Pair();
        attacker.Target = target.Index;
        target.Status = CharacterStatus.Unconscious;

        Assert.Equal(CombatMap.NoDude, Targeting.CurrentTarget(attacker, all));

        attacker.Target = target.Index;
        Assert.Equal(target.Index, Targeting.CurrentTarget(attacker, all, unconsciousOk: true));
    }
}
