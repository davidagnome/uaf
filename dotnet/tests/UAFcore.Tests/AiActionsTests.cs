using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers enumerating what a computer-run combatant could do, and picking from it.</summary>
public class AiActionsTests
{
    private static CombatMap Map()
    {
        var map = new CombatMap(30, 30) { CombatantCount = 20 };
        map.FillHoles();
        return map;
    }

    private static Combatant Orc(int index = 0, int x = 5, int y = 5) =>
        new(index, isFriendly: false, new CombatantIcon(1, 1), $"orc{index}")
        {
            X = x,
            Y = y,
            MaxMovement = 12,
            HitPoints = 10,
        };

    private static Combatant Hero(int index, int x, int y,
                                  CharacterStatus status = CharacterStatus.Okay) =>
        new(index, isFriendly: true, new CombatantIcon(1, 1), $"hero{index}")
        {
            X = x,
            Y = y,
            Status = status,
            HitPoints = 10,
        };

    private static AiWeapon Sword(int damage = 5) =>
        new(WeaponClass.HandCutting, Range: 1, damage);

    private static AiWeapon Bow(int damage = 4) =>
        new(WeaponClass.Bow, Range: 10, damage);

    private static AiWeapon Wand() =>
        new(WeaponClass.SpellCaster, Range: 10, AverageDamage: 0, HasSpell: true);

    // ---- what gets enumerated ------------------------------------------------------------------

    [Fact]
    public void One_action_is_offered_per_target_and_weapon()
    {
        var self = Orc();
        var all = new List<Combatant> { self, Hero(1, 6, 5), Hero(2, 9, 5) };

        var actions = AiActions.For(self, all, [Sword(), Bow()]);

        // Sword reaches only the adjacent hero; the bow reaches only the distant one (it refuses
        // an adjacent target). Both heroes can be advanced on.
        Assert.Equal(1, actions.Count(a => a.Type == AiActionType.MeleeWeapon));
        Assert.Equal(1, actions.Count(a => a.Type == AiActionType.RangedWeapon));
        Assert.Equal(2, actions.Count(a => a.Type == AiActionType.Advance));
    }

    [Fact]
    public void No_action_is_offered_against_the_casters_own_side()
    {
        var self = Orc();
        var all = new List<Combatant> { self, Orc(1, 6, 5) };

        Assert.Empty(AiActions.For(self, all, [Sword()], unarmedAttacks: 1));
    }

    [Fact]
    public void A_target_off_the_map_is_not_considered()
    {
        var self = Orc();
        var gone = Hero(1, -1, -1);
        var all = new List<Combatant> { self, gone };

        Assert.Empty(AiActions.For(self, all, [Sword()]));
    }

    [Fact]
    public void The_dead_and_the_petrified_are_not_offered_as_targets()
    {
        var self = Orc();
        var all = new List<Combatant>
        {
            self,
            Hero(1, 6, 5, CharacterStatus.Dead),
            Hero(2, 4, 5, CharacterStatus.Petrified),
        };

        Assert.Empty(AiActions.For(self, all, [Sword()]));
    }

    [Fact]
    public void Unarmed_attacks_yield_one_candidate_each()
    {
        var self = Orc();
        var all = new List<Combatant> { self, Hero(1, 6, 5) };

        var actions = AiActions.For(self, all, [], unarmedAttacks: 3);

        Assert.Equal(3, actions.Count(a => a.Type == AiActionType.Judo));
    }

    [Fact]
    public void A_combatant_never_attacks_itself_unarmed()
    {
        // The identity check is on the unarmed path alone; every other kind relies on the friendly
        // test, which also catches self.
        var self = Orc();

        Assert.Empty(AiActions.For(self, [self], [], unarmedAttacks: 2));
    }

    [Fact]
    public void A_spell_item_with_no_spell_yields_nothing_at_all()
    {
        var self = Orc();
        var all = new List<Combatant> { self, Hero(1, 8, 5) };
        var empty = new AiWeapon(WeaponClass.SpellCaster, Range: 10, HasSpell: false);

        Assert.DoesNotContain(AiActions.For(self, all, [empty]),
                              a => a.Type == AiActionType.SpellCaster);
    }

    [Fact]
    public void Judo_and_melee_only_suppresses_spell_items_and_bows()
    {
        var self = Orc();
        var all = new List<Combatant> { self, Hero(1, 6, 5), Hero(2, 9, 5) };

        var actions = AiActions.For(self, all, [Sword(), Bow(), Wand()],
                                    unarmedAttacks: 1, judoMeleeOnly: true);

        Assert.DoesNotContain(actions, a => a.Type == AiActionType.RangedWeapon);
        Assert.DoesNotContain(actions, a => a.Type == AiActionType.SpellCaster);
        Assert.Contains(actions, a => a.Type == AiActionType.MeleeWeapon);
        Assert.Contains(actions, a => a.Type == AiActionType.Judo);
    }

    [Fact]
    public void Advancing_is_offered_even_on_an_adjacent_target()
    {
        // The distance22 > 8 guard was removed in 2016: a combatant out of attacks could not
        // advance on the enemy beside it, so it oscillated between further ones.
        var self = Orc();
        var all = new List<Combatant> { self, Hero(1, 6, 5) };

        var actions = AiActions.For(self, all, []);

        Assert.Contains(actions, a => a.Type == AiActionType.Advance && a.Target == 1);
    }

    [Fact]
    public void A_combatant_that_cannot_move_is_offered_no_advance()
    {
        var self = Orc();
        var all = new List<Combatant> { self, Hero(1, 9, 5) };

        Assert.Empty(AiActions.For(self, all, [], canMove: false));
    }

    // ---- what gets chosen ----------------------------------------------------------------------

    [Fact]
    public void A_monster_in_reach_attacks_rather_than_advances()
    {
        var map = Map();
        var self = Orc();
        var hero = Hero(1, 6, 5);
        var all = new List<Combatant> { self, hero };
        map.Place(self.X, self.Y, self.Index);
        map.Place(hero.X, hero.Y, hero.Index);

        var plan = MonsterAi.Think(self, all, map, (_, _) => true, [Sword()]);

        Assert.Equal(AiDecision.Attack, plan.Decision);
        Assert.Equal(1, plan.Target);
    }

    [Fact]
    public void A_monster_out_of_reach_advances_on_the_closer_target()
    {
        var map = Map();
        var self = Orc();
        var near = Hero(1, 12, 5);
        var far = Hero(2, 20, 5);
        var all = new List<Combatant> { self, near, far };
        map.Place(self.X, self.Y, self.Index);
        map.Place(near.X, near.Y, near.Index);
        map.Place(far.X, far.Y, far.Index);

        var plan = MonsterAi.Think(self, all, map, (_, _) => false, [Sword()]);

        Assert.Equal(AiDecision.Move, plan.Decision);
        Assert.Equal(1, plan.Target);
    }

    [Fact]
    public void A_wand_is_preferred_over_a_sword_within_reach_of_both()
    {
        var map = Map();
        var self = Orc();
        var hero = Hero(1, 8, 5);
        var all = new List<Combatant> { self, hero };
        map.Place(self.X, self.Y, self.Index);
        map.Place(hero.X, hero.Y, hero.Index);

        var plan = MonsterAi.Think(self, all, map, (_, _) => true, [Sword(), Bow(), Wand()]);

        Assert.Equal(AiDecision.Attack, plan.Decision);
        Assert.Equal(1, plan.Target);
    }

    [Fact]
    public void Nothing_worth_doing_is_a_guard()
    {
        // "The only action left is to guard."
        var map = Map();
        var self = Orc();
        var all = new List<Combatant> { self, Hero(1, 6, 5, CharacterStatus.Dead) };

        var plan = MonsterAi.Think(self, all, map, (_, _) => true, [Sword()]);

        Assert.Equal(AiDecision.Guard, plan.Decision);
    }

    [Fact]
    public void An_advance_with_nowhere_to_go_becomes_a_guard()
    {
        // The chosen action stands; the engine turns it into a guard rather than trying the
        // next-best one.
        var map = Map();
        var self = Orc();
        var hero = Hero(1, 6, 5);
        var all = new List<Combatant> { self, hero };
        map.Place(self.X, self.Y, self.Index);
        map.Place(hero.X, hero.Y, hero.Index);

        // No weapons and no natural attacks either, so the only candidate is the advance on an
        // adjacent target. Leaving TotalAttacks at its default of one would give it a judo attack,
        // which outranks advancing -- correctly.
        self.TotalAttacks = 0;
        var plan = MonsterAi.Think(self, all, map, (_, _) => false, []);

        Assert.Equal(AiDecision.Guard, plan.Decision);
        Assert.Equal(1, plan.Target);
    }

    [Fact]
    public void Fleeing_still_beats_the_script()
    {
        // A turned monster runs from whoever turned it, and does not stop to consider the script.
        var map = Map();
        var self = Orc(0, 5, 5);
        self.IsTurned = true;
        self.LastAttacker = 1;
        var hero = Hero(1, 6, 5);
        var all = new List<Combatant> { self, hero };
        map.Place(self.X, self.Y, self.Index);
        map.Place(hero.X, hero.Y, hero.Index);

        var plan = MonsterAi.Think(self, all, map, (_, _) => true, [Sword()]);

        Assert.Equal(AiDecision.Flee, plan.Decision);
    }
}
