using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the casting clock: when a begun spell lands, and what withdraws it.</summary>
public class SpellCastingTests
{
    private static (int WaitUntil, SpellCastingTime Timing) Schedule(
        int castingTime, SpellCastingTime type, int initiative = 5, int round = 3) =>
        PendingSpellList.Schedule(castingTime, type, initiative, round);

    // ---- scheduling ----------------------------------------------------------------------------

    [Fact]
    public void An_immediate_spell_lands_on_the_casters_own_initiative()
    {
        var (wait, timing) = Schedule(0, SpellCastingTime.Immediate, initiative: 7);

        Assert.Equal(7, wait);
        Assert.Equal(SpellCastingTime.Immediate, timing);
    }

    [Fact]
    public void Casting_time_is_added_to_the_initiative()
    {
        var (wait, timing) = Schedule(4, SpellCastingTime.Initiative, initiative: 5);

        Assert.Equal(9, wait);
        Assert.Equal(SpellCastingTime.Initiative, timing);
    }

    [Fact]
    public void A_spell_that_would_run_past_the_initiative_order_lands_at_the_end_of_the_round()
    {
        // Not deferred to a later round: the reference says outright it does not want to wait many
        // rounds, and re-times the spell to this round's end.
        var (wait, timing) = Schedule(20, SpellCastingTime.Initiative, initiative: 6, round: 3);

        Assert.Equal(3, wait);
        Assert.Equal(SpellCastingTime.Rounds, timing);
    }

    [Fact]
    public void The_ceiling_is_never_initiative_not_the_last_walked_slot()
    {
        // 23 is INITIATIVE_Never; the walk stops at 22. A spell landing exactly on 23 is left
        // alone by the test (`> INITIATIVE_Never`), and then never comes due on initiative --
        // which is why the round-increment escape in Service exists.
        var (wait, timing) = Schedule(CombatRound.NeverInitiative - 5,
                                      SpellCastingTime.Initiative, initiative: 5);

        Assert.Equal(CombatRound.NeverInitiative, wait);
        Assert.Equal(SpellCastingTime.Initiative, timing);
    }

    [Fact]
    public void A_deferred_spell_is_wrongly_made_immediate_when_the_round_matches_the_initiative()
    {
        // The reference's two tests in the initiative branch are not exclusive. Round 5 and a
        // caster on initiative 5: the spell is re-timed to waitUntil=round=5, and then the second
        // test reads that 5 as an initiative and calls it immediate. Reproduced deliberately.
        var (wait, timing) = Schedule(19, SpellCastingTime.Initiative, initiative: 5, round: 5);

        Assert.Equal(5, wait);
        Assert.Equal(SpellCastingTime.Immediate, timing);
    }

    [Theory]
    [InlineData(SpellCastingTime.Initiative)]
    [InlineData(SpellCastingTime.Rounds)]
    [InlineData(SpellCastingTime.Turns)]
    public void A_casting_time_of_zero_is_immediate_whatever_the_type(SpellCastingTime type)
    {
        var (wait, timing) = Schedule(0, type, initiative: 5, round: 3);

        Assert.Equal(5, wait);
        Assert.Equal(SpellCastingTime.Immediate, timing);
    }

    [Fact]
    public void Rounds_are_counted_from_the_current_round()
    {
        var (wait, timing) = Schedule(2, SpellCastingTime.Rounds, round: 3);

        Assert.Equal(5, wait);
        Assert.Equal(SpellCastingTime.Rounds, timing);
    }

    [Fact]
    public void A_turn_is_ten_rounds()
    {
        var (wait, timing) = Schedule(2, SpellCastingTime.Turns, round: 3);

        Assert.Equal(3 + (2 * PendingSpellList.RoundsPerTurn), wait);
        Assert.Equal(SpellCastingTime.Turns, timing);
    }

    [Fact]
    public void A_negative_casting_time_is_clamped_rather_than_running_backwards()
    {
        var (wait, timing) = Schedule(-4, SpellCastingTime.Rounds, initiative: 5, round: 3);

        Assert.Equal(5, wait);
        Assert.Equal(SpellCastingTime.Immediate, timing);
    }

    // ---- the list ------------------------------------------------------------------------------

    [Fact]
    public void An_immediate_spell_is_never_queued()
    {
        var list = new PendingSpellList();

        int key = list.Begin(caster: 0, "magic missile", castingTime: 0,
                             SpellCastingTime.Immediate, initiative: 5, round: 1);

        Assert.Equal(-1, key);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void A_rounds_typed_spell_with_no_casting_time_is_queued_and_comes_due_at_once()
    {
        // The queue test reads the spell's declared type, not the rewritten timing -- so this one
        // takes a trip through the list and then activates on the very next service.
        var list = new PendingSpellList();

        int key = list.Begin(caster: 0, "bless", castingTime: 0,
                             SpellCastingTime.Rounds, initiative: 5, round: 1);

        Assert.NotEqual(-1, key);
        Assert.Equal(1, list.Count);

        var fired = new List<string>();
        Assert.True(list.Service(0, currentInitiative: 1, currentRound: 1, s => fired.Add(s.SpellId)));
        Assert.Equal(["bless"], fired);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void An_initiative_spell_waits_for_its_slot()
    {
        var list = new PendingSpellList();
        list.Begin(caster: 0, "sleep", castingTime: 4, SpellCastingTime.Initiative,
                   initiative: 5, round: 1);

        Assert.False(list.Service(0, currentInitiative: 8, currentRound: 1, _ => { }));
        Assert.Equal(1, list.Count);

        Assert.True(list.Service(0, currentInitiative: 9, currentRound: 1, _ => { }));
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void A_new_round_forces_through_an_initiative_spell_whose_slot_never_came()
    {
        var list = new PendingSpellList();
        list.Begin(caster: 0, "sleep", castingTime: 17, SpellCastingTime.Initiative,
                   initiative: 6, round: 1);

        Assert.False(list.Service(0, currentInitiative: 1, currentRound: 2, _ => { }));
        Assert.True(list.Service(roundInc: 1, currentInitiative: 1, currentRound: 2, _ => { }));
    }

    [Fact]
    public void A_rounds_spell_waits_for_its_round_and_ignores_the_initiative()
    {
        var list = new PendingSpellList();
        list.Begin(caster: 0, "haste", castingTime: 2, SpellCastingTime.Rounds,
                   initiative: 5, round: 1);

        Assert.False(list.Service(0, currentInitiative: CombatRound.MaxInitiative,
                                  currentRound: 2, _ => { }));
        Assert.True(list.Service(0, currentInitiative: 1, currentRound: 3, _ => { }));
    }

    [Fact]
    public void Spells_activate_in_the_order_they_were_begun()
    {
        var list = new PendingSpellList();
        list.Begin(0, "first", 1, SpellCastingTime.Rounds, 5, 1);
        list.Begin(1, "second", 1, SpellCastingTime.Rounds, 5, 1);

        var fired = new List<string>();
        list.Service(0, 1, 2, s => fired.Add(s.SpellId));

        Assert.Equal(["first", "second"], fired);
    }

    [Fact]
    public void One_spell_coming_due_does_not_drag_the_others_out_with_it()
    {
        // The reference's castIt is declared outside its loop and never reset, so an entry that
        // activates leaves the flag set for every entry after it. Not reproduced -- see Service.
        var list = new PendingSpellList();
        list.Begin(0, "now", 1, SpellCastingTime.Rounds, 5, 1);
        list.Begin(1, "later", 9, SpellCastingTime.Rounds, 5, 1);

        var fired = new List<string>();
        list.Service(0, 1, 2, s => fired.Add(s.SpellId));

        Assert.Equal(["now"], fired);
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void An_interrupted_caster_withdraws_its_own_spell_and_leaves_the_rest()
    {
        var list = new PendingSpellList();
        int mine = list.Begin(0, "mine", 3, SpellCastingTime.Rounds, 5, 1);
        list.Begin(1, "theirs", 3, SpellCastingTime.Rounds, 5, 1);

        Assert.True(list.Remove(mine));
        Assert.False(list.Remove(mine));
        Assert.Equal(1, list.Count);
        Assert.Equal("theirs", list.Spells[0].SpellId);
    }

    [Fact]
    public void Removing_by_caster_clears_everything_that_combatant_had_in_flight()
    {
        var list = new PendingSpellList();
        list.Begin(2, "one", 3, SpellCastingTime.Rounds, 5, 1);
        list.Begin(2, "two", 4, SpellCastingTime.Rounds, 5, 1);
        list.Begin(3, "other", 3, SpellCastingTime.Rounds, 5, 1);

        Assert.True(list.RemoveFor(2));
        Assert.Equal(1, list.Count);
        Assert.Equal(3, list.Spells[0].Caster);
    }

    [Fact]
    public void Keys_are_not_reused_after_a_withdrawal()
    {
        // A caster holds its key across rounds; handing the same number to a later spell would let
        // one caster withdraw another's.
        var list = new PendingSpellList();
        int first = list.Begin(0, "one", 3, SpellCastingTime.Rounds, 5, 1);
        list.Remove(first);
        int second = list.Begin(1, "two", 3, SpellCastingTime.Rounds, 5, 1);

        Assert.NotEqual(first, second);
    }
}
