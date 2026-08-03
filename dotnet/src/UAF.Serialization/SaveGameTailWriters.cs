using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes what a savegame carries after its vaults — the inverse of
/// <see cref="SaveGameTailReaders"/> (<c>Dgngame.cpp:443-465</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Attributes go out through the ASL's <c>Save</c> path, not <c>Serialize</c>.</b> That path
/// skips read-only entries and counts the filtered set rather than the whole one — the reference
/// walks the list twice for exactly that reason. A savegame's attribute lists are therefore a
/// <i>subset</i> of the design's by construction, which is the point: a save carries what
/// gameplay changed, and the design supplies the rest.
/// </para>
/// </remarks>
public static class SaveGameTailWriters
{
    public static void Write(IArchiveWriteCursor ar, SaveGameTail tail)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(tail);

        WriteActiveSpells(ar, tail.ActiveSpells);

        WriteSavedAttributes(ar, tail.Spells, AslMaps.SpellData);

        AslWriter.Save(ar, SaveGameWriter.WrittenVersion, AslMaps.GlobalStats,
                       tail.GlobalAttributes);
        WriteCombatTreasure(ar, tail.CombatTreasure);

        if (tail.Levels.Count != SaveGameTailReaders.LevelSlots)
        {
            throw new ArgumentException(
                $"a save writes exactly {SaveGameTailReaders.LevelSlots} LEVEL_STATS, not " +
                $"{tail.Levels.Count}.", nameof(tail));
        }

        foreach (var level in tail.Levels)
        {
            WriteLevelStats(ar, level);
        }

        WriteSavedAttributes(ar, tail.Keys, AslMaps.SpecialObjectData);
        WriteSavedAttributes(ar, tail.SpecialItems, AslMaps.SpecialObjectData);
        WriteSavedAttributes(ar, tail.Items, AslMaps.ItemData);
        WriteSavedAttributes(ar, tail.Monsters, AslMaps.MonsterData);
    }

    /// <summary>Writes an <c>ACTIVE_SPELL_LIST</c>: a count then the spells.</summary>
    public static void WriteActiveSpells(IArchiveWriteCursor ar,
                                         IReadOnlyList<ActiveSpell> spells)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(spells);

        ar.WriteInt32(spells.Count);
        foreach (var spell in spells)
        {
            WriteActiveSpell(ar, spell);
        }
    }

    /// <summary>
    /// Writes one <c>ACTIVE_SPELL</c> — in the order the <b>loading</b> branch reads, which is not
    /// the order the reference's storing branch writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the one place in the format where the two branches disagree, and the
    /// disagreement is a defect in the reference.</b> Storing writes <c>Lingers</c>,
    /// <c>casterLevel</c>, <c>lingerData</c> (<c>Spell.h:1288</c>); loading reads <c>Lingers</c>,
    /// <c>lingerData</c>, <c>casterLevel</c> (<c>:1310</c>). A <c>SPELL_LINGER_DATA</c> is never
    /// zero-length — it is at least a flag and two counts, twelve bytes — so the two orders cannot
    /// coincide, and <b>a save the reference wrote with any active spell in it does not read back
    /// correctly in the reference either</b>: <c>lingerData</c> consumes <c>casterLevel</c>'s four
    /// bytes and everything after drifts.
    /// </para>
    /// <para>
    /// <b>The loading order is what this writes</b>, deliberately. The rule everywhere else in this
    /// port is to transcribe the storing branch, and it is set aside here for the reason the rule
    /// exists: what matters is that the file loads. Following the storing branch would reproduce a
    /// stream that nothing — not this port, not the reference — can read. The cost is that output
    /// differs from a reference-written save in this one structure, and the corpus cannot tell:
    /// both shipped saves have an empty active-spell list.
    /// </para>
    /// </remarks>
    public static void WriteActiveSpell(IArchiveWriteCursor ar, ActiveSpell spell)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(spell);

        ar.WriteInt32(spell.Key);
        WriteActor(ar, spell.Caster);
        WriteActor(ar, spell.Target);

        ar.WriteString(spell.SpellId);               // verbatim: a SPELL_ID
        ar.WriteUInt32(spell.StopTime);
        ar.WriteUInt32(spell.CountTime);

        // Loading order -- see the remarks.
        ar.WriteInt32(spell.Lingers);
        WriteLingerData(ar, spell.LingerData);
        ar.WriteInt32(spell.CasterLevel);
    }

    /// <summary>Writes an <c>ActorType</c> (<c>Globals.cpp:4119</c>).</summary>
    public static void WriteActor(IArchiveWriteCursor ar, ActorRef actor)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(actor);

        ar.WriteString(actor.ClassId);               // verbatim: a CLASS_ID
        ar.WriteUInt32(actor.EnemyAlly);
        ar.WriteUInt32(actor.Flags);
        ar.WriteUInt32(actor.Instance);
        ar.WriteString(actor.RaceId);                // verbatim: a RACE_ID
        ar.WriteInt32(actor.Level);
        ar.WriteString(actor.ItemId);                // verbatim: an ITEM_ID
    }

    /// <summary>Writes a <c>SPELL_LINGER_DATA</c> (<c>Spell.h:1080</c>).</summary>
    public static void WriteLingerData(IArchiveWriteCursor ar, SpellLingerData linger)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(linger);

        ar.WriteInt32(linger.OnceOnly);

        ar.WriteInt32(linger.MapData.Count);
        foreach ((int x, int y) in linger.MapData)
        {
            ar.WriteInt32(x);                        // a Win32 POINT: two LONGs
            ar.WriteInt32(y);
        }

        ar.WriteInt32(linger.AffectedTargets.Count);
        foreach (int target in linger.AffectedTargets)
        {
            ar.WriteInt32(target);
        }
    }

    /// <summary>Writes a <c>COMBAT_TREASURE_DATA</c> (<c>GameEvent.cpp:7808</c>).</summary>
    /// <remarks>
    /// <b>Items then money</b> — the opposite order to the <c>COMBAT_TREASURE</c> event four lines
    /// below it in the same file. Transposing them costs nothing at read time and everything after.
    /// </remarks>
    public static void WriteCombatTreasure(IArchiveWriteCursor ar, CombatTreasureData treasure)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(treasure);

        MonsterLeafWriters.WriteItemList(ar, treasure.Items);
        MonsterLeafWriters.WriteMoneySack(ar, treasure.Money);
    }

    /// <summary>Writes one database's saved attributes: a count, then a name and an ASL each.</summary>
    public static void WriteSavedAttributes(IArchiveWriteCursor ar,
                                            IReadOnlyList<SavedAttributes> saved, string mapName)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(saved);

        ar.WriteInt32(saved.Count);
        foreach (var record in saved)
        {
            ar.WriteString(record.Name);             // verbatim: the id the loader matches on
            AslWriter.Save(ar, SaveGameWriter.WrittenVersion, mapName, record.Attributes);
        }
    }

    /// <summary>Writes one <c>LEVEL_STATS</c>'s saved state (<c>GlobalData.cpp:3429</c>).</summary>
    /// <remarks>
    /// <b>The structure's own length is decided by an attribute it carries.</b>
    /// <c>LEVEL_STATS::Save</c> inserts <c>__LEVEL_STATS_VERSION</c> into the attribute list, writes
    /// the list, then deletes the entry again — and the two trailing tables are gated on the value.
    /// Nothing else in the format decides how many bytes follow from inside its own ASL, and a
    /// writer that simply wrote the attributes it was handed would emit a version of 0 and then
    /// two tables the reader does not go looking for.
    /// </remarks>
    public static void WriteLevelStats(IArchiveWriteCursor ar, SavedLevelStats level)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(level);

        // The marker goes back in for the write and is not part of the level's own attributes.
        var attributes = new List<AslEntry>(level.Attributes)
        {
            // ASLF_MODIFIED, as the reference stamps it (GlobalData.cpp:3432) -- and it has to be
            // a flag the Save path keeps, since ReadOnly entries are filtered out.
            new(SaveGameTailReaders.StatsVersionAttribute, (byte)AslFlags.Modified,
                level.StatsVersion.ToString()),
        };

        AslWriter.Save(ar, SaveGameWriter.WrittenVersion, AslMaps.LevelStats, attributes);

        if (level.StatsVersion >= 1)
        {
            CellContentsWriters.WriteWallOverrides(
                ar, level.Overrides ?? new WallOverrides([]));

            if (level.StatsVersion >= 2)
            {
                CellContentsWriters.WriteCellContents(
                    ar, level.Contents ?? new CellLevelContents([]));
            }
        }
    }
}
