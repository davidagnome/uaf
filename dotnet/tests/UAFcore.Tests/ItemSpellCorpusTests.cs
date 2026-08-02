using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Checks the item spell id — the whole of the USE path — against the shipped designs.
/// </summary>
/// <remarks>
/// The field was read and discarded by the item reader until combat needed it, so it has never
/// been diffed against the oracle. What can be checked here is stronger in one way: every spell an
/// item names must exist in the same design's spell database.
/// </remarks>
public class ItemSpellCorpusTests
{
    private const string Corpus = "/Volumes/Data/Dev/uaf/reference";

    [Theory]
    [InlineData("SomethingWild.dsn", 135)]
    [InlineData("Case.dsn", 78)]
    public void Every_spell_an_item_names_exists_in_the_same_design(string design, int expected)
    {
        string root = Path.Combine(Corpus, design);
        if (!Directory.Exists(root))
        {
            return;
        }

        var loaded = LoadedDesign.Open(root);
        var withSpell = (loaded.Items?.Items ?? [])
            .Where(i => !string.IsNullOrEmpty(i.Names.SpellId))
            .ToList();

        Assert.Equal(expected, withSpell.Count);
        Assert.All(withSpell, i => Assert.NotNull(loaded.Spell(i.Names.SpellId)));
    }

    [Fact]
    public void A_design_below_the_version_gate_carries_no_item_spells_at_all()
    {
        // The field is only on the wire at 0.999647 and above -- a bare literal in the C++ with no
        // named constant. ci-tier3 predates it, so its items name no spells and the USE command
        // has nothing to invoke there.
        string root = Path.Combine(Corpus, "ci-tier3");
        if (!Directory.Exists(root))
        {
            return;
        }

        var loaded = LoadedDesign.Open(root);
        var items = loaded.Items?.Items ?? [];

        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Empty(i.Names.SpellId));
    }
}
