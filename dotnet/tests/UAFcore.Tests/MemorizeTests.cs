using UAFcore;

namespace UAFcore.Tests;

/// <summary>Covers the memorisation clock — per spell and per book.</summary>
public class MemorizeTests
{
    private static SpellList Book(params (string Id, int Level, int Selected)[] spells)
    {
        var book = new SpellList();

        foreach (var (id, level, selected) in spells)
        {
            book.Add(id, level).Selected = selected;
        }

        return book;
    }

    // ---- one spell -------------------------------------------------------------------------------

    [Fact]
    public void Selected_is_how_many_copies_the_caster_wants()
    {
        var entry = new SpellList().Add("magic missile", level: 1);
        entry.Selected = 3;

        Assert.True(entry.HasUnmemorized);

        entry.Memorized = 3;
        Assert.False(entry.HasUnmemorized);
    }

    [Fact]
    public void A_spell_takes_fifteen_minutes_a_level()
    {
        Assert.Equal(15, SpellListEntry.MemorizeMinutes(1));
        Assert.Equal(45, SpellListEntry.MemorizeMinutes(3));
        Assert.Equal(135, SpellListEntry.MemorizeMinutes(9));
    }

    [Fact]
    public void A_copy_lands_when_the_time_is_enough_and_not_before()
    {
        var entry = new SpellList().Add("magic missile", level: 1);
        entry.Selected = 1;

        Assert.False(entry.AddMemorizeTime(14));
        Assert.Equal(0, entry.Memorized);

        Assert.True(entry.AddMemorizeTime(1));
        Assert.Equal(1, entry.Memorized);
        Assert.True(entry.JustMemorized);
    }

    [Fact]
    public void The_clock_resets_between_copies()
    {
        var entry = new SpellList().Add("magic missile", level: 1);
        entry.Selected = 2;

        entry.AddMemorizeTime(15);
        Assert.Equal(1, entry.Memorized);
        Assert.Equal(0, entry.MemorizeTime);

        Assert.False(entry.AddMemorizeTime(14));
        Assert.True(entry.AddMemorizeTime(1));
        Assert.Equal(2, entry.Memorized);
    }

    [Fact]
    public void A_spell_that_has_everything_it_wants_takes_no_more_time()
    {
        var entry = new SpellList().Add("magic missile", level: 1, memorized: 2);
        entry.Selected = 2;

        Assert.False(entry.AddMemorizeTime(1000));
        Assert.Equal(0, entry.MemorizeTime);
    }

    [Fact]
    public void Memorising_them_all_at_once_ignores_the_clock()
    {
        var entry = new SpellList().Add("fireball", level: 3);
        entry.Selected = 4;

        Assert.True(entry.AddMemorizeTime(0, all: true));
        Assert.Equal(4, entry.Memorized);
    }

    [Fact]
    public void The_announcement_flag_is_cleared_on_the_next_tick()
    {
        // The reference's announcement loop clears it as it prints, and IncMemorizedTime clears it
        // again on entry -- so a copy finished and never announced is forgotten.
        var entry = new SpellList().Add("magic missile", level: 1);
        entry.Selected = 2;

        entry.AddMemorizeTime(15);
        Assert.True(entry.JustMemorized);

        entry.AddMemorizeTime(1);
        Assert.False(entry.JustMemorized);
    }

    // ---- the book --------------------------------------------------------------------------------

    [Fact]
    public void Preparation_is_keyed_on_the_highest_level_still_wanted()
    {
        Assert.Equal(0, SpellList.PrepMinutes(0));
        Assert.Equal(4 * 60, SpellList.PrepMinutes(2));
        Assert.Equal(6 * 60, SpellList.PrepMinutes(3));
        Assert.Equal(8 * 60, SpellList.PrepMinutes(6));
        Assert.Equal(10 * 60, SpellList.PrepMinutes(8));
        Assert.Equal(12 * 60, SpellList.PrepMinutes(9));
    }

    [Fact]
    public void A_book_with_nothing_outstanding_prepares_for_no_time()
    {
        var book = Book(("magic missile", 1, 0));

        Assert.Equal(0, book.BeginPreparing());
    }

    [Fact]
    public void One_third_level_spell_costs_six_hours_before_its_forty_five_minutes()
    {
        var book = Book(("fireball", 3, 1));

        Assert.Equal(6 * 60, book.BeginPreparing());

        // Nothing is memorised during the preparation, however long it runs.
        for (int i = 0; i < 6 * 60; i++)
        {
            Assert.False(book.AddMemorizeTime(1));
        }

        Assert.Equal(0, book.Entries[0].Memorized);
    }

    [Fact]
    public void Only_one_spell_memorises_at_a_time()
    {
        // The first entry still wanting copies takes the whole slice; everything after it waits.
        var book = Book(("magic missile", 1, 1), ("shield", 1, 1));
        book.BeginPreparing();

        // Walk out the preparation.
        for (int i = 0; i <= 4 * 60; i++)
        {
            book.AddMemorizeTime(1);
        }

        for (int i = 0; i < 20; i++)
        {
            book.AddMemorizeTime(1);
        }

        Assert.Equal(1, book.Entries[0].Memorized);
        Assert.Equal(0, book.Entries[1].Memorized);
    }

    [Fact]
    public void The_book_prepares_once_and_not_once_per_spell()
    {
        var book = Book(("magic missile", 1, 2));
        book.BeginPreparing();

        for (int i = 0; i <= 4 * 60; i++)
        {
            book.AddMemorizeTime(1);
        }

        Assert.Equal(0, book.PrepTimeNeeded);

        // The second copy starts memorising straight away.
        for (int i = 0; i < 30; i++)
        {
            book.AddMemorizeTime(1);
        }

        Assert.Equal(2, book.Entries[0].Memorized);
    }

    [Fact]
    public void Memorising_the_whole_book_skips_the_preparation_entirely()
    {
        var book = Book(("magic missile", 1, 2), ("fireball", 3, 1));
        book.BeginPreparing();

        book.AddMemorizeTime(0, all: true);

        Assert.Equal(2, book.Entries[0].Memorized);
        Assert.Equal(1, book.Entries[1].Memorized);
    }

    [Fact]
    public void The_rest_estimate_is_the_shortfall_plus_the_preparation()
    {
        var book = Book(("magic missile", 1, 2), ("fireball", 3, 1));

        Assert.Equal((2 * 10) + (1 * 10) + (6 * 60), book.RestTimeNeeded(_ => 10));
    }

    [Fact]
    public void A_surplus_shortens_the_estimate_rather_than_counting_as_nothing()
    {
        // The live loop has no guard that selected exceeds memorized -- the commented-out version
        // above it did -- so a spell with more copies than wanted subtracts minutes.
        var book = Book(("magic missile", 1, 0));
        book.Entries[0].Memorized = 3;

        Assert.Equal(-30, book.RestTimeNeeded(_ => 10));
    }
}
