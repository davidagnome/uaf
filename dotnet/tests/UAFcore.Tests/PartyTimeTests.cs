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
    public void An_unconscious_character_never_recovers_by_resting()
    {
        // The auto-heal skips the unconscious, and the block that would wake them is unreachable
        // -- see PartyTime's remarks. A character who goes down stays down.
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
    }

    [Fact]
    public void An_empty_party_is_left_alone()
    {
        var passed = PartyTime.Advance(new Party(), new RestClock(), Day, true, false);

        Assert.Empty(passed.Healed);
    }
}
