using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the memorise screen's working list and its shared slots.</summary>
public class MemorizeListTests
{
    private static SchoolAbility School(string id, params int[] baseSlots)
    {
        var ability = new SchoolAbility(id, SpellAbility.MaxSpellLevel)
        {
            MaxSpellLevel = baseSlots.Length,
        };

        for (int i = 0; i < baseSlots.Length; i++)
        {
            ability.Base[i] = baseSlots[i];
        }

        return ability;
    }

    /// <summary>A book of spells named "<c>school/level</c>" so the lookup is trivial.</summary>
    private static MemorizeList Build(
        (string School, int Level, int Selected, int Memorized)[] spells,
        Dictionary<string, SchoolAbility> abilities,
        IEnumerable<SpellAdjustment>? adjustments = null)
    {
        var book = new SpellList();
        var lookup = new Dictionary<string, (string, int)>();

        for (int i = 0; i < spells.Length; i++)
        {
            var (school, level, selected, memorized) = spells[i];
            string id = $"{school}{level}-{i}";

            var entry = book.Add(id, level, memorized);
            entry.Selected = selected;
            lookup[id] = (school, level);
        }

        return MemorizeList.Build(book.Entries, id => lookup.TryGetValue(id, out var s) ? s : null,
                                  abilities, adjustments);
    }

    [Fact]
    public void Slots_come_from_the_schools_base_and_bonus_at_that_level()
    {
        var wizard = School("wizard", 3, 2);
        wizard.Bonus[0] = 1;

        var list = Build([("wizard", 1, 0, 0)], new() { ["wizard"] = wizard });

        Assert.Equal(4, list.Items[0].Available);
    }

    [Fact]
    public void A_spell_whose_school_gives_no_slots_is_absent_rather_than_listed()
    {
        // The row is built and then dropped unless available > 0, so the player never sees it.
        var list = Build([("wizard", 1, 0, 0)], new() { ["wizard"] = School("wizard", 0) });

        Assert.Empty(list.Items);
    }

    [Fact]
    public void A_spell_above_the_schools_maximum_level_is_absent()
    {
        var list = Build([("wizard", 3, 0, 0)], new() { ["wizard"] = School("wizard", 2, 2) });

        Assert.Empty(list.Items);
    }

    [Fact]
    public void A_school_the_character_does_not_have_is_absent()
    {
        var list = Build([("cleric", 1, 0, 0)], new() { ["wizard"] = School("wizard", 3) });

        Assert.Empty(list.Items);
    }

    [Fact]
    public void Slots_are_shared_across_every_spell_of_the_same_school_and_level()
    {
        var list = Build([("wizard", 1, 0, 0), ("wizard", 1, 0, 0)],
                         new() { ["wizard"] = School("wizard", 2) });

        Assert.Equal(2, list.Items[0].Available);
        Assert.Equal(2, list.Items[1].Available);

        list.Select(list.Items[0]);

        Assert.Equal(1, list.Items[0].Available);
        Assert.Equal(1, list.Items[1].Available);
    }

    [Fact]
    public void A_different_level_keeps_its_own_slots()
    {
        var list = Build([("wizard", 1, 0, 0), ("wizard", 2, 0, 0)],
                         new() { ["wizard"] = School("wizard", 2, 2) });

        list.Select(list.Items[0]);

        Assert.Equal(1, list.Items[0].Available);
        Assert.Equal(2, list.Items[1].Available);
    }

    [Fact]
    public void What_is_already_selected_has_already_been_paid_for()
    {
        // The second pass subtracts each row's selected count from every row at that school and
        // level, itself included -- so what is left is really the slots still free.
        var list = Build([("wizard", 1, 2, 0), ("wizard", 1, 0, 0)],
                         new() { ["wizard"] = School("wizard", 3) });

        Assert.Equal(1, list.Items[0].Available);
        Assert.Equal(1, list.Items[1].Available);
    }

    [Fact]
    public void An_adjustment_scales_then_adds()
    {
        var adjust = new SpellAdjustment("wizard", "", 1, 9, 200, 1);
        var list = Build([("wizard", 1, 0, 0)], new() { ["wizard"] = School("wizard", 3) },
                         [adjust]);

        Assert.Equal((3 * 200 / 100) + 1, list.Items[0].Available);
    }

    [Fact]
    public void A_wildcard_adjustment_applies_to_every_school()
    {
        var adjust = new SpellAdjustment("*", "", 1, 9, 100, 5);
        var list = Build([("cleric", 1, 0, 0)], new() { ["cleric"] = School("cleric", 1) },
                         [adjust]);

        Assert.Equal(6, list.Items[0].Available);
    }

    [Fact]
    public void An_adjustment_outside_the_level_range_is_ignored()
    {
        var adjust = new SpellAdjustment("wizard", "", 4, 9, 100, 5);
        var list = Build([("wizard", 1, 0, 0)], new() { ["wizard"] = School("wizard", 3) },
                         [adjust]);

        Assert.Equal(3, list.Items[0].Available);
    }

    // ---- the three commands ----------------------------------------------------------------------

    [Fact]
    public void Selecting_and_unselecting_move_the_slot_back_and_forth()
    {
        var list = Build([("wizard", 1, 0, 0)], new() { ["wizard"] = School("wizard", 2) });
        var item = list.Items[0];

        list.Select(item);
        Assert.Equal(1, item.Selected);
        Assert.Equal(1, item.Available);

        list.Unselect(item);
        Assert.Equal(0, item.Selected);
        Assert.Equal(2, item.Available);
    }

    [Fact]
    public void Forgetting_drops_a_copy_without_giving_the_slot_back()
    {
        // The reference's comment says the slot should come back and its code does not do it --
        // correctly, because `selected` still holds the slot and the copy will be memorised again.
        var list = Build([("wizard", 1, 1, 1)], new() { ["wizard"] = School("wizard", 2) });
        var item = list.Items[0];

        Assert.Equal(1, item.Available);

        MemorizeList.Forget(item);

        Assert.Equal(0, item.Memorized);
        Assert.Equal(1, item.Selected);
        Assert.Equal(1, item.Available);
    }

    [Fact]
    public void The_slot_comes_back_by_unselecting_after_forgetting()
    {
        var list = Build([("wizard", 1, 1, 1)], new() { ["wizard"] = School("wizard", 2) });
        var item = list.Items[0];

        MemorizeList.Forget(item);
        list.Unselect(item);

        Assert.Equal(2, item.Available);
    }

    // ---- when the menu lights up -----------------------------------------------------------------

    [Fact]
    public void Select_is_dark_with_no_slots_left()
    {
        var list = Build([("wizard", 1, 1, 0)], new() { ["wizard"] = School("wizard", 1) });

        Assert.False(MemorizeList.CanSelect(list.Items[0]));
    }

    [Fact]
    public void Unselect_only_goes_down_to_what_is_memorised()
    {
        // A copy in the caster's head is dropped with FORGET, not by unselecting it.
        var list = Build([("wizard", 1, 2, 2)], new() { ["wizard"] = School("wizard", 3) });
        var item = list.Items[0];

        Assert.False(MemorizeList.CanUnselect(item));

        list.Select(item);
        Assert.True(MemorizeList.CanUnselect(item));
    }

    [Fact]
    public void Forget_is_dark_with_nothing_memorised()
    {
        var list = Build([("wizard", 1, 1, 0)], new() { ["wizard"] = School("wizard", 3) });

        Assert.False(MemorizeList.CanForget(list.Items[0]));
    }

    [Fact]
    public void Nothing_is_written_back_until_the_commit()
    {
        var book = new SpellList();
        var entry = book.Add("magic missile", level: 1);
        entry.Selected = 0;

        var list = MemorizeList.Build(
            book.Entries, _ => ("wizard", 1),
            new Dictionary<string, SchoolAbility> { ["wizard"] = School("wizard", 3) });

        list.Select(list.Items[0]);
        Assert.Equal(0, entry.Selected);

        list.Commit(book);
        Assert.Equal(1, entry.Selected);
    }
}
