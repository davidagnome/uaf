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
                                     IReadOnlyList<uint> ExperienceLevels);


/// <summary>
/// Reads <c>baseclass.dat</c>'s records (<c>BASE_CLASS_DATA::Serialize</c>,
/// <c>class.cpp:5721</c>, loading branch).
/// </summary>
/// <remarks>
/// <para>
/// <b>Partial: the record is read up to and including the experience levels, and no further.</b>
/// <b>Only the first record of a file decodes</b> — the cursor is not positioned at the second.
/// That is enough for levelling, which needs the thresholds.
/// </para>
/// <para>
/// An attempt to complete the record — <c>m_allowedAlignments</c> (a <c>WORD</c>), the 40-byte
/// <c>THAC0</c> blob, the spell-bonus ability, bonus spells and <c>CASTING_INFO</c> — desynchronised
/// the stream, failing with a string-table index past the end of the table. The field widths were
/// all confirmed from the header and <c>CAR::Serialize(char*, n)</c> is a plain n-byte read, so
/// something between the experience levels and the end of the record is still unaccounted for.
/// Reverted rather than shipped: a reader that drifts produces plausible records, and this one has
/// no oracle to catch that.
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

        return new BaseclassRecord(tag, preSpellNameKey, name, requirements, races,
                                   ReadExperienceLevels(ar));
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
