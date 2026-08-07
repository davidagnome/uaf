using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>Covers a record's ability list and the read-only rule that guards it.</summary>
public class SpecabListTests
{
    private static SpecabList Carrying(bool readOnly, params (string Name, string Value)[] items) =>
        new(readOnly, items.Select(i => new KeyValuePair<string, string>(i.Name, i.Value)));

    [Fact]
    public void An_ability_that_is_there_gives_its_value()
    {
        Assert.Equal("7", Carrying(false, ("Ward", "7")).Get("Ward"));
    }

    [Fact]
    public void An_ability_that_is_not_gives_the_sentinel()
    {
        // Not an empty string, so a blank value stays distinguishable from an absent one.
        var list = Carrying(false, ("Ward", ""));

        Assert.Equal("", list.Get("Ward"));
        Assert.Equal(GpdlScriptContext.NoSuchAbility, list.Get("Missing"));
    }

    [Fact]
    public void A_live_records_list_accepts_writes()
    {
        var list = Carrying(false);

        Assert.True(list.Set("Ward", "7"));
        Assert.Equal("7", list.Get("Ward"));
    }

    [Fact]
    public void Writing_an_ability_that_is_there_replaces_it()
    {
        var list = Carrying(false, ("Ward", "7"));

        list.Set("Ward", "9");

        Assert.Equal("9", list.Get("Ward"));
        Assert.Single(list.Abilities);
    }

    [Fact]
    public void A_database_records_list_refuses_writes()
    {
        // Items, monsters, spells, classes and abilities all construct theirs read-only: the
        // definition is shared by every copy in the design.
        var list = Carrying(true, ("Ward", "7"));

        Assert.False(list.Set("Ward", "9"));
        Assert.Equal("7", list.Get("Ward"));
    }

    [Fact]
    public void Deleting_gives_back_the_value_that_was_there()
    {
        var list = Carrying(false, ("Ward", "7"));

        Assert.Equal("7", list.Delete("Ward"));
        Assert.Empty(list.Abilities);
    }

    [Fact]
    public void Deleting_what_is_not_there_gives_the_sentinel()
    {
        Assert.Equal(GpdlScriptContext.NoSuchAbility, Carrying(false).Delete("Ward"));
    }

    [Fact]
    public void A_refused_delete_is_indistinguishable_from_an_absent_one()
    {
        // Both answer the sentinel, so a script writing to a database record cannot tell it was
        // refused.
        var readOnly = Carrying(true, ("Ward", "7"));

        Assert.Equal(GpdlScriptContext.NoSuchAbility, readOnly.Delete("Ward"));
        Assert.Equal(GpdlScriptContext.NoSuchAbility, Carrying(false).Delete("Ward"));
        Assert.Equal("7", readOnly.Get("Ward"));      // and nothing was removed
    }

    [Fact]
    public void Every_refusal_is_counted_not_only_the_first()
    {
        // The reference's suppression guard has no braces, so only the dialog flag is conditional
        // and the log line runs on every refusal. The count keeps that shape.
        var list = Carrying(true);

        list.Set("A", "1");
        list.Set("B", "2");
        list.Delete("C");

        Assert.Equal(3, list.Refused);
    }

    [Fact]
    public void A_writable_list_refuses_nothing()
    {
        var list = Carrying(false);

        list.Set("A", "1");
        list.Delete("A");

        Assert.Equal(0, list.Refused);
    }
}
