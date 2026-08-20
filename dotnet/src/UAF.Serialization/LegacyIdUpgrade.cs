namespace UAF.Serialization;

/// <summary>
/// Resolves the pre-0.998101 numeric database keys a monster carries into modern names.
/// </summary>
/// <remarks>
/// <para>
/// Below <see cref="DesignVersion.SpellNames"/> a record refers to another database by an
/// <c>int</c> key rather than by name. A monster does it twice: an attack's spell
/// (<c>Monster.cpp:130</c>) and each item it holds. The modern form is a <c>SPELL_ID</c> or
/// <c>ITEM_ID</c> — the target record's own name — so converting means looking the key up in the
/// database it points at, which is why this is a pass after loading rather than something the
/// reader can do.
/// </para>
/// <para>
/// <b>The reference has the lookup and does not call it for a monster's attack.</b>
/// <c>SPELL_DATA_TYPE::FindPreVersionSpellNamesSpellID</c> (<c>Spell.cpp:10483</c>) is used from
/// <c>PicSlot.cpp</c> and three places in <c>GameEvent.cpp</c>, but <c>preVersionSpellNames_gsID</c>
/// on <c>ATTACK_DATA</c> is read at <c>Monster.cpp:130</c> and <b>never read again anywhere in the
/// tree</b> — so upgrading a legacy design through the reference silently drops every monster
/// attack's spell. <b>This is a deliberate divergence:</b> the port resolves it, because the
/// information is right there and losing it is a defect rather than a behaviour worth reproducing.
/// </para>
/// <para>
/// <b>A key that resolves to nothing leaves the record refusing to write</b> rather than writing an
/// empty name. The reference pops a message box and carries on with an empty <c>SPELL_ID</c>; here
/// an unresolvable key means the design is not self-consistent, and saying so beats writing a
/// monster whose attack has quietly lost its spell.
/// </para>
/// </remarks>
public static class LegacyIdUpgrade
{
    /// <summary>Whether this monster still carries a numeric key of either kind.</summary>
    public static bool NeedsUpgrade(MonsterRecord monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        return monster.Attacks.Any(a => a.LegacySpellId > 0)
               || (monster.Items?.Items.Any(i => i.LegacyItemId > 0) ?? false);
    }

    /// <summary>
    /// The monster with its numeric keys resolved against the two databases.
    /// </summary>
    /// <param name="spells">The design's spells, for an attack's spell key.</param>
    /// <param name="items">The design's items, for a held item's key.</param>
    /// <remarks>
    /// <b>Only a key greater than zero is a reference.</b> The reference initialises
    /// <c>preVersionSpellNames_gsID</c> to <b>-1</b> (<c>Monster.h:146</c>), and that is what an
    /// attack with no spell carries — all 58 of <c>DefaultDesign</c>'s are -1. Nothing is dropped:
    /// a key that resolves to no record keeps its number, so the writer still refuses and the
    /// failure stays visible.
    /// </remarks>
    public static MonsterRecord Upgrade(MonsterRecord monster,
                                        IReadOnlyList<SpellRecord> spells,
                                        IReadOnlyList<ItemRecord> items)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(items);

        if (!NeedsUpgrade(monster))
        {
            return monster;
        }

        var attacks = monster.Attacks.Select(a =>
            a.LegacySpellId > 0 && SpellNamed(spells, a.LegacySpellId) is { } spell
                ? a with { SpellId = spell, LegacySpellId = 0 }
                : a).ToList();

        var held = monster.Items is null
            ? null
            : monster.Items with
            {
                Items = [.. monster.Items.Items.Select(i =>
                    i.LegacyItemId > 0 && ItemNamed(items, i.LegacyItemId) is { } name
                        ? i with { ItemId = name, LegacyItemId = 0 }
                        : i)],
            };

        return monster with { Attacks = attacks, Items = held };
    }

    /// <summary>Every monster in a database, converted.</summary>
    public static IReadOnlyList<MonsterRecord> Upgrade(IReadOnlyList<MonsterRecord> monsters,
                                                       IReadOnlyList<SpellRecord> spells,
                                                       IReadOnlyList<ItemRecord> items)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        return monsters.Any(NeedsUpgrade)
            ? [.. monsters.Select(m => Upgrade(m, spells, items))]
            : monsters;
    }

    /// <summary>
    /// The name of the spell carrying this legacy key, or null.
    /// </summary>
    /// <remarks>
    /// <c>SPELL_DATA_TYPE::FindPreVersionSpellNamesSpellID</c>: a linear search for the record
    /// whose own <c>preSpellNameKey</c> matches, taking the first.
    /// </remarks>
    private static string? SpellNamed(IReadOnlyList<SpellRecord> spells, int key)
    {
        foreach (var spell in spells)
        {
            if (spell.PreSpellNameKey == key)
            {
                return spell.Name;
            }
        }

        return null;
    }

    /// <summary><c>ITEM_DATA_TYPE::FindPreVersionSpellNamesItemID</c> (<c>Items.cpp:6463</c>).</summary>
    private static string? ItemNamed(IReadOnlyList<ItemRecord> items, int key)
    {
        foreach (var item in items)
        {
            if (item.Names.PreSpellNameKey == key)
            {
                return item.Names.UniqueName;
            }
        }

        return null;
    }
}
