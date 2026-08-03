using UAF.Rules;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// NPCs joining and leaving the party (<c>ADD_NPC_DATA</c> and <c>REMOVE_NPC_DATA</c>).
/// </summary>
/// <remarks>
/// Both events are one call each into <c>PARTY::addNPCToParty</c> / <c>removeNPCFromParty</c>, so
/// that is what these exercise. Three of each appear in the corpus, all in one design.
/// </remarks>
public class EventNpcTests
{
    private const int Cap = 6;

    private static EventControl Control() =>
        new(0, 0, 0, (int)ChainTrigger.Always, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// A party member. <paramref name="npc"/> sets <c>CharacterRecord.Type</c> to
    /// <see cref="EventNpc.NpcType"/> — <b>both events gate on it</b>, so a record left at the
    /// player-character kind is invisible to <c>addNPCToParty</c> and <c>isNPCinParty</c> alike.
    /// </summary>
    private static CharacterRecord Member(string name = "Aramil", int charisma = 10,
                                          int morale = 50, bool npc = false) =>
        new(0, npc ? EventNpc.NpcType : (byte)1, "human", 0, "fighter", 0, 0, 0, "", 0, name, name,
            0, 0, 0, 0, 0, 10, 10, 0, 0, 0, morale, 0, 0, 0, 0, 0, 0,
            0, 0, 0, new AbilityScores(0, 0, 0, 0, 0, 0, charisma),
            0, 0, 0, 0, 0, 0, [new BaseclassStats("fighter", 0, 0, 0, 0)], [], [], 0, 0, 0,
            null, 0, null, 0, 0, 0, 0, 0, "", 0, "",
            new SpellBook(0, []), 0, 0, [], [], NoPic, new ItemList([], new ReadyItems([])),
            new SpecabBlock([], [], []), []);

    // CharacterId, not Name, is what isNPCinParty matches on -- so the fixture sets both.

    private static Party Roster(params CharacterRecord[] members)
    {
        var party = new Party { Pooled = new Purse(MoneyRules.Default) };
        foreach (var member in members)
        {
            party.Add(new Character(member, MoneyRules.Default));
        }
        return party;
    }

    private static AddNpcEvent AddEvent(string id = "Sir Kay", int hitPointMod = 100) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.AddNpc, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            0, id, hitPointMod, 0);

    private static RemoveNpcEvent RemoveEvent(string id = "Sir Kay") =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.RemoveNPCEvent, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            0, id);

    private static Func<string, CharacterRecord?> Design(params CharacterRecord[] records) =>
        id => records.FirstOrDefault(
            r => string.Equals(r.Name, id, StringComparison.OrdinalIgnoreCase));

    // ---- the morale table ------------------------------------------------------------------------

    [Theory]
    [InlineData(3, -30)]
    [InlineData(8, -5)]
    [InlineData(14, 5)]
    [InlineData(18, 40)]
    public void The_charisma_table_is_the_reference_table(int charisma, int expected)
    {
        Assert.Equal(expected, EventNpc.MoraleModifier(charisma));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(13)]
    public void Nine_to_thirteen_is_a_deliberate_hole(int charisma)
    {
        // The reference's own `default: break; // 9..13`.
        Assert.Equal(0, EventNpc.MoraleModifier(charisma));
    }

    [Theory]
    [InlineData(19)]
    [InlineData(25)]
    [InlineData(2)]
    public void Anything_off_the_ends_of_the_table_scores_nothing(int charisma)
    {
        // The switch is on discrete values, not ranges, and stops at 18 -- so an exceptional
        // charisma granted by a spell effect earns the same as an average one. Almost certainly
        // not the intent, and reproduced rather than extended.
        Assert.Equal(0, EventNpc.MoraleModifier(charisma));
    }

    // ---- joining ---------------------------------------------------------------------------------

    [Fact]
    public void A_named_npc_joins_the_party()
    {
        var party = Roster(Member("Aramil"));

        var outcome = EventNpc.Add(AddEvent("Sir Kay"), party,
                                   Design(Member("Sir Kay", npc: true)), MoneyRules.Default, Cap);

        Assert.Equal(AddNpcResult.Joined, outcome.Result);
        Assert.Equal(2, party.Count);
        Assert.Equal("Sir Kay", party.Members[1].Name);
    }

    [Fact]
    public void An_id_the_design_does_not_define_seats_nobody_and_says_nothing()
    {
        var party = Roster(Member("Aramil"));

        var outcome = EventNpc.Add(AddEvent("Nobody"), party,
                                   Design(Member("Sir Kay", npc: true)), MoneyRules.Default, Cap);

        Assert.Equal(AddNpcResult.NoSuchNpc, outcome.Result);
        Assert.Null(outcome.Joined);
        Assert.Single(party.Members);
    }

    [Fact]
    public void A_full_roster_is_the_one_failure_the_reference_reports()
    {
        var party = Roster([.. Enumerable.Range(0, Cap).Select(i => Member($"PC{i}"))]);

        var outcome = EventNpc.Add(AddEvent("Sir Kay"), party,
                                   Design(Member("Sir Kay", npc: true)), MoneyRules.Default, Cap);

        Assert.Equal(AddNpcResult.PartyFull, outcome.Result);
        Assert.Equal(Cap, party.Count);
    }

    [Fact]
    public void The_morale_bonus_comes_from_the_party_already_seated()
    {
        // The table is indexed by the best charisma among the EXISTING members, not the joiner's,
        // which is why the modifier is reported -- it is otherwise invisible.
        var party = Roster(Member("Aramil", charisma: 18));

        var outcome = EventNpc.Add(AddEvent("Sir Kay"), party,
                                   Design(Member("Sir Kay", charisma: 3, npc: true)),
                                   MoneyRules.Default, Cap);

        Assert.Equal(40, outcome.MoraleModifier);
        Assert.Equal(18, EventNpc.HighestCharisma(Roster(Member(charisma: 18))));
    }

    [Fact]
    public void Joining_an_empty_party_earns_no_bonus()
    {
        var outcome = EventNpc.Add(AddEvent("Sir Kay"), Roster(),
                                   Design(Member("Sir Kay", npc: true)), MoneyRules.Default, Cap);

        Assert.Equal(AddNpcResult.Joined, outcome.Result);
        Assert.Equal(0, outcome.MoraleModifier);
    }

    // ---- leaving ---------------------------------------------------------------------------------

    [Fact]
    public void A_matching_npc_leaves_and_its_slot_is_reported()
    {
        var party = Roster(Member("Aramil"), Member("Sir Kay", npc: true), Member("Jozan"));

        var outcome = EventNpc.Remove(RemoveEvent("Sir Kay"), party);

        Assert.True(outcome.Removed);
        Assert.Equal(1, outcome.Index);
        Assert.Equal("Sir Kay", outcome.Member!.Name);
        Assert.Equal(["Aramil", "Jozan"], party.Members.Select(m => m.Name));
    }

    [Fact]
    public void Removing_someone_who_is_not_there_changes_nothing()
    {
        var party = Roster(Member("Aramil"));

        var outcome = EventNpc.Remove(RemoveEvent("Sir Kay"), party);

        Assert.False(outcome.Removed);
        Assert.Equal(-1, outcome.Index);
        Assert.Single(party.Members);
    }

    // ---- the roster gap this needed --------------------------------------------------------------

    [Fact]
    public void Removing_the_last_member_pulls_the_active_index_back()
    {
        // The index is what TAB cycles and what every "who tries"/"who pays" event reads, so
        // leaving it past the end would make Active read off the roster.
        var party = Roster(Member("Aramil"), Member("Sir Kay", npc: true));
        party.ActiveCharacter = 1;

        EventNpc.Remove(RemoveEvent("Sir Kay"), party);

        Assert.Equal(0, party.ActiveCharacter);
        Assert.Equal("Aramil", party.Active!.Name);
    }

    [Fact]
    public void Emptying_the_roster_leaves_the_active_index_at_zero()
    {
        var party = Roster(Member("Aramil", npc: true));
        party.ActiveCharacter = 0;

        EventNpc.Remove(RemoveEvent("Aramil"), party);

        Assert.Empty(party.Members);
        Assert.Equal(0, party.ActiveCharacter);
        Assert.Null(party.Active);
    }

    [Fact]
    public void Removing_an_index_outside_the_roster_is_ignored()
    {
        var party = Roster(Member("Aramil"));

        party.RemoveAt(5);
        party.RemoveAt(-1);

        Assert.Single(party.Members);
    }
}
