using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// One row of the memorise screen (<c>MEMORIZE_ITEM</c>).
/// </summary>
/// <param name="SpellId">The spell, as the character's book names it.</param>
/// <param name="SchoolId">Its school — half of what a slot belongs to.</param>
/// <param name="Level">Its level — the other half.</param>
public sealed class MemorizeItem(string spellId, string schoolId, int level)
{
    public string SpellId { get; } = spellId;

    public string SchoolId { get; } = schoolId;

    public int Level { get; } = level;

    /// <summary>How many copies the caster wants (<c>numSelected</c>).</summary>
    public int Selected { get; set; }

    /// <summary>How many are ready (<c>numMemorized</c>).</summary>
    public int Memorized { get; set; }

    /// <summary>
    /// Slots left at this school and level (<c>available</c>).
    /// </summary>
    /// <remarks>
    /// <b>Shared, not per spell.</b> Every row of the same school <i>and</i> level carries the
    /// same number and they move together — selecting one wizard spell of level three takes a
    /// slot from every other level-three wizard spell on the screen.
    /// </remarks>
    public int Available { get; set; }
}

/// <summary>
/// The memorise screen's working copy of a character's spell book
/// (<c>SPELL_TEXT_LIST::FillMemorizeSpellListText</c>, <c>Spell.cpp:8735</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The screen edits a copy and EXIT is the commit.</b> Nothing is written back to the character
/// until the player leaves, and there is no cancel — <c>MEMORIZE_MENU_DATA</c>'s EXIT case walks
/// this list and assigns <c>selected</c> and <c>memorized</c> onto the real spells
/// (<c>RunEvent.cpp:25135</c>). Escaping does the same thing, because escape <i>is</i> EXIT.
/// </para>
/// <para>
/// <b>A spell whose school gives no slots never appears at all.</b> The row is built and then
/// dropped unless <c>available &gt; 0</c>, so a character who cannot cast at that level does not
/// see the spell listed as unavailable — it is simply absent.
/// </para>
/// </remarks>
public sealed class MemorizeList
{
    private readonly List<MemorizeItem> items = [];

    public IReadOnlyList<MemorizeItem> Items => items;

    /// <summary>
    /// Builds the working list from a character's book and its casting ability.
    /// </summary>
    /// <param name="book">The character's spells, in book order.</param>
    /// <param name="schoolOf">The school and level of a spell, or null for one the design lost.</param>
    /// <param name="abilities">What the character may cast, by school.</param>
    /// <param name="adjustments">Script adjustments to the slot counts.</param>
    /// <remarks>
    /// <para>
    /// <b>Slots come from the school ability's base plus bonus at that level</b>, then through any
    /// adjustment whose school matches — or is the wildcard <c>*</c> — and whose level range
    /// covers it: <c>available = available * percent / 100 + bonus</c>, applied in order.
    /// </para>
    /// <para>
    /// <b>The shared count is worked out in a second pass.</b> Every row's <c>selected</c> is
    /// subtracted from the <c>available</c> of every row at the same school and level, itself
    /// included — so what is left really is the slots still free, and a row's own selections have
    /// already been paid for.
    /// </para>
    /// </remarks>
    public static MemorizeList Build(
        IEnumerable<SpellListEntry> book,
        Func<string, (string School, int Level)?> schoolOf,
        IReadOnlyDictionary<string, SchoolAbility> abilities,
        IEnumerable<SpellAdjustment>? adjustments = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(schoolOf);
        ArgumentNullException.ThrowIfNull(abilities);

        var list = new MemorizeList();
        var adjusts = adjustments?.ToList() ?? [];

        foreach (var entry in book)
        {
            if (schoolOf(entry.SpellId) is not var (school, level))
            {
                continue;
            }

            if (!abilities.TryGetValue(school, out var ability)
                || level > ability.MaxSpellLevel
                || level < 1 || level > ability.Base.Length)
            {
                continue;
            }

            int available = ability.Base[level - 1] + ability.Bonus[level - 1];

            foreach (var adjustment in adjusts)
            {
                if (adjustment.SchoolId is not ("*") && adjustment.SchoolId != school)
                {
                    continue;
                }

                if (adjustment.FirstLevel > level || adjustment.LastLevel < level)
                {
                    continue;
                }

                available = (available * adjustment.Percent / 100) + adjustment.Bonus;
            }

            if (available <= 0)
            {
                continue;
            }

            list.items.Add(new MemorizeItem(entry.SpellId, school, level)
            {
                Selected = entry.Selected,
                Memorized = entry.Memorized,
                Available = available,
            });
        }

        foreach (var item in list.items)
        {
            if (item.Selected > 0)
            {
                list.Spend(item, item.Selected);
            }
        }

        return list;
    }

    /// <summary>Takes slots from every row at this row's school and level.</summary>
    private void Spend(MemorizeItem of, int count)
    {
        foreach (var item in items)
        {
            if (item.SchoolId == of.SchoolId && item.Level == of.Level)
            {
                item.Available -= count;
            }
        }
    }

    /// <summary>
    /// Asks for one more copy (<c>IncreaseSpellSelectedCount</c>, <c>Spell.cpp:9662</c>).
    /// </summary>
    /// <remarks>
    /// <b>Nothing here checks the slot count.</b> The reference adds to <c>numSelected</c> and
    /// subtracts from <c>available</c> unconditionally; the only thing stopping a caster
    /// overcommitting is the menu, which darkens SELECT when <c>available</c> reaches zero. Kept
    /// that way, with the guard where the reference puts it.
    /// </remarks>
    public void Select(MemorizeItem item, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.Selected += count;
        Spend(item, count);
    }

    /// <inheritdoc cref="Select"/>
    public void Unselect(MemorizeItem item) => Select(item, -1);

    /// <summary>
    /// Drops a memorised copy (<c>IncreaseSpellMemorizedCount</c>, <c>Spell.cpp:9708</c>).
    /// </summary>
    /// <remarks>
    /// <b>The slot does not come back, and the comment saying it should is wrong.</b> The
    /// reference's function carries "Now we need to decrease the available counts for all spells
    /// of this school and level" and then returns without doing it — correctly, as it turns out:
    /// <c>selected</c> still holds the slot, so the copy will simply be memorised again. The slot
    /// is released by UNSELECT, not by FORGET.
    /// </remarks>
    public static void Forget(MemorizeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.Memorized--;
    }

    /// <summary>Whether SELECT lights up for this row.</summary>
    public static bool CanSelect(MemorizeItem item) =>
        item is not null && item.Available > 0;

    /// <summary>
    /// Whether UNSELECT lights up.
    /// </summary>
    /// <remarks>
    /// <b>Only down to what is already memorised.</b> A copy in the caster's head is dropped with
    /// FORGET, not by unselecting it.
    /// </remarks>
    public static bool CanUnselect(MemorizeItem item) =>
        item is not null && item.Selected > item.Memorized;

    /// <summary>Whether FORGET lights up.</summary>
    public static bool CanForget(MemorizeItem item) =>
        item is not null && item.Memorized > 0;

    /// <summary>
    /// Writes the working copy back onto the character's book — what EXIT does.
    /// </summary>
    public void Commit(SpellList book)
    {
        ArgumentNullException.ThrowIfNull(book);

        foreach (var item in items)
        {
            if (book.Find(item.SpellId) is { } entry)
            {
                entry.Selected = item.Selected;
                entry.Memorized = item.Memorized;
            }
        }
    }
}
