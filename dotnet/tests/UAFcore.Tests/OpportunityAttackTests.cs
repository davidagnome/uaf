using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers attacks earned by an opponent moving into or out of reach
/// (<c>CheckOpponentFreeAttack</c>, <c>Combatant.cpp:10060</c>).
/// </summary>
public class OpportunityAttackTests
{
    private static CombatMap OpenMap()
    {
        var map = new CombatMap(25, 25);
        map.FillHoles();
        map.CombatantCount = 16;
        return map;
    }

    private static Combatant Place(CombatMap map, int index, bool friendly, int x, int y)
    {
        var c = new Combatant(index, friendly, new CombatantIcon(1, 1), $"c{index}")
        {
            X = x,
            Y = y,
            Kind = friendly ? CombatantKind.Character : CombatantKind.Monster,
            AvailableAttacks = 1,
            TotalAttacks = 1,
        };
        map.Place(x, y, index);
        return c;
    }

    /// <summary>Everybody can attack everybody, so the tests exercise the movement rules.</summary>
    private static readonly Func<Combatant, int, bool> AlwaysCanAttack = (_, _) => true;

    [Fact]
    public void Retreating_from_an_enemy_earns_it_a_free_attack()
    {
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var enemy = Place(map, 1, false, 6, 5);
        var all = new List<Combatant> { mover, enemy };

        var owed = OpportunityAttacks.Check(mover, 5, 5, 3, 5, all, map, AlwaysCanAttack);

        var attack = Assert.Single(owed);
        Assert.Equal(enemy.Index, attack.Attacker);
        Assert.Equal(OpportunityKind.Free, attack.Kind);
    }

    [Fact]
    public void Walking_up_to_a_guarding_enemy_earns_it_a_guard_attack()
    {
        var map = OpenMap();
        var mover = Place(map, 0, true, 3, 5);
        var enemy = Place(map, 1, false, 6, 5);
        enemy.State = CombatantState.Guarding;
        var all = new List<Combatant> { mover, enemy };

        var owed = OpportunityAttacks.Check(mover, 3, 5, 5, 5, all, map, AlwaysCanAttack);

        var attack = Assert.Single(owed);
        Assert.Equal(OpportunityKind.Guard, attack.Kind);
        Assert.Equal(1, attack.Attacks);        // exactly one, always
    }

    [Fact]
    public void An_enemy_that_is_not_guarding_gets_nothing_when_approached()
    {
        // Guarding-CanGuardAttack refuses unless the attacker is actually in the guarding state.
        var map = OpenMap();
        var mover = Place(map, 0, true, 3, 5);
        var enemy = Place(map, 1, false, 6, 5);
        var all = new List<Combatant> { mover, enemy };

        Assert.Empty(OpportunityAttacks.Check(mover, 3, 5, 5, 5, all, map, AlwaysCanAttack));
    }

    [Fact]
    public void A_guarding_enemy_with_no_attacks_left_gets_nothing()
    {
        var map = OpenMap();
        var mover = Place(map, 0, true, 3, 5);
        var enemy = Place(map, 1, false, 6, 5);
        enemy.State = CombatantState.Guarding;
        enemy.AvailableAttacks = 0;
        var all = new List<Combatant> { mover, enemy };

        Assert.Empty(OpportunityAttacks.Check(mover, 3, 5, 5, 5, all, map, AlwaysCanAttack));
    }

    [Fact]
    public void Staying_adjacent_earns_nothing_either_way()
    {
        // The rule is about crossing the boundary, not about being near.
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var enemy = Place(map, 1, false, 6, 5);
        enemy.State = CombatantState.Guarding;
        var all = new List<Combatant> { mover, enemy };

        // (5,5) -> (6,6) is still in the ring around the enemy at (6,5).
        Assert.Empty(OpportunityAttacks.Check(mover, 5, 5, 6, 6, all, map, AlwaysCanAttack));
    }

    [Fact]
    public void A_free_attack_grants_the_attackers_whole_complement()
    {
        // The shipped script returns hook parameter 8, which is TOTAL attacks -- not remaining.
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var enemy = Place(map, 1, false, 6, 5);
        enemy.TotalAttacks = 3;
        enemy.AvailableAttacks = 1;
        var all = new List<Combatant> { mover, enemy };

        var owed = OpportunityAttacks.Check(mover, 5, 5, 3, 5, all, map, AlwaysCanAttack);
        Assert.Equal(3, Assert.Single(owed).Attacks);
    }

    [Fact]
    public void A_ranged_weapon_earns_no_opportunity_attack()
    {
        // The one rule both shipped scripts agree on.
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var archer = Place(map, 1, false, 6, 5);
        var all = new List<Combatant> { mover, archer };

        Assert.Empty(OpportunityAttacks.Check(mover, 5, 5, 3, 5, all, map, AlwaysCanAttack,
                                              hasRangedWeapon: _ => true));
    }

    [Fact]
    public void A_casting_combatant_never_interrupts_itself_to_swing()
    {
        // Breaking off to attack would lose the spell.
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var caster = Place(map, 1, false, 6, 5);
        caster.State = CombatantState.Casting;
        var all = new List<Combatant> { mover, caster };

        Assert.Empty(OpportunityAttacks.Check(mover, 5, 5, 3, 5, all, map, AlwaysCanAttack));
    }

    [Fact]
    public void An_ally_is_never_owed_an_attack()
    {
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var ally = Place(map, 1, true, 6, 5);
        ally.State = CombatantState.Guarding;
        var all = new List<Combatant> { mover, ally };

        Assert.Empty(OpportunityAttacks.Check(mover, 5, 5, 3, 5, all, map, AlwaysCanAttack));
    }

    [Fact]
    public void An_enemy_that_cannot_reach_the_mover_is_owed_nothing()
    {
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var enemy = Place(map, 1, false, 6, 5);
        var all = new List<Combatant> { mover, enemy };

        Assert.Empty(OpportunityAttacks.Check(mover, 5, 5, 3, 5, all, map, (_, _) => false));
    }

    [Fact]
    public void Free_attacks_are_ordered_after_guard_attacks_so_they_resolve_first()
    {
        // The queue is a stack: pushing guard attacks first leaves free attacks on top. The
        // reference's own comment says so.
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var leftBehind = Place(map, 1, false, 4, 5);
        var walkedInto = Place(map, 2, false, 8, 5);
        walkedInto.State = CombatantState.Guarding;
        var all = new List<Combatant> { mover, leftBehind, walkedInto };

        var owed = OpportunityAttacks.Check(mover, 5, 5, 7, 5, all, map, AlwaysCanAttack);

        Assert.Equal(2, owed.Count);
        Assert.Equal(OpportunityKind.Guard, owed[0].Kind);
        Assert.Equal(OpportunityKind.Free, owed[1].Kind);

        var queue = new TurnQueue();
        queue.Push(mover.Index, affectStats: true, 0, 0);
        OpportunityAttacks.Queue(owed, queue, mover, map, 5, 5, 7, 5, all);

        // Free attacker on top, guard attacker under it, mover at the bottom.
        Assert.Equal([leftBehind.Index, walkedInto.Index, mover.Index], queue.Order);
    }

    [Fact]
    public void Queueing_rewinds_the_mover_and_parks_its_destination()
    {
        // The mover goes back where it came from while the attacks resolve; the square it was
        // heading for is kept on the queue entry so the step can finish afterwards.
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var enemy = Place(map, 1, false, 4, 5);
        var all = new List<Combatant> { mover, enemy };

        mover.X = 7;                        // pretend the step already happened
        mover.Y = 5;
        map.Remove(5, 5);
        map.Place(7, 5, mover.Index);

        var owed = OpportunityAttacks.Check(mover, 5, 5, 7, 5, all, map, AlwaysCanAttack);
        var queue = new TurnQueue();
        queue.Push(mover.Index, affectStats: true, 0, 0);

        OpportunityAttacks.Queue(owed, queue, mover, map, 5, 5, 7, 5, all);

        Assert.Equal((5, 5), (mover.X, mover.Y));
        Assert.Equal(mover.Index, map.OccupantAt(5, 5));
        Assert.Equal(CombatMap.NoDude, map.OccupantAt(7, 5));
        Assert.Equal(CombatantState.None, mover.State);
    }

    [Fact]
    public void A_queued_attacker_is_woken_up_and_aimed_at_the_mover()
    {
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var enemy = Place(map, 1, false, 4, 5);
        enemy.TurnIsDone = true;
        var all = new List<Combatant> { mover, enemy };

        var owed = OpportunityAttacks.Check(mover, 5, 5, 7, 5, all, map, AlwaysCanAttack);
        var queue = new TurnQueue();
        queue.Push(mover.Index, affectStats: true, 0, 0);
        OpportunityAttacks.Queue(owed, queue, mover, map, 5, 5, 7, 5, all);

        Assert.False(enemy.TurnIsDone);
        Assert.Equal(mover.Index, enemy.Target);

        // Queued without affecting stats, which is what marks it an interruption rather than a
        // turn -- and what CombatRound.IsFreeAttacker keys on.
        Assert.False(queue.AffectsStats);
        Assert.Equal(1, queue.FreeAttacks);
    }

    [Fact]
    public void The_round_reports_an_interrupting_attacker()
    {
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var enemy = Place(map, 1, false, 4, 5);
        var all = new List<Combatant> { mover, enemy };

        var round = new CombatRound(i => all[i].State);
        round.IsStartingNewRound = false;
        round.Queue.Push(mover.Index, affectStats: true, 0, 0);
        round.Queue.NotStartOfTurn();

        var owed = OpportunityAttacks.Check(mover, 5, 5, 7, 5, all, map, AlwaysCanAttack);
        OpportunityAttacks.Queue(owed, round.Queue, mover, map, 5, 5, 7, 5, all);

        Assert.True(round.IsFreeAttacker);
        Assert.Equal(enemy.Index, round.Queue.Top);

        // And once the interruption pops, the mover is back on top marked for a resumed turn.
        round.Queue.Pop();
        Assert.Equal(mover.Index, round.Queue.Top);
        Assert.True(round.Queue.RestartInterruptedTurn);
    }

    [Fact]
    public void Nothing_is_queued_when_nothing_is_owed()
    {
        var map = OpenMap();
        var mover = Place(map, 0, true, 5, 5);
        var all = new List<Combatant> { mover };

        var queue = new TurnQueue();
        queue.Push(mover.Index, affectStats: true, 0, 0);

        OpportunityAttacks.Queue([], queue, mover, map, 5, 5, 7, 5, all);

        Assert.Equal([mover.Index], queue.Order);
        Assert.Equal(-1, queue.DelayedX);       // destination not parked
    }
}
