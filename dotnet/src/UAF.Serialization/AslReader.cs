using UAF.Common;

namespace UAF.Serialization;

/// <summary>Per-entry flags (<c>ASL.h:143-154</c>).</summary>
[Flags]
public enum AslFlags : byte
{
    None = 0,

    /// <summary>Cannot be deleted, changed, or written to a savegame. Implies <see cref="Design"/>.</summary>
    ReadOnly = 1,

    /// <summary>Changed during gameplay.</summary>
    Modified = 2,

    /// <summary>Created by the editor. Informational.</summary>
    Design = 4,

    /// <summary>System attribute. Informational.</summary>
    System = 8,

    /// <summary>What the editor stamps on attributes it creates: <c>ReadOnly | Design</c>.</summary>
    Editor = ReadOnly | Design,
}

/// <summary>One attribute/string-list entry: a key, a flags byte, and a value.</summary>
public sealed record AslEntry(string Key, byte Flags, string Value);

/// <summary>
/// Every ASL map name used across the codebase — the literal each call site passes, which the
/// reader must match exactly.
/// </summary>
/// <remarks>
/// <para>
/// These are sync markers, not labels: a mismatch is how a desynchronised stream announces
/// itself, so they are spelled out rather than derived from a type name.
/// </para>
/// <para>
/// <b>The names follow no convention.</b> Most end in <c>_ATTRIBUTES</c>, three end in
/// <c>_ATTR</c>, and <see cref="Tale"/> and <see cref="TavernTale"/> have no suffix at all.
/// Searching for one spelling finds a convincing subset and misses the rest — which is exactly
/// what happened when this list was first built from a <c>_ATTRIBUTES</c> grep.
/// </para>
/// </remarks>
public static class AslMaps
{
    public const string Character = "CHARACTER_ATTRIBUTES";
    public const string GlobalStats = "GLOBAL_STATS_ATTRIBUTES";
    public const string ItemData = "ITEM_DATA_ATTRIBUTES";
    public const string Level = "LEVEL_ATTRIBUTES";
    public const string LevelStats = "LEVEL_STATS_ATTRIBUTES";
    public const string MonsterData = "MONSTER_DATA_ATTRIBUTES";
    public const string Party = "PARTY_ATTRIBUTES";
    public const string QuestData = "QUEST_DATA_ATTRIBUTES";
    public const string RaceData = "RACE_DATA_ATTRIBUTES";
    public const string SpecialObjectData = "SPECIAL_OBJECT_DATA_ATTRIBUTES";
    public const string SpellData = "SPELL_DATA_ATTRIBUTES";
    public const string Zone = "ZONE_ATTRIBUTES";

    // Names that do NOT end in _ATTRIBUTES.
    public const string EventControl = "EVENTCONT_ATTR";
    public const string EventData = "EVENT_DATA_ATTR";
    public const string StepEvent = "STEPEVENT_ATTR";
    public const string TimeEvent = "TIME_EVENT_ATTR";
    public const string Tale = "TALE";
    public const string TavernTale = "TAVTALE";
}

/// <summary>
/// Reads the attribute/string list (ASL) block that terminates most records.
/// </summary>
/// <remarks>
/// <para>
/// Almost every major class ends with <c>*_asl.Serialize(ar, "…_ATTRIBUTES")</c>, so a record
/// cannot be read to completion — and a reader cannot advance to the next record — without this.
/// The format (<c>ASL.cpp:1386</c>) is compact despite the size of the file it lives in:
/// </para>
/// <code>
///   if (globalData.version &gt;= _ASL_LEVEL_)        // 0.505; below this NOTHING is read
///   {
///       ar &gt;&gt; mapName;                             // must equal the expected name
///       ar &gt;&gt; count;                               // WORD -- 16 bits, not 32
///       for (count) { key; flags; value }          // string, byte, string
///   }
/// </code>
/// <para>
/// <b>The map name is a self-validating sync marker.</b> The reference throws when it does not
/// match the expected literal (<c>ASL.cpp:1410</c>), which makes this block unusually good at
/// catching a desynchronised stream: a misaligned reader almost never produces the exact
/// expected string. That is worth preserving rather than skipping past.
/// </para>
/// <para>
/// <b>The block carries real payload, not just metadata.</b> <c>Items.cpp:2627</c> notes that
/// <c>MissileArt</c> "is serialized in attribute map" — fields migrated here instead of getting
/// a new version gate. So an ASL cannot be read-and-discarded; some record fields exist nowhere
/// else.
/// </para>
/// <para>
/// <b>Position in a record.</b> ASL is the last thing a record writes, immediately after its
/// special abilities (<c>Items.cpp:2631-2632</c>):
/// </para>
/// <code>
///   specAbs.Serialize(ar, ver, m_idName, "item");
///   item_asl.Serialize(ar, "ITEM_DATA_ATTRIBUTES");
/// </code>
/// <para>
/// Both halves of that tail are therefore required to walk from one record to the next; ASL
/// alone is not sufficient.
/// </para>
/// <para>
/// <b>Entries are hash-ordered, not insertion-ordered.</b> The underlying container is a
/// <c>CMapStringToPtr</c> walked with <c>GetNextAssoc</c>, so the order is whatever the hash
/// produces. The same four global-stats keys come out as
/// <c>RunAsVersion, GuidedTourVersion, SpecialItemKeyQtyVersion, ItemUseEventVersion</c> in the
/// uncompressed DefaultDesign but as
/// <c>GuidedTourVersion, ItemUseEventVersion, RunAsVersion, SpecialItemKeyQtyVersion</c> in all
/// three compressed designs tested (2.53, 3.55, 5.28). Look entries up by key, never by index,
/// and compare round-trips as sets.
/// </para>
/// <para>
/// <b>The compressed form cannot be read by seeking.</b> In the designs examined every key is
/// written out fresh, but entries sharing a value store a string-table index instead of the
/// text — and that index counts strings interned since the start of the stream. So the plain
/// encoding of a block is self-describing while the compressed encoding of the same block is
/// not.
/// </para>
/// </remarks>
public static class AslReader
{
    /// <summary>
    /// Below this version the block is absent entirely — no name, no count, no bytes consumed.
    /// <c>_ASL_LEVEL_</c> is <c>_VERSION_0505_</c> (<c>Externs.h:185</c>).
    /// </summary>
    public static DesignVersion MinimumVersion => DesignVersion.V0505;

    /// <summary>True when this version stores an ASL block at all.</summary>
    public static bool IsPresent(DesignVersion version) => version >= MinimumVersion;

    /// <summary>
    /// Whether an entry is written to a <b>savegame</b>. Design files keep everything.
    /// </summary>
    /// <remarks>
    /// Two write paths share one read format. <c>Serialize</c> (<c>ASL.cpp:1386</c>) writes every
    /// entry and is what design files contain; <c>Save</c> (<c>ASL.cpp:1489</c>) counts and writes
    /// only entries without <see cref="AslFlags.ReadOnly"/>. Because reading is identical, this
    /// only matters once the port writes savegames — but getting the count wrong there produces a
    /// file that reads back cleanly with silently missing attributes.
    /// </remarks>
    public static bool IsSavedInSavegame(AslEntry entry) =>
        ((AslFlags)entry.Flags & AslFlags.ReadOnly) == 0;

    /// <summary>
    /// Applies the key fixup the <b>compressed</b> path performs (<c>ASL.cpp:1236</c>): every
    /// character below 0x20 has 0x20 added to it.
    /// </summary>
    /// <remarks>
    /// This exists only in the <c>CAR</c> overload — the <c>CArchive</c> twin
    /// (<c>ASL.cpp:1247</c>) reads the key verbatim. So the same key can differ between an
    /// uncompressed and a compressed design, and a shared reader that applies the fixup
    /// unconditionally corrupts keys in plain files.
    /// </remarks>
    public static string FixUpCompressedKey(string key)
    {
        Span<char> buffer = key.Length <= 128 ? stackalloc char[key.Length] : new char[key.Length];
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            buffer[i] = c < 0x20 ? (char)(c + 0x20) : c;
        }
        return new string(buffer);
    }

    /// <summary>Reads an ASL block from an uncompressed stream.</summary>
    public static List<AslEntry> Read(MfcArchiveReader ar, DesignVersion version, string expectedMapName) =>
        Read(ArchiveCursor.For(ar), version, expectedMapName);

    /// <summary>Reads an ASL block from a compressed CAR stream, applying the key fixup.</summary>
    public static List<AslEntry> Read(CarArchiveReader ar, DesignVersion version, string expectedMapName) =>
        Read(ArchiveCursor.For(ar), version, expectedMapName);

    /// <summary>
    /// Reads an ASL block, applying the key fixup only when the cursor is a compressed one.
    /// </summary>
    public static List<AslEntry> Read(IArchiveCursor ar, DesignVersion version, string expectedMapName)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var entries = new List<AslEntry>();
        if (!IsPresent(version))
        {
            return entries;
        }

        VerifyMapName(ar.ReadString(), expectedMapName);

        ushort count = ar.ReadUInt16();          // WORD, not int
        for (int i = 0; i < count; i++)
        {
            // ASL.cpp:1236 applies the fixup; the CArchive twin at :1247 reads verbatim.
            string key = ar.ReadString();
            if (ar.IsCompressed)
            {
                key = FixUpCompressedKey(key);
            }

            byte flags = ar.ReadByte();
            string value = ar.ReadString();
            entries.Add(new AslEntry(key, flags, value));
        }
        return entries;
    }

    private static void VerifyMapName(string actual, string expected)
    {
        if (actual != expected)
        {
            // The reference throws 7 here (ASL.cpp:1420). Treating a mismatch as fatal is the
            // point: it is the cheapest reliable signal that the stream has desynchronised.
            throw new InvalidDataException(
                $"ASL map name mismatch: expected '{expected}', found '{actual}'. " +
                "The stream is misaligned before this block.");
        }
    }
}
