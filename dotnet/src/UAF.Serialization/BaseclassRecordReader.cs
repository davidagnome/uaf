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

/// <summary>One hit-dice entry (<c>DICEDATA</c>, <c>Externs.h:1834</c>).</summary>
/// <remarks>
/// <b>The wire order is <c>sides</c>, <c>nbr</c>, <c>bonus</c></b> (<c>class.cpp:6180</c>) — not the
/// struct's declaration order, which puts <c>nbr</c> first. Transcribing the struct rather than the
/// loading branch swaps two of the three fields and nothing detects it: both are plausible small
/// integers.
/// </remarks>
public sealed record HitDice(int Sides, int Nbr, int Bonus);

/// <summary>A skill and its value (<c>SKILL</c>, <c>class.cpp:4879</c>).</summary>
public sealed record Skill(string SkillId, int Value);

/// <summary>
/// One of a baseclass's skill adjustments. The four families (<c>class.cpp:5336</c>, <c>:5371</c>,
/// <c>:5388</c>, <c>:5405</c>) share this shape but <b>not</b> their wire formats.
/// </summary>
/// <remarks>
/// <b>Not to be confused with <see cref="SkillAdjustment"/></b>, which is a character's
/// <c>SKILL_ADJ</c> (<c>Char.h:267</c>) — a different structure with a different wire format. The
/// C++ names are <c>SKILL_ADJ</c> and <c>SKILL_ADJUSTMENT_*</c>, one letter of context apart.
/// </remarks>
/// <param name="SourceId">
/// The thing being adjusted against: an <c>ABILITY_ID</c>, <c>BASECLASS_ID</c> or <c>RACE_ID</c>.
/// Empty for the script family, which carries two names instead.
/// </param>
/// <param name="AdjustmentTable">
/// The per-level or per-prime table, as raw bytes. 50 for the ability family (25 × <c>short</c>),
/// 80 for the baseclass family (40 × <c>short</c>), 2 for the race family — which stores a
/// <b>single</b> <c>short</c> rather than a table — and empty for the script family.
/// </param>
public sealed record BaseclassSkillAdjustment(string SkillId, string SourceId,
                                             char AdjustmentType, byte[] AdjustmentTable,
                                             string SpecialAbilityName, string ScriptName);

/// <summary>Bonus experience by ability score (<c>BONUS_XP</c>, <c>class.cpp:5354</c>).</summary>
public sealed record BonusExperience(string AbilityId, char BonusType, byte[] Bonus);

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

/// <summary>A complete <c>BASE_CLASS_DATA</c> record.</summary>
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
                                     SpecabBlock SpecialAbilities,
                                     IReadOnlyList<HitDice> HitDice,
                                     IReadOnlyList<Skill> Skills,
                                     IReadOnlyList<BaseclassSkillAdjustment> AbilityAdjustments,
                                     IReadOnlyList<BaseclassSkillAdjustment> BaseclassAdjustments,
                                     IReadOnlyList<BaseclassSkillAdjustment> RaceAdjustments,
                                     IReadOnlyList<BaseclassSkillAdjustment> ScriptAdjustments,
                                     IReadOnlyList<BonusExperience> BonusExperience);


/// <summary>
/// Reads <c>baseclass.dat</c>'s records (<c>BASE_CLASS_DATA::Serialize</c>,
/// <c>class.cpp:5721</c>, loading branch).
/// </summary>
/// <remarks>
/// <para>
/// <b>Complete: a whole file walks to its exact last byte.</b> The record does not end at the
/// <c>Specab</c> block, as an earlier revision of the porting plan recorded. After it come a
/// 40-entry hit-dice table and six bare-<c>int</c>-counted lists (<c>class.cpp:6176</c> onward):
/// <c>m_skills</c>, the four <c>m_skillAdjustments*</c> families, and <c>m_bonusXP</c>.
/// </para>
/// <para>
/// <b>The four adjustment families look interchangeable and are not.</b> Ability and baseclass
/// blit a <c>short</c> table of 25 and 40 entries respectively; race stores a <b>single</b>
/// <c>short</c> read with <c>car &gt;&gt;</c>; script carries two extra strings and neither an
/// adjustment type nor a table. Transcribing one and reusing it for the others drifts by up to 80
/// bytes per entry.
/// </para>
/// <para>
/// <b>Verified without an oracle</b>, by the whole-file assertion this format makes cheap: five
/// designs — <c>SomethingWild</c>, <c>Case</c>, <c>Ambassador's_Letter</c>, <c>dc-default</c> and
/// the CI-saved 5.29 design — walk 57 records in total and every one lands on exact EOF, with names
/// a rulebook would recognise (<c>assassin</c>, <c>cleric</c>, <c>druid</c>, …) plus a design's own
/// inventions (<c>randamdi</c>, <c>larcener</c>). A tagged database carries no per-record length,
/// so a reader that is wrong anywhere cannot reach the last record, let alone stop exactly at the
/// end of it.
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

    /// <summary><c>short skillAdj[HIGHEST_CHARACTER_PRIME]</c> — 25 × 2.</summary>
    public const int AbilityAdjustmentTableSize = HighestCharacterPrime * sizeof(short);

    /// <summary><c>short skillAdj[HIGHEST_CHARACTER_LEVEL]</c> — 40 × 2.</summary>
    public const int BaseclassAdjustmentTableSize = Thac0Size * sizeof(short);

    /// <summary>A single <c>short</c> — the race family stores no table at all.</summary>
    public const int RaceAdjustmentTableSize = sizeof(short);

    /// <summary><c>int bonus[HIGHEST_CHARACTER_PRIME]</c> — 25 × 4.</summary>
    public const int BonusExperienceTableSize = HighestCharacterPrime * sizeof(int);

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

        // See the remarks on this class for why the version is a literal rather than read.
        var specabs = SpecabReader.Read(ar, SpecabVersion);

        // The hit-dice table: fixed length, no count, and read sides-before-nbr.
        var dice = new List<HitDice>(Thac0Size);
        for (int i = 0; i < Thac0Size; i++)
        {
            int sides = ar.ReadInt32();
            int nbr = ar.ReadInt32();
            dice.Add(new HitDice(sides, nbr, ar.ReadInt32()));
        }

        // Six lists, every one counted with a bare int rather than ReadCount.
        var skills = ReadList(ar, ReadSkill);
        var abilityAdj = ReadList(ar, c => ReadSkillAdjustment(c, AbilityAdjustmentTableSize));
        var baseclassAdj = ReadList(ar, c => ReadSkillAdjustment(c, BaseclassAdjustmentTableSize));
        var raceAdj = ReadList(ar, c => ReadSkillAdjustment(c, RaceAdjustmentTableSize));
        var scriptAdj = ReadList(ar, ReadScriptAdjustment);
        var bonusXp = ReadList(ar, ReadBonusExperience);

        return new BaseclassRecord(tag, preSpellNameKey, name, requirements, races, levels,
                                   alignments, thac0, spellBonusAbility, bonusSpells, casting,
                                   specabs, dice, skills, abilityAdj, baseclassAdj, raceAdj,
                                   scriptAdj, bonusXp);
    }

    /// <summary>Reads every record of an already-opened <c>baseclass.dat</c> body.</summary>
    public static List<BaseclassRecord> ReadAll(IArchiveCursor ar, uint count)
    {
        ArgumentNullException.ThrowIfNull(ar);

        var records = new List<BaseclassRecord>((int)count);
        for (uint i = 0; i < count; i++)
        {
            records.Add(Read(ar));
        }
        return records;
    }

    /// <summary>A bare-<c>int</c>-counted list, the framing all six tail lists use.</summary>
    private static List<T> ReadList<T>(IArchiveCursor ar, Func<IArchiveCursor, T> readOne)
    {
        int count = ar.ReadInt32();
        var list = new List<T>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            list.Add(readOne(ar));
        }
        return list;
    }

    private static Skill ReadSkill(IArchiveCursor ar) =>
        new(ar.ReadString(), ar.ReadInt32());

    /// <summary>
    /// The ability, baseclass and race families (<c>class.cpp:5336</c>, <c>:5371</c>, <c>:5388</c>).
    /// </summary>
    /// <remarks>
    /// Identical field order, three different table widths — and the race family's
    /// <c>skillAdj</c> is a lone <c>short</c> read with <c>car &gt;&gt;</c>, not a blitted array, so
    /// it is 2 bytes where the others are 50 and 80.
    /// </remarks>
    private static BaseclassSkillAdjustment ReadSkillAdjustment(IArchiveCursor ar, int tableBytes)
    {
        string skillId = ar.ReadString();
        string sourceId = ar.ReadString();
        char adjType = (char)ar.ReadByte();
        return new BaseclassSkillAdjustment(skillId, sourceId, adjType, ar.ReadBytes(tableBytes), "", "");
    }

    /// <summary>
    /// The script family (<c>class.cpp:5405</c>) — three strings, and <b>no</b> adjustment type or
    /// table, unlike its three siblings.
    /// </summary>
    private static BaseclassSkillAdjustment ReadScriptAdjustment(IArchiveCursor ar) =>
        new(ar.ReadString(), "", '\0', [], ar.ReadString(), ar.ReadString());

    private static BonusExperience ReadBonusExperience(IArchiveCursor ar)
    {
        string abilityId = ar.ReadString();
        char bonusType = (char)ar.ReadByte();
        return new BonusExperience(abilityId, bonusType, ar.ReadBytes(BonusExperienceTableSize));
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
