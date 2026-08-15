using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>classes.dat</c> (<c>CLASS_DATA::Serialize</c>, <c>class.cpp:7936</c>, storing branch,
/// inside <c>CLASS_DATA_TYPE::Serialize</c>, <c>class.cpp:8649</c>).
/// </summary>
/// <remarks>
/// <para>
/// The shortest of the three <c>class.cpp</c> record writers, and the only one of the five that
/// embeds an <c>ITEM_LIST</c> — so it is the only one whose refusals can come from the item layer.
/// </para>
/// <para>
/// <b>The starting equipment is written back exactly as it was read, including items the design no
/// longer defines.</b> The reference resolves every entry against <c>itemData</c> as it loads and
/// silently drops the unresolvable ones (<c>Items.cpp:1700</c>, "Undefined item named %s"), so a
/// reference load-and-save quietly prunes the list. <see cref="ClassRecordReader"/> deliberately
/// keeps them — dropping records while parsing would make the reader's output depend on load order
/// — and this writes what it kept. An editor that wants the reference's pruning must do it above
/// this layer, where it can say what it removed.
/// </para>
/// <para>
/// <b>One record, both count framings.</b> The baseclass list uses <c>WriteCount</c> and the
/// hit-dice bonus list a bare <c>int</c> (<c>class.cpp:7953</c> and <c>:7962</c>), the same split
/// <c>BASE_CLASS_DATA</c> has.
/// </para>
/// </remarks>
public static class ClassRecordWriter
{
    /// <summary>
    /// The earliest design version whose reader reads exactly the shape written here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The file carries no version; this is an assumption about <c>game.dat</c>.</b> Unlike
    /// <see cref="BaseclassRecordWriter"/> this record really does consult it — the reference passes
    /// <c>globalData.version</c> to both the special-abilities block and the starting equipment
    /// (<c>class.cpp:7957</c>, <c>:7971</c>), where its sibling hard-codes 0.930.
    /// </para>
    /// <para>
    /// Bound by <c>VersionSpellNames</c>, and by the <c>ITEM</c> rather than by anything the class
    /// record owns: below it an editor-role reader takes a numeric item id where this writes an
    /// <c>ITEM_ID</c> string (<c>Items.cpp:840</c>).
    /// </para>
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.SpellNames;

    /// <summary>
    /// Whether a record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A carried item held by the pre-0.998101 numeric id is the one legacy shape this record
    /// can be caught by</b>, and it is inherited from <see cref="MonsterRecordWriter"/>'s list of
    /// the same: the number indexes the item database by ordinal and the modern field is a name,
    /// so writing an empty <c>ITEM_ID</c> would equip the class with nothing.
    /// </para>
    /// <para>
    /// The bonus-value table is 25 bytes with no length on the wire, so a short one is checked here
    /// for the same reason <see cref="BaseclassRecordWriter"/> checks THAC0.
    /// </para>
    /// </remarks>
    public static bool CanWrite(ClassRecord classRecord, out string reason)
    {
        ArgumentNullException.ThrowIfNull(classRecord);

        if (classRecord.Tag != ClassRecordReader.SupportedTag)
        {
            reason = $"Class '{classRecord.Name}' is tagged '{classRecord.Tag}'; only " +
                     $"{ClassRecordReader.SupportedTag} has a storing branch. The reference reads " +
                     "Bc0 and CL1-CL4 through editor-only conversion paths and writes CL5 back.";
            return false;
        }

        foreach (var bonus in classRecord.HitDiceLevelBonuses)
        {
            if (bonus.BonusValues.Length != ClassRecordReader.BonusValueCount)
            {
                reason = $"Class '{classRecord.Name}' has a hit-dice bonus for " +
                         $"'{bonus.BaseclassId}' with {bonus.BonusValues.Length} values, not " +
                         $"{ClassRecordReader.BonusValueCount}. The table is a fixed array and " +
                         "its length is never written.";
                return false;
            }
        }

        if (!DicePlusWriter.CanWrite(classRecord.StrengthBonusDice, out string diceReason))
        {
            reason = $"Class '{classRecord.Name}' has a legacy strength-bonus expression: " +
                     diceReason;
            return false;
        }

        if (classRecord.StartingEquipment.Items.Any(i => i.LegacyItemId != 0))
        {
            reason = $"Class '{classRecord.Name}' starts with an item held by the pre-0.998101 " +
                     "numeric id, which resolves against the item database. Writing an empty " +
                     "ITEM_ID would leave the class starting with nothing.";
            return false;
        }

        if (classRecord.StartingEquipment.Ready.Slots.Count != MonsterLeafReaders.ReadySlotCount)
        {
            reason = $"Class '{classRecord.Name}' has " +
                     $"{classRecord.StartingEquipment.Ready.Slots.Count} equipment slots, not " +
                     $"{MonsterLeafReaders.ReadySlotCount}. The count is compile-time in the " +
                     "reference and never written.";
            return false;
        }

        if (!SpecabWriter.CanWrite(classRecord.SpecialAbilities))
        {
            reason = $"Class '{classRecord.Name}' has special abilities still in the pre-0.921 " +
                     "shape; see SpecabWriter.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one <c>CLASS_DATA</c>.</summary>
    /// <exception cref="NotSupportedException">
    /// When the record holds a shape that cannot go out — see <see cref="CanWrite"/>.
    /// </exception>
    /// <remarks>
    /// <b>The special-abilities block is passed empty owner strings.</b> The reference calls
    /// <c>m_specialAbilities.Serialize(car, version, "", "")</c> (<c>class.cpp:7957</c>) where the
    /// baseclass and race records pass their own name and a category. The two extra arguments are
    /// diagnostics, never bytes, which is why <see cref="SpecabWriter"/> has no parameter for them
    /// — but the asymmetry is the sort of thing that reads like a missing field.
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, ClassRecord classRecord)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(classRecord);

        if (!CanWrite(classRecord, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteString(ClassRecordReader.SupportedTag);
        ar.WriteInt32(classRecord.PreSpellNameKey);
        ar.WriteString(ArchiveStringConventions.Encode(classRecord.Name));

        ar.WriteCount((uint)classRecord.Baseclasses.Count);
        foreach (string baseclass in classRecord.Baseclasses)
        {
            // BASECLASS_ID::Serialize is a plain string write (class.cpp:7007); the port's reader
            // decodes the blank sentinel here and the reference does not -- see
            // BaseclassRecordWriter.WriteAbilityRequirement for why the reference wins.
            ar.WriteString(baseclass);
        }

        SpecabWriter.Write(ar, classRecord.SpecialAbilities);

        // A bare int, unlike the baseclass list four lines up.
        ar.WriteInt32(classRecord.HitDiceLevelBonuses.Count);
        foreach (var bonus in classRecord.HitDiceLevelBonuses)
        {
            // baseclassID then ability -- the struct declares ability first (class.h:1513).
            ar.WriteString(bonus.BaseclassId);
            ar.WriteString(bonus.Ability);
            ar.WriteBytes(bonus.BonusValues);
        }

        DicePlusWriter.Write(ar, classRecord.StrengthBonusDice);
        MonsterLeafWriters.WriteItemList(ar, classRecord.StartingEquipment);
        ar.WriteString(classRecord.HitDiceBaseclassId);   // verbatim: a BASECLASS_ID
    }

    /// <summary>Writes every record of a <c>classes.dat</c> body, without the count.</summary>
    public static void WriteAll(IArchiveWriteCursor ar, IReadOnlyList<ClassRecord> classes)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(classes);

        foreach (var classRecord in classes)
        {
            if (!CanWrite(classRecord, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        foreach (var classRecord in classes)
        {
            Write(ar, classRecord);
        }
    }

    /// <summary>Writes a whole <c>classes.dat</c>: tag, compression byte, count, records.</summary>
    public static void WriteFile(Stream stream, IReadOnlyList<ClassRecord> classes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(classes);

        TaggedDatabaseWriter.WriteFile(stream, TaggedDatabase.Class, (uint)classes.Count,
                                       ar => WriteAll(ar, classes));
    }
}
