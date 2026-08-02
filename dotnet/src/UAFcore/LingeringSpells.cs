namespace UAFcore;

/// <summary>
/// A cast spell that has stayed on the map (<c>SPELL_LINGER_DATA</c>, <c>Spell.h:1068</c>).
/// </summary>
/// <param name="Key">
/// The active-spell entry this belongs to — the same key every effect of the cast is parented to.
/// </param>
/// <param name="SpellId">The spell, for its name and its blockage rule.</param>
/// <param name="Caster">The combatant who cast it. It keeps affecting people after their turn.</param>
/// <param name="OnceOnly">
/// Whether a combatant caught once is caught again on later rounds (<c>LingerOnceOnly</c>).
/// </param>
/// <remarks>
/// <b>Only combat spells linger.</b> The reference sets <c>aspell.Lingers</c> to
/// <c>IsCombatActive() ? pSdata->Lingers : FALSE</c> (<c>Char.cpp:16324</c>) — a spell cast in camp
/// leaves nothing behind however its record is authored, because there is no map to leave it on.
/// </remarks>
public sealed class LingeringSpell(int key, string spellId, int caster, bool onceOnly,
                                   IEnumerable<(int X, int Y)> squares)
{
    private readonly HashSet<(int, int)> squares = [.. squares];
    private readonly List<int> caught = [];

    /// <inheritdoc cref="LingeringSpell(int, string, int, bool, IEnumerable{ValueTuple{int, int}})"/>
    public int Key { get; } = key;

    /// <inheritdoc cref="LingeringSpell(int, string, int, bool, IEnumerable{ValueTuple{int, int}})"/>
    public string SpellId { get; } = spellId;

    /// <inheritdoc cref="LingeringSpell(int, string, int, bool, IEnumerable{ValueTuple{int, int}})"/>
    public int Caster { get; } = caster;

    /// <inheritdoc cref="LingeringSpell(int, string, int, bool, IEnumerable{ValueTuple{int, int}})"/>
    public bool OnceOnly { get; } = onceOnly;

    /// <summary>The squares it covers.</summary>
    public IReadOnlyCollection<(int X, int Y)> Squares => squares;

    /// <summary>Everyone it has caught so far, in the order it caught them.</summary>
    public IReadOnlyList<int> Caught => caught;

    /// <summary>Whether it covers a square.</summary>
    public bool Covers(int x, int y) => squares.Contains((x, y));

    /// <summary>
    /// Whether a combatant standing here would be caught
    /// (<c>AffectsTarget</c>, <c>Spell.h:1225</c>).
    /// </summary>
    /// <remarks>
    /// <b>Any one square of the footprint is enough.</b> The test walks the spell's squares looking
    /// for one inside the combatant's box, so a large monster is caught by a cloud touching only
    /// its corner.
    /// </remarks>
    public bool Affects(int target, int x, int y, int width = 1, int height = 1)
    {
        bool touching = squares.Any(s => s.Item1 >= x && s.Item2 >= y
                                         && s.Item1 < x + width && s.Item2 < y + height);

        return touching && IsEligible(target);
    }

    /// <summary>
    /// Whether it may catch this combatant again (<c>EligibleTarget</c>, <c>Spell.h:1213</c>).
    /// </summary>
    /// <remarks>
    /// <b>The once-only flag reads backwards from its name.</b> A combatant not yet caught is
    /// always eligible; one already caught is eligible again exactly when the spell is <i>not</i>
    /// once-only. So "once only" means once per combatant, not once in total.
    /// </remarks>
    public bool IsEligible(int target) => !caught.Contains(target) || !OnceOnly;

    /// <summary>Records that it has caught a combatant (<c>AddTarget</c>, <c>Spell.h:1201</c>).</summary>
    public void Catch(int target)
    {
        if (!caught.Contains(target))
        {
            caught.Add(target);
        }
    }
}

/// <summary>
/// The lingering spells on the map (the linger half of <c>ACTIVE_SPELL_LIST</c>).
/// </summary>
/// <remarks>
/// <b>These are checked at the head of every round, for every combatant on the map</b>
/// (<c>Combatants.cpp:4605</c>) — not when somebody moves. A combatant that walks into a cloud and
/// out again within one round is never caught by it; one that ends the round standing in it is
/// caught at the start of the next.
/// </remarks>
public sealed class LingeringSpellList
{
    private readonly List<LingeringSpell> spells = [];

    public IReadOnlyList<LingeringSpell> Spells => spells;

    public int Count => spells.Count;

    /// <summary>Adds a cast that stays on the map.</summary>
    public LingeringSpell Add(int key, string spellId, int caster, bool onceOnly,
                              IEnumerable<(int X, int Y)> squares)
    {
        var spell = new LingeringSpell(key, spellId, caster, onceOnly, squares);
        spells.Add(spell);
        return spell;
    }

    /// <summary>Drops a cast whose active-spell entry has expired.</summary>
    public bool Remove(int key) => spells.RemoveAll(s => s.Key == key) > 0;

    public void Clear() => spells.Clear();

    /// <summary>
    /// Every lingering spell that would catch a combatant standing here
    /// (<c>ActivateLingerSpellsOnTarget</c>, <c>Spell.cpp:8231</c>).
    /// </summary>
    /// <remarks>
    /// <b>Catching is recorded as a side effect of asking.</b> The reference adds the target to
    /// each spell's list as it activates it, which is what makes a once-only spell stop catching
    /// the same combatant — so this marks them too.
    /// </remarks>
    public List<LingeringSpell> Catch(int target, int x, int y, int width = 1, int height = 1)
    {
        var caught = spells.Where(s => s.Affects(target, x, y, width, height)).ToList();

        foreach (var spell in caught)
        {
            spell.Catch(target);
        }

        return caught;
    }

    /// <summary>
    /// Whether any lingering spell bars a square
    /// (<c>LingerSpellBlocksCombatant</c>, <c>Spell.cpp:7787</c>).
    /// </summary>
    /// <param name="blockageScript">
    /// Stands in for the <c>SPELL_LINGER_BLOCKAGE</c> hook, which is given the spell and the
    /// combatant and answers <c>'N'</c> to let them through.
    /// </param>
    /// <remarks>
    /// <b>A lingering spell blocks by default.</b> The reference sets its answer to "blocks" and
    /// only clears it when the script explicitly returns <c>'N'</c>, so a design that writes no
    /// blockage script gets a wall of fire that really is a wall. Getting this default backwards
    /// would let everybody walk through every cloud.
    /// </remarks>
    public bool Blocks(int x, int y, Func<LingeringSpell, bool>? blockageScript = null) =>
        spells.Any(s => s.Covers(x, y) && (blockageScript?.Invoke(s) ?? true));
}
