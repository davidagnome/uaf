using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the turn queue and the round clock
/// (<c>QueuedCombatantData</c>, <c>getNextCombatant</c>, <c>GetCombatState</c>).
/// </summary>
public class CombatRoundTests
{
    // ---- the turn queue --------------------------------------------------------------------

    [Fact]
    public void An_empty_queue_answers_every_question_without_throwing()
    {
        // The round asks these constantly and the reference never guards the call sites, so the
        // defaults are part of the contract rather than defensive padding.
        var q = new TurnQueue();

        Assert.Equal(CombatMap.NoDude, q.Top);
        Assert.True(q.IsEmpty);
        Assert.False(q.AffectsStats);
        Assert.Equal(0, q.FreeAttacks);
        Assert.Equal(0, q.GuardAttacks);
        Assert.Equal(-1, q.DelayedX);
        Assert.Equal(-1, q.DelayedY);
        Assert.False(q.StartOfTurn);
        Assert.False(q.RestartInterruptedTurn);
        Assert.Equal(0, q.DecrementFreeAttacks());

        q.Pop();                    // must not throw
        q.NotStartOfTurn();
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void Push_puts_a_combatant_in_front_and_pop_gives_the_turn_back()
    {
        // A stack, not a queue: an interrupting attacker acts before whoever was mid-turn.
        var q = new TurnQueue();
        q.PushTail(1, affectStats: true);
        Assert.Equal(1, q.Top);

        q.Push(7, affectStats: false, freeAttacks: 2, guardAttacks: 0);
        Assert.Equal(7, q.Top);
        Assert.False(q.AffectsStats);
        Assert.Equal(2, q.FreeAttacks);

        q.Pop();
        Assert.Equal(1, q.Top);
        Assert.True(q.AffectsStats);
    }

    [Fact]
    public void Interrupting_a_combatant_that_had_already_started_marks_it_for_restart()
    {
        // RestartInterruptedTurn = !StartOfTurn on the displaced head (Combatant.h:737), so a
        // combatant interrupted before it ever acted comes back as a fresh turn instead.
        var q = new TurnQueue();

        q.Push(1, true, 0, 0);
        Assert.True(q.StartOfTurn);

        q.NotStartOfTurn();                 // combatant 1 has now begun acting
        q.Push(2, false, 1, 0);             // interrupted mid-turn
        q.Pop();

        Assert.Equal(1, q.Top);
        Assert.True(q.RestartInterruptedTurn);
        Assert.False(q.StartOfTurn);
    }

    [Fact]
    public void A_combatant_interrupted_before_it_acted_returns_as_a_fresh_turn()
    {
        var q = new TurnQueue();
        q.Push(1, true, 0, 0);              // start of turn, not yet acted
        q.Push(2, false, 1, 0);
        q.Pop();

        Assert.Equal(1, q.Top);
        Assert.False(q.RestartInterruptedTurn);
        Assert.True(q.StartOfTurn);
    }

    [Fact]
    public void Push_tail_does_not_mark_a_start_of_turn()
    {
        // Only Push sets the flag. A combatant that reaches the top because others popped off
        // never reports a start of turn -- an asymmetry that is in the original.
        var q = new TurnQueue();
        q.PushTail(3, affectStats: true);

        Assert.Equal(3, q.Top);
        Assert.False(q.StartOfTurn);
        Assert.False(q.RestartInterruptedTurn);
    }

    [Fact]
    public void A_combatant_can_be_pulled_out_of_the_middle_of_the_queue()
    {
        var q = new TurnQueue();
        q.PushTail(1, true);
        q.PushTail(2, true);
        q.PushTail(3, true);

        q.Remove(2);
        Assert.Equal([1, 3], q.Order);

        q.Remove(99);                       // absent: no change, no throw
        Assert.Equal([1, 3], q.Order);
    }

    [Fact]
    public void Attack_counters_decrement_toward_the_caller()
    {
        var q = new TurnQueue();
        q.Push(1, false, freeAttacks: 2, guardAttacks: 1);

        Assert.Equal(1, q.DecrementFreeAttacks());
        Assert.Equal(0, q.DecrementFreeAttacks());
        Assert.Equal(0, q.DecrementGuardAttacks());
    }

    // ---- the state dispatch ----------------------------------------------------------------

    [Fact]
    public void Combat_over_outranks_everything_else()
    {
        // The precedence is the state machine. An encounter already won must not keep taking
        // turns, so this order is not cosmetic.
        var round = new CombatRound(_ => CombatantState.Attacking);
        round.Queue.Push(0, true, 0, 0);
        round.IsStartingNewRound = true;
        round.IsOver = true;

        Assert.Equal(CombatState.CombatOver, round.State());
    }

    [Fact]
    public void A_new_round_outranks_a_new_combatant()
    {
        var round = new CombatRound(_ => CombatantState.Attacking);
        round.Queue.Push(0, true, 0, 0);          // sets StartOfTurn
        Assert.True(round.IsStartOfTurn);

        round.IsStartingNewRound = true;
        Assert.Equal(CombatState.StartNewRound, round.State());

        round.IsStartingNewRound = false;
        Assert.Equal(CombatState.NewCombatant, round.State());
    }

    [Fact]
    public void Once_the_turn_is_under_way_the_combatants_own_state_shows_through()
    {
        var round = new CombatRound(_ => CombatantState.Casting);
        round.IsStartingNewRound = false;
        round.Queue.Push(0, true, 0, 0);
        round.Queue.NotStartOfTurn();

        Assert.Equal(CombatState.Casting, round.State());
    }

    [Fact]
    public void An_empty_queue_reports_no_state_rather_than_reading_a_combatant()
    {
        var round = new CombatRound(_ => throw new InvalidOperationException("should not be asked"));
        round.IsStartingNewRound = false;

        Assert.Equal(CombatState.None, round.State());
    }

    [Fact]
    public void A_free_attacker_is_distinguished_by_not_spending_its_own_turn()
    {
        // The !AffectsStats term is what separates an interrupting attack from an ordinary turn
        // that happens to have attacks left.
        var round = new CombatRound();

        round.Queue.Push(1, affectStats: true, freeAttacks: 3, guardAttacks: 0);
        Assert.False(round.IsFreeAttacker);

        round.Queue.Push(2, affectStats: false, freeAttacks: 3, guardAttacks: 0);
        Assert.True(round.IsFreeAttacker);
        Assert.False(round.IsGuardAttacker);

        round.Queue.Push(3, affectStats: false, freeAttacks: 0, guardAttacks: 1);
        Assert.False(round.IsFreeAttacker);
        Assert.True(round.IsGuardAttacker);
    }

    // ---- turn advance ----------------------------------------------------------------------

    [Fact]
    public void Advance_returns_the_queued_combatant_before_looking_at_initiative()
    {
        var round = new CombatRound();
        round.Queue.Push(5, true, 0, 0);

        int dude = round.Advance(_ => false, i => i + 1, combatantCount: 8);
        Assert.Equal(5, dude);
        Assert.Equal(1, round.CurrentInitiative);   // never had to walk
    }

    [Fact]
    public void Advance_drains_finished_combatants_off_the_queue()
    {
        var round = new CombatRound();
        round.Queue.PushTail(1, true);
        round.Queue.PushTail(2, true);
        round.Queue.PushTail(3, true);

        // 1 and 2 are done; 3 is not.
        int dude = round.Advance(i => i < 3, _ => 99, combatantCount: 4);

        Assert.Equal(3, dude);
        Assert.Equal([3], round.Queue.Order);
    }

    [Fact]
    public void Advance_walks_the_initiative_order_when_the_queue_is_empty()
    {
        // Initiative 1..22, lowest first. The combatant found is pushed, so it reports a start of
        // turn -- which is how the dispatch knows to announce a new combatant.
        var round = new CombatRound();
        var initiative = new[] { 7, 3, 3, 12 };

        int first = round.Advance(_ => false, i => initiative[i], combatantCount: 4);
        Assert.Equal(1, first);                     // initiative 3, and index 1 comes before 2
        Assert.Equal(3, round.CurrentInitiative);
        Assert.True(round.IsStartOfTurn);
    }

    [Fact]
    public void The_round_is_spent_when_initiative_runs_past_its_limit()
    {
        var round = new CombatRound();

        // Everybody has already acted.
        int dude = round.Advance(_ => true, _ => 5, combatantCount: 4);

        Assert.Equal(CombatMap.NoDude, dude);
        Assert.True(round.CurrentInitiative > CombatRound.MaxInitiative);
    }

    [Fact]
    public void Initiative_above_the_limit_is_never_reached()
    {
        var round = new CombatRound();
        Assert.Equal(CombatMap.NoDude,
                     round.Advance(_ => false, _ => CombatRound.MaxInitiative + 1,
                                   combatantCount: 2));
    }

    [Fact]
    public void Beginning_a_round_rewinds_initiative_and_announces_itself()
    {
        var round = new CombatRound();
        round.Advance(_ => true, _ => 5, combatantCount: 2);
        Assert.True(round.CurrentInitiative > 1);

        round.BeginRound();

        Assert.Equal(1, round.Round);
        Assert.Equal(1, round.CurrentInitiative);
        Assert.True(round.IsStartingNewRound);
        Assert.Equal(CombatState.StartNewRound, round.State());
    }

    [Fact]
    public void A_last_round_still_runs_and_the_next_rollover_ends_the_fight()
    {
        var round = new CombatRound();
        round.BeginRound();
        round.IsLastRound = true;

        Assert.False(round.IsOver);         // the round marked last runs to completion

        round.BeginRound();
        Assert.True(round.IsOver);
    }

    [Fact]
    public void A_fight_where_nothing_happens_eventually_stops()
    {
        var round = new CombatRound();
        for (int i = 0; i < CombatRound.MaxIdleRounds - 1; i++)
        {
            round.RecordActivity(anythingHappened: false);
        }
        Assert.False(round.IsOver);

        round.RecordActivity(anythingHappened: false);
        Assert.True(round.IsOver);
    }

    [Fact]
    public void Any_activity_resets_the_idle_count()
    {
        var round = new CombatRound();
        for (int i = 0; i < CombatRound.MaxIdleRounds - 1; i++)
        {
            round.RecordActivity(anythingHappened: false);
        }

        round.RecordActivity(anythingHappened: true);
        Assert.Equal(0, round.IdleRounds);

        round.RecordActivity(anythingHappened: false);
        Assert.False(round.IsOver);
    }

    [Fact]
    public void A_whole_round_can_be_driven_from_start_to_rollover()
    {
        // Everybody acts once, in initiative order, and the round then rolls over. This is the
        // loop RunEvent.cpp drives; nothing here needs a combatant model.
        var initiative = new[] { 5, 2, 9, 2 };
        var acted = new List<int>();
        var done = new bool[4];

        var round = new CombatRound(_ => CombatantState.None);
        round.BeginRound();
        Assert.Equal(CombatState.StartNewRound, round.State());
        round.IsStartingNewRound = false;

        for (int guard = 0; guard < 50; guard++)
        {
            int dude = round.Advance(i => done[i], i => initiative[i], combatantCount: 4);
            if (dude == CombatMap.NoDude)
            {
                break;
            }

            Assert.Equal(CombatState.NewCombatant, round.State());
            round.Queue.NotStartOfTurn();

            acted.Add(dude);
            done[dude] = true;
        }

        // Initiative 2 first (indices 1 and 3, in index order), then 5, then 9.
        Assert.Equal([1, 3, 0, 2], acted);

        round.BeginRound();
        Assert.Equal(2, round.Round);
        Assert.Equal(1, round.CurrentInitiative);
    }
}
