using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers <c>HEAL_PARTY_DATA</c> — hit points, curses, and the drain that was never written.
/// </summary>
/// <remarks>
/// No shipped design contains one of these, so nothing observed can be leaned on: every case here
/// is pinned against <c>PARTY::HealParty</c> (<c>Party.cpp:4925</c>) and
/// <c>CHARACTER::SetHitPoints</c> (<c>Char.cpp:14787</c>) read line by line. The characters are
/// minimal records, as in <see cref="PartyTests"/>, because what is under test is which field each
/// branch touches.
/// </remarks>
public class EventHealTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>A die whose every answer is scripted, and which records what it was asked for.</summary>
    private sealed class Dice(params int[] rolls)
    {
        private readonly Queue<int> queued = new(rolls);

        /// <summary>The number of sides asked for, in order.</summary>
        public List<int> Asked { get; } = [];

        public int Roll(int sides)
        {
            Asked.Add(sides);
            return queued.Count > 0 ? queued.Dequeue() : 1;
        }
    }

    private static ItemInstance Item(string itemId, byte cursed) =>
        new(Key: 1, itemId, LegacyItemId: 0, ReadyLocation: 0, Quantity: 1, Identified: 1,
            Charges: 1, cursed, Paid: 0);

    private static CharacterRecord Member(string name, int hitPoints = 10, int maxHitPoints = 10,
                                          CharacterStatus status = CharacterStatus.Okay,
                                          ItemInstance[]? items = null)
    {
        var carried = new ItemList(items ?? [], new ReadyItems([]));

        return new CharacterRecord(
            0, 0, "human", (int)Gender.Male, "fighter", 0, 0, (int)status, "", 0, name, "",
            0, 0, 0, 0, 0, hitPoints, maxHitPoints, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, 0),
            0, 0, 0, 0, 0, 0, [new BaseclassStats("fighter", 0, 0, 0, 0)], [], [], 0, 0, 0,
            null, 0, null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, carried,
            new SpecabBlock([], [], []), []);
    }

    private static Party Roster(params CharacterRecord[] members)
    {
        var party = new Party();
        foreach (var member in members)
        {
            party.Add(new Character(member, UAF.Rules.MoneyRules.Default));
        }

        return party;
    }

    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static HealPartyEvent Heal(PartyAffect who = PartyAffect.EntireParty,
                                       int amount = 0, byte mode = 0,
                                       bool hitPoints = true, bool curse = false,
                                       bool drain = false, byte chance = 100) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.HealParty, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            HealHitPoints: hitPoints ? 1 : 0, HealDrain: drain ? 1 : 0,
            HealCurse: curse ? 1 : 0, chance, (int)who, amount, mode);

    // ---- what a zero amount means --------------------------------------------------------------

    [Fact]
    public void A_zero_amount_and_mode_together_mean_the_pre_0882_full_heal()
    {
        // Below 0.882 the two fields are not serialized at all, so the reference runs on whatever
        // Clear left there -- 100 and 1, not 0 and 0 (GameEvent.cpp:13726). The reader has nothing
        // to read and writes zeroes, so the zero pair has to be read back as the full heal it was.
        // Treating it as "add 0 literal" would turn every pre-0.882 heal event into a no-op.
        Assert.Equal((HealAdjust.AddPercentOfMax, 100), EventHeal.Adjustment(Heal()));

        var party = Roster(Member("hurt", hitPoints: 3, maxHitPoints: 10));
        EventHeal.Apply(Heal(), party, new Dice().Roll);

        Assert.Equal(10, party.Members[0].HitPoints);
    }

    [Fact]
    public void The_sentinel_is_the_pair_not_the_mode_on_its_own()
    {
        // Mode 0 with a real amount is the editor's "Add to Current", which designs at 0.882 and
        // above use normally. Keying the legacy default off the mode alone would break all of them.
        Assert.Equal((HealAdjust.AddToCurrent, 5), EventHeal.Adjustment(Heal(amount: 5)));
    }

    // ---- the three adjustment modes ------------------------------------------------------------

    [Fact]
    public void Add_to_current_is_a_literal_number_of_hit_points()
    {
        var party = Roster(Member("hurt", hitPoints: 2, maxHitPoints: 30));

        var result = EventHeal.Apply(Heal(amount: 6, mode: 0), party, new Dice().Roll);

        Assert.Equal(8, party.Members[0].HitPoints);
        Assert.Equal((1, 6), (result.Healed, result.HitPointsRestored));
    }

    [Fact]
    public void Add_percent_of_max_is_a_share_of_the_character_s_own_maximum()
    {
        // Each character's own max, not a party figure -- so the same event heals two characters
        // by different amounts.
        var party = Roster(Member("small", hitPoints: 1, maxHitPoints: 12),
                           Member("large", hitPoints: 1, maxHitPoints: 40));

        EventHeal.Apply(Heal(amount: 25, mode: 1), party, new Dice().Roll);

        Assert.Equal(4, party.Members[0].HitPoints);     // 1 + 25% of 12
        Assert.Equal(11, party.Members[1].HitPoints);    // 1 + 25% of 40
    }

    [Fact]
    public void Set_to_percent_of_max_assigns_rather_than_adds_and_never_lowers_anyone()
    {
        var party = Roster(Member("low", hitPoints: 2, maxHitPoints: 20),
                           Member("high", hitPoints: 18, maxHitPoints: 20));

        EventHeal.Apply(Heal(amount: 50, mode: 2), party, new Dice().Roll);

        Assert.Equal(10, party.Members[0].HitPoints);    // set to half, not raised by half
        // The reference writes `if (totalHp < currHp) totalHp = currHp` -- without it this
        // character would be knocked from 18 down to 10 by an event called Heal Party.
        Assert.Equal(18, party.Members[1].HitPoints);
    }

    [Fact]
    public void The_two_percent_modes_truncate_at_different_points()
    {
        // Mode 1 converts current + share to int; mode 2 converts the share on its own. With
        // max 30 and 35% the share is 10.500000000000002, so mode 1 on 1 hit point gives 11 and
        // mode 2 gives 10 -- one number apart from the same inputs.
        var adding = Roster(Member("a", hitPoints: 1, maxHitPoints: 30));
        var setting = Roster(Member("b", hitPoints: 1, maxHitPoints: 30));

        EventHeal.Apply(Heal(amount: 35, mode: 1), adding, new Dice().Roll);
        EventHeal.Apply(Heal(amount: 35, mode: 2), setting, new Dice().Roll);

        Assert.Equal(11, adding.Members[0].HitPoints);
        Assert.Equal(10, setting.Members[0].HitPoints);
    }

    [Fact]
    public void A_negative_share_truncates_toward_zero_after_it_is_added_not_before()
    {
        // 10 hit points and a share of -3.3000000000000003 truncates from 6.699... to 6. Rounding
        // the share to -3 first and adding would give 7, which is the natural way to write it and
        // is wrong by one on every fractional negative.
        var party = Roster(Member("hurt", hitPoints: 10, maxHitPoints: 10));

        EventHeal.Apply(Heal(amount: -33, mode: 1), party, new Dice().Roll);

        Assert.Equal(6, party.Members[0].HitPoints);
    }

    // ---- what SetHitPoints does with the answer ------------------------------------------------

    [Fact]
    public void Healing_clamps_to_the_character_s_own_maximum()
    {
        var party = Roster(Member("hurt", hitPoints: 4, maxHitPoints: 10));

        var result = EventHeal.Apply(Heal(amount: 900, mode: 0), party, new Dice().Roll);

        Assert.Equal(10, party.Members[0].HitPoints);
        // The reported total is the clamped change, not the 900 the event asked for.
        Assert.Equal(6, result.HitPointsRestored);
    }

    [Fact]
    public void A_negative_amount_kills_and_stops_at_ten_below_zero()
    {
        // Nothing validates HowMuchHP in the editor and the adding modes pass it straight through,
        // so an event named Heal Party really can be a killing one.
        var party = Roster(Member("doomed", hitPoints: 5, maxHitPoints: 20));

        var result = EventHeal.Apply(Heal(amount: -500, mode: 0), party, new Dice().Roll);

        Assert.Equal(Character.DeadAt, party.Members[0].HitPoints);
        Assert.Equal(CharacterStatus.Dead, party.Members[0].Status);
        Assert.Equal(-15, result.HitPointsRestored);     // 5 down to -10, and negative on purpose
    }

    [Fact]
    public void Between_minus_one_and_minus_nine_only_a_character_who_was_okay_is_marked_dead()
    {
        // Exactly -10 is Dead unconditionally; above it the reference marks Dead only from Okay,
        // leaving an already petrified or fled character in the state they were in.
        var party = Roster(Member("okay", hitPoints: 1, maxHitPoints: 20),
                           Member("stone", hitPoints: 1, maxHitPoints: 20,
                                  status: CharacterStatus.Petrified),
                           Member("gone", hitPoints: 1, maxHitPoints: 20,
                                  status: CharacterStatus.Petrified));

        EventHeal.Apply(Heal(amount: -5, mode: 0), party, new Dice().Roll);

        Assert.Equal(CharacterStatus.Dead, party.Members[0].Status);
        Assert.Equal(CharacterStatus.Petrified, party.Members[1].Status);

        // ...and the -10 floor overrides that, so a big enough blow does mark the same character.
        EventHeal.Apply(Heal(amount: -500, mode: 0), party, new Dice().Roll);
        Assert.Equal(CharacterStatus.Dead, party.Members[2].Status);
    }

    [Fact]
    public void Landing_on_exactly_zero_leaves_the_status_alone()
    {
        // SetHitPoints marks Dead below zero and HealParty clears the status above it, so zero
        // falls through both tests. A `>= 0` in either place would close the gap and be wrong.
        var party = Roster(Member("out", hitPoints: 4, maxHitPoints: 20,
                                  status: CharacterStatus.Unconscious));

        EventHeal.Apply(Heal(amount: -4, mode: 0), party, new Dice().Roll);

        Assert.Equal(0, party.Members[0].HitPoints);
        Assert.Equal(CharacterStatus.Unconscious, party.Members[0].Status);
    }

    [Fact]
    public void Any_heal_that_lands_above_zero_clears_the_status()
    {
        // This is the half of the event that has nothing to do with hit points: the status write
        // is unconditional above zero, so a heal event is also the design's cure for a character
        // who is unconscious, poisoned or dead-but-not-gone.
        var party = Roster(Member("down", hitPoints: -6, maxHitPoints: 10,
                                  status: CharacterStatus.Dead));

        EventHeal.Apply(Heal(), party, new Dice().Roll);  // the pre-0.882 full heal

        Assert.Equal(4, party.Members[0].HitPoints);     // -6 + 100% of 10
        Assert.Equal(CharacterStatus.Okay, party.Members[0].Status);
    }

    // ---- who it reaches, and the roll --------------------------------------------------------

    [Fact]
    public void The_random_member_is_drawn_before_the_switch_so_every_heal_costs_a_roll()
    {
        // rndDude is the first line of HealParty, above the switch that decides whether anything
        // wants it. Drawing it lazily would shift every later roll in a recorded run.
        var dice = new Dice(1);
        EventHeal.Apply(Heal(PartyAffect.EntireParty), Roster(Member("a"), Member("b")),
                        dice.Roll);

        Assert.Equal([2], dice.Asked);                   // one roll, of a die the size of the party

        var none = new Dice(1);
        EventHeal.Apply(Heal(PartyAffect.None), Roster(Member("a")), none.Roll);
        Assert.Equal([1], none.Asked);                   // even the branch that returns immediately
    }

    [Fact]
    public void An_empty_party_draws_nothing()
    {
        // RollDice returns early for a die of no sides without touching the generator.
        var dice = new Dice();

        EventHeal.Apply(Heal(PartyAffect.OneAtRandom), new Party(), dice.Roll);

        Assert.Empty(dice.Asked);
    }

    [Fact]
    public void Entire_party_reaches_everyone_and_active_character_reaches_one()
    {
        var all = Roster(Member("a", hitPoints: 1), Member("b", hitPoints: 1));
        EventHeal.Apply(Heal(PartyAffect.EntireParty, amount: 3, mode: 0), all, new Dice().Roll);
        Assert.Equal([4, 4], all.Members.Select(m => m.HitPoints));

        var one = Roster(Member("a", hitPoints: 1), Member("b", hitPoints: 1));
        one.ActiveCharacter = 1;
        EventHeal.Apply(Heal(PartyAffect.ActiveCharacter, amount: 3, mode: 0), one,
                        new Dice().Roll);
        Assert.Equal([1, 4], one.Members.Select(m => m.HitPoints));
    }

    [Fact]
    public void One_at_random_uses_the_roll_taken_before_the_switch_minus_one()
    {
        // rndDude = RollDice(numCharacters, 1) - 1, so a roll of 2 on a party of three is the
        // middle member. Forgetting the -1 shifts the whole party by one and can never pick the
        // first.
        var party = Roster(Member("a", hitPoints: 1), Member("b", hitPoints: 1),
                           Member("c", hitPoints: 1));

        EventHeal.Apply(Heal(PartyAffect.OneAtRandom, amount: 3, mode: 0), party,
                        new Dice(2).Roll);

        Assert.Equal([1, 4, 1], party.Members.Select(m => m.HitPoints));
    }

    [Fact]
    public void Chance_on_each_rolls_once_per_character_in_each_of_its_two_passes()
    {
        // The hit points pass and the curse pass roll separately, so a character can be healed and
        // stay cursed. One shared roll per character would correlate them.
        var party = Roster(Member("a", hitPoints: 1, items: [Item("ring", cursed: 1)]),
                           Member("b", hitPoints: 1, items: [Item("ring", cursed: 1)]));

        //           party  hp:a hp:b  curse:a curse:b
        var dice = new Dice(1, 10, 90, 90, 10);

        var result = EventHeal.Apply(
            Heal(PartyAffect.ChanceOnEach, amount: 3, mode: 0, curse: true, chance: 50),
            party, dice.Roll);

        Assert.Equal([2, 100, 100, 100, 100], dice.Asked);
        Assert.Equal(4, party.Members[0].HitPoints);     // rolled 10, under the chance of 50
        Assert.Equal(1, party.Members[1].HitPoints);     // rolled 90, over it
        Assert.Equal(1, party.Members[0].Items[0].Cursed);   // ...and the reverse on the curses
        Assert.Equal(0, party.Members[1].Items[0].Cursed);
        Assert.Equal((1, 1), (result.Healed, result.CursesLifted));
    }

    [Fact]
    public void Chance_is_ignored_by_every_affect_mode_but_chance_on_each()
    {
        // The editor greys the box out and forces it to 100 for the other three, but the runtime
        // never reads it there -- so a design carrying a chance of 0 still heals the whole party.
        var party = Roster(Member("a", hitPoints: 1), Member("b", hitPoints: 1));

        EventHeal.Apply(Heal(PartyAffect.EntireParty, amount: 3, mode: 0, chance: 0), party,
                        new Dice().Roll);

        Assert.Equal([4, 4], party.Members.Select(m => m.HitPoints));
    }

    [Fact]
    public void No_party_member_and_an_affect_outside_the_enum_both_do_nothing()
    {
        // The reference returns for NoPartyMember and its switch has no default, so an unknown
        // value falls straight out of it. Neither is an error.
        var party = Roster(Member("a", hitPoints: 1));

        EventHeal.Apply(Heal(PartyAffect.None, amount: 3, mode: 0), party, new Dice().Roll);
        Assert.Equal(1, party.Members[0].HitPoints);

        var stray = Heal(amount: 3, mode: 0) with { Who = 99 };
        Assert.Equal(new HealOutcome(), EventHeal.Apply(stray, party, new Dice().Roll));
        Assert.Equal(1, party.Members[0].HitPoints);
    }

    // ---- the bare `else return` ----------------------------------------------------------------

    [Fact]
    public void An_adjustment_mode_above_two_abandons_the_whole_event_curses_included()
    {
        // The reference's chain of ifs ends in `else return`, which leaves HealParty rather than
        // the arithmetic -- so the curse pass below it never runs. Treating it as "skip the hit
        // points" would silently lift curses the reference leaves in place.
        var party = Roster(Member("a", hitPoints: 1, items: [Item("ring", cursed: 1)]));

        var result = EventHeal.Apply(Heal(amount: 5, mode: 7, curse: true), party,
                                     new Dice().Roll);

        Assert.True(result.Abandoned);
        Assert.Equal(1, party.Members[0].HitPoints);
        Assert.Equal(1, party.Members[0].Items[0].Cursed);
    }

    [Fact]
    public void Under_entire_party_it_stops_at_the_first_character()
    {
        // Only reachable with a mode the editor cannot produce, but the shape is worth pinning:
        // the return leaves the loop, it does not continue it.
        var party = Roster(Member("a", hitPoints: 1), Member("b", hitPoints: 1));

        var result = EventHeal.Apply(Heal(amount: 5, mode: 3), party, new Dice().Roll);

        Assert.Equal((0, true), (result.Healed, result.Abandoned));
    }

    [Fact]
    public void An_unreadable_mode_is_harmless_when_nothing_reaches_the_arithmetic()
    {
        var party = Roster(Member("a", items: [Item("ring", cursed: 1)]));

        // The `else return` sits inside `if (data.HealHP)`, so an event that only lifts curses
        // never reaches it.
        var noHitPoints = EventHeal.Apply(
            Heal(amount: 5, mode: 9, hitPoints: false, curse: true), party, new Dice().Roll);

        Assert.False(noHitPoints.Abandoned);
        Assert.Equal(1, noHitPoints.CursesLifted);

        // ...and under ChanceOnEach the guard is per character, so a member who fails the hit
        // points roll never reaches the arithmetic and cannot abandon the event -- leaving the
        // curse pass free to run on a mode the reference cannot understand.
        party.Members[0].Items[0] = party.Members[0].Items[0] with { Cursed = 1 };
        var missed = EventHeal.Apply(
            Heal(PartyAffect.ChanceOnEach, amount: 5, mode: 9, curse: true, chance: 50),
            party, new Dice(1, 90, 10).Roll);            // party, hp (fails), curse (passes)

        Assert.False(missed.Abandoned);
        Assert.Equal(1, missed.CursesLifted);
    }

    // ---- curses and the drain that is not there ------------------------------------------------

    [Fact]
    public void Lifting_curses_clears_the_flag_on_the_character_s_own_items()
    {
        var party = Roster(Member("a", items: [Item("sword", cursed: 1), Item("rope", cursed: 0),
                                               Item("ring", cursed: 1)]));

        var result = EventHeal.Apply(Heal(curse: true, hitPoints: false), party, new Dice().Roll);

        Assert.Equal([0, 0, 0], party.Members[0].Items.Select(i => (int)i.Cursed));
        Assert.Equal(2, result.CursesLifted);            // the uncursed one is not counted
    }

    [Fact]
    public void The_curse_pass_cannot_reach_the_party_s_own_carried_list()
    {
        // A known gap, not an oversight: the reference walks each character's myItems and has no
        // party-level list at all. Party.Carried is this port's stand-in for treasure pickups, so
        // a cursed item acquired that way stays cursed until inventories are per-character.
        var party = Roster(Member("a"));
        party.Carried.Add(Item("cursed loot", cursed: 1));

        EventHeal.Apply(Heal(curse: true, hitPoints: false), party, new Dice().Roll);

        Assert.Equal(1, party.Carried[0].Cursed);
    }

    [Fact]
    public void Healing_drain_does_nothing_at_all()
    {
        // All four branches of the reference reach WriteDebugString("Heal Drain not coded yet")
        // and stop. A design that ticks the box gets a screen of text and no restored levels.
        var record = Member("drained", hitPoints: 4);
        var party = Roster(record);
        party.Members[0].Baseclasses[0].PreviousLevel = 3;

        var result = EventHeal.Apply(Heal(drain: true, hitPoints: false), party, new Dice().Roll);

        Assert.Equal(3, party.Members[0].Baseclasses[0].PreviousLevel);
        Assert.Equal(new HealOutcome(), result);
    }
}
