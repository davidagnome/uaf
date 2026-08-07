using UAF.Data;
using UAF.Scripting;
using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the restart-per-item walk over a character's inventory.</summary>
public class PossessionWalkTests
{
    /// <summary>An item record whose <paramref name="script"/> hook returns <paramref name="answer"/>.</summary>
    private static ItemRecord Record(string ability, string script, string answer) =>
        new(new ItemNames(0, "", "", "", "", "", ""),
            HitArt: null, MissileArt: null,
            new ItemScalars("", 0, 0, 0, 0, 0, 0, 0),
            new ItemCombat(0, 1, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
            new ItemTail(0, 0, 0, [], 0, 0, 0, "", "", 0, 0, null, 0, 0,
                         new SpecabBlock([new SpecabPair(ability, "p")], [], []), []));

    private static SpecialAbility Ability(string name, string script, string answer) =>
        new(name, [new SpecialAbilityEntry(script, $"$RETURN \"{answer}\";",
                                           SpecialAbilityEntryKind.Script)]);

    private static ItemInstance Item(string id, int key) =>
        new(key, id, 0, Inventory.NotReady, 1, 1, 0, 0, 0);

    private static GpdlUnhostedEnvironment Host()
    {
        var host = new GpdlUnhostedEnvironment();
        host.Context.Push();
        return host;
    }

    /// <summary>Every item answers the letter of its id.</summary>
    private static Func<string, ItemRecord?> Database(params string[] ids) =>
        id => ids.Contains(id) ? Record($"SA_{id}", "HOOK", id) : null;

    private static GlobalScripts Scripts(params string[] ids) =>
        new([.. ids.Select(id => Ability($"SA_{id}", "HOOK", id))]);

    private static string Run(IReadOnlyList<ItemInstance> carried, params string[] ids) =>
        PossessionWalk.Run(carried, "HOOK", Database(ids), Scripts(ids), Host());

    // ---- the walk ----------------------------------------------------------------------------

    [Fact]
    public void Every_item_gets_its_own_script()
    {
        var carried = new List<ItemInstance> { Item("a", 1), Item("b", 2) };

        Assert.Equal("ab", Run(carried, "a", "b"));
    }

    [Fact]
    public void The_answers_are_concatenated_rather_than_overwritten()
    {
        // Which is the opposite of ForEachPartyMember, whose sibling walk keeps only the last.
        var carried = new List<ItemInstance> { Item("a", 1), Item("b", 2), Item("a", 3) };

        Assert.Equal("aba", Run(carried, "a", "b"));
    }

    [Fact]
    public void Three_copies_of_one_item_run_its_script_three_times()
    {
        // The script hangs off the item record, so every copy runs it.
        var carried = new List<ItemInstance> { Item("a", 1), Item("a", 2), Item("a", 3) };

        Assert.Equal("aaa", Run(carried, "a"));
    }

    [Fact]
    public void An_empty_pack_answers_nothing()
    {
        Assert.Equal("", Run([]));
    }

    [Fact]
    public void An_item_the_design_lost_is_skipped_but_still_marked()
    {
        // Skipped rather than run, and marked all the same -- otherwise the restart would find it
        // unprocessed for ever and the walk would not terminate.
        var carried = new List<ItemInstance> { Item("ghost", 1), Item("a", 2) };

        Assert.Equal("a", Run(carried, "a"));
    }

    // ---- surviving the script's own edits ------------------------------------------------------

    [Fact]
    public void An_item_added_during_the_walk_is_not_visited()
    {
        // Everything present at the start is marked unprocessed; anything inserted afterwards
        // arrives already marked. The restart is for iterator safety, not to pick up new work.
        var carried = new List<ItemInstance> { Item("a", 1) };
        var host = Host();
        int runs = 0;

        var database = Database("a", "b");
        var scripts = new GlobalScripts(
        [
            new SpecialAbility("SA_a",
                [new SpecialAbilityEntry("HOOK", "$RETURN \"a\";",
                                         SpecialAbilityEntryKind.Script)]),
        ]);

        string result = PossessionWalk.Run(
            carried, "HOOK",
            id =>
            {
                if (runs++ == 0)
                {
                    carried.Add(Item("b", 2));
                }
                return database(id);
            },
            scripts, host);

        Assert.Equal("a", result);
        Assert.Equal(2, carried.Count);          // the new item is there, and was not run
    }

    [Fact]
    public void An_item_removed_during_the_walk_does_not_break_it()
    {
        // The reference restarts rather than continuing precisely so a mutated list cannot leave
        // it on a stale position. Removing the item after this one would invalidate an iterator.
        var carried = new List<ItemInstance> { Item("a", 1), Item("b", 2), Item("a", 3) };
        var host = Host();
        bool removed = false;

        var database = Database("a", "b");

        string result = PossessionWalk.Run(
            carried, "HOOK",
            id =>
            {
                if (!removed)
                {
                    removed = true;
                    carried.RemoveAt(carried.Count - 1);
                }
                return database(id);
            },
            Scripts("a", "b"), host);

        Assert.Equal("ab", result);
        Assert.Equal(2, carried.Count);
    }

    [Fact]
    public void Clearing_the_pack_mid_walk_ends_it()
    {
        var carried = new List<ItemInstance> { Item("a", 1), Item("b", 2) };
        var host = Host();
        bool cleared = false;

        var database = Database("a", "b");

        string result = PossessionWalk.Run(
            carried, "HOOK",
            id =>
            {
                if (!cleared)
                {
                    cleared = true;
                    carried.Clear();
                }
                return database(id);
            },
            Scripts("a", "b"), host);

        Assert.Equal("a", result);
        Assert.Empty(carried);
    }
}
