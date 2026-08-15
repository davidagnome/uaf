using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>baseclass.dat</c> (<c>BASE_CLASS_DATA::Serialize</c>, <c>class.cpp:5594</c>, storing
/// branch, inside <c>BASE_CLASS_DATA_TYPE::Serialize</c>, <c>class.cpp:7267</c>).
/// </summary>
/// <remarks>
/// <para>
/// The longest record in the design folder, and the one where the storing branch is dramatically
/// shorter than the loading one: everything between <c>class.cpp:5721</c> and <c>:6176</c> — the
/// hard-coded AD&amp;D tables the editor synthesises for a <c>Bcd1</c> file, all four hundred lines
/// of it — exists only on the way in. What goes out is the flat <c>Bcd5</c> shape below.
/// </para>
/// <para>
/// <b>The version handed to <c>Specab</c> differs between the two halves, and it does not
/// matter.</b> Loading fakes 0.930 (<c>class.cpp:6136</c>) because designs are packaged with a
/// newer <c>baseclass.dat</c> than their <c>game.dat</c>; storing passes the real
/// <c>globalData.version</c> (<c>class.cpp:5650</c>). The block's own legacy branch is gated on
/// <c>!IsStoring()</c>, so both land on the modern <c>A_CStringPAIR_L</c> whatever the number is —
/// which is why <see cref="SpecabWriter"/> takes no version at all.
/// </para>
/// <para>
/// <b>Three count framings appear in one record and none of them is interchangeable.</b> The
/// ability-requirement and allowed-race lists use <c>WriteCount</c>; the experience levels, bonus
/// spells, casting entries and all six tail lists use a bare <c>int</c>. Under compression those
/// happen to be the same four bytes, which is exactly why getting it wrong here would survive every
/// test this port can run and break the moment a plain-archive caller appeared.
/// </para>
/// <para>
/// <b>The hit-dice table has no count and the fields are written out of declaration order.</b>
/// Forty entries, always, and each is <c>sides</c>, <c>nbr</c>, <c>bonus</c> (<c>class.cpp:5653</c>)
/// where <c>DICEDATA</c> declares <c>nbr</c> first. Transcribing the struct swaps two plausible
/// small integers and nothing downstream notices.
/// </para>
/// </remarks>
public static class BaseclassRecordWriter
{
    /// <summary>
    /// The earliest design version whose reader reads exactly the shape written here.
    /// </summary>
    /// <remarks>
    /// <b>This record is the one tagged database that does not consult the design version at
    /// all.</b> Its shape is selected entirely by the <c>Bcd5</c> tag inside each record, and the
    /// embedded special-abilities block is read at a hard-coded 0.930 rather than at the design's
    /// version. The value is stated anyway, and matches its siblings, so that a caller writing a
    /// whole design has one bound to compare against instead of a special case to remember.
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.SpellNames;

    /// <summary>
    /// Whether a record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of these are shape checks rather than legacy-format refusals, because this record is
    /// full of <b>fixed-length tables that carry no length on the wire</b>. A short THAC0 or a
    /// 39-entry hit-dice list produces a file that reads back as though the next record started
    /// early, so they are caught here rather than written.
    /// </para>
    /// <para>
    /// <b>A record below <c>Bcd5</c> cannot be reached through this port's reader</b>, which
    /// refuses them outright — the check is here for a record built by hand or carried across from
    /// somewhere else.
    /// </para>
    /// </remarks>
    public static bool CanWrite(BaseclassRecord baseclass, out string reason)
    {
        ArgumentNullException.ThrowIfNull(baseclass);

        if (baseclass.Tag != BaseclassRecordReader.SupportedTag)
        {
            reason = $"Baseclass '{baseclass.Name}' is tagged '{baseclass.Tag}'; only " +
                     $"{BaseclassRecordReader.SupportedTag} has a storing branch. The older " +
                     "shapes are reconstructed from hard-coded AD&D tables as they load " +
                     "(class.cpp:5860 onward) and there is no path that writes them back.";
            return false;
        }

        if (baseclass.Thac0.Length != BaseclassRecordReader.Thac0Size)
        {
            reason = $"Baseclass '{baseclass.Name}' has {baseclass.Thac0.Length} THAC0 entries, " +
                     $"not {BaseclassRecordReader.Thac0Size}. The table is a fixed array in the " +
                     "reference and its length is never written, so a short one is read as the " +
                     "start of the next field.";
            return false;
        }

        if (baseclass.HitDice.Count != BaseclassRecordReader.Thac0Size)
        {
            reason = $"Baseclass '{baseclass.Name}' has {baseclass.HitDice.Count} hit-dice " +
                     $"entries, not {BaseclassRecordReader.Thac0Size}. Like THAC0 this table is " +
                     "written without a count.";
            return false;
        }

        foreach (var casting in baseclass.Casting)
        {
            if (!CanWriteCastingInfo(casting, baseclass.Name, out reason))
            {
                return false;
            }
        }

        if (!CanWriteAdjustments(baseclass.AbilityAdjustments,
                                 BaseclassRecordReader.AbilityAdjustmentTableSize,
                                 "ability", baseclass.Name, out reason)
            || !CanWriteAdjustments(baseclass.BaseclassAdjustments,
                                    BaseclassRecordReader.BaseclassAdjustmentTableSize,
                                    "baseclass", baseclass.Name, out reason)
            || !CanWriteAdjustments(baseclass.RaceAdjustments,
                                    BaseclassRecordReader.RaceAdjustmentTableSize,
                                    "race", baseclass.Name, out reason))
        {
            return false;
        }

        foreach (var bonus in baseclass.BonusExperience)
        {
            if (bonus.Bonus.Length != BaseclassRecordReader.BonusExperienceTableSize)
            {
                reason = $"Baseclass '{baseclass.Name}' has a bonus-experience table of " +
                         $"{bonus.Bonus.Length} bytes for '{bonus.AbilityId}', not " +
                         $"{BaseclassRecordReader.BonusExperienceTableSize}.";
                return false;
            }
        }

        if (!SpecabWriter.CanWrite(baseclass.SpecialAbilities))
        {
            reason = $"Baseclass '{baseclass.Name}' has special abilities still in the pre-0.921 " +
                     "shape; see SpecabWriter.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one <c>BASE_CLASS_DATA</c>.</summary>
    /// <exception cref="NotSupportedException">
    /// When the record holds a shape that cannot go out — see <see cref="CanWrite"/>.
    /// </exception>
    /// <remarks>
    /// <b>Only <c>m_name</c> goes through the blank sentinel.</b> The allowed races, the
    /// spell-bonus ability and every id inside the tail lists are written verbatim, because they
    /// are <c>CString</c>-derived <c>*_ID</c> types the reference writes with a plain
    /// <c>car &lt;&lt;</c>. The port's reader applies the sentinel to some of them anyway — see the
    /// remarks on <see cref="WriteAbilityRequirement"/>.
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, BaseclassRecord baseclass)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(baseclass);

        if (!CanWrite(baseclass, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteString(BaseclassRecordReader.SupportedTag);
        ar.WriteInt32(baseclass.PreSpellNameKey);
        ar.WriteString(ArchiveStringConventions.Encode(baseclass.Name));

        // WriteCount for these two, a bare int for everything else in the record.
        ar.WriteCount((uint)baseclass.AbilityRequirements.Count);
        foreach (var requirement in baseclass.AbilityRequirements)
        {
            WriteAbilityRequirement(ar, requirement);
        }

        ar.WriteCount((uint)baseclass.AllowedRaces.Count);
        foreach (string race in baseclass.AllowedRaces)
        {
            ar.WriteString(race);                     // verbatim: a RACE_ID
        }

        WriteExperienceLevels(ar, baseclass.ExperienceLevels);

        ar.WriteUInt16(baseclass.AllowedAlignments);  // WORD -- class.h:1830
        ar.WriteBytes(baseclass.Thac0);               // 40 bytes, no count
        ar.WriteString(baseclass.SpellBonusAbility);  // verbatim: an ABILITY_ID

        // BYTE triples of prime/level/quantity, counted with a bare int.
        ar.WriteInt32(baseclass.BonusSpells.Length);
        ar.WriteBytes(baseclass.BonusSpells);

        ar.WriteInt32(baseclass.Casting.Count);
        foreach (var casting in baseclass.Casting)
        {
            WriteCastingInfo(ar, casting);
        }

        SpecabWriter.Write(ar, baseclass.SpecialAbilities);

        // Fixed 40 entries, no count, and sides before nbr -- see the class remarks.
        foreach (var dice in baseclass.HitDice)
        {
            ar.WriteInt32(dice.Sides);
            ar.WriteInt32(dice.Nbr);
            ar.WriteInt32(dice.Bonus);
        }

        WriteSkillList(ar, baseclass.Skills);
        WriteAbilityAdjustments(ar, baseclass.AbilityAdjustments);
        WriteBaseclassAdjustments(ar, baseclass.BaseclassAdjustments);
        WriteRaceAdjustments(ar, baseclass.RaceAdjustments);
        WriteScriptAdjustments(ar, baseclass.ScriptAdjustments);

        ar.WriteInt32(baseclass.BonusExperience.Count);
        foreach (var bonus in baseclass.BonusExperience)
        {
            ar.WriteString(bonus.AbilityId);          // verbatim
            ar.WriteByte((byte)bonus.BonusType);      // char -- one byte
            ar.WriteBytes(bonus.Bonus);
        }
    }

    /// <summary>Writes every record of a <c>baseclass.dat</c> body, without the count.</summary>
    public static void WriteAll(IArchiveWriteCursor ar, IReadOnlyList<BaseclassRecord> baseclasses)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(baseclasses);

        foreach (var baseclass in baseclasses)
        {
            if (!CanWrite(baseclass, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        foreach (var baseclass in baseclasses)
        {
            Write(ar, baseclass);
        }
    }

    /// <summary>Writes a whole <c>baseclass.dat</c>: tag, compression byte, count, records.</summary>
    public static void WriteFile(Stream stream, IReadOnlyList<BaseclassRecord> baseclasses)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(baseclasses);

        TaggedDatabaseWriter.WriteFile(stream, TaggedDatabase.Baseclass, (uint)baseclasses.Count,
                                       ar => WriteAll(ar, baseclasses));
    }

    // ---- leaves, shared with races.dat -------------------------------------------------------

    /// <summary>Writes an <c>ABILITY_REQ</c> (<c>class.cpp:2778</c>, storing branch).</summary>
    /// <remarks>
    /// <para>
    /// <b>Its own version string comes first and is always <c>ABL1</c>.</b> <c>ABL0</c> is a
    /// read-only shape — the editor consumes a <c>DWORD</c> key after the id and there is no branch
    /// that emits one — so a requirement loaded from <c>DefaultDesign</c>'s <c>ABL0</c> records goes
    /// back out as <c>ABL1</c>, exactly as the reference would write it.
    /// </para>
    /// <para>
    /// <b>The four limits are <c>short</c>, not <c>int</c></b> (<c>class.h:986</c>). Eight bytes
    /// per requirement, and a baseclass has several.
    /// </para>
    /// <para>
    /// <b>The id is written verbatim while the port's reader decodes it as a blank sentinel.</b>
    /// The reference does neither on the way in nor on the way out — <c>car &lt;&lt; m_abilityID</c>
    /// and <c>car &gt;&gt; m_abilityID</c> with no <c>"*"</c> test either side. Matching the
    /// reference is the right side to be on: writing the sentinel instead would hand the engine a
    /// requirement against an ability literally named <c>"*"</c>. The consequence is that an id
    /// that really was stored as <c>"*"</c> is read as empty and written back as empty; no design
    /// in the corpus has one.
    /// </para>
    /// </remarks>
    public static void WriteAbilityRequirement(IArchiveWriteCursor ar,
                                               AbilityRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(requirement);

        ar.WriteString("ABL1");
        ar.WriteString(requirement.AbilityId);        // verbatim -- see the remarks
        ar.WriteUInt16((ushort)requirement.Min);
        ar.WriteUInt16((ushort)requirement.MinMod);
        ar.WriteUInt16((ushort)requirement.Max);
        ar.WriteUInt16((ushort)requirement.MaxMod);
    }

    /// <summary>
    /// Writes the experience thresholds
    /// (<c>CAR::operator&lt;&lt;(CArray&lt;DWORD,DWORD&gt;&amp;)</c>, <c>class.cpp:12030</c>).
    /// </summary>
    /// <remarks>
    /// <b>An <c>int</c> count and then one bulk write</b>, not <c>size</c> separate <c>DWORD</c>
    /// writes — and the count is a plain <c>int</c> rather than <c>WriteCount</c>, which is where
    /// this differs from the two lists above it in the record. The bytes are identical through the
    /// LZW layer; the count is not.
    /// </remarks>
    public static void WriteExperienceLevels(IArchiveWriteCursor ar, IReadOnlyList<uint> levels)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(levels);

        ar.WriteInt32(levels.Count);
        if (levels.Count == 0)
        {
            // The reference skips the payload entirely at size 0 rather than writing nothing of
            // nothing -- same bytes, but worth mirroring so the branch exists on both sides.
            return;
        }

        byte[] raw = new byte[levels.Count * sizeof(uint)];
        for (int i = 0; i < levels.Count; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                raw.AsSpan(i * sizeof(uint)), levels[i]);
        }
        ar.WriteBytes(raw);
    }

    /// <summary>Writes a <c>CASTING_INFO</c> (<c>class.cpp:12372</c>).</summary>
    /// <remarks>
    /// Two strings then three blitted tables, and the two by-prime tables go out <b>level first,
    /// count second</b>. Both are 25 bytes of plausible small numbers, so swapping them gives every
    /// caster the wrong spell limits and produces a file that reads back without complaint.
    /// </remarks>
    public static void WriteCastingInfo(IArchiveWriteCursor ar, CastingInfo casting)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(casting);

        // Verbatim in the reference; the port's reader decodes them -- see WriteAbilityRequirement.
        ar.WriteString(casting.SchoolId);
        ar.WriteString(casting.PrimeAbility);

        ar.WriteBytes(casting.SpellsPerLevel);
        ar.WriteBytes(casting.MaxSpellLevelByPrime);
        ar.WriteBytes(casting.MaxSpellsByPrime);
    }

    /// <summary>The <c>m_skills</c> list: a bare <c>int</c> count, then id/value pairs.</summary>
    public static void WriteSkillList(IArchiveWriteCursor ar, IReadOnlyList<Skill> skills)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(skills);

        ar.WriteInt32(skills.Count);
        foreach (var skill in skills)
        {
            ar.WriteString(skill.SkillId);            // verbatim: a SKILL_ID
            ar.WriteInt32(skill.Value);
        }
    }

    /// <summary>
    /// The ability family (<c>class.cpp:5336</c>) — a 25-entry <c>short</c> table, blitted.
    /// </summary>
    public static void WriteAbilityAdjustments(
        IArchiveWriteCursor ar, IReadOnlyList<BaseclassSkillAdjustment> adjustments) =>
        WriteAdjustments(ar, adjustments, BaseclassRecordReader.AbilityAdjustmentTableSize);

    /// <summary>
    /// The baseclass family (<c>class.cpp:5371</c>) — a 40-entry <c>short</c> table.
    /// </summary>
    public static void WriteBaseclassAdjustments(
        IArchiveWriteCursor ar, IReadOnlyList<BaseclassSkillAdjustment> adjustments) =>
        WriteAdjustments(ar, adjustments, BaseclassRecordReader.BaseclassAdjustmentTableSize);

    /// <summary>
    /// The race family (<c>class.cpp:5388</c>) — a <b>single</b> <c>short</c>, not a table.
    /// </summary>
    /// <remarks>
    /// Two bytes where its siblings write 50 and 80. The field is even called <c>skillAdj</c> like
    /// theirs (<c>class.h:1120</c>); only its declaration says it is scalar.
    /// </remarks>
    public static void WriteRaceAdjustments(
        IArchiveWriteCursor ar, IReadOnlyList<BaseclassSkillAdjustment> adjustments) =>
        WriteAdjustments(ar, adjustments, BaseclassRecordReader.RaceAdjustmentTableSize);

    /// <summary>
    /// The script family (<c>class.cpp:5405</c>) — three strings, and <b>no</b> adjustment type or
    /// table.
    /// </summary>
    /// <remarks>
    /// The odd one out of the four. <see cref="BaseclassSkillAdjustment.SourceId"/>,
    /// <see cref="BaseclassSkillAdjustment.AdjustmentType"/> and
    /// <see cref="BaseclassSkillAdjustment.AdjustmentTable"/> have no place on the wire here — the
    /// reader leaves them empty and this drops them, which is the same thing from the other side.
    /// </remarks>
    public static void WriteScriptAdjustments(
        IArchiveWriteCursor ar, IReadOnlyList<BaseclassSkillAdjustment> adjustments)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(adjustments);

        ar.WriteInt32(adjustments.Count);
        foreach (var adjustment in adjustments)
        {
            ar.WriteString(adjustment.SkillId);
            ar.WriteString(adjustment.SpecialAbilityName);
            ar.WriteString(adjustment.ScriptName);
        }
    }

    private static void WriteAdjustments(IArchiveWriteCursor ar,
                                         IReadOnlyList<BaseclassSkillAdjustment> adjustments,
                                         int tableBytes)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(adjustments);

        ar.WriteInt32(adjustments.Count);
        foreach (var adjustment in adjustments)
        {
            if (adjustment.AdjustmentTable.Length != tableBytes)
            {
                throw new ArgumentException(
                    $"a skill adjustment for '{adjustment.SkillId}' carries " +
                    $"{adjustment.AdjustmentTable.Length} table bytes where this family writes " +
                    $"{tableBytes}. The four families share a record shape and not a table width; " +
                    "the length is never written, so a mismatch shifts everything after it.",
                    nameof(adjustments));
            }

            ar.WriteString(adjustment.SkillId);       // verbatim: a SKILL_ID
            ar.WriteString(adjustment.SourceId);      // verbatim: an ABILITY_ID/BASECLASS_ID/RACE_ID
            ar.WriteByte((byte)adjustment.AdjustmentType);
            ar.WriteBytes(adjustment.AdjustmentTable);
        }
    }

    private static bool CanWriteAdjustments(IReadOnlyList<BaseclassSkillAdjustment> adjustments,
                                            int tableBytes, string family, string owner,
                                            out string reason)
    {
        foreach (var adjustment in adjustments)
        {
            if (adjustment.AdjustmentTable.Length != tableBytes)
            {
                reason = $"'{owner}' has a {family} skill adjustment for " +
                         $"'{adjustment.SkillId}' with {adjustment.AdjustmentTable.Length} table " +
                         $"bytes, not {tableBytes}.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool CanWriteCastingInfo(CastingInfo casting, string owner, out string reason)
    {
        int spellLimits = BaseclassRecordReader.Thac0Size * BaseclassRecordReader.MaxSpellLevel;

        if (casting.SpellsPerLevel.Length != spellLimits)
        {
            reason = $"'{owner}' has a casting entry for '{casting.SchoolId}' whose spell-limit " +
                     $"table is {casting.SpellsPerLevel.Length} bytes, not {spellLimits}.";
            return false;
        }

        if (casting.MaxSpellLevelByPrime.Length != BaseclassRecordReader.HighestCharacterPrime
            || casting.MaxSpellsByPrime.Length != BaseclassRecordReader.HighestCharacterPrime)
        {
            reason = $"'{owner}' has a casting entry for '{casting.SchoolId}' whose by-prime " +
                     $"tables are {casting.MaxSpellLevelByPrime.Length} and " +
                     $"{casting.MaxSpellsByPrime.Length} bytes; both are " +
                     $"{BaseclassRecordReader.HighestCharacterPrime}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
