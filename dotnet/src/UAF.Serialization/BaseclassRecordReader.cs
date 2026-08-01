using UAF.Common;

namespace UAF.Serialization;

/// <summary>One ability score a baseclass requires (<c>ABILITY_REQ</c>, <c>class.h:985</c>).</summary>
/// <param name="AbilityId">Which score, by name — a string, like every other <c>*_ID</c>.</param>
/// <remarks>
/// <b>The four limits are <c>short</c>, not <c>int</c>.</b> Reading them four bytes wide drifts the
/// stream by eight per requirement, and a baseclass has several.
/// </remarks>
public sealed record AbilityRequirement(string AbilityId, short Min, short MinMod,
                                        short Max, short MaxMod);

/// <summary>
/// One class's spell-casting progression (<c>CASTING_INFO</c>, <c>class.cpp:12372</c>).
/// </summary>
/// <param name="SpellsPerLevel">
/// <c>HIGHEST_CHARACTER_LEVEL</c> × <c>MAX_SPELL_LEVEL</c> = 40 × 9, blitted as one blob.
/// </param>
/// <param name="Bonus">25 bytes — <c>HIGHEST_CHARACTER_PRIME</c>.</param>
/// <param name="Penalty">25 bytes, likewise.</param>
public sealed record CastingInfo(string Name, string AbilityId, byte[] SpellsPerLevel,
                                 byte[] Bonus, byte[] Penalty);

/// <summary>
/// A <c>BASE_CLASS_DATA</c> record, as far as <see cref="BaseclassRecordReader"/> reads it —
/// through the special abilities. The hit-dice table and the six skill lists after them are not
/// read; see that class's remarks.
/// </summary>
/// <remarks>
/// <see cref="ExperienceLevels"/> is what levelling needs: the cumulative experience required for
/// each level in this baseclass.
/// </remarks>
public sealed record BaseclassRecord(string Tag, int PreSpellNameKey, string Name,
                                     IReadOnlyList<AbilityRequirement> AbilityRequirements,
                                     IReadOnlyList<string> AllowedRaces,
                                     IReadOnlyList<uint> ExperienceLevels,
                                     ushort AllowedAlignments, byte[] Thac0,
                                     string SpellBonusAbility, byte[] BonusSpells,
                                     IReadOnlyList<CastingInfo> Casting,
                                     SpecabBlock SpecialAbilities);


/// <summary>
/// Reads <c>baseclass.dat</c>'s records (<c>BASE_CLASS_DATA::Serialize</c>,
/// <c>class.cpp:5721</c>, loading branch).
/// </summary>
/// <remarks>
/// <para>
/// <b>Partial: reads through the <c>Specab</c> block, which is as far as the record is understood.</b>
/// Only the first record of a file decodes — the cursor is not left at the second. Everything
/// levelling needs (<see cref="ExperienceLevels"/>) is here.
/// </para>
/// <para>
/// <b>What still follows, read from <c>class.cpp:6176</c> onward.</b> The plan previously recorded
/// the <c>Specab</c> block as the end of the record; it is not. After it come a 40-entry hit-dice
/// table (<c>sides</c>, <c>nbr</c>, <c>bonus</c> per <c>HIGHEST_CHARACTER_LEVEL</c>) and then six
/// bare-<c>int</c>-counted lists — <c>m_skills</c> (<c>SKILL</c>), the four
/// <c>m_skillAdjustments*</c> families (<c>SKILL_ADJ</c>, which takes a version string) and
/// <c>m_bonusXP</c> (<c>BONUS_XP</c>). Four more leaf serializers, none of them yet transcribed.
/// </para>
/// <para>
/// That correction was found the way the others were: record 0 decoded perfectly against the byte
/// map — alignments <c>0x01c0</c>, THAC0 opening 20,20,20,20,19,19,19,19, an empty spell-bonus
/// ability, zero bonus spells, zero casting entries, and a single special ability
/// <c>baseclass_NameSuppress = "Y"</c> — and then record 1's tag read as <c>Dexterity</c>. One
/// string of drift, from a structure nobody had read the source for.
/// </para>
/// <para>
/// <b>The version passed to <c>Specab</c> is hard-coded to 0.930, not the record tag and not the
/// design version.</b> The source explains why: people package old designs with a new
/// <c>baseclass.dat</c>, so the real design version would send it down the legacy branch. 0.930 is
/// above the 0.920 legacy gate, so a <c>Bcd5</c> record always takes the modern
/// <c>A_CStringPAIR_L</c> path.
/// </para>
/// <para>
/// <b>Three different count framings appear in one record.</b> The ability and race lists use
/// <c>ReadCount()</c>; the experience levels use a bare <c>int</c>. They are not interchangeable,
/// and nothing in the field names distinguishes them.
/// </para>
/// <para>
/// <b>Only <c>Bcd5</c> is accepted.</b> All three <c>reference/</c> designs carry it. The engine
/// itself refuses anything below <c>Bcd2</c> outright — "you must install a new one",
/// <c>class.cpp:5734</c> — which includes <c>DefaultDesign</c>'s <c>Bcd1</c>, so there is no
/// fixture for the older shapes and no engine path that would reach them.
/// </para>
/// <para>
/// <b>Each record carries its own version tag</b>, distinct from the container's: a file tagged
/// <c>BaseclassV1</c> holds <c>Bcd1</c> or <c>Bcd5</c> records depending on the design. The two
/// axes are independent and neither predicts the other.
/// </para>
/// </remarks>
public static class BaseclassRecordReader
{
    /// <summary>The only record version this reads.</summary>
    public const string SupportedTag = "Bcd5";

    /// <summary>
    /// The version below which the engine refuses the whole file (<c>class.cpp:5731</c>).
    /// </summary>
    public const string EngineMinimumTag = "Bcd2";

    /// <summary><c>HIGHEST_CHARACTER_LEVEL</c> (<c>Externs.h:199</c>) — the THAC0 blob's length.</summary>
    public const int Thac0Size = 40;

    /// <summary><c>MAX_SPELL_LEVEL</c>.</summary>
    public const int MaxSpellLevel = 9;

    /// <summary><c>HIGHEST_CHARACTER_PRIME</c>.</summary>
    public const int HighestCharacterPrime = 25;

    /// <summary>
    /// The version <c>BASE_CLASS_DATA</c> hands <c>Specab</c> at <c>intVer &gt;= 5</c>
    /// (<c>class.cpp:6136</c>) — a literal, deliberately not the design's own version.
    /// </summary>
    public static DesignVersion SpecabVersion => new(0.930);

    /// <summary>Reads one record's leading fields, through the experience levels.</summary>
    /// <exception cref="InvalidDataException">The record is not <see cref="SupportedTag"/>.</exception>
    public static BaseclassRecord Read(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string tag = ar.ReadString();
        if (tag != SupportedTag)
        {
            throw new InvalidDataException(
                string.CompareOrdinal(tag, EngineMinimumTag) < 0
                    ? $"baseclass record '{tag}' is below {EngineMinimumTag}; the engine refuses "
                      + "this file outright and asks for a newer baseclass.dat."
                    : $"baseclass record '{tag}' is not {SupportedTag}; only that shape is ported.");
        }

        // Bcd5 alone reads this; Bcd4 and below take an editor-only path.
        int preSpellNameKey = ar.ReadInt32();

        string name = ArchiveStringConventions.Decode(ar.ReadString());

        // Counted with ReadCount, unlike the bonus-spell and casting lists further down, which use
        // a bare int. The two framings are not interchangeable and this record uses both.
        uint requirementCount = ar.ReadCount();
        var requirements = new List<AbilityRequirement>((int)requirementCount);
        for (uint i = 0; i < requirementCount; i++)
        {
            requirements.Add(ReadAbilityRequirement(ar));
        }

        uint raceCount = ar.ReadCount();
        var races = new List<string>((int)raceCount);
        for (uint i = 0; i < raceCount; i++)
        {
            races.Add(ArchiveStringConventions.Decode(ar.ReadString()));
        }

        var levels = ReadExperienceLevels(ar);

        // Gated `ver >= "Bcd1"` in the reference, which every Bcd5 record satisfies; the else
        // branch defaults it to 0x1ff. A WORD, not an int -- class.h:1830.
        ushort alignments = ar.ReadUInt16();

        byte[] thac0 = ar.ReadBytes(Thac0Size);
        string spellBonusAbility = ar.ReadString();

        // A bare int, like the experience levels and unlike the two lists above -- not ReadCount.
        int bonusSpellCount = ar.ReadInt32();
        byte[] bonusSpells = bonusSpellCount > 0 ? ar.ReadBytes(bonusSpellCount) : [];

        int castingCount = ar.ReadInt32();
        var casting = new List<CastingInfo>(Math.Max(castingCount, 0));
        for (int i = 0; i < castingCount; i++)
        {
            casting.Add(ReadCastingInfo(ar));
        }

        // The tail, and the piece whose absence desynchronised the first attempt. See the remarks
        // on this class for why the version is a literal rather than anything read from the file.
        var specabs = SpecabReader.Read(ar, SpecabVersion);

        return new BaseclassRecord(tag, preSpellNameKey, name, requirements, races, levels,
                                   alignments, thac0, spellBonusAbility, bonusSpells, casting,
                                   specabs);
    }

    /// <summary>Reads a <c>CASTING_INFO</c> (<c>class.cpp:12372</c>).</summary>
    /// <remarks>
    /// Three blitted tables rather than field-by-field reads. <c>CAR::Serialize(char*, n)</c> is a
    /// plain n-byte <c>decompress</c> (<c>class.cpp:12064</c>), so a bulk read is byte-identical to
    /// the reference's — the LZW layer emits a byte stream either way.
    /// </remarks>
    public static CastingInfo ReadCastingInfo(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string name = ArchiveStringConventions.Decode(ar.ReadString());
        string abilityId = ArchiveStringConventions.Decode(ar.ReadString());

        return new CastingInfo(name, abilityId,
                               ar.ReadBytes(Thac0Size * MaxSpellLevel),
                               ar.ReadBytes(HighestCharacterPrime),
                               ar.ReadBytes(HighestCharacterPrime));
    }

    /// <summary>Reads an <c>ABILITY_REQ</c> (<c>class.cpp:2778</c>, loading branch).</summary>
    /// <remarks>
    /// Its own version string comes first, and anything but <c>"ABL1"</c> is rejected — the
    /// reference logs "Unknown ABILITY_LIMITS version" and returns an error.
    /// </remarks>
    public static AbilityRequirement ReadAbilityRequirement(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string version = ar.ReadString();
        string abilityId = ArchiveStringConventions.Decode(ar.ReadString());

        if (version != "ABL1")
        {
            throw new InvalidDataException($"Unknown ABILITY_LIMITS version = {version}");
        }

        return new AbilityRequirement(abilityId, (short)ar.ReadUInt16(), (short)ar.ReadUInt16(),
                                      (short)ar.ReadUInt16(), (short)ar.ReadUInt16());
    }

    /// <summary>
    /// Reads the experience thresholds (<c>CAR::operator&gt;&gt;(CArray&lt;DWORD,DWORD&gt;&amp;)</c>,
    /// <c>class.cpp:12046</c>).
    /// </summary>
    /// <remarks>
    /// <b>An <c>int</c> count and then a single bulk read</b> — <c>decompress(&amp;warray[0],
    /// size * sizeof(DWORD))</c>, not <c>size</c> separate DWORD reads. The bytes are the same
    /// through the LZW layer, but the count is a plain <c>int</c> rather than
    /// <see cref="IArchiveCursor.ReadCount"/>, which is where this differs from the two lists
    /// above it.
    /// </remarks>
    public static List<uint> ReadExperienceLevels(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int size = ar.ReadInt32();
        var levels = new List<uint>(Math.Max(size, 0));
        if (size <= 0)
        {
            return levels;
        }

        var raw = ar.ReadBytes(size * sizeof(uint));
        for (int i = 0; i < size; i++)
        {
            levels.Add(BitConverter.ToUInt32(raw, i * sizeof(uint)));
        }

        return levels;
    }
}
