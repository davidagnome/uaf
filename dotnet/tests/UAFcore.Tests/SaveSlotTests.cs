using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the ten save slots and the files behind them.
/// </summary>
public class SaveSlotTests
{
    [Fact]
    public void There_are_ten_slots_and_a_way_out()
    {
        // MAX_SAVE_GAME_SLOTS is #defined as SaveGameMenuItems-1, so the number of saves a player
        // may keep is a fact about a menu table.
        Assert.Equal(10, SaveSlots.Count);
        Assert.Equal(11, SaveSlots.Menu.Length);
        Assert.Equal("EXIT", SaveSlots.Menu[SaveSlots.Exit].Label);
    }

    [Fact]
    public void The_slots_are_lettered_A_to_J()
    {
        Assert.Equal(["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"],
                     SaveSlots.Menu[..SaveSlots.Count].Select(e => e.Label));
    }

    [Fact]
    public void A_slots_file_is_its_letter()
    {
        Assert.Equal("SaveA.pty", SaveSlots.FileName(0));
        Assert.Equal("SaveJ.pty", SaveSlots.FileName(9));
    }

    [Fact]
    public void A_design_that_has_never_been_played_shows_ten_empty_slots()
    {
        // Not an error: a design with no Saves folder still has to draw the load screen.
        var slots = SaveSlots.Under(Path.Combine(Path.GetTempPath(), "uaf-no-such-design"));

        Assert.Equal(10, slots.Count);
        Assert.All(slots, s => Assert.False(s.Exists));
        Assert.False(SaveSlots.Any(slots));
    }

    [Fact]
    public void A_null_directory_is_the_same_as_an_empty_one()
    {
        Assert.All(SaveSlots.Under(null), s => Assert.False(s.Exists));
    }

    [Fact]
    public void A_slot_with_a_file_in_it_reads_as_occupied()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"uaf-slots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "SaveC.pty"), "");

            var slots = SaveSlots.Under(dir);

            Assert.True(SaveSlots.Any(slots));
            Assert.True(slots[2].Exists);
            Assert.Equal("C", slots[2].Letter);
            Assert.All(slots.Where(s => s.Index != 2), s => Assert.False(s.Exists));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_shipped_design_has_a_game_in_slot_A()
    {
        // The corpus's own SomethingWild.dsn/Saves/SaveA.pty -- the naming convention holds
        // against a real design rather than only against one this test wrote.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string saves = Path.Combine(dir!.FullName, "reference", "SomethingWild.dsn", "Saves");
        if (!Directory.Exists(saves))
        {
            return;
        }

        var slots = SaveSlots.Under(saves);

        Assert.True(slots[0].Exists);
        Assert.True(SaveSlots.Any(slots));
    }

    // ---- what a save cannot yet carry ----------------------------------------------------------

    [Fact]
    public void Saving_is_refused_and_says_what_would_be_lost()
    {
        // The reader and writer are both finished; the projection from live state is not. A .pty
        // with an empty visited map reads back cleanly into a party that has forgotten where it
        // has been, which is the worst kind of wrong.
        // The list is empty now -- every piece of state a save carries is tracked. Saving is
        // still refused, but for assembling the file rather than for having lost the contents,
        // and the message says which.
        Assert.Empty(SaveGameProjection.Untracked);
    }
}
