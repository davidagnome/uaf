using UAF.Common;

namespace UAF.Serialization;

/// <summary>An <c>ABILITY_DATA</c> record (<c>ability.dat</c>).</summary>
/// <param name="Roll">
/// The dice a new character's score is rolled from. Everything else about an ability — how it
/// adjusts a to-hit number, what a score of 18 is worth — lives in the skill tables; this record
/// holds only its name and its dice.
/// </param>
public sealed record AbilityRecord(string Name, string Abbreviation, DicePlus Roll,
                                   SpecabBlock SpecialAbilities);

/// <summary>
/// Reads <c>ability.dat</c> (<c>ABILITY_DATA::Serialize</c>, <c>class.cpp:3996</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The seventh tagged database, and the last one unread.</b> Its file name was already in
/// <see cref="TaggedDatabaseReader.FileName"/> and nothing ever opened it — which was invisible
/// until the character generator needed the dice a strength score is rolled from.
/// </para>
/// <para>
/// One version tag, <c>Abd0</c>, and an unknown one is a hard stop rather than a guess: the
/// reference logs and returns a failure, and there is no second shape to fall back to.
/// </para>
/// </remarks>
public static class AbilityRecordReader
{
    /// <summary>The only record version (<c>class.cpp:3999</c>).</summary>
    public const string SupportedTag = "Abd0";

    /// <summary>Reads one record.</summary>
    /// <remarks>
    /// <b>The per-record tag is inside the record, not the container's.</b> Unlike the race and
    /// class databases, where the container's tag decides the shape, each ability writes its own
    /// <c>Abd0</c> first — so the tag is read here rather than passed in.
    /// </remarks>
    public static AbilityRecord Read(IArchiveCursor ar, DesignVersion version,
                                     ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string tag = ar.ReadString();
        if (tag != SupportedTag)
        {
            throw new InvalidDataException(
                $"unknown ABILITY_DATA version '{tag}'; only {SupportedTag} exists.");
        }

        // A pre-VersionSpellNames editor stream carries the old numeric key here. The engine's
        // does not -- the #ifdef is UAFEDITOR-only -- so this is one of the places where the two
        // read different bytes from the same file.
        if (role == ArchiveRole.Editor && version < DesignVersion.SpellNames)
        {
            ar.ReadUInt32();
        }

        string name = ArchiveStringConventions.Decode(ar.ReadString());
        string abbreviation = ArchiveStringConventions.Decode(ar.ReadString());
        var roll = DicePlusReader.Read(ar);

        var specabs = version >= DesignVersion.SpecialAbilities
            ? SpecabReader.Read(ar, version)
            : new SpecabBlock([], [], []);

        return new AbilityRecord(name, abbreviation, roll, specabs);
    }

    /// <summary>Reads a whole database's worth.</summary>
    public static List<AbilityRecord> ReadAll(IArchiveCursor ar, uint count, DesignVersion version,
                                              ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var records = new List<AbilityRecord>((int)count);
        for (uint i = 0; i < count; i++)
        {
            records.Add(Read(ar, version, role));
        }
        return records;
    }
}
