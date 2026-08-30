using UAF.Common;

namespace UAF.Serialization;

/// <summary>A spell a spellgroup names (<c>SPELL_REFERENCE</c>, <c>class.h:926</c>).</summary>
/// <remarks>
/// The reference carries only a spell id; the numeric key the editor once wrote beside it is a
/// pre-<see cref="DesignVersion.SpellNames"/> editor-only field, discarded on read.
/// </remarks>
public sealed record SpellReference(string SpellId);

/// <summary>A spellgroup a spellgroup nests (<c>SPELLGROUP_REFERENCE</c>, <c>class.h:942</c>).</summary>
public sealed record SpellGroupReference(string SpellGroupId);

/// <summary>A <c>SPELLGROUP_DATA</c> record (<c>spellgroups.dat</c>).</summary>
public sealed record SpellGroupRecord(string Name, IReadOnlyList<SpellReference> Spells,
                                      IReadOnlyList<SpellGroupReference> SpellGroups);

/// <summary>
/// Reads <c>spellgroups.dat</c>'s records (<c>SPELLGROUP_DATA::Serialize</c>,
/// <c>class.cpp:9224</c>, loading branch).
/// </summary>
/// <remarks>
/// <para>
/// <b>The container tag and the design version both decide the record's shape, in different
/// places.</b> The container tag — <c>"SpGrpV1"</c> in <c>DefaultDesign</c> — gates a numeric key
/// on the spellgroup reference (<c>class.cpp:1435</c>) and chooses which version each spell
/// reference is read at (<c>class.cpp:9270</c>). The design version gates the trailing special
/// abilities (<c>globalData.version &gt;= _SPECIAL_ABILITIES_VERSION_</c>, <c>class.cpp:9297</c>).
/// </para>
/// <para>
/// <b>The special-abilities block is refused, not read.</b> It uses
/// <see cref="SPECIAL_ABILITIES"/>'s name/type overload (<c>Specab.cpp:1418</c>), which no reader
/// in this port models, and the only design that ships a spellgroups file — <c>DefaultDesign</c> at
/// 0.915 — is below the 0.930 gate and carries none. Reading it against no fixture would be the
/// guess the porting plan's rules forbid.
/// </para>
/// </remarks>
public static class SpellGroupRecordReader
{
    /// <summary>The record's own version tag (<c>class.cpp:9228</c>).</summary>
    public const string SupportedTag = "SG0";

    /// <summary>
    /// The container tag below which an editor stream writes a numeric key per spellgroup
    /// reference (<c>class.cpp:1435</c>, lexicographic).
    /// </summary>
    public const string ReferenceKeyGate = "SpGrpV3";

    /// <summary>
    /// Reads one record.
    /// </summary>
    /// <param name="ar">The payload cursor, positioned after the container's count.</param>
    /// <param name="containerTag">The container's tag, e.g. <c>"SpGrpV1"</c>.</param>
    /// <param name="designVersion">The design version, off <c>game.dat</c>.</param>
    public static SpellGroupRecord Read(IArchiveCursor ar, string containerTag,
                                        DesignVersion designVersion,
                                        ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string tag = ar.ReadString();
        if (tag != SupportedTag)
        {
            throw new InvalidDataException(
                $"unknown SPELLGROUP_DATA version '{tag}'; only {SupportedTag} exists.");
        }

        // The name's numeric key is read before the name, editor-only, for pre-SpGrpV3 containers.
        if (role == ArchiveRole.Editor && string.CompareOrdinal(containerTag, ReferenceKeyGate) < 0)
        {
            ar.ReadUInt32();
        }

        string name = ArchiveStringConventions.Decode(ar.ReadString());

        // A spell reference is read at VersionSpellIDs before SpGrpV2 and VersionSpellNames after.
        DesignVersion spellRefVersion = string.CompareOrdinal(containerTag, "SpGrpV2") < 0
            ? DesignVersion.SpellIDs
            : DesignVersion.SpellNames;

        uint spellCount = ar.ReadCount();
        var spells = new List<SpellReference>((int)spellCount);
        for (uint i = 0; i < spellCount; i++)
        {
            spells.Add(ReadSpellReference(ar, spellRefVersion, role));
        }

        uint groupCount = ar.ReadCount();
        var groups = new List<SpellGroupReference>((int)groupCount);
        for (uint i = 0; i < groupCount; i++)
        {
            groups.Add(ReadSpellGroupReference(ar, containerTag, role));
        }

        RefuseSpecialAbilities(designVersion);

        return new SpellGroupRecord(name, spells, groups);
    }

    /// <summary>Reads a whole database's worth.</summary>
    public static List<SpellGroupRecord> ReadAll(IArchiveCursor ar, uint count,
                                                 string containerTag, DesignVersion designVersion,
                                                 ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var records = new List<SpellGroupRecord>((int)count);
        for (uint i = 0; i < count; i++)
        {
            records.Add(Read(ar, containerTag, designVersion, role));
        }

        return records;
    }

    private static SpellReference ReadSpellReference(IArchiveCursor ar, DesignVersion version,
                                                     ArchiveRole role)
    {
        string spellId = ArchiveStringConventions.Decode(ar.ReadString());

        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            ar.ReadUInt32();
        }

        return new SpellReference(spellId);
    }

    private static SpellGroupReference ReadSpellGroupReference(IArchiveCursor ar,
                                                               string containerTag, ArchiveRole role)
    {
        string groupId = ArchiveStringConventions.Decode(ar.ReadString());

        if (role == ArchiveRole.Editor && string.CompareOrdinal(containerTag, ReferenceKeyGate) < 0)
        {
            ar.ReadUInt32();
        }

        return new SpellGroupReference(groupId);
    }

    /// <summary>
    /// The <c>SPECIAL_ABILITIES</c> name/type overload (<c>Specab.cpp:1418</c>) is not ported; the
    /// only fixture is below its gate. Refuse rather than read an unverifiable shape.
    /// </summary>
    private static void RefuseSpecialAbilities(DesignVersion designVersion)
    {
        if (designVersion >= DesignVersion.SpecialAbilities)
        {
            throw new InvalidDataException(
                $"spellgroup at design version {designVersion} carries a special-abilities block; "
                + "the name/type overload is not ported and no fixture exercises it.");
        }
    }
}
