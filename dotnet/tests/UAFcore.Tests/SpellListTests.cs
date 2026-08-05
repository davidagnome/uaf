using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the caster's spell book: what is castable, and what spending one costs.</summary>
public class SpellListTests
{
    [Fact]
    public void Only_memorised_spells_are_castable()
    {
        var book = new SpellList();
        book.Add("magic missile", level: 1, memorized: 2);
        book.Add("sleep", level: 1);

        Assert.Equal(["magic missile"], book.Castable.Select(e => e.SpellId));
    }

    [Fact]
    public void Adding_a_spell_twice_tops_up_the_entry_rather_than_duplicating_it()
    {
        var book = new SpellList();
        book.Add("sleep", level: 1, memorized: 1);
        book.Add("sleep", level: 1, memorized: 2);

        Assert.Single(book.Entries);
        Assert.Equal(3, book.Find("sleep")!.Memorized);
    }

    [Fact]
    public void Spending_a_copy_takes_exactly_one()
    {
        var book = new SpellList();
        book.Add("sleep", level: 1, memorized: 3);

        Assert.True(book.DecrementMemorized("sleep"));
        Assert.Equal(2, book.Find("sleep")!.Memorized);
    }

    [Fact]
    public void The_count_argument_is_a_zero_test_and_nothing_more()
    {
        // The reference takes a count, refuses when it is zero, and then decrements by one whatever
        // it was -- so asking for five spends one.
        var book = new SpellList();
        book.Add("sleep", level: 1, memorized: 5);

        Assert.False(book.DecrementMemorized("sleep", 0));
        Assert.Equal(5, book.Find("sleep")!.Memorized);

        Assert.True(book.DecrementMemorized("sleep", 5));
        Assert.Equal(4, book.Find("sleep")!.Memorized);
    }

    [Fact]
    public void An_unselected_spell_is_cast_without_ever_being_used_up()
    {
        // SetUnMemorized returns early when `selected` is zero. `selected` is how many copies
        // the caster wants next time, so it has no business gating the spend -- but it does.
        var book = new SpellList();
        var entry = book.Add("sleep", level: 1, memorized: 2);
        entry.Selected = 0;

        Assert.True(book.DecrementMemorized("sleep"));
        Assert.Equal(2, entry.Memorized);
    }

    [Fact]
    public void Spending_reports_whether_the_spell_was_found_not_whether_a_copy_went()
    {
        var book = new SpellList();
        book.Add("sleep", level: 1);

        Assert.True(book.DecrementMemorized("sleep"));    // known, but none memorised
        Assert.False(book.DecrementMemorized("fireball")); // not in the book at all
    }

    [Fact]
    public void Memorised_count_never_goes_negative()
    {
        var book = new SpellList();
        book.Add("sleep", level: 1, memorized: 1);

        book.DecrementMemorized("sleep");
        book.DecrementMemorized("sleep");

        Assert.Equal(0, book.Find("sleep")!.Memorized);
    }
}
