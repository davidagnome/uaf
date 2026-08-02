using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the packed list of spells a character may still learn.</summary>
public class KnowableSpellsTests
{
    private static AttributeList Stored(string list)
    {
        var attributes = new AttributeList();
        attributes.Insert(KnowableSpells.Key, list);
        return attributes;
    }

    private static string Raw(AttributeList attributes) =>
        attributes.Find(KnowableSpells.Key) ?? string.Empty;

    // ---- adding --------------------------------------------------------------------------------

    [Fact]
    public void The_first_spell_becomes_the_whole_list()
    {
        var attributes = new AttributeList();

        Assert.True(KnowableSpells.Add(attributes, "sleep"));
        Assert.Equal("?sleep", Raw(attributes));
    }

    [Fact]
    public void Further_spells_are_concatenated_with_a_leading_delimiter()
    {
        // A bare concatenation of ?name entries -- the delimiter prefixes rather than separates.
        var attributes = new AttributeList();
        KnowableSpells.Add(attributes, "sleep");
        KnowableSpells.Add(attributes, "magic missile");

        Assert.Equal("?sleep?magic missile", Raw(attributes));
    }

    [Fact]
    public void A_spell_already_in_the_list_is_not_added_twice()
    {
        var attributes = Stored("?sleep");

        Assert.False(KnowableSpells.Add(attributes, "sleep"));
        Assert.Equal("?sleep", Raw(attributes));
    }

    [Fact]
    public void A_spell_the_character_already_knows_is_never_added()
    {
        var attributes = new AttributeList();

        Assert.False(KnowableSpells.Add(attributes, "sleep", alreadyKnown: true));
        Assert.Empty(Raw(attributes));
    }

    [Fact]
    public void A_name_that_is_a_prefix_of_another_entry_silently_fails_to_be_added()
    {
        // Membership is a substring test, not an entry test: "?Fire" is found inside "?Fireball".
        // The storage format's own consequence, and a design's spell names were chosen against it.
        var attributes = Stored("?Fireball");

        Assert.False(KnowableSpells.Add(attributes, "Fire"));
        Assert.Equal("?Fireball", Raw(attributes));
    }

    [Fact]
    public void The_other_way_round_works_because_the_longer_name_is_not_a_substring()
    {
        var attributes = Stored("?Fire");

        Assert.True(KnowableSpells.Add(attributes, "Fireball"));
        Assert.Equal("?Fire?Fireball", Raw(attributes));
    }

    // ---- removing ------------------------------------------------------------------------------

    [Fact]
    public void The_last_entry_is_removed_as_a_suffix()
    {
        // It has nothing after it, so the bounded "?name?" search cannot find it -- which is why
        // the suffix branch exists at all.
        var attributes = Stored("?sleep?magic missile");

        Assert.True(KnowableSpells.Remove(attributes, "magic missile"));
        Assert.Equal("?sleep", Raw(attributes));
    }

    [Fact]
    public void An_entry_in_the_middle_keeps_the_delimiter_that_follows_it()
    {
        // The trailing delimiter belongs to the next entry, so the removal leaves it behind.
        var attributes = Stored("?a?b?c");

        Assert.True(KnowableSpells.Remove(attributes, "b"));
        Assert.Equal("?a?c", Raw(attributes));
    }

    [Fact]
    public void The_first_entry_of_several_is_removed_cleanly()
    {
        var attributes = Stored("?a?b?c");

        Assert.True(KnowableSpells.Remove(attributes, "a"));
        Assert.Equal("?b?c", Raw(attributes));
    }

    [Fact]
    public void The_only_entry_is_removed_leaving_nothing()
    {
        var attributes = Stored("?sleep");

        Assert.True(KnowableSpells.Remove(attributes, "sleep"));
        Assert.Empty(Raw(attributes));
    }

    [Fact]
    public void Removing_a_name_that_is_not_there_leaves_the_list_alone()
    {
        var attributes = Stored("?a?b");

        Assert.False(KnowableSpells.Remove(attributes, "c"));
        Assert.Equal("?a?b", Raw(attributes));
    }

    [Fact]
    public void A_list_shorter_than_the_entry_is_rejected_before_either_branch()
    {
        var attributes = Stored("?a");

        Assert.False(KnowableSpells.Remove(attributes, "something long"));
    }

    [Fact]
    public void Removing_from_an_empty_store_does_nothing()
    {
        Assert.False(KnowableSpells.Remove(new AttributeList(), "sleep"));
    }

    // ---- clearing and reading ------------------------------------------------------------------

    [Fact]
    public void Clearing_drops_the_attribute_entirely()
    {
        var attributes = Stored("?a?b");

        Assert.True(KnowableSpells.Clear(attributes));
        Assert.Null(attributes.Find(KnowableSpells.Key));
        Assert.False(KnowableSpells.Clear(attributes));
    }

    [Fact]
    public void The_list_unpacks_to_its_names()
    {
        Assert.Equal(["sleep", "magic missile", "shield"],
                     KnowableSpells.All(Stored("?sleep?magic missile?shield")));
    }

    [Fact]
    public void An_absent_or_empty_list_unpacks_to_nothing()
    {
        Assert.Empty(KnowableSpells.All(new AttributeList()));
        Assert.Empty(KnowableSpells.All(Stored(string.Empty)));
    }

    [Fact]
    public void Adding_and_removing_round_trips_through_the_packing()
    {
        var attributes = new AttributeList();
        foreach (string name in new[] { "alpha", "beta", "gamma", "delta" })
        {
            KnowableSpells.Add(attributes, name);
        }

        KnowableSpells.Remove(attributes, "beta");
        KnowableSpells.Remove(attributes, "delta");

        Assert.Equal(["alpha", "gamma"], KnowableSpells.All(attributes));
    }

    // ---- where it lives ------------------------------------------------------------------------

    [Fact]
    public void The_list_is_written_as_a_change_during_play()
    {
        // ASLF_MODIFIED, so it goes into the save with the character.
        var attributes = new AttributeList();
        KnowableSpells.Add(attributes, "sleep");

        Assert.Contains(attributes.Saveable, e => e.Key == KnowableSpells.Key);
    }
}
