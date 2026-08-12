namespace UAF.Import.Frua;

/// <summary>
/// The 128 stock FRUA monster names (<c>MonsterLabels</c>, <c>UAFWinEd/UAImport.cpp:374</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Extracted mechanically from the C++</b>, per this port's rule about generated tables. Index
/// 0 is the string "none" rather than an empty slot, and the reference guards every lookup with
/// <c>index &gt; 0</c>, so it is never reached.
/// </para>
/// <para>
/// This is the <i>second</i> tier of monster resolution. A design's own <c>MONST###.DAT</c>
/// records come first and are keyed by the same index; this table names the stock monster for an
/// index the design does not override.
/// </para>
/// </remarks>
public static class FruaMonsterLabels
{
    /// <summary>The highest index the table covers.</summary>
    public const int Count = 128;

    private static readonly string[] Names =
    [
        "none", "Kobold", "Goblin", "Orc", "Hobgoblin", "Orc Chieftain", "Hobgoblin Ldr",
        "Gnoll", "Ogre", "Troll", "Hill Giant", "Fire Giant", "Frost Giant", "Cloud Giant",
        "Storm Giant", "Giant Rat", "Carrion Crawler", "Enormous Spider", "Skeleton", "Zombie",
        "Ghoul", "Ghast", "Wight", "Wraith", "Mummy", "Spectre", "Vampire", "Lich", "Lizard Man",
        "Lizard Man King", "Minotaur", "Displacer Beast", "Boring Beetle", "Griffon", "Hydra",
        "Wyvern", "Black Dragon", "Blue Dragon", "Green Dragon", "Red Dragon", "White Dragon",
        "Dracolich", "Basilisk", "Gorgon", "Cockatrice", "Beholder", "Ogre Mage", "Bulette",
        "Shambling Mound", "Margoyle", "Dracolisk", "Mobat", "Black Pudding", "Otyugh",
        "Neo Otyugh", "Salamander", "Efreeti", "Earth Elemental", "Fire Elemental", "Umber Hulk",
        "Ettin", "Owlbear", "Bugbear", "Medusa", "Giant Spider", "Phase Spider",
        "Poisonous Snake", "Hell Hound", "Giant Crocodile", "Drider", "Iron Golem", "Rakshasa",
        "Purple Worm", "Drow Champion", "Drow Priest", "Drow Sorceress", "Drow Priestess",
        "Warrior", "Conjurer", "Acolyte", "Goon", "Archer", "Theurgist", "Priest", "Thug",
        "Evil Champion", "Magician", "Dark Cleric", "Rogue", "Dark Knight", "Necromancer",
        "High Priest", "Thief", "Dark Warlord", "Wizard", "Archpriest", "Master Thief",
        "Dark Overlord", "Master Wizard", "Dark Disciple", "High Thief", "Vampire Lord",
        "Vampiress", "Dazmilar", "Ogre Shaman", "Sir Dutiocs", "Vidruand", "Hill Giant Shaman",
        "Vampire Priest", "Drow Amazon", "Rakshasa Rukh", "Road Guard", "Kallithrea", "Yemandra",
        "Krondasz", "Arderiel", "Tornilee", "Alias", "Dragonbait", "Nacacia", "Priam", "Vala",
        "Silk", "Captain Daenor", "Grunschka", "Storm", "Shal", "Raizel"
    ];

    /// <summary>The stock name for an index, or null when it is out of range.</summary>
    /// <remarks>
    /// <b>Index 0 yields null, not "none".</b> The reference's <c>index &gt; 0</c> guard means
    /// slot 0 can never be looked up, so returning its placeholder string would invent a monster
    /// called "none".
    /// </remarks>
    public static string? Name(int index) =>
        index > 0 && index < Count ? Names[index] : null;
}
