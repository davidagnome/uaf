using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the attribute store a design's scripts read and write.</summary>
public class AttributeListTests
{
    private static AslEntry Entry(string key, string value,
                                  AttributeFlags flags = AttributeFlags.None) =>
        new(key, (byte)flags, value);

    // ---- inserting and finding -----------------------------------------------------------------

    [Fact]
    public void An_attribute_can_be_stored_and_read_back()
    {
        var list = new AttributeList();

        Assert.False(list.Insert("Combat Result", "Win"));
        Assert.Equal("Win", list.Find("Combat Result"));
    }

    [Fact]
    public void Insert_reports_whether_the_key_was_already_there()
    {
        // The reference returns TRUE for the overwrite case, which reads backwards from "did it
        // work" -- callers testing it are testing for a pre-existing value.
        var list = new AttributeList();

        Assert.False(list.Insert("k", "first"));
        Assert.True(list.Insert("k", "second"));
        Assert.Equal("second", list.Find("k"));
    }

    [Fact]
    public void An_existing_entrys_flags_are_replaced_not_merged()
    {
        // Inserting over a read-only attribute with no flags makes it writable. The reference does
        // not guard that.
        var list = new AttributeList();
        list.Insert("k", "v", AttributeFlags.ReadOnly);
        list.Insert("k", "v2");

        Assert.Equal(AttributeFlags.None, (AttributeFlags)list.Entry("k")!.Flags);
    }

    [Fact]
    public void A_key_that_is_not_there_reads_as_null()
    {
        Assert.Null(new AttributeList().Find("missing"));
        Assert.Null(new AttributeList().Entry("missing"));
    }

    [Fact]
    public void Entries_come_out_ordered_by_key()
    {
        var list = new AttributeList();
        list.Insert("zebra", "1");
        list.Insert("apple", "2");
        list.Insert("mango", "3");

        Assert.Equal(["apple", "mango", "zebra"], list.Entries.Select(e => e.Key));
    }

    // ---- removing ------------------------------------------------------------------------------

    [Fact]
    public void Removing_returns_what_was_held()
    {
        var list = new AttributeList();
        list.Insert("k", "v");

        Assert.Equal("v", list.Remove("k"));
        Assert.Null(list.Remove("k"));
    }

    [Fact]
    public void Read_only_is_not_enforced_by_the_container()
    {
        // The flag's comment says such an attribute "can't be deleted", but Delete takes a key and
        // removes whatever it finds. The protection lives in the callers and the save path.
        var list = new AttributeList();
        list.Insert("k", "v", AttributeFlags.ReadOnly);

        Assert.Equal("v", list.Remove("k"));
    }

    // ---- saving --------------------------------------------------------------------------------

    [Fact]
    public void A_save_holds_everything_except_read_only()
    {
        // A read-only attribute comes from the design and is reloaded with it; storing it would
        // let a stale copy override the design later.
        var list = new AttributeList();
        list.Insert("design", "fixed", AttributeFlags.ReadOnly);
        list.Insert("progress", "chapter2", AttributeFlags.Modified);
        list.Insert("plain", "value");

        Assert.Equal(["plain", "progress"], list.Saveable.Select(e => e.Key));
    }

    [Fact]
    public void Read_only_survives_a_restore_and_the_rest_is_replaced()
    {
        var list = new AttributeList();
        list.Insert("design", "fixed", AttributeFlags.ReadOnly);
        list.Insert("stale", "old");

        list.CommitRestore([Entry("fresh", "new"), Entry("design", "hijacked",
                                                         AttributeFlags.ReadOnly)]);

        Assert.Equal("fixed", list.Find("design"));   // the save cannot override it
        Assert.Equal("new", list.Find("fresh"));
        Assert.Null(list.Find("stale"));              // and cannot leave it behind
    }

    [Fact]
    public void A_restore_from_nothing_clears_the_saveable_half_only()
    {
        var list = new AttributeList();
        list.Insert("design", "fixed", AttributeFlags.ReadOnly);
        list.Insert("progress", "chapter2");

        list.CommitRestore([]);

        Assert.Equal(1, list.Count);
        Assert.Equal("fixed", list.Find("design"));
    }

    // ---- the combat verdict --------------------------------------------------------------------

    [Fact]
    public void The_combat_result_key_is_spelled_with_a_space()
    {
        // A design tests it by that exact name.
        Assert.Equal("Combat Result", AttributeList.CombatResultKey);
    }

    [Theory]
    [InlineData(CombatResult.Win, "Win")]
    [InlineData(CombatResult.Lose, "Lose")]
    [InlineData(CombatResult.Flee, "Flee")]
    [InlineData(CombatResult.LoseButNeverDies, "LoseButNeverDies")]
    public void Every_verdict_has_the_string_a_design_compares_against(
        CombatResult result, string expected)
    {
        var list = new AttributeList();
        list.Insert(AttributeList.CombatResultKey, CombatAftermath.ResultText(result),
                    AttributeFlags.Modified);

        Assert.Equal(expected, list.Find(AttributeList.CombatResultKey));
    }

    [Fact]
    public void The_verdict_is_written_as_a_change_during_play()
    {
        // ASLF_MODIFIED, as the results screen writes it -- a change rather than a first
        // insertion, so it goes into the save.
        var list = new AttributeList();
        list.Insert(AttributeList.CombatResultKey, "Win", AttributeFlags.Modified);

        Assert.Contains(list.Saveable, e => e.Key == AttributeList.CombatResultKey);
    }
}
