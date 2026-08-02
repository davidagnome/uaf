namespace UAFcore;

/// <summary>
/// One spell a combatant knows, and how many copies are ready to cast
/// (<c>CHARACTER_SPELL</c>, <c>Spell.h:60</c>).
/// </summary>
/// <param name="spellId">The spell in the design's table.</param>
/// <param name="level">The spell's level, carried here for convenience as the reference does.</param>
public sealed class SpellListEntry(string spellId, int level)
{
    /// <inheritdoc cref="SpellListEntry(string, int)"/>
    public string SpellId { get; } = spellId;

    /// <summary>The spell's level.</summary>
    public int Level { get; } = level;

    /// <summary>How many copies are memorised and ready.</summary>
    public int Memorized { get; set; }

    /// <summary>
    /// Whether the caster will memorise this spell again (<c>selected</c>).
    /// </summary>
    /// <remarks>
    /// <b>This gates spending, not just re-memorising.</b> <c>SetUnMemorized</c> returns without
    /// doing anything when <c>selected</c> is false (<c>Spell.cpp:1254</c>), so an unselected
    /// spell is cast without ever being used up. Odd, but it is what ships.
    /// </remarks>
    public bool Selected { get; set; } = true;
}

/// <summary>
/// The spells a combatant carries into a fight (<c>SPELL_LIST</c> through <c>spellBookType</c>).
/// </summary>
/// <remarks>
/// Only the part combat needs: what is known, and what is ready. Learning, scribing and the
/// memorisation clock all live outside a fight and are not ported here.
/// </remarks>
public sealed class SpellList
{
    private readonly List<SpellListEntry> entries = [];

    public IReadOnlyList<SpellListEntry> Entries => entries;

    /// <summary>Adds a spell to the book, or returns the entry already there.</summary>
    public SpellListEntry Add(string spellId, int level, int memorized = 0)
    {
        if (Find(spellId) is { } existing)
        {
            existing.Memorized += memorized;
            return existing;
        }

        var entry = new SpellListEntry(spellId, level) { Memorized = memorized };
        entries.Add(entry);
        return entry;
    }

    /// <summary>The entry for a spell, or null when it is not in the book.</summary>
    public SpellListEntry? Find(string spellId) =>
        entries.FirstOrDefault(e => e.SpellId == spellId);

    /// <summary>
    /// The spells that can be cast right now — what the CAST menu lists
    /// (<c>FillCastSpellListText</c>, <c>Disptext.cpp:417</c>).
    /// </summary>
    public IEnumerable<SpellListEntry> Castable => entries.Where(e => e.Memorized > 0);

    /// <summary>
    /// Spends one memorised copy (<c>DecMemorized</c>, <c>Spell.cpp:1666</c>).
    /// </summary>
    /// <param name="count">
    /// <b>Tested against zero and then ignored.</b> The reference takes a count, returns false when
    /// it is zero, and then decrements by exactly one whatever it was — so asking for five spends
    /// one. Kept as a parameter because callers pass it and the zero test is real.
    /// </param>
    /// <returns>Whether the spell was in the book at all — <i>not</i> whether a copy was spent.</returns>
    /// <remarks>
    /// A spell with no memorised copies left, or one that is not <see cref="SpellListEntry.Selected"/>,
    /// still returns true here: the reference's <c>DecMemorized</c> reports only that it found the
    /// spell, and <c>SetUnMemorized</c> swallows both refusals silently.
    /// </remarks>
    public bool DecrementMemorized(string spellId, int count = 1)
    {
        if (count == 0)
        {
            return false;
        }

        if (Find(spellId) is not { } entry)
        {
            return false;
        }

        if (entry.Selected && entry.Memorized > 0)
        {
            entry.Memorized--;
        }

        return true;
    }
}
