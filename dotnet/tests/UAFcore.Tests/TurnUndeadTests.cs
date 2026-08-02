using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers turning undead: who is reached, and what happens to them.</summary>
public class TurnUndeadTests
{
    private static Combatant Undead(int index, CharacterStatus status = CharacterStatus.Okay) =>
        new(index, isFriendly: false, new CombatantIcon(1, 1), $"skeleton{index}")
        {
            Status = status,
            X = index,
            Y = 0,
        };

    private static Combatant Cleric() =>
        new(0, isFriendly: true, new CombatantIcon(1, 1), "cleric");

    /// <summary>Everything is a skeleton unless it is the cleric.</summary>
    private static Func<Combatant, TurnData?> AllSkeletons(bool destroys = false) =>
        c => c.IsFriendly ? null : new TurnData("skeleton", NumberToTurn: 99, destroys);

    // ---- who gets reached ----------------------------------------------------------------------

    [Fact]
    public void The_attempt_reaches_as_many_as_it_was_allowed()
    {
        var all = new List<Combatant> { Cleric(), Undead(1), Undead(2), Undead(3) };

        var results = TurnUndead.Resolve(all, AllSkeletons(),
                                         new Dictionary<string, int> { ["skeleton"] = 2 });

        Assert.Equal(2, results.Count);
        Assert.Equal([1, 2], results.Select(r => r.Combatant));
    }

    [Fact]
    public void A_category_the_attempt_did_not_reach_is_untouched()
    {
        var all = new List<Combatant> { Cleric(), Undead(1) };

        var results = TurnUndead.Resolve(all, c => c.IsFriendly
                                             ? null
                                             : new TurnData("vampire", 99, false),
                                         new Dictionary<string, int> { ["skeleton"] = 5 });

        Assert.Empty(results);
    }

    [Fact]
    public void Anything_without_turning_data_is_not_undead_and_is_skipped()
    {
        var all = new List<Combatant> { Cleric(), Undead(1) };

        var results = TurnUndead.Resolve(all, _ => null,
                                         new Dictionary<string, int> { ["skeleton"] = 5 });

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(CharacterStatus.Dead)]
    [InlineData(CharacterStatus.Gone)]
    public void The_dead_and_the_gone_never_consume_a_slot(CharacterStatus status)
    {
        var all = new List<Combatant> { Cleric(), Undead(1, status), Undead(2) };

        var results = TurnUndead.Resolve(all, AllSkeletons(),
                                         new Dictionary<string, int> { ["skeleton"] = 1 });

        Assert.Equal([2], results.Select(r => r.Combatant));
    }

    // ---- the two passes ------------------------------------------------------------------------

    [Fact]
    public void A_standing_monster_is_turned_before_one_already_running()
    {
        // Pass 0 skips anyone fleeing, so a cleric never spends the whole attempt on monsters that
        // were leaving anyway.
        var all = new List<Combatant>
        {
            Cleric(),
            Undead(1, CharacterStatus.Running),
            Undead(2),
        };

        var results = TurnUndead.Resolve(all, AllSkeletons(),
                                         new Dictionary<string, int> { ["skeleton"] = 1 });

        Assert.Equal([2], results.Select(r => r.Combatant));
    }

    [Fact]
    public void A_fleeing_monster_is_still_reached_when_slots_are_left_over()
    {
        var all = new List<Combatant>
        {
            Cleric(),
            Undead(1, CharacterStatus.Fled),
            Undead(2),
        };

        var results = TurnUndead.Resolve(all, AllSkeletons(),
                                         new Dictionary<string, int> { ["skeleton"] = 2 });

        // The standing one first, on pass 0; the fleeing one on pass 1.
        Assert.Equal([2, 1], results.Select(r => r.Combatant));
    }

    [Fact]
    public void Nobody_is_turned_twice_across_the_two_passes()
    {
        var all = new List<Combatant> { Cleric(), Undead(1), Undead(2) };

        var results = TurnUndead.Resolve(all, AllSkeletons(),
                                         new Dictionary<string, int> { ["skeleton"] = 99 });

        Assert.Equal(2, results.Count);
        Assert.Equal(results.Count, results.Select(r => r.Combatant).Distinct().Count());
    }

    // ---- turned versus destroyed ---------------------------------------------------------------

    [Fact]
    public void A_turned_monster_runs_from_whoever_turned_it()
    {
        var cleric = Cleric();
        var all = new List<Combatant> { cleric, Undead(1) };
        var map = new CombatMap(30, 30) { CombatantCount = 10 };
        map.Place(1, 0, combatant: 1);

        var results = TurnUndead.Resolve(all, AllSkeletons(),
                                         new Dictionary<string, int> { ["skeleton"] = 1 });
        TurnUndead.Apply(all, map, new TurnQueue(), cleric, results);

        Assert.Equal(TurnResult.Turned, results[0].Result);
        Assert.Equal(CharacterStatus.Running, all[1].Status);
        Assert.True(all[1].IsTurned);
        Assert.Equal(cleric.Index, all[1].LastAttacker);
        Assert.Equal(1, map.OccupantAt(1, 0));   // still on the map, just leaving
    }

    [Fact]
    public void Ending_a_turn_only_marks_it_done_for_whoever_is_actually_acting()
    {
        // EndTurn checks that this combatant is at the top of the queue before marking anything
        // (Combatant.cpp:6882). Turning happens on the cleric's turn, so the monsters it reaches
        // are not the acting combatant and simply have their state set -- it was never their turn
        // to finish.
        var cleric = Cleric();
        var acting = Undead(1);
        var bystander = Undead(2);
        var all = new List<Combatant> { cleric, acting, bystander };
        var map = new CombatMap(30, 30) { CombatantCount = 10 };

        var queue = new TurnQueue();
        queue.Push(acting.Index, affectStats: true, freeAttacks: 0, guardAttacks: 0);

        var results = TurnUndead.Resolve(all, AllSkeletons(),
                                         new Dictionary<string, int> { ["skeleton"] = 2 });
        TurnUndead.Apply(all, map, queue, cleric, results);

        Assert.True(acting.TurnIsDone);
        Assert.False(bystander.TurnIsDone);
        Assert.Equal(CharacterStatus.Running, bystander.Status);
    }

    [Fact]
    public void A_destroyed_monster_leaves_the_map()
    {
        var cleric = Cleric();
        var all = new List<Combatant> { cleric, Undead(1) };
        var map = new CombatMap(30, 30) { CombatantCount = 10 };
        map.Place(1, 0, combatant: 1);

        var results = TurnUndead.Resolve(all, AllSkeletons(destroys: true),
                                         new Dictionary<string, int> { ["skeleton"] = 1 });
        TurnUndead.Apply(all, map, new TurnQueue(), cleric, results);

        Assert.Equal(TurnResult.Destroyed, results[0].Result);
        Assert.Equal(CharacterStatus.Gone, all[1].Status);
        Assert.Equal(CombatMap.NoDude, map.OccupantAt(1, 0));
    }

    // ---- who may try ---------------------------------------------------------------------------

    [Fact]
    public void The_sentinel_for_not_turning_is_ninety_nine_not_zero()
    {
        // GetTurnUndeadLevel() < 99 is the whole condition, so any lower value passes -- including
        // zero, and including a negative one.
        var cleric = Cleric();

        Assert.False(TurnUndead.CanTurn(cleric, TurnUndead.CannotTurn));
        Assert.True(TurnUndead.CanTurn(cleric, TurnUndead.CannotTurn - 1));
        Assert.True(TurnUndead.CanTurn(cleric, 0));
        Assert.True(TurnUndead.CanTurn(cleric, -3));
    }

    [Fact]
    public void A_combatant_that_has_finished_its_turn_cannot_try()
    {
        var cleric = Cleric();
        cleric.TurnIsDone = true;

        Assert.False(TurnUndead.CanTurn(cleric, 1));
    }
}
