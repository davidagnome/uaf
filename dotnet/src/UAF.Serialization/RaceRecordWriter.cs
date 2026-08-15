using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>races.dat</c> (<c>RACE_DATA::Serialize</c>, <c>class.cpp:2965</c>, storing branch,
/// inside <c>RACE_DATA_TYPE::Serialize</c>, <c>class.cpp:3464</c>).
/// </summary>
/// <remarks>
/// <para>
/// Structurally a sibling of <see cref="BaseclassRecordWriter"/> — ability requirements, five
/// bare-<c>int</c>-counted skill lists and a <c>Specab</c> tail — with dice for the physical ranges
/// where the baseclass has experience thresholds, and an ASL block in the middle that the baseclass
/// has nowhere.
/// </para>
/// <para>
/// <b>The storing branch has no version fork at all and the loading branch has three.</b> Reading
/// gates <c>preSpellNameKey</c> on the container tag, derives five flags from the race's <i>name</i>
/// below <c>RaceV2</c>, and skips the five skill lists below <c>VersionSpellNames</c>. Writing does
/// none of that: the record goes out whole, which is why <see cref="TaggedDatabaseWriter"/> must
/// stamp the container <c>RaceV3</c> — the tag is what tells the reader that
/// <c>preSpellNameKey</c> is present, and at exactly <c>RaceV2</c> it is not read.
/// </para>
/// <para>
/// <b>The ASL block here is the third entry point, not the usual one.</b> Storing calls
/// <c>car.Serialize(m_race_asl, …)</c> and loading calls <c>car.DeSerialize</c>
/// (<c>class.cpp:12095</c> and <c>:12117</c>); both use an <c>int</c> count where
/// <c>A_ASLENTRY_L::Serialize</c> — what every other record reaches — uses a <c>WORD</c>. Writing
/// the 16-bit form desynchronises everything after the block by two bytes, and the block is
/// followed by five counted lists that would then read plausible garbage counts.
/// </para>
/// </remarks>
public static class RaceRecordWriter
{
    /// <summary>
    /// The earliest design version whose reader reads exactly the shape written here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The file carries no version; this is an assumption about <c>game.dat</c>.</b> Bound by
    /// <c>VersionSpellNames</c>, where the five skill and skill-adjustment lists arrive
    /// (<c>class.cpp:3121</c>). Below it the reader stops after the ASL and takes this record's
    /// skill list as the <b>next</b> record's <c>preSpellNameKey</c>.
    /// </para>
    /// <para>
    /// Note the gate is the <i>design's</i> version and not the container tag, so a modern
    /// <c>races.dat</c> dropped into an old design is misread by the reference as well.
    /// </para>
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.SpellNames;

    /// <summary>
    /// Whether a record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// The five dice expressions are the interesting ones: a race carries weight, height, age, max
    /// age and base movement, and any of them still in a numeric <c>DICEPLUS</c> form has no text
    /// to write — see <see cref="DicePlusWriter.CanWrite"/>.
    /// </remarks>
    public static bool CanWrite(RaceRecord race, out string reason)
    {
        ArgumentNullException.ThrowIfNull(race);

        (string what, DicePlus dice)[] rolls =
        [
            ("weight", race.Weight), ("height", race.Height), ("age", race.Age),
            ("maximum age", race.MaxAge), ("base movement", race.BaseMovement),
        ];

        foreach ((string what, var dice) in rolls)
        {
            if (!DicePlusWriter.CanWrite(dice, out string diceReason))
            {
                reason = $"Race '{race.Name}' has a legacy {what} expression: {diceReason}";
                return false;
            }
        }

        if (!CanWriteAdjustments(race.AbilityAdjustments,
                                 BaseclassRecordReader.AbilityAdjustmentTableSize,
                                 "ability", race.Name, out reason)
            || !CanWriteAdjustments(race.BaseclassAdjustments,
                                    BaseclassRecordReader.BaseclassAdjustmentTableSize,
                                    "baseclass", race.Name, out reason)
            || !CanWriteAdjustments(race.RaceAdjustments,
                                    BaseclassRecordReader.RaceAdjustmentTableSize,
                                    "race", race.Name, out reason))
        {
            return false;
        }

        if (!SpecabWriter.CanWrite(race.SpecialAbilities))
        {
            reason = $"Race '{race.Name}' has special abilities still in the pre-0.921 shape; " +
                     "see SpecabWriter.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one <c>RACE_DATA</c>.</summary>
    /// <exception cref="NotSupportedException">
    /// When the record holds a shape that cannot go out — see <see cref="CanWrite"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The record opens with <c>preSpellNameKey</c> and no tag of its own.</b> Unlike
    /// <c>ABILITY_DATA</c>, <c>BASE_CLASS_DATA</c> and <c>CLASS_DATA</c>, a race record carries no
    /// version string — the container's tag is the only version axis — so there is nothing here to
    /// announce a misaligned stream until the ASL map name several fields later.
    /// </para>
    /// <para>
    /// <b>The five flags are <c>BOOL</c>, which is a four-byte <c>int</c> and is not always
    /// 0 or 1.</b> <c>m_findSecretDoor</c> holds 5 or 2 (<c>class.cpp:3104</c>), so narrowing them
    /// to a boolean on the way through would lose the value <i>and</i> the width.
    /// </para>
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, RaceRecord race)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(race);

        if (!CanWrite(race, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteInt32(race.PreSpellNameKey);
        ar.WriteString(ArchiveStringConventions.Encode(race.Name));

        DicePlusWriter.Write(ar, race.Weight);
        DicePlusWriter.Write(ar, race.Height);
        DicePlusWriter.Write(ar, race.Age);
        DicePlusWriter.Write(ar, race.MaxAge);

        // WriteCount here; every list below uses a bare int.
        ar.WriteCount((uint)race.AbilityRequirements.Count);
        foreach (var requirement in race.AbilityRequirements)
        {
            BaseclassRecordWriter.WriteAbilityRequirement(ar, requirement);
        }

        DicePlusWriter.Write(ar, race.BaseMovement);

        ar.WriteInt32(race.CanChangeClass);
        ar.WriteInt32(race.DwarfResistance);
        ar.WriteInt32(race.GnomeResistance);
        ar.WriteInt32(race.FindSecretDoor);
        ar.WriteInt32(race.FindSecretDoorSearching);

        // The int-counted ASL, not the WORD-counted one -- see the class remarks.
        AslWriter.WriteDeSerialized(ar, WrittenVersion, AslMaps.RaceData, race.Attributes);

        BaseclassRecordWriter.WriteSkillList(ar, race.Skills);
        BaseclassRecordWriter.WriteAbilityAdjustments(ar, race.AbilityAdjustments);
        BaseclassRecordWriter.WriteBaseclassAdjustments(ar, race.BaseclassAdjustments);
        BaseclassRecordWriter.WriteRaceAdjustments(ar, race.RaceAdjustments);
        BaseclassRecordWriter.WriteScriptAdjustments(ar, race.ScriptAdjustments);

        SpecabWriter.Write(ar, race.SpecialAbilities);
    }

    /// <summary>Writes every record of a <c>races.dat</c> body, without the count.</summary>
    public static void WriteAll(IArchiveWriteCursor ar, IReadOnlyList<RaceRecord> races)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(races);

        foreach (var race in races)
        {
            if (!CanWrite(race, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        foreach (var race in races)
        {
            Write(ar, race);
        }
    }

    /// <summary>
    /// Writes a whole <c>races.dat</c>: tag, compression byte, count, records.
    /// </summary>
    /// <remarks>
    /// <b>The container goes out as <c>RaceV3</c> whatever it came in as</b>, which is both what the
    /// reference does and what this record shape requires — see the class remarks. A record read
    /// from a <c>RaceV2</c> container is the one case where that upgrade is not free: neither build
    /// reads <c>preSpellNameKey</c> at exactly <c>RaceV2</c>, the reference leaves its own field at
    /// the <c>-1</c> it initialises to (<c>class.cpp:3048</c>) and <see cref="RaceRecordReader"/>
    /// leaves it at 0, so the two disagree by one integer in a legacy editor key. No design in the
    /// corpus ships <c>RaceV2</c>; every one is <c>RaceV1</c> (refused by the reader) or
    /// <c>RaceV3</c>.
    /// </remarks>
    public static void WriteFile(Stream stream, IReadOnlyList<RaceRecord> races)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(races);

        TaggedDatabaseWriter.WriteFile(stream, TaggedDatabase.Race, (uint)races.Count,
                                       ar => WriteAll(ar, races));
    }

    private static bool CanWriteAdjustments(IReadOnlyList<BaseclassSkillAdjustment> adjustments,
                                            int tableBytes, string family, string owner,
                                            out string reason)
    {
        foreach (var adjustment in adjustments)
        {
            if (adjustment.AdjustmentTable.Length != tableBytes)
            {
                reason = $"Race '{owner}' has a {family} skill adjustment for " +
                         $"'{adjustment.SkillId}' with {adjustment.AdjustmentTable.Length} table " +
                         $"bytes, not {tableBytes}.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }
}
