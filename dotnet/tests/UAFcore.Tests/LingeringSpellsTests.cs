using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers a spell left on the map and who it catches.</summary>
public class LingeringSpellsTests
{
    private static LingeringSpell Cloud(bool onceOnly = false) =>
        new(key: 1, "stinking cloud", caster: 0, onceOnly,
            [(5, 5), (5, 6), (6, 5), (6, 6)]);

    // ---- who it catches ------------------------------------------------------------------------

    [Fact]
    public void A_combatant_standing_in_it_is_caught()
    {
        Assert.True(Cloud().Affects(target: 3, x: 5, y: 5));
    }

    [Fact]
    public void A_combatant_standing_clear_of_it_is_not()
    {
        Assert.False(Cloud().Affects(target: 3, x: 9, y: 9));
    }

    [Fact]
    public void One_square_of_a_large_footprint_is_enough()
    {
        // The test walks the spell's squares looking for one inside the combatant's box, so a cloud
        // touching only the corner of a big monster still catches it.
        Assert.True(Cloud().Affects(target: 3, x: 4, y: 4, width: 2, height: 2));
    }

    [Fact]
    public void A_footprint_that_only_reaches_the_edge_is_still_clear()
    {
        // The box is half-open: x..x+width-1. A 2x2 at (3,3) covers 3-4, which stops short of 5.
        Assert.False(Cloud().Affects(target: 3, x: 3, y: 3, width: 2, height: 2));
    }

    // ---- eligibility ---------------------------------------------------------------------------

    [Fact]
    public void A_repeating_spell_catches_the_same_combatant_every_round()
    {
        var cloud = Cloud(onceOnly: false);

        Assert.True(cloud.Affects(3, 5, 5));
        cloud.Catch(3);
        Assert.True(cloud.Affects(3, 5, 5));
    }

    [Fact]
    public void A_once_only_spell_does_not_catch_the_same_combatant_twice()
    {
        // "Once only" means once per combatant, not once in total.
        var cloud = Cloud(onceOnly: true);

        Assert.True(cloud.Affects(3, 5, 5));
        cloud.Catch(3);
        Assert.False(cloud.Affects(3, 5, 5));
    }

    [Fact]
    public void A_once_only_spell_still_catches_somebody_new()
    {
        var cloud = Cloud(onceOnly: true);
        cloud.Catch(3);

        Assert.True(cloud.Affects(4, 5, 5));
    }

    [Fact]
    public void Catching_the_same_combatant_twice_records_it_once()
    {
        var cloud = Cloud();
        cloud.Catch(3);
        cloud.Catch(3);

        Assert.Equal([3], cloud.Caught);
    }

    // ---- the list ------------------------------------------------------------------------------

    [Fact]
    public void Catching_marks_every_spell_that_caught_them()
    {
        var list = new LingeringSpellList();
        list.Add(1, "cloud", caster: 0, onceOnly: true, [(5, 5)]);
        list.Add(2, "fire", caster: 0, onceOnly: true, [(5, 5)]);
        list.Add(3, "ice", caster: 0, onceOnly: true, [(9, 9)]);

        var caught = list.Catch(target: 4, x: 5, y: 5);

        Assert.Equal(2, caught.Count);
        Assert.All(caught, s => Assert.Equal([4], s.Caught));
        Assert.Empty(list.Spells[2].Caught);
    }

    [Fact]
    public void A_once_only_spell_stops_catching_after_the_first_round()
    {
        var list = new LingeringSpellList();
        list.Add(1, "cloud", caster: 0, onceOnly: true, [(5, 5)]);

        Assert.Single(list.Catch(4, 5, 5));
        Assert.Empty(list.Catch(4, 5, 5));
    }

    [Fact]
    public void An_expired_cast_is_removed_by_its_key()
    {
        var list = new LingeringSpellList();
        list.Add(1, "cloud", 0, false, [(5, 5)]);
        list.Add(2, "fire", 0, false, [(6, 6)]);

        Assert.True(list.Remove(1));
        Assert.False(list.Remove(1));
        Assert.Equal(1, list.Count);
    }

    // ---- blocking ------------------------------------------------------------------------------

    [Fact]
    public void A_lingering_spell_blocks_its_squares_by_default()
    {
        // The reference sets its answer to "blocks" and only clears it when the script returns 'N',
        // so a design with no blockage script gets a wall of fire that really is a wall.
        var list = new LingeringSpellList();
        list.Add(1, "wall of fire", 0, false, [(5, 5), (5, 6)]);

        Assert.True(list.Blocks(5, 5));
        Assert.True(list.Blocks(5, 6));
        Assert.False(list.Blocks(7, 7));
    }

    [Fact]
    public void A_script_saying_no_lets_combatants_through()
    {
        var list = new LingeringSpellList();
        list.Add(1, "harmless mist", 0, false, [(5, 5)]);

        Assert.False(list.Blocks(5, 5, blockageScript: _ => false));
    }

    [Fact]
    public void One_blocking_spell_is_enough()
    {
        var list = new LingeringSpellList();
        list.Add(1, "mist", 0, false, [(5, 5)]);
        list.Add(2, "fire", 0, false, [(5, 5)]);

        Assert.True(list.Blocks(5, 5, s => s.SpellId == "fire"));
    }

    [Fact]
    public void An_empty_map_blocks_nothing()
    {
        Assert.False(new LingeringSpellList().Blocks(5, 5));
    }
}
