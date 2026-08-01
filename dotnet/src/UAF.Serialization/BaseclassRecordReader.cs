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
/// As much of a <c>BASE_CLASS_DATA</c> record as this port reads.
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
                                     string SpellBonusAbility, IReadOnlyList<byte> BonusSpells,
                                     IReadOnlyList<CastingInfo> Casting);

/// <summary>How one baseclass casts from one school (<c>CASTING_INFO</c>, <c>class.h:1695</c>).</summary>
/// <remarks>
/// Two strings and three fixed tables, all blitted rather than serialized field by field:
/// <c>m_spellLimits</c> is 40 × 9 bytes (level × spell level), and the two prime tables are 25 each.
/// </remarks>
public sealed record CastingInfo(string SchoolId, string PrimeAbility, byte[] SpellLimits,
                                 byte[] MaxSpellLevelsByPrime, byte[] MaxSpellsByPrime);

/// <summary>
/// Reads <c>baseclass.dat</c>'s records (<c>BASE_CLASS_DATA::Serialize</c>,
/// <c>class.cpp:5721</c>, loading branch).
/// </summary>
/// <remarks>
/// <para>
/// <b>Complete for <c>Bcd5</c>, so a whole file walks.</b> The record ends with three fixed-size
/// blobs — <c>THAC0</c> at 40 bytes and <c>CASTING_INFO</c>'s 360 + 25 + 25 — which are
/// <c>car.Serialize(buf, n)</c> bulk reads rather than field sequences, and sizing any of them
/// from the wrong constant desynchronises every record after the first.
/// </para>
/// <para>
/// <b>Three different count framings appear in one record.</b> The ability and race lists use
/// <c>ReadCount()</c>; the experience levels, bonus spells and casting list each use a bare
/// <c>int</c>. They are not interchangeable, and nothing in the field names distinguishes them.
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

        var experienceLevels = ReadExperienceLevels(ar);

        // WORD, not int -- 0x1ff is the default and fits in 16 bits. Bcd5 is above the gate, so
        // this is always present here; below Bcd1 the reference defaults it instead of reading.
        ushort allowedAlignments = ar.ReadUInt16();

        // char THAC0[HIGHEST_CHARACTER_LEVEL], blitted whole.
        var thac0 = ar.ReadBytes(HighestCharacterLevel);

        string spellBonusAbility = ArchiveStringConventions.Decode(ar.ReadString());

        // A bare int, unlike the two lists above.
        int bonusCount = ar.ReadInt32();
        var bonusSpells = new List<byte>(Math.Max(bonusCount, 0));
        for (int i = 0; i < bonusCount; i++)
        {
            bonusSpells.Add(ar.ReadByte());
        }

        int castingCount = ar.ReadInt32();
        var casting = new List<CastingInfo>(Math.Max(castingCount, 0));
        for (int i = 0; i < castingCount; i++)
        {
            casting.Add(ReadCastingInfo(ar));
        }

        return new BaseclassRecord(tag, preSpellNameKey, name, requirements, races,
                                   experienceLevels, allowedAlignments, thac0,
                                   spellBonusAbility, bonusSpells, casting);
    }

    /// <summary><c>HIGHEST_CHARACTER_LEVEL</c> (<c>Externs.h:199</c>).</summary>
    public const int HighestCharacterLevel = 40;

    /// <summary><c>HIGHEST_CHARACTER_PRIME</c> (<c>Externs.h:203</c>).</summary>
    public const int HighestCharacterPrime = 25;

    /// <summary><c>MAX_SPELL_LEVEL</c> (<c>Externs.h:207</c>).</summary>
    public const int MaxSpellLevel = 9;

    /// <summary>Reads a <c>CASTING_INFO</c> (<c>class.cpp:12372</c>).</summary>
    /// <remarks>
    /// The three tables are blitted, so their sizes come from the array declarations rather than
    /// from anything on the wire: 40 × 9 for the spell limits and 25 for each prime table.
    /// </remarks>
    public static CastingInfo ReadCastingInfo(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string schoolId = ArchiveStringConventions.Decode(ar.ReadString());
        string primeAbility = ArchiveStringConventions.Decode(ar.ReadString());

        return new CastingInfo(schoolId, primeAbility,
                               ar.ReadBytes(HighestCharacterLevel * MaxSpellLevel),
                               ar.ReadBytes(HighestCharacterPrime),
                               ar.ReadBytes(HighestCharacterPrime));
    }

    /// <summary>Reads every record in an already-opened <c>baseclass.dat</c> body.</summary>
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
