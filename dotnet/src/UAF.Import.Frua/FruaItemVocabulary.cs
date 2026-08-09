namespace UAF.Import.Frua;

/// <summary>
/// The words FRUA builds item names from (<c>ImportUAItemVocab</c>,
/// <c>UAFWinEd/UAImport.cpp:5432</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Extracted mechanically from the C++ rather than typed</b>, per this port's standing rule
/// about generated tables. 126 entries, index 0 being the empty string.
/// </para>
/// <para>
/// <b>Index 77 is <c>"Bundle of"</c> and is load-bearing.</b> When an item's third name index is
/// 77, its second is read as a <i>quantity</i> rather than as a word — see
/// <see cref="FruaItem.Name"/>. That is the only value in the table with syntax attached to it.
/// </para>
/// </remarks>
public static class FruaItemVocabulary
{
    /// <summary>The index that turns the second name field into a count.</summary>
    public const int BundleOf = 77;

    /// <summary>The highest index the reference will look up; 126 and above are ignored.</summary>
    public const int Limit = 126;

    private static readonly string[] WordsById =
    [
        "", "Battle Axe", "Hand Axe", "Club", "Dagger", "Dart", "Hammer", "Javelin", "Mace",
        "Morning Star", "Military Pick", "Awl Pike", "Bolt", "Scimitar", "Spear",
        "Quarter Staff", "Bastard Sword", "Broad Sword", "Long Sword", "Short Sword",
        "Two-Handed Sword", "Trident", "Composite Long Bow", "Composite Short Bow", "Long Bow",
        "Short Bow", "Fine", "Light Crossbow", "Sling", "Staff", "Arrow", "Leather", "Ring",
        "Scale", "Chain", "Banded", "Plate", "Shield", "Cleric", "Scroll", "Mage", "Helm",
        "Belt", "Robe", "Cloak", "Boots", "Ring", "Mail", "Armor", "Of Prot", "Bracers", "Wand",
        "Elixir", "Potion", "Youth", "Ruby", "Boulder", "Dragon Breath", "Displacement", "Eyes",
        "Drow", "Elfin Chain", "Ice Storm", "Sapphire", "Emerald", "Wizardry", "Hornet's Nest",
        "Fire Resistance", "Stone", "Good Luck", "Flail", "Halberd", "Gauntlets", "Periapt",
        "Health", "Cursed", "Blessed", "Bundle of", "Ogre Power", "Girdle", "Giant Strength",
        "Mirror", "Necklace", "Dragon", "vs Giants", "vorpal", "cold resistance", "Diamond",
        "Lightning", "Fireballs", "of", "Vulnerability", "Speed", "Silver", "Extra", "Healing",
        "Charming", "Fear", "Magic Missiles", "Missiles", "1 Spell", "2 Spells", "3 Spells",
        "Paralyzation", "Invisibility", "Cute Yellow Canary", "AC 10", "AC 6", "AC 4", "AC 3",
        "AC 2", "+1", "+2", "+3", "+4", "+5", "-1", "-2", "-3", "Electric Immunity",
        "Gaze Resistance", "Spiritual", "Gem", "Jewelry", "blinking", "from evil"
    ];

    /// <summary>The word at an index, or the empty string when it is out of range.</summary>
    /// <remarks>
    /// The reference guards every lookup with <c>&gt; 0 &amp;&amp; &lt; 126</c>, so 0 and anything
    /// past the table contribute nothing to a name rather than failing.
    /// </remarks>
    public static string Word(int index) =>
        index > 0 && index < Limit ? WordsById[index] : string.Empty;

    /// <summary>How many words the table holds.</summary>
    public static int Count => WordsById.Length;
}
