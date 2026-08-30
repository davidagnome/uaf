using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>TRAIT_DATA</c> record (<c>traits.dat</c>).</summary>
/// <param name="Key">The record's numeric key, written before the name.</param>
/// <param name="Name">The trait's name.</param>
/// <param name="Abbreviation">The trait's short form.</param>
/// <param name="Roll">The dice expression a check against the trait rolls.</param>
public sealed record TraitRecord(uint Key, string Name, string Abbreviation, DicePlus Roll);

/// <summary>
/// Reads <c>traits.dat</c>'s records (<c>TRAIT_DATA::Serialize</c>, <c>class.cpp:8873</c>, loading
/// branch).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only <c>"Tr1"</c> is read.</b> <c>"Tr0"</c> is the legacy shape that rebuilds a dice
/// expression from an adjustment list (<c>class.cpp:8893</c>), and nothing ships it — the storing
/// branch has written <c>"Tr1"</c> for every available design.
/// </para>
/// <para>
/// <b>The special-abilities block is refused, not read.</b> It uses
/// <see cref="SPECIAL_ABILITIES"/>'s name/type overload (<c>Specab.cpp:1418</c>), which no reader
/// in this port models, and the only design that ships a traits file — <c>DefaultDesign</c> at
/// 0.915 — is below the 0.930 gate and carries none.
/// </para>
/// </remarks>
public static class TraitRecordReader
{
    /// <summary>The only record version (<c>class.cpp:8877</c>).</summary>
    public const string SupportedTag = "Tr1";

    /// <summary>
    /// Reads one record.
    /// </summary>
    /// <param name="ar">The payload cursor, positioned after the container's count.</param>
    /// <param name="designVersion">The design version, off <c>game.dat</c>.</param>
    public static TraitRecord Read(IArchiveCursor ar, DesignVersion designVersion,
                                   ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string tag = ar.ReadString();
        if (tag != SupportedTag)
        {
            throw new InvalidDataException(
                $"unknown TRAIT_DATA version '{tag}'; only {SupportedTag} exists.");
        }

        uint key = ar.ReadUInt32();
        string name = ArchiveStringConventions.Decode(ar.ReadString());
        string abbreviation = ArchiveStringConventions.Decode(ar.ReadString());
        var roll = DicePlusReader.Read(ar);

        RefuseSpecialAbilities(designVersion);

        return new TraitRecord(key, name, abbreviation, roll);
    }

    /// <summary>Reads a whole database's worth.</summary>
    public static List<TraitRecord> ReadAll(IArchiveCursor ar, uint count,
                                            DesignVersion designVersion,
                                            ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var records = new List<TraitRecord>((int)count);
        for (uint i = 0; i < count; i++)
        {
            records.Add(Read(ar, designVersion, role));
        }

        return records;
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
                $"trait at design version {designVersion} carries a special-abilities block; "
                + "the name/type overload is not ported and no fixture exercises it.");
        }
    }
}
