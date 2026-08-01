using UAF.Common;

namespace UAF.Serialization;

/// <summary>A <c>RACE_DATA</c> record (<c>races.dat</c>).</summary>
/// <param name="Skills">
/// What the level cap comes from: a race may define <c>MaxLevel$SYS$</c>, which caps a character's
/// level independently of its baseclass.
/// </param>
public sealed record RaceRecord(int PreSpellNameKey, string Name,
                                DicePlus Weight, DicePlus Height, DicePlus Age, DicePlus MaxAge,
                                IReadOnlyList<AbilityRequirement> AbilityRequirements,
                                DicePlus BaseMovement,
                                int CanChangeClass, int DwarfResistance, int GnomeResistance,
                                int FindSecretDoor, int FindSecretDoorSearching,
                                IReadOnlyList<AslEntry> Attributes,
                                IReadOnlyList<Skill> Skills,
                                IReadOnlyList<BaseclassSkillAdjustment> AbilityAdjustments,
                                IReadOnlyList<BaseclassSkillAdjustment> BaseclassAdjustments,
                                IReadOnlyList<BaseclassSkillAdjustment> RaceAdjustments,
                                IReadOnlyList<BaseclassSkillAdjustment> ScriptAdjustments,
                                SpecabBlock SpecialAbilities);

/// <summary>
/// Reads <c>races.dat</c>'s records (<c>RACE_DATA::Serialize</c>, <c>class.cpp:2965</c>, loading
/// branch).
/// </summary>
/// <remarks>
/// <para>
/// Structurally a sibling of <see cref="BaseclassRecordReader"/> — ability requirements, five
/// bare-<c>int</c>-counted skill lists and a <c>Specab</c> tail — with dice for the physical
/// ranges in place of experience thresholds.
/// </para>
/// <para>
/// <b>The version passed to <c>Specab</c> is the design's, not a literal.</b> <c>BASE_CLASS_DATA</c>
/// hard-codes 0.930; this uses <c>globalData.version</c> (<c>class.cpp:3177</c>), the same as
/// <see cref="ClassRecordReader"/>. So <c>game.dat</c> must be read first.
/// </para>
/// <para>
/// <b><c>preSpellNameKey</c> is the one place a tagged database really does fork by build.</b> The
/// editor reads it below <c>RaceV2</c>; both builds read it above <c>RaceV2</c>; neither reads it
/// at exactly <c>RaceV2</c>. So for a <c>RaceV1</c> file — which <c>DefaultDesign</c> ships — the
/// editor and the engine consume different numbers of bytes from the same record. The audit in
/// docs/PORTING-PLAN.md concluded the two builds never disagree, and it is right about
/// <c>DesignVersion</c>-gated files; a tagged database sits on its own version axis where that
/// argument does not reach. Pass the <see cref="ArchiveRole"/> that matches the reader you are
/// standing in.
/// </para>
/// </remarks>
public static class RaceRecordReader
{
    /// <summary>The tag at and below which the build flavour changes what is read.</summary>
    public const string PreSpellNameKeyPivot = "RaceV2";

    /// <summary>The lowest container tag this reads.</summary>
    public const string SupportedTag = "RaceV2";

    /// <summary>Reads one record.</summary>
    /// <param name="tag">The container's tag, which gates several fields.</param>
    /// <param name="version">The design version from <c>game.dat</c> — see the remarks.</param>
    public static RaceRecord Read(IArchiveCursor ar, string tag, DesignVersion version,
                                  ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(tag);

        int compare = string.CompareOrdinal(tag, PreSpellNameKeyPivot);

        if (compare < 0)
        {
            // Only DefaultDesign ships RaceV1, and it is the shape where the editor and the engine
            // read different streams (below). Transcribing both halves against a single fixture
            // that cannot distinguish them is how a drifted reader gets shipped, so it is refused
            // outright -- the same call made for baseclass Bcd1 and class CL1.
            throw new InvalidDataException(
                $"race container '{tag}' is below {PreSpellNameKeyPivot}; only {SupportedTag} and "
                + "above are ported.");
        }

        int preSpellNameKey = 0;
        if (compare > 0 || (compare < 0 && role == ArchiveRole.Editor))
        {
            preSpellNameKey = ar.ReadInt32();
        }

        string name = ArchiveStringConventions.Decode(ar.ReadString());

        var weight = DicePlusReader.Read(ar);
        var height = DicePlusReader.Read(ar);
        var age = DicePlusReader.Read(ar);
        var maxAge = DicePlusReader.Read(ar);

        uint requirementCount = ar.ReadCount();
        var requirements = new List<AbilityRequirement>((int)requirementCount);
        for (uint i = 0; i < requirementCount; i++)
        {
            requirements.Add(BaseclassRecordReader.ReadAbilityRequirement(ar, role));
        }

        var movement = DicePlusReader.Read(ar);

        // Below RaceV2 the editor DERIVES these five from the race's name and reads nothing
        // (class.cpp:3100) -- Human can change class, Dwarf and Gnome have their resistances, Elf
        // finds secret doors. Reading them anyway consumes twenty bytes that were never written.
        int canChangeClass = 0, dwarfResistance = 0, gnomeResistance = 0;
        int findSecretDoor = 0, findSecretDoorSearching = 0;

        if (!(compare < 0 && role == ArchiveRole.Editor))
        {
            // BOOL is a four-byte int and is not always boolean, so these stay ints here.
            canChangeClass = ar.ReadInt32();
            dwarfResistance = ar.ReadInt32();
            gnomeResistance = ar.ReadInt32();
            findSecretDoor = ar.ReadInt32();
            findSecretDoorSearching = ar.ReadInt32();
        }
        else
        {
            canChangeClass = Matches(name, "Human");
            dwarfResistance = Matches(name, "Dwarf");
            gnomeResistance = Matches(name, "Gnome");
            findSecretDoor = Matches(name, "Elf") != 0 ? 5 : 2;
            findSecretDoorSearching = Matches(name, "Elf") != 0 ? 2 : 1;
        }

        var attributes = AslReader.ReadDeSerialized(ar, version, "RACE_DATA_ATTRIBUTES");

        // The five skill lists arrived with VersionSpellNames and are absent below it -- and the
        // gate is the DESIGN's version, not the container tag, so an old design with a new
        // races.dat still skips them.
        List<Skill> skills = [];
        List<BaseclassSkillAdjustment> abilityAdj = [], baseclassAdj = [], raceAdj = [],
                                       scriptAdj = [];

        if (version >= DesignVersion.SpellNames)
        {
            skills = BaseclassRecordReader.ReadSkillList(ar);
            abilityAdj = BaseclassRecordReader.ReadAbilityAdjustments(ar);
            baseclassAdj = BaseclassRecordReader.ReadBaseclassAdjustments(ar);
            raceAdj = BaseclassRecordReader.ReadRaceAdjustments(ar);
            scriptAdj = BaseclassRecordReader.ReadScriptAdjustments(ar);
        }

        var specabs = SpecabReader.Read(ar, version);

        return new RaceRecord(preSpellNameKey, name, weight, height, age, maxAge, requirements,
                              movement, canChangeClass, dwarfResistance, gnomeResistance,
                              findSecretDoor, findSecretDoorSearching, attributes, skills,
                              abilityAdj, baseclassAdj, raceAdj, scriptAdj, specabs);
    }

    /// <summary>The name comparison the derived flags use — case-insensitive, as <c>CompareNoCase</c>.</summary>
    private static int Matches(string name, string other) =>
        string.Equals(name, other, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    /// <summary>Reads every record of an already-opened <c>races.dat</c> body.</summary>
    public static List<RaceRecord> ReadAll(IArchiveCursor ar, uint count, string tag,
                                           DesignVersion version,
                                           ArchiveRole role = ArchiveRole.Editor)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var records = new List<RaceRecord>((int)count);
        for (uint i = 0; i < count; i++)
        {
            records.Add(Read(ar, tag, version, role));
        }
        return records;
    }
}
