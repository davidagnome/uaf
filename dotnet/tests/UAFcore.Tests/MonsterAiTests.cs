using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the auto combatant's decision (<c>COMBATANT::Think</c>, <c>Combatant.cpp:2080</c>).
/// </summary>
/// <remarks>
/// The unscripted half only — the scripted branch ranks actions with a Forth program, and the
/// Forth VM is not started.
/// </remarks>
public class MonsterAiTests
{
    private static CombatMap OpenMap(int size = 25)
    {
        var map = new CombatMap(size, size);
        map.FillHoles();
        map.CombatantCount = 16;
        return map;
    }

    private static Combatant Make(CombatMap map, int index, bool friendly, int x, int y)
    {
        var c = new Combatant(index, friendly, new CombatantIcon(1, 1), $"c{index}")
        {
            X = x,
            Y = y,
            Kind = friendly ? CombatantKind.Character : CombatantKind.Monster,
            IsAuto = true,
            AvailableAttacks = 1,
            MaxMovement = 12,
        };
        map.Place(x, y, index);
        return c;
    }

    /// <summary>Attackable only when adjacent, which is what a melee weapon means.</summary>
    private static Func<Combatant, int, bool> Melee(IReadOnlyList<Combatant> all) =>
        (self, target) =>
        {
            var t = all.FirstOrDefault(c => c.Index == target);
            return t is not null && t.IsOnCombatMap()
                   && CombatMap.Distance(self.X, self.Y, t.X, t.Y) <= 1;
        };

    [Fact]
    public void With_nobody_to_fight_a_combatant_guards()
    {
        var map = OpenMap();
        var alone = Make(map, 0, true, 5, 5);
        var ally = Make(map, 1, true, 6, 5);
        var all = new List<Combatant> { alone, ally };

        var plan = MonsterAi.Think(alone, all, map, Melee(all));
        Assert.Equal(AiDecision.Guard, plan.Decision);
    }

    [Fact]
    public void An_adjacent_enemy_is_attacked_from_where_you_stand()
    {
        var map = OpenMap();
        var self = Make(map, 0, true, 5, 5);
        var enemy = Make(map, 1, false, 6, 5);
        var all = new List<Combatant> { self, enemy };

        var plan = MonsterAi.Think(self, all, map, Melee(all));

        Assert.Equal(AiDecision.Attack, plan.Decision);
        Assert.Equal(1, plan.Target);
        Assert.Null(plan.Path);
    }

    [Fact]
    public void A_distant_enemy_is_walked_toward()
    {
        var map = OpenMap();
        var self = Make(map, 0, true, 5, 5);
        var enemy = Make(map, 1, false, 15, 5);
        var all = new List<Combatant> { self, enemy };

        var plan = MonsterAi.Think(self, all, map, Melee(all));

        Assert.Equal(AiDecision.Move, plan.Decision);
        Assert.Equal(1, plan.Target);
        Assert.NotNull(plan.Path);

        // The route stops beside the target, not on it: the destination rectangle is the target's
        // footprint expanded by one, because the target is standing in its own square.
        var end = plan.Path.Destination!.Value;
        Assert.InRange(CombatMap.Distance(end.X, end.Y, 15, 5), 1, 1);
    }

    [Fact]
    public void The_nearest_enemy_is_chosen()
    {
        var map = OpenMap();
        var self = Make(map, 0, true, 5, 5);
        var far = Make(map, 1, false, 20, 5);
        var near = Make(map, 2, false, 9, 5);
        var all = new List<Combatant> { self, far, near };

        var plan = MonsterAi.Think(self, all, map, Melee(all));
        Assert.Equal(near.Index, plan.Target);
    }

    [Fact]
    public void A_visible_enemy_beats_a_closer_one_behind_a_wall()
    {
        // The reference's own comment: targets are ordered by distance, but the nearest may be on
        // the far side of a wall, so the shortest straight line is not the shortest walk.
        var map = OpenMap(30);
        var self = Make(map, 0, true, 5, 5);
        var behindWall = Make(map, 1, false, 9, 5);
        var visible = Make(map, 2, false, 5, 16);
        var all = new List<Combatant> { self, behindWall, visible };

        for (int y = 0; y < 30; y++)
        {
            map.SetTile(7, y, 1);
        }

        var plan = MonsterAi.Think(self, all, map, Melee(all));
        Assert.Equal(visible.Index, plan.Target);
    }

    [Fact]
    public void An_enemy_behind_a_wall_is_still_chosen_when_nothing_is_visible()
    {
        // Line of sight is preferred, not required -- the fallback pass takes any enemy at all.
        var map = OpenMap(30);
        var self = Make(map, 0, true, 5, 5);
        var hidden = Make(map, 1, false, 15, 5);
        var all = new List<Combatant> { self, hidden };

        for (int y = 0; y < 30; y++)
        {
            map.SetTile(10, y, 1);
        }

        var plan = MonsterAi.Think(self, all, map, Melee(all));
        Assert.Equal(hidden.Index, plan.Target);
    }

    [Fact]
    public void A_target_that_has_gone_is_replaced()
    {
        var map = OpenMap();
        var self = Make(map, 0, true, 5, 5);
        var dead = Make(map, 1, false, 6, 5);
        var alive = Make(map, 2, false, 8, 5);
        var all = new List<Combatant> { self, dead, alive };

        self.Target = dead.Index;
        dead.Status = CharacterStatus.Dead;

        var plan = MonsterAi.Think(self, all, map, Melee(all));
        Assert.Equal(alive.Index, plan.Target);
    }

    [Fact]
    public void A_combatant_that_cannot_reach_anybody_guards()
    {
        // Walled in with an enemy outside: a target is found, but no route to it exists.
        var map = OpenMap();
        var self = Make(map, 0, true, 5, 5);
        var enemy = Make(map, 1, false, 20, 20);
        var all = new List<Combatant> { self, enemy };

        for (int y = 3; y <= 7; y++)
        {
            for (int x = 3; x <= 7; x++)
            {
                if ((x, y) != (5, 5)) { map.SetTile(x, y, 1); }
            }
        }

        var plan = MonsterAi.Think(self, all, map, Melee(all));
        Assert.Equal(AiDecision.Guard, plan.Decision);
        Assert.Equal(enemy.Index, plan.Target);     // it knew who it wanted
    }

    [Fact]
    public void A_combatant_with_no_movement_left_guards_rather_than_walking()
    {
        var map = OpenMap();
        var self = Make(map, 0, true, 5, 5);
        var enemy = Make(map, 1, false, 15, 5);
        var all = new List<Combatant> { self, enemy };

        self.Movement = self.MaxMovement;

        var plan = MonsterAi.Think(self, all, map, Melee(all));
        Assert.Equal(AiDecision.Guard, plan.Decision);
    }

    // ---- fleeing ---------------------------------------------------------------------------

    [Fact]
    public void Fleeing_beats_everything_including_an_adjacent_enemy()
    {
        var map = OpenMap();
        var self = Make(map, 0, true, 5, 5);
        var enemy = Make(map, 1, false, 6, 5);
        var all = new List<Combatant> { self, enemy };

        self.IsFleeing = true;
        self.LastAttacker = enemy.Index;

        var plan = MonsterAi.Think(self, all, map, Melee(all));
        Assert.Equal(AiDecision.Flee, plan.Decision);
        Assert.NotNull(plan.Path);
    }

    [Fact]
    public void A_turned_undead_flees_the_same_way()
    {
        // The reference has the block twice, once for fleeing flags and once for turned, differing
        // only in a trace message.
        var map = OpenMap();
        var self = Make(map, 0, false, 5, 5);
        var cleric = Make(map, 1, true, 6, 5);
        var all = new List<Combatant> { self, cleric };

        self.IsTurned = true;
        self.LastAttacker = cleric.Index;

        Assert.Equal(AiDecision.Flee, MonsterAi.Think(self, all, map, Melee(all)).Decision);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(24, 5)]
    [InlineData(5, 0)]
    [InlineData(5, 24)]
    public void A_fleeing_combatant_already_on_an_edge_leaves_the_map(int x, int y)
    {
        // The edge test fires before any pathing: standing on the edge means leaving, not walking
        // to it.
        var map = OpenMap();
        var self = Make(map, 0, true, x, y);
        var enemy = Make(map, 1, false, 12, 12);
        var all = new List<Combatant> { self, enemy };

        self.IsFleeing = true;
        self.LastAttacker = enemy.Index;

        Assert.Equal(AiDecision.LeaveMap, MonsterAi.Think(self, all, map, Melee(all)).Decision);
    }

    [Fact]
    public void A_fleeing_combatant_that_cannot_move_guards()
    {
        var map = OpenMap();
        var self = Make(map, 0, true, 5, 5);
        var enemy = Make(map, 1, false, 12, 12);
        var all = new List<Combatant> { self, enemy };

        self.IsFleeing = true;
        self.LastAttacker = enemy.Index;
        self.Movement = self.MaxMovement;

        Assert.Equal(AiDecision.Guard, MonsterAi.Think(self, all, map, Melee(all)).Decision);
    }

    [Fact]
    public void Fleeing_heads_away_from_the_pursuer()
    {
        var map = OpenMap();
        var self = Make(map, 0, true, 12, 12);
        var chaser = Make(map, 1, false, 12, 20);       // to the south
        var all = new List<Combatant> { self, chaser };

        self.IsFleeing = true;
        self.LastAttacker = chaser.Index;

        var plan = MonsterAi.Think(self, all, map, Melee(all));
        Assert.Equal(AiDecision.Flee, plan.Decision);

        // Away from a southern pursuer means north.
        var end = plan.Path!.Destination!.Value;
        Assert.True(end.Y < self.Y, $"fled to y={end.Y}, which is not away from y={chaser.Y}");
    }

    // ---- the whole loop --------------------------------------------------------------------

    [Fact]
    public void A_fight_between_two_auto_combatants_reaches_a_conclusion()
    {
        // The pieces together: AI, pathing, movement, attack and the round clock. Nothing here
        // drives the fight but the ported code.
        var map = OpenMap(30);
        var hero = Make(map, 0, true, 3, 3);
        var orc = Make(map, 1, false, 20, 20);
        var all = new List<Combatant> { hero, orc };
        int heroHp = 20, orcHp = 6;

        var rng = new Random(7);
        int Roll(int sides) => rng.Next(1, sides + 1);
        var sword = new ReadiedWeapon(WeaponClass.HandCutting, Range: 1);

        bool CanAttack(Combatant self, int target)
        {
            var t = all.FirstOrDefault(c => c.Index == target);
            return Targeting.CanAttack(self, t, map, sword) == AttackRefusal.None;
        }

        var round = new CombatRound(i => all[i].State);
        hero.Initiative = 1;
        orc.Initiative = 2;

        for (int r = 1; r <= 40 && orcHp > 0 && heroHp > 0; r++)
        {
            round.BeginRound();
            round.IsStartingNewRound = false;
            foreach (var c in all) { c.TurnIsDone = true; c.BeginRound(1, isAuto: true); }

            for (int guard = 0; guard < 50; guard++)
            {
                int who = round.Advance(i => all[i].IsDone(), i => all[i].Initiative, all.Count);
                if (who == CombatMap.NoDude) { break; }
                round.Queue.NotStartOfTurn();

                var self = all[who];
                var plan = MonsterAi.Think(self, all, map, CanAttack);

                if (plan.Decision == AiDecision.Attack)
                {
                    var target = all[plan.Target];
                    var result = Attack.Resolve(self, target, map, Roll, new DamageRoll(1, 6, 1),
                                                sword, attackerThac0: 15, targetArmorClass: 8,
                                                currentRound: r);
                    if (result.Hit)
                    {
                        if (target.IsFriendly) { heroHp -= result.Damage; }
                        else { orcHp -= result.Damage; }

                        if (orcHp <= 0) { orc.Status = CharacterStatus.Dead; }
                        if (heroHp <= 0) { hero.Status = CharacterStatus.Dead; }
                    }
                }
                else if (plan.Decision == AiDecision.Move)
                {
                    var steps = plan.Path!.Steps.ToList();
                    while (steps.Count > 0
                           && CombatMovement.TakeNextStep(self, map, steps,
                                                          canAttack: CanAttack)
                              == MoveOutcome.Moved)
                    {
                    }
                }

                self.EndTurn(round.Queue);
            }
        }

        // They started 17 squares apart and had to close before anything could happen.
        Assert.True(orcHp <= 0 || heroHp <= 0, "the fight never resolved");
        Assert.True(CombatMap.Distance(hero.X, hero.Y, orc.X, orc.Y) <= 2,
                    "they never actually met");
    }
}
