using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers moving a character up and down the marching order (ENCAMP / ALTER / ORDER).
/// </summary>
/// <remarks>
/// <b>The ends wrap rather than stopping</b>, which is the whole shape of the screen: a player
/// holding a key cycles the party instead of jamming against slot one.
/// </remarks>
public class PartyOrderTests
{
    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static readonly ItemList NoItems = new([], new ReadyItems([]));

    private static CharacterRecord Member(string name) =>
        NewCharacter.Blank with { Name = name };

    private static Party Of(params string[] names)
    {
        var party = new Party();
        foreach (string name in names)
        {
            party.Add(new Character(Member(name), UAF.Rules.MoneyRules.Default));
        }
        return party;
    }

    private static string[] Names(Party party) => [.. party.Members.Select(m => m.Name)];

    [Fact]
    public void A_party_of_one_cannot_be_reordered()
    {
        var party = Of("Aramil");

        party.MoveActiveEarlier();
        party.MoveActiveLater();

        Assert.Equal(["Aramil"], Names(party));
        Assert.Equal(0, party.ActiveCharacter);
    }

    [Fact]
    public void An_empty_party_is_left_alone()
    {
        var party = Of();

        party.MoveActiveEarlier();
        party.MoveActiveLater();

        Assert.Empty(party.Members);
    }

    [Fact]
    public void Moving_earlier_swaps_with_the_one_in_front()
    {
        var party = Of("A", "B", "C");
        party.ActiveCharacter = 2;

        party.MoveActiveEarlier();

        Assert.Equal(["A", "C", "B"], Names(party));
        Assert.Equal(1, party.ActiveCharacter);
    }

    [Fact]
    public void Moving_later_swaps_with_the_one_behind()
    {
        var party = Of("A", "B", "C");
        party.ActiveCharacter = 0;

        party.MoveActiveLater();

        Assert.Equal(["B", "A", "C"], Names(party));
        Assert.Equal(1, party.ActiveCharacter);
    }

    [Fact]
    public void The_active_index_follows_the_character_it_moved()
    {
        // Which is what lets a second press keep moving the same one.
        var party = Of("A", "B", "C", "D");
        party.ActiveCharacter = 3;

        party.MoveActiveEarlier();
        party.MoveActiveEarlier();

        Assert.Equal(["A", "D", "B", "C"], Names(party));
        Assert.Equal(1, party.ActiveCharacter);
        Assert.Equal("D", party.Active!.Name);
    }

    [Fact]
    public void Moving_the_front_character_earlier_sends_it_to_the_back()
    {
        // Everyone else shifts forward and the active one lands last -- a rotation, not a refusal.
        var party = Of("A", "B", "C");
        party.ActiveCharacter = 0;

        party.MoveActiveEarlier();

        Assert.Equal(["B", "C", "A"], Names(party));
        Assert.Equal(2, party.ActiveCharacter);
    }

    [Fact]
    public void Moving_the_back_character_later_brings_it_to_the_front()
    {
        var party = Of("A", "B", "C");
        party.ActiveCharacter = 2;

        party.MoveActiveLater();

        Assert.Equal(["C", "A", "B"], Names(party));
        Assert.Equal(0, party.ActiveCharacter);
    }

    [Fact]
    public void A_full_cycle_returns_the_party_to_where_it_started()
    {
        var party = Of("A", "B", "C", "D");
        party.ActiveCharacter = 0;

        for (int i = 0; i < party.Members.Count; i++)
        {
            party.MoveActiveLater();
        }

        Assert.Equal(["A", "B", "C", "D"], Names(party));
        Assert.Equal(0, party.ActiveCharacter);
    }
}
