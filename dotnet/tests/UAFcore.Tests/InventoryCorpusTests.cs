using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Reads the shipped savegames and checks what the engine makes of the gear the party is
/// carrying.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a plausible-looking constant was wrong for three rounds and no unit test
/// could have caught it: <c>NOTRDY</c> is a packed word, not zero, and zero is the weapon hand.
/// Every carried item in every shipped save is worn, and two of them are stored as a bare zero —
/// so an engine that reads zero as "in the pack" strips the party's weapons on load and nothing
/// in the file looks wrong.
/// </para>
/// <para>
/// The counts are the corpus's, not a rule about savegames in general. They are pinned so that a
/// change to either conversion table has to explain itself against real files.
/// </para>
/// </remarks>
public class InventoryCorpusTests
{
    private static string? Save(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string path = Path.Combine(dir.FullName, "reference",
            relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    private static List<ItemInstance> Carried(string relative)
    {
        string? path = Save(relative);
        if (path is null)
        {
            return [];
        }

        return [.. SaveGameReader.Read(path).Characters.SelectMany(c => c.Items.Items)];
    }

    [Fact]
    public void An_old_save_stores_bare_ordinals_that_name_the_hands()
    {
        // Version 2.81. Two zeroes and four ones -- weapon hand and shield hand, not "no" and
        // "yes" as the field's boolean ancestor would suggest.
        var carried = Carried("Ambassador's_Letter/Saves/SaveA.pty");
        if (carried.Count == 0)
        {
            return;                        // corpus not present
        }

        Assert.Equal(6, carried.Count);
        Assert.Equal([0u, 0u, 1u, 1u, 1u, 1u], carried.Select(i => i.ReadyLocation).Order());

        Assert.All(carried, i => Assert.True(Inventory.IsReady(i)));

        var words = carried.Select(i => Inventory.ReadyWord(i.ReadyLocation)).ToList();
        Assert.Equal(2, words.Count(w => w == "WEAPON"));
        Assert.Equal(4, words.Count(w => w == "SHIELD"));
    }

    [Fact]
    public void A_modern_save_stores_the_packed_words()
    {
        // Version 3.65: the same slots, spelled out rather than numbered.
        var carried = Carried("SomethingWild.dsn/Saves/SaveA.pty");
        if (carried.Count == 0)
        {
            return;
        }

        Assert.Equal(13, carried.Count);
        Assert.All(carried, i => Assert.True(i.ReadyLocation > 1_000_000_000));
        Assert.Equal(["ARMOR", "SHIELD", "WEAPON"],
                     carried.Select(i => Inventory.ReadyWord(i.ReadyLocation)).Distinct().Order());
    }

    [Fact]
    public void No_shipped_save_holds_the_not_ready_word()
    {
        // Worth stating plainly: the corpus gives no example of an unworn carried item, so the
        // NOTRDY value below is taken from the reference's own constant rather than from a file.
        var carried = Carried("Ambassador's_Letter/Saves/SaveA.pty")
            .Concat(Carried("SomethingWild.dsn/Saves/SaveA.pty"))
            .ToList();

        if (carried.Count == 0)
        {
            return;
        }

        Assert.DoesNotContain(carried, i => i.ReadyLocation == Inventory.NotReady);
    }
}
