using UAF.Rules;

namespace UAFcore;

/// <summary>One spell the temple will cast, and what it charges.</summary>
/// <param name="SpellId">The spell in the design's table.</param>
/// <param name="Name">What the list shows.</param>
/// <param name="Level">The spell's level.</param>
/// <param name="Cost">The price after the temple's cost factor.</param>
public sealed record TempleSpell(string SpellId, string Name, int Level, int Cost);

/// <summary>
/// What a temple will cast for the party, and for how much
/// (<c>FillTempleCastSpellListText</c>, <c>Spell.cpp:8831</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The list is the temple's own memorised spells, not the party's.</b> The reference builds it
/// from a synthesised "TempleBishop" (<c>RunEvent.cpp:12408</c>) — a maximum-level cleric and
/// magic-user created on first use and kept in the design's NPC list, so that any spell the temple
/// carries can actually be cast. What the party sees is whatever that character has memorised.
/// </para>
/// <para>
/// <b>Only memorised copies are offered.</b> A spell in the temple's book with none ready does not
/// appear — the temple is out of it until it memorises again.
/// </para>
/// </remarks>
public static class TempleSpells
{
    /// <summary>
    /// The spells on offer, priced.
    /// </summary>
    /// <param name="book">The temple's spell book.</param>
    /// <param name="factor">The event's <c>costFactor</c>.</param>
    /// <param name="maxLevel">
    /// The event's <c>maxLevel</c> — the highest spell this temple will cast at all.
    /// </param>
    /// <param name="spellOf">A spell's name, level and cast cost, or null for one the design lost.</param>
    public static IReadOnlyList<TempleSpell> Offered(
        SpellList book, CostFactor factor, int maxLevel,
        Func<string, (string Name, int Level, int Cost)?> spellOf)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(spellOf);

        var offered = new List<TempleSpell>();

        foreach (var entry in book.Entries)
        {
            if (entry.Memorized <= 0 || spellOf(entry.SpellId) is not var (name, level, cost))
            {
                continue;
            }

            if (level > maxLevel)
            {
                continue;
            }

            offered.Add(new TempleSpell(entry.SpellId, name, level, Prices.Apply(factor, cost)));
        }

        return offered;
    }
}
