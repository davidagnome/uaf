using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers what the passage of game time does to a party.</summary>
public class PartyTimeTests
{
    private const int Day = RestClock.MinutesPerDay;

    private static Party Of(params CharacterStatus[] statuses)
    {
        var party = new Party();

        foreach (var status in statuses)
        {
            var member = new Character(
                NewCharacter.Blank with { Name = $"M{party.Count}" }, MoneyRules.Default);

            member.Status = status;
            member.HitPoints = 5;
            member.MaxHitPoints = 20;
            party.Add(member);
        }

        return party;
    }

    [Fact]
    public void A_day_of_unbroken_rest_heals_a_point_each()
    {
        var party = Of(CharacterStatus.Okay, CharacterStatus.Okay);
        var clock = new RestClock();

        var passed = PartyTime.Advance(party, clock, Day, resting: true, newDay: false);

        Assert.Equal(2, passed.Healed.Count);
        Assert.All(party.Members, m => Assert.Equal(6, m.HitPoints));
        Assert.True(passed.Redraw);
    }

    [Fact]
    public void Walking_for_a_day_heals_nothing()
    {
        var party = Of(CharacterStatus.Okay);
        var clock = new RestClock();

        var passed = PartyTime.Advance(party, clock, Day, resting: false, newDay: false);

        Assert.Empty(passed.Healed);
        Assert.Equal(5, party.Members[0].HitPoints);
    }

    [Fact]
    public void The_auto_heal_skips_the_unconscious()
    {
        // They are woken when the rest screen opens rather than by the clock, so a character who
        // somehow stays under gains nothing from the day.
        var party = Of(CharacterStatus.Unconscious);
        var clock = new RestClock();

        for (int i = 0; i < 10; i++)
        {
            PartyTime.Advance(party, clock, Day, resting: true, newDay: false);
        }

        Assert.Equal(CharacterStatus.Unconscious, party.Members[0].Status);
        Assert.Equal(5, party.Members[0].HitPoints);
    }

    [Fact]
    public void Opening_a_rest_wakes_the_unconscious_at_one_hit_point()
    {
        // PARTY::BeginResting, called from the rest screen's OnInitialEvent. Woken, not healed --
        // so that the day's auto-heal, which skips the unconscious, can reach them at all.
        var party = Of(CharacterStatus.Unconscious, CharacterStatus.Okay,
                       CharacterStatus.Dead);

        var woken = PartyTime.BeginResting(party);

        Assert.Single(woken);
        Assert.Equal(CharacterStatus.Okay, party.Members[0].Status);
        Assert.Equal(1, party.Members[0].HitPoints);

        Assert.Equal(5, party.Members[1].HitPoints);            // already awake, untouched
        Assert.Equal(CharacterStatus.Dead, party.Members[2].Status);
    }

    [Fact]
    public void A_script_can_refuse_to_wake_someone()
    {
        var party = Of(CharacterStatus.Unconscious);

        var woken = PartyTime.BeginResting(party, vetoed: _ => true);

        Assert.Empty(woken);
        Assert.Equal(CharacterStatus.Unconscious, party.Members[0].Status);
    }

    [Fact]
    public void Waking_then_resting_a_day_heals_the_point()
    {
        // The two halves together: BeginResting brings them round at one hit point, and the day
        // then finds them Okay and adds to it.
        var party = Of(CharacterStatus.Unconscious);
        var clock = new RestClock();

        PartyTime.BeginResting(party);
        PartyTime.Advance(party, clock, Day, resting: true, newDay: false);

        Assert.Equal(2, party.Members[0].HitPoints);
    }

    [Fact]
    public void The_dead_and_the_petrified_are_not_alive_but_the_dying_are()
    {
        Assert.True(PartyTime.IsAlive(Of(CharacterStatus.Dying).Members[0]));
        Assert.True(PartyTime.IsAlive(Of(CharacterStatus.Running).Members[0]));
        Assert.False(PartyTime.IsAlive(Of(CharacterStatus.Dead).Members[0]));
        Assert.False(PartyTime.IsAlive(Of(CharacterStatus.Petrified).Members[0]));
        Assert.False(PartyTime.IsAlive(Of(CharacterStatus.Gone).Members[0]));
    }

    [Fact]
    public void A_petrified_character_rests_all_night_for_nothing()
    {
        var party = Of(CharacterStatus.Petrified);
        var clock = new RestClock();

        PartyTime.Advance(party, clock, Day, resting: true, newDay: false);

        Assert.Equal(5, party.Members[0].HitPoints);
    }

    [Fact]
    public void An_interrupted_rest_loses_everything_it_had_banked()
    {
        var party = Of(CharacterStatus.Okay);
        var clock = new RestClock();

        PartyTime.Advance(party, clock, Day - 1, resting: true, newDay: false);
        PartyTime.Advance(party, clock, 1, resting: false, newDay: false);
        var passed = PartyTime.Advance(party, clock, Day - 1, resting: true, newDay: false);

        Assert.Empty(passed.Healed);
        Assert.Equal(5, party.Members[0].HitPoints);
    }

    [Fact]
    public void Nothing_elapsed_does_nothing_at_all()
    {
        var party = Of(CharacterStatus.Okay);
        var clock = new RestClock();

        var passed = PartyTime.Advance(party, clock, 0, resting: true, newDay: true);

        Assert.False(passed.Redraw);
        Assert.Empty(passed.Healed);
        Assert.Null(passed.Memorized);
    }

    // ---- memorising while resting ----------------------------------------------------------------

    private static Character Caster(Party party, params (string Id, int Level, int Want)[] spells)
    {
        var member = party.Members[0];

        foreach (var (id, level, want) in spells)
        {
            member.Book.Add(id, level).Selected = want;
        }

        return member;
    }

    [Fact]
    public void Resting_memorises_a_minute_at_a_time()
    {
        var party = Of(CharacterStatus.Okay);
        var caster = Caster(party, ("magic missile", 1, 1));
        var clock = new RestClock();

        // Fifteen minutes for a first-level spell.
        PartyTime.Advance(party, clock, 14, resting: true, newDay: false);
        Assert.Equal(0, caster.Book.Entries[0].Memorized);

        PartyTime.Advance(party, clock, 1, resting: true, newDay: false);
        Assert.Equal(1, caster.Book.Entries[0].Memorized);
    }

    [Fact]
    public void A_coarse_step_still_gives_every_minute_to_memorising()
    {
        // Unlike the auto-heal, which grants at most one point a cycle, the memorisation loop runs
        // once per elapsed minute -- so a single long step finishes several copies.
        var party = Of(CharacterStatus.Okay);
        var caster = Caster(party, ("magic missile", 1, 3));
        var clock = new RestClock();

        PartyTime.Advance(party, clock, 45, resting: true, newDay: false);

        Assert.Equal(3, caster.Book.Entries[0].Memorized);
    }

    [Fact]
    public void Nobody_memorises_while_awake()
    {
        var party = Of(CharacterStatus.Okay);
        var caster = Caster(party, ("magic missile", 1, 1));
        var clock = new RestClock();

        PartyTime.Advance(party, clock, 600, resting: false, newDay: false);

        Assert.Equal(0, caster.Book.Entries[0].Memorized);
    }

    [Fact]
    public void A_character_who_cannot_cast_is_skipped()
    {
        var party = Of(CharacterStatus.Okay);
        var caster = Caster(party, ("magic missile", 1, 1));
        var clock = new RestClock();

        PartyTime.Advance(party, clock, 60, resting: true, newDay: false,
                          canCast: _ => false);

        Assert.Equal(0, caster.Book.Entries[0].Memorized);
    }

    [Fact]
    public void Finishing_a_copy_announces_it_by_name()
    {
        var party = Of(CharacterStatus.Okay);
        Caster(party, ("mm", 1, 1));
        var clock = new RestClock();

        var passed = PartyTime.Advance(party, clock, 15, resting: true, newDay: false,
                                       nameOf: _ => "Magic Missile");

        Assert.Equal("M0 memorizes Magic Missile", passed.Memorized);
    }

    [Fact]
    public void Only_the_last_announcement_survives_the_step()
    {
        // A minute that finishes nothing clears the paused text, so a long step that ends quietly
        // shows nothing at all -- however many copies it finished on the way.
        var party = Of(CharacterStatus.Okay);
        Caster(party, ("mm", 1, 1));
        var clock = new RestClock();

        var passed = PartyTime.Advance(party, clock, 60, resting: true, newDay: false,
                                       nameOf: _ => "Magic Missile");

        Assert.Equal(1, party.Members[0].Book.Entries[0].Memorized);
        Assert.Null(passed.Memorized);
    }

    [Fact]
    public void An_empty_party_is_left_alone()
    {
        var passed = PartyTime.Advance(new Party(), new RestClock(), Day, true, false);

        Assert.Empty(passed.Healed);
    }
}
