using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// One actor reference inside an active spell (<c>ActorType</c>, <c>Globals.cpp:4119</c>) — the
/// context that identifies a caster or a target.
/// </summary>
public sealed record ActorRef(
    string ClassId, uint EnemyAlly, uint Flags, uint Instance,
    string RaceId, int Level, string ItemId);

/// <summary>
/// The map squares and targets a lingering spell has already touched
/// (<c>SPELL_LINGER_DATA</c>, <c>Spell.h:1080</c>).
/// </summary>
public sealed record SpellLingerData(
    int OnceOnly, IReadOnlyList<(int X, int Y)> MapData, IReadOnlyList<int> AffectedTargets);

/// <summary>One spell still running when the game was saved (<c>ACTIVE_SPELL</c>).</summary>
public sealed record ActiveSpell(
    int Key, ActorRef Caster, ActorRef Target, string SpellId,
    uint StopTime, uint CountTime, int Lingers, int CasterLevel,
    SpellLingerData LingerData);

/// <summary>
/// One database's saved attributes: the record's name, and the attribute list under it.
/// </summary>
/// <remarks>
/// The shape every <c>Save</c>/<c>Restore</c> pair in a savegame's tail uses. The name is how the
/// loader matches a saved record against the design it is loading into, which is what lets a
/// design gain records without invalidating old saves.
/// </remarks>
public sealed record SavedAttributes(string Name, IReadOnlyList<AslEntry> Attributes);

/// <summary>
/// The treasure a combat left behind (<c>COMBAT_TREASURE_DATA</c>, <c>GameEvent.cpp:7808</c>).
/// </summary>
/// <remarks>
/// <b>Items then money — the opposite order to the <c>COMBAT_TREASURE</c> event</b>, which writes
/// money then items four lines further down the same file. The names differ by one word and the
/// layouts are transposed; nothing but reading both declarations distinguishes them.
/// </remarks>
public sealed record CombatTreasureData(ItemList Items, MoneySack Money);

/// <summary>One level's saved state (<c>LEVEL_STATS::Save</c>, <c>GlobalData.cpp:3429</c>).</summary>
public sealed record SavedLevelStats(
    IReadOnlyList<AslEntry> Attributes, int StatsVersion,
    WallOverrides? Overrides, CellLevelContents? Contents);

/// <summary>Everything a savegame carries after its global vaults.</summary>
public sealed record SaveGameTail(
    IReadOnlyList<ActiveSpell> ActiveSpells,
    IReadOnlyList<SavedAttributes> Spells,
    IReadOnlyList<AslEntry> GlobalAttributes, CombatTreasureData CombatTreasure,
    IReadOnlyList<SavedLevelStats> Levels,
    IReadOnlyList<SavedAttributes> Keys,
    IReadOnlyList<SavedAttributes> SpecialItems,
    IReadOnlyList<SavedAttributes> Items,
    IReadOnlyList<SavedAttributes> Monsters);

/// <summary>
/// Reads what a savegame writes after its vaults (<c>Dgngame.cpp:443-465</c>) — an
/// <c>ACTIVE_SPELL_LIST</c> and seven <c>Save</c>/<c>Restore</c> pairs.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>Save</c> and <c>Restore</c> are a third verb pair, and they write almost nothing.</b>
/// <c>ITEM_DATA::Save</c> is one line — the record's attribute list — and so are the monster and
/// spell ones. A database-level <c>Save</c> adds a count and a name per record so the loader can
/// match objects up. Only <c>GLOBAL_STATS::Save</c> carries anything else, a trailing
/// <c>combatTreasure</c>, and <c>LEVEL_INFO::Save</c> is 255 <c>LEVEL_STATS</c> whatever the design
/// holds.
/// </para>
/// <para>
/// <b>The asymmetry to know about before touching this.</b> <c>Save</c> writes attributes through
/// the ASL's <i>save</i> path, which skips read-only entries; <c>Restore</c> reads them with the
/// ordinary <c>Serialize</c>. So a saved attribute list is a <b>subset</b> of the design's, by
/// construction, and a round trip through this port will not recover entries the save never
/// carried.
/// </para>
/// </remarks>
public static class SaveGameTailReaders
{
    /// <summary><c>MAX_LEVELS</c> — how many <c>LEVEL_STATS</c> a save always carries.</summary>
    public const int LevelSlots = 255;

    /// <summary>
    /// The synthetic attribute <c>LEVEL_STATS::Save</c> inserts and then deletes
    /// (<c>GlobalData.cpp:3432</c>).
    /// </summary>
    /// <remarks>
    /// It is not a member of the structure — it is written into the attribute list purely so the
    /// reader can tell which of the two trailing tables follow, then removed again. Version 1
    /// brings the wall overrides, version 2 the cell contents.
    /// </remarks>
    public const string StatsVersionAttribute = "__LEVEL_STATS_VERSION";

    public static SaveGameTail Read(IArchiveCursor ar, DesignVersion version, ArchiveRole role)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var activeSpells = ReadActiveSpells(ar, version);

        var spells = ReadSavedAttributes(ar, version, AslMaps.SpellData);

        var globalAttributes = AslReader.Read(ar, version, AslMaps.GlobalStats);
        var combatTreasure = new CombatTreasureData(
            MonsterLeafReaders.ReadItemList(ar, version, role),
            MonsterLeafReaders.ReadMoneySack(ar, version));

        var levels = new List<SavedLevelStats>(LevelSlots);
        for (int i = 0; i < LevelSlots; i++)
        {
            levels.Add(ReadLevelStats(ar, version));
        }

        var keys = ReadSavedAttributes(ar, version, AslMaps.SpecialObjectData);
        var specialItems = ReadSavedAttributes(ar, version, AslMaps.SpecialObjectData);
        var items = ReadSavedAttributes(ar, version, AslMaps.ItemData);
        var monsters = ReadSavedAttributes(ar, version, AslMaps.MonsterData);

        return new SaveGameTail(activeSpells, spells, globalAttributes, combatTreasure,
                                levels, keys, specialItems, items, monsters);
    }

    /// <summary>Reads an <c>ACTIVE_SPELL_LIST</c> (<c>Spell.cpp:7994</c>): a count then the spells.</summary>
    public static List<ActiveSpell> ReadActiveSpells(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var spells = new List<ActiveSpell>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            spells.Add(ReadActiveSpell(ar, version));
        }
        return spells;
    }

    /// <summary>
    /// Reads one <c>ACTIVE_SPELL</c> (<c>Spell.h:1278</c>).
    /// </summary>
    /// <remarks>
    /// <b>The storing and loading branches disagree about field order</b>, which is the one place
    /// in this format where they do. Storing writes <c>Lingers</c>, <c>casterLevel</c>,
    /// <c>lingerData</c>; loading reads <c>Lingers</c>, <c>lingerData</c>, <c>casterLevel</c>. A
    /// <c>SPELL_LINGER_DATA</c> is never zero-length — it is at least a flag and two counts — so
    /// the two cannot agree, and a save the reference wrote with any active spell in it does not
    /// read back correctly in the reference either. This reader follows the <i>loading</i> branch,
    /// because that is what decides whether a file loads; see
    /// <see cref="SaveGameTailWriters.WriteActiveSpell"/> for what the writer does about it.
    /// </remarks>
    public static ActiveSpell ReadActiveSpell(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int key = ar.ReadInt32();
        var caster = ReadActor(ar, version);

        // Below 1.0303 the target was not stored at all; the reference clears it.
        var target = version.Value >= 1.0303
            ? ReadActor(ar, version)
            : new ActorRef(string.Empty, 0, 0, 0, string.Empty, 0, string.Empty);

        string spellId = ar.ReadString();
        uint stopTime = ar.ReadUInt32();
        uint countTime = ar.ReadUInt32();

        int lingers = 0;
        var lingerData = new SpellLingerData(0, [], []);
        if (version >= DesignVersion.V0906)
        {
            lingers = ar.ReadInt32();
            lingerData = ReadLingerData(ar);
        }

        int casterLevel = version.Value >= 0.975 ? ar.ReadInt32() : 0;

        return new ActiveSpell(key, caster, target, spellId, stopTime, countTime,
                               lingers, casterLevel, lingerData);
    }

    /// <summary>Reads an <c>ActorType</c> (<c>Globals.cpp:4119</c>).</summary>
    public static ActorRef ReadActor(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string classId = ar.ReadString();
        uint enemyAlly = ar.ReadUInt32();
        uint flags = ar.ReadUInt32();
        uint instance = ar.ReadUInt32();
        string raceId = ar.ReadString();

        int level = version >= DesignVersion.V06991 ? ar.ReadInt32() : 0;
        string itemId = ar.ReadString();

        return new ActorRef(classId, enemyAlly, flags, instance, raceId, level, itemId);
    }

    /// <summary>Reads a <c>SPELL_LINGER_DATA</c> (<c>Spell.h:1080</c>).</summary>
    public static SpellLingerData ReadLingerData(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int onceOnly = ar.ReadInt32();

        int mapCount = ar.ReadInt32();
        var map = new List<(int, int)>(Math.Max(mapCount, 0));
        for (int i = 0; i < mapCount; i++)
        {
            map.Add((ar.ReadInt32(), ar.ReadInt32()));   // a Win32 POINT: two LONGs
        }

        int targetCount = ar.ReadInt32();
        var targets = new List<int>(Math.Max(targetCount, 0));
        for (int i = 0; i < targetCount; i++)
        {
            targets.Add(ar.ReadInt32());
        }

        return new SpellLingerData(onceOnly, map, targets);
    }

    /// <summary>Reads a database's saved attributes: a count, then a name and an ASL each.</summary>
    public static List<SavedAttributes> ReadSavedAttributes(IArchiveCursor ar,
                                                            DesignVersion version, string mapName)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();
        var saved = new List<SavedAttributes>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            string name = ar.ReadString();
            saved.Add(new SavedAttributes(name, AslReader.Read(ar, version, mapName)));
        }
        return saved;
    }

    /// <summary>Reads one <c>LEVEL_STATS</c>'s saved state (<c>GlobalData.cpp:3451</c>).</summary>
    /// <remarks>
    /// The two trailing tables are gated on <see cref="StatsVersionAttribute"/>, an attribute the
    /// save itself carries rather than a field — so the structure's length is decided by its own
    /// attribute list, which nothing else in the format does.
    /// </remarks>
    public static SavedLevelStats ReadLevelStats(IArchiveCursor ar, DesignVersion version)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var attributes = AslReader.Read(ar, version, AslMaps.LevelStats);

        int statsVersion = 0;
        var carried = attributes.FirstOrDefault(a => a.Key == StatsVersionAttribute);
        if (carried is not null && int.TryParse(carried.Value, out int parsed))
        {
            statsVersion = parsed;
        }

        WallOverrides? overrides = null;
        CellLevelContents? contents = null;
        if (statsVersion >= 1)
        {
            overrides = CellContentsReaders.ReadWallOverrides(ar);
            if (statsVersion >= 2)
            {
                contents = CellContentsReaders.ReadCellContents(ar);
            }
        }

        // The reference deletes the marker after reading it, so it is not part of the level's
        // attributes as far as anything downstream is concerned.
        var kept = attributes.Where(a => a.Key != StatsVersionAttribute).ToList();

        return new SavedLevelStats(kept, statsVersion, overrides, contents);
    }
}
