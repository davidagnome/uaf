using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Turns a pre-0.998101 item's <c>Usable_by_Class</c> bitmask into a baseclass list.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the conversion the reference defers rather than skips.</b> Below
/// <see cref="DesignVersion.SpellNames"/> an item says which classes may use it as seven bits;
/// above it, as a counted list of <c>BASECLASS_ID</c> names. The reader cannot convert on the spot
/// because the answer depends on <c>classes.dat</c>, which has not been read yet — so
/// <c>ITEM_DATA::Serialize</c> stashes the mask in <c>preSpellNamesUsability</c> and
/// <c>FixPreSpellNamesUsability</c> (<c>Items.cpp:6422</c>) walks the whole database afterwards.
/// This is that walk.
/// </para>
/// <para>
/// <b>A design's own class names win over the built-in ones.</b> Each bit maps to one of the seven
/// classic classes, and for each the reference looks for the design's class carrying that legacy
/// key (<c>CLASS_DATA_TYPE::FindPreVersionSpellNamesClassID</c>, <c>class.cpp:7172</c>) and takes
/// its <i>first</i> baseclass. A design that renamed its fighter gets its own name; only a design
/// that never defined one falls back on <c>"fighter"</c>.
/// </para>
/// <para>
/// <b>Without this, a legacy design cannot be saved at all.</b> <c>ItemRecordWriter</c> refuses a
/// record still carrying the mask rather than write an item usable by nobody, so every design below
/// 0.998101 — the editor's own template among them — was readable and unwritable.
/// </para>
/// </remarks>
public static class ItemUsabilityUpgrade
{
    /// <summary>
    /// The seven classic classes: the bit that names one, its legacy class key, and the baseclass
    /// name to use when the design defines no class with that key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flags from <c>Externs.h:977</c>, class keys from <c>classType_Editor</c>
    /// (<c>GameRules.h:126</c>), names from <c>GlobalData.cpp:104</c>. <b>The bit order and the key
    /// order are not the same</b> — the fighter is bit 8 and key 0, the magic user bit 1 and key 4
    /// — so neither can be derived from the other.
    /// </para>
    /// <para>
    /// <b>The order of this table is the order the list comes out in</b>, because a duplicate is
    /// skipped rather than moved. It is the order the reference calls
    /// <c>AddUsableBaseclass</c> in, which is not the bit order either.
    /// </para>
    /// </remarks>
    private static readonly (int Flag, int ClassKey, string Baseclass)[] Classic =
    [
        (8, 0, "fighter"),
        (1, 4, "magicUser"),
        (2, 1, "cleric"),
        (4, 5, "thief"),
        (16, 3, "paladin"),
        (32, 2, "ranger"),
        (64, 6, "druid"),
    ];

    /// <summary>Whether this record still needs converting.</summary>
    public static bool NeedsUpgrade(ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Tail.LegacyUsableByClass != 0;
    }

    /// <summary>
    /// The baseclass names a mask stands for, in the order the reference adds them.
    /// </summary>
    /// <param name="mask">The <c>Usable_by_Class</c> bits.</param>
    /// <param name="classes">The design's classes, or empty to use the built-in names.</param>
    public static IReadOnlyList<string> BaseclassesFor(int mask, IReadOnlyList<ClassRecord> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);

        var names = new List<string>();

        foreach (var (flag, key, fallback) in Classic)
        {
            if ((mask & flag) == 0)
            {
                continue;
            }

            string name = Resolve(key, fallback, classes);

            // "Already there" is checked against the list being built, not against the mask, so
            // two bits resolving to the same renamed class contribute one entry.
            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// The design's own name for a classic class, or the built-in one.
    /// </summary>
    /// <remarks>
    /// <b>The reference's fallback here is unreachable by accident and this one diverges from
    /// it.</b> <c>FindPreVersionSpellNamesClassID</c> ends by assigning
    /// <c>classID = ClassText[classType]</c> and then immediately overwriting it with
    /// <c>ClassText[0]</c> — a dead store — so a design missing the class it is asked about gets
    /// whatever class zero happens to be, and then that class's first baseclass. Reproducing that
    /// would attach items to an unrelated class. The built-in name for the class actually asked
    /// about is what the caller already initialised the id to, and is what this returns.
    /// </remarks>
    private static string Resolve(int classKey, string fallback, IReadOnlyList<ClassRecord> classes)
    {
        foreach (var candidate in classes)
        {
            if (candidate.PreSpellNameKey == classKey)
            {
                // The class's FIRST baseclass, not its name: a class is a combination of
                // baseclasses, and an item usable by fighters is usable by the fighter baseclass.
                return candidate.Baseclasses.Count > 0 ? candidate.Baseclasses[0] : fallback;
            }
        }

        return fallback;
    }

    /// <summary>
    /// The record with its mask converted, or the record unchanged when it has none.
    /// </summary>
    /// <remarks>
    /// <b>An item that already carries a baseclass list keeps it</b> and the mask is dropped: the
    /// two are alternatives on the wire, so a record with both has been through this once already.
    /// </remarks>
    public static ItemRecord Upgrade(ItemRecord item, IReadOnlyList<ClassRecord> classes)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(classes);

        if (!NeedsUpgrade(item))
        {
            return item;
        }

        var names = item.Tail.UsableByBaseclass.Count > 0
            ? item.Tail.UsableByBaseclass
            : BaseclassesFor(item.Tail.LegacyUsableByClass, classes);

        return item with
        {
            Tail = item.Tail with { LegacyUsableByClass = 0, UsableByBaseclass = names },
        };
    }

    /// <summary>The whole database converted.</summary>
    public static ItemDatabase Upgrade(ItemDatabase database, IReadOnlyList<ClassRecord> classes)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(classes);

        return database.Items.Any(NeedsUpgrade)
            ? database with { Items = [.. database.Items.Select(i => Upgrade(i, classes))] }
            : database;
    }
}
