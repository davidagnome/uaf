using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Reads the leading, name-bearing fields of monster and spell records.
/// </summary>
/// <remarks>
/// <para>
/// Monsters (<c>Monster.cpp:668</c>) and spells (<c>Spell.cpp:3874</c>) share a preamble that is
/// simpler than <c>ITEM_DATA</c>'s:
/// </para>
/// <code>
///   if (ver &lt; VersionSpellNames || ver &gt;= VersionSaveIDs)  ar &gt;&gt; preSpellNameKey;
///   DAS(ar, Name);
/// </code>
/// <para>
/// Two differences from items are worth stating, because "all three databases look alike" is the
/// assumption that would produce a wrong reader:
/// </para>
/// <list type="bullet">
///   <item>There is <b>no <c>spellID</c></b> field here. Items read one — as a <i>string</i>,
///     since <c>SPELL_ID</c> derives from <c>CString</c> — and copying the item preamble across
///     would consume a field that does not exist.</item>
///   <item>The record class exposes a single public <c>CString Name</c> (<c>Monster.h:382</c>,
///     <c>Spell.h:460</c>) rather than <c>ITEM_DATA</c>'s private
///     <c>m_idName</c>/<c>m_uniqueName</c> pair. There is no id/unique distinction to preserve.</item>
/// </list>
/// <para>
/// The <c>|</c> qualifier convention still applies to the single name — the oracle reports spells
/// such as <c>"Detect Magic|Cleric"</c> — so it must not be stripped.
/// </para>
/// </remarks>
public static class DatabaseRecordReader
{
    /// <summary>
    /// True when the record stores <c>preSpellNameKey</c> at this version. The window in which it
    /// is <i>absent</i> is <c>[VersionSpellNames, VersionSaveIDs)</c> — the same range in which
    /// the editor warns it cannot load reliably.
    /// </summary>
    public static bool HasPreSpellNameKey(DesignVersion version) =>
        version < DesignVersion.SpellNames || version >= DesignVersion.SaveIDs;

    /// <summary>Reads a monster or spell record's preamble from an uncompressed stream.</summary>
    public static (int PreSpellNameKey, string Name) ReadPreamble(
        MfcArchiveReader ar, DesignVersion version)
    {
        int key = HasPreSpellNameKey(version) ? ar.ReadInt32() : -1;
        return (key, ArchiveStringConventions.Decode(ar.ReadString()));
    }

    /// <summary>Reads a monster or spell record's preamble from a compressed CAR stream.</summary>
    public static (int PreSpellNameKey, string Name) ReadPreamble(
        CarArchiveReader ar, DesignVersion version)
    {
        int key = HasPreSpellNameKey(version) ? ar.ReadInt32() : -1;
        return (key, ArchiveStringConventions.Decode(ar.ReadString()));
    }
}
