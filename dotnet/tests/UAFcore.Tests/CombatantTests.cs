using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the combatant entity (<c>COMBATANT</c>, <c>Combatant.cpp</c>).
/// </summary>
public class CombatantTests
{
    private static Combatant Fighter(int index = 0, bool friendly = true) =>
        new(index, friendly, new CombatantIcon(1, 1), $"fighter{index}");

    [Theory]
    [InlineData(CharacterStatus.Okay, true)]
    [InlineData(CharacterStatus.Running, true)]
    [InlineData(CharacterStatus.Animated, true)]
    [InlineData(CharacterStatus.Fled, false)]
    [InlineData(CharacterStatus.Gone, false)]
    [InlineData(CharacterStatus.TempGone, false)]
    [InlineData(CharacterStatus.Dead, false)]
    [InlineData(CharacterStatus.Unconscious, false)]
    [InlineData(CharacterStatus.Dying, false)]
    [InlineData(CharacterStatus.Petrified, false)]
    public void Being_on_the_combat_map_is_decided_by_status(CharacterStatus status, bool expected)
    {
        // The test names the statuses that keep a combatant OFF the map, so anything unlisted --
        // Animated and Running included -- counts as present.
        var c = Fighter();
        c.Status = status;
        Assert.Equal(expected, c.IsOnCombatMap());
    }

    [Fact]
    public void Unconscious_and_petrified_can_each_be_allowed_explicitly()
    {
        var c = Fighter();

        c.Status = CharacterStatus.Unconscious;
        Assert.False(c.IsOnCombatMap());
        Assert.True(c.IsOnCombatMap(unconsciousOk: true));

        c.Status = CharacterStatus.Petrified;
        Assert.False(c.IsOnCombatMap());
        Assert.True(c.IsOnCombatMap(petrifiedOk: true));

        // Dying counts as unconscious, which is why that is not a plain equality test.
        c.Status = CharacterStatus.Dying;
        Assert.True(c.IsUnconscious);
        Assert.True(c.IsOnCombatMap(unconsciousOk: true));
    }

    [Fact]
    public void A_healthy_combatant_with_a_turn_left_is_not_done()
    {
        var c = Fighter();
        Assert.False(c.IsDone());
    }

    [Fact]
    public void Petrified_short_circuits_before_the_readiness_check()
    {
        // The status test comes first in the reference, so a petrified combatant is done whatever
        // any readiness script would have said.
        var c = Fighter();
        c.Status = CharacterStatus.Petrified;
        c.IsCombatReady = true;

        Assert.True(c.IsDone());
        Assert.False(c.TurnIsDone);     // and it did not latch on the way through
    }

    [Fact]
    public void A_combatant_a_script_has_benched_is_done()
    {
        var c = Fighter();
        c.IsCombatReady = false;
        Assert.True(c.IsDone());
    }

    [Fact]
    public void Asking_whether_a_combatant_is_done_can_latch_it()
    {
        // IsDone mutates: being off the map sets TurnIsDone rather than merely returning true, so
        // the answer sticks even if the status later changes back. The round depends on the latch.
        var c = Fighter();
        c.Status = CharacterStatus.Fled;

        Assert.True(c.IsDone());
        Assert.True(c.TurnIsDone);

        c.Status = CharacterStatus.Okay;
        Assert.True(c.IsDone());        // still done -- the latch, not the status
    }

    [Fact]
    public void A_free_attacker_with_no_target_has_nothing_to_do()
    {
        // The latch happens before the return, not after, so the very first ask already says
        // done -- and it stays done for the ordinary question afterwards.
        var c = Fighter();
        Assert.True(c.IsDone(freeAttack: true));
        Assert.True(c.TurnIsDone);
        Assert.True(c.IsDone());

        var withTarget = Fighter();
        withTarget.Target = 3;
        Assert.False(withTarget.IsDone(freeAttack: true));
        Assert.False(withTarget.TurnIsDone);
    }

    [Fact]
    public void Ending_a_turn_only_works_for_the_combatant_at_the_top_of_the_queue()
    {
        // The reference guards on qcomb.Top() == self, so an interrupted combatant cannot end a
        // turn it is not currently taking.
        var queue = new TurnQueue();
        queue.Push(0, affectStats: true, 0, 0);

        var other = Fighter(index: 1);
        other.EndTurn(queue, CombatantState.Guarding);

        Assert.Equal(CombatantState.Guarding, other.State);   // state still changes
        Assert.False(other.TurnIsDone);                       // but nothing else does
        Assert.Equal(0, queue.Top);                           // and the queue is untouched
    }

    [Fact]
    public void Ending_an_ordinary_turn_latches_done_and_pops_the_queue()
    {
        var queue = new TurnQueue();
        queue.Push(0, affectStats: true, 0, 0);

        var c = Fighter();
        c.EndTurn(queue);

        Assert.True(c.TurnIsDone);
        Assert.Equal(CombatantState.None, c.State);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void A_spent_interrupter_pops_without_latching_the_combatant()
    {
        // The one case that does not latch: AffectStats false and no attacks banked -- which is
        // exactly the entry about to be popped anyway.
        var queue = new TurnQueue();
        queue.Push(0, affectStats: false, freeAttacks: 0, guardAttacks: 0);

        var c = Fighter();
        c.EndTurn(queue);

        Assert.False(c.TurnIsDone);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void An_interrupter_with_attacks_banked_still_latches()
    {
        var queue = new TurnQueue();
        queue.Push(0, affectStats: false, freeAttacks: 2, guardAttacks: 0);

        var c = Fighter();
        c.EndTurn(queue);

        Assert.True(c.TurnIsDone);
    }

    // ---- the round reset -------------------------------------------------------------------

    [Fact]
    public void A_new_round_clears_the_turn_and_refills_movement()
    {
        var c = Fighter();
        c.TurnIsDone = true;
        c.Movement = 7;
        c.DiagonalMoves = 3;
        c.State = CombatantState.Attacking;

        c.BeginRound(attacksThisRound: 2);

        Assert.False(c.TurnIsDone);
        Assert.Equal(CombatantState.None, c.State);
        Assert.Equal(0, c.Movement);
        Assert.Equal(0, c.DiagonalMoves);
        Assert.Equal(2, c.AvailableAttacks);
    }

    [Fact]
    public void A_combatant_that_cannot_act_is_skipped_entirely()
    {
        // Gated on charCanTakeAction() && IsDone(): an unconscious combatant keeps whatever it had
        // rather than being handed a fresh round.
        var c = Fighter();
        c.Status = CharacterStatus.Unconscious;
        c.TurnIsDone = true;
        c.Movement = 7;

        c.BeginRound(attacksThisRound: 2);

        Assert.True(c.TurnIsDone);
        Assert.Equal(7, c.Movement);
        Assert.Equal(0, c.AvailableAttacks);
    }

    [Fact]
    public void A_combatant_still_mid_turn_is_left_alone()
    {
        var c = Fighter();
        c.TurnIsDone = false;           // has not finished
        c.Movement = 4;

        c.BeginRound(attacksThisRound: 2);

        Assert.Equal(4, c.Movement);
        Assert.Equal(0, c.AvailableAttacks);
    }

    [Fact]
    public void A_caster_keeps_its_state_but_still_gets_a_fresh_round_of_movement()
    {
        // The attacks and movement block sits OUTSIDE the ICS_Casting check, so returning early
        // for a caster would leave it with last round's movement.
        var c = Fighter();
        c.TurnIsDone = true;
        c.State = CombatantState.Casting;
        c.Movement = 7;

        c.BeginRound(attacksThisRound: 3);

        Assert.Equal(CombatantState.Casting, c.State);
        Assert.True(c.TurnIsDone);      // the state reset, including this, was skipped
        Assert.Equal(0, c.Movement);    // but this was not
        Assert.Equal(3, c.AvailableAttacks);
    }

    [Fact]
    public void Guarding_persists_by_a_different_rule_for_auto_and_player_combatants()
    {
        var auto = Fighter();
        auto.TurnIsDone = true;
        auto.State = CombatantState.Guarding;
        auto.BeginRound(1, continueGuarding: false, isAuto: true);
        Assert.Equal(CombatantState.Guarding, auto.State);       // kept outright

        var player = Fighter();
        player.TurnIsDone = true;
        player.State = CombatantState.Guarding;
        player.BeginRound(1, continueGuarding: true, isAuto: false);
        Assert.Equal(CombatantState.ContinueGuarding, player.State);

        var notAsked = Fighter();
        notAsked.TurnIsDone = true;
        notAsked.State = CombatantState.Guarding;
        notAsked.BeginRound(1, continueGuarding: false, isAuto: false);
        Assert.Equal(CombatantState.None, notAsked.State);
    }

    [Fact]
    public void An_unused_half_attack_carries_over_but_cannot_be_banked()
    {
        // new + leftover, clamped to ceil(new). So half an attack survives one round...
        var c = Fighter();
        c.TurnIsDone = true;
        c.AvailableAttacks = 0.5;
        c.BeginRound(attacksThisRound: 1.5);
        Assert.Equal(2.0, c.AvailableAttacks);

        // ...but a combatant that hoards them stays at the cap.
        c.TurnIsDone = true;
        c.BeginRound(attacksThisRound: 1.5);
        Assert.Equal(2.0, c.AvailableAttacks);
    }

    // ---- driving a round with real combatants ----------------------------------------------

    [Fact]
    public void A_round_runs_to_completion_over_real_combatants()
    {
        // The round clock and the entity, together: everybody acts once in initiative order and
        // the round then rolls over. This is what the two pieces were built to do.
        var all = new List<Combatant>
        {
            Fighter(0), Fighter(1), Fighter(2, friendly: false), Fighter(3, friendly: false),
        };
        int[] initiative = [5, 2, 9, 2];
        for (int i = 0; i < all.Count; i++)
        {
            all[i].Initiative = initiative[i];
        }

        var round = new CombatRound(i => all[i].State);
        round.BeginRound();
        Assert.Equal(CombatState.StartNewRound, round.State());
        round.IsStartingNewRound = false;

        foreach (var c in all)
        {
            c.BeginRound(attacksThisRound: 1);
        }

        var acted = new List<int>();
        for (int guard = 0; guard < 50; guard++)
        {
            int dude = round.Advance(i => all[i].IsDone(), i => all[i].Initiative, all.Count);
            if (dude == CombatMap.NoDude)
            {
                break;
            }

            round.Queue.NotStartOfTurn();
            acted.Add(dude);
            all[dude].EndTurn(round.Queue, CombatantState.Guarding);
        }

        Assert.Equal([1, 3, 0, 2], acted);
        Assert.All(all, c => Assert.True(c.TurnIsDone));
        Assert.True(round.Queue.IsEmpty);
    }

    [Fact]
    public void A_combatant_that_flees_mid_round_stops_being_offered_turns()
    {
        var all = new List<Combatant> { Fighter(0), Fighter(1), Fighter(2) };
        for (int i = 0; i < all.Count; i++)
        {
            all[i].Initiative = i + 1;
            all[i].BeginRound(attacksThisRound: 1);
        }

        var round = new CombatRound(i => all[i].State);
        round.IsStartingNewRound = false;

        // Combatant 0 acts, then combatant 1 flees before its turn comes round.
        int first = round.Advance(i => all[i].IsDone(), i => all[i].Initiative, all.Count);
        Assert.Equal(0, first);
        all[0].EndTurn(round.Queue);

        all[1].Status = CharacterStatus.Fled;

        int next = round.Advance(i => all[i].IsDone(), i => all[i].Initiative, all.Count);
        Assert.Equal(2, next);      // 1 was skipped
    }
}
