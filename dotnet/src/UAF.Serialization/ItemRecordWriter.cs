using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>ITEM_DATA</c> (<c>Items.cpp:2677</c>) — the second whole record type.
/// </summary>
/// <remarks>
/// <para>
/// The closest sibling of <see cref="MonsterRecordWriter"/>: it shares three of the four leaves and
/// the same rule that the storing branch carries no version gates. What differs is the shape —
/// <b>an item record ends <i>at</i> its ASL</b> where a monster's continues past it — and one
/// oddity of its own.
/// </para>
/// <para>
/// <b>Use the <c>CAR</c> overload, not the <c>CArchive</c> one.</b> The latter's storing branch
/// opens with <c>die("We should not be serializing itemdata with CArchive")</c>
/// (<c>Items.cpp:2348</c>) — code that cannot run, describing a format that is never produced.
/// Transcribing from it would give a shape nothing reads.
/// </para>
/// <para>
/// <b><c>HitArt</c> is written twice and <c>MissileArt</c> once.</b> The pair goes out early, then
/// <c>HitArt</c> alone again near the end (<c>:2698</c> and <c>:2744</c>). Both are on the wire and
/// the reader consumes both; writing one would leave a whole <c>PIC_DATA</c> missing and every
/// record after it misaligned. The trailing comment "MissileArt is serialized in attribute map"
/// explains the asymmetry: the second copy is <c>HitArt</c>'s combat-directory form, and missile
/// art keeps its own place in the ASL rather than being repeated.
/// </para>
/// </remarks>
public static class ItemRecordWriter
{
    /// <inheritdoc cref="MonsterRecordWriter.WrittenVersion"/>
    /// <remarks>
    /// The same bound as the monster writer and for the same reason — the embedded
    /// <c>PIC_DATA</c>'s <c>RestartFrame</c> arrives at 5.24. Every gate in the item record is
    /// open well below that.
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.V524;

    /// <summary>
    /// Whether a record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two legacy shapes survive reading with no modern form to go out as:
    /// </para>
    /// <list type="bullet">
    /// <item><b>A usability bitmask instead of a baseclass list.</b> Below
    /// <see cref="DesignVersion.SpellNames"/> an editor-role record carries
    /// <c>Usable_by_Class</c> — seven bits — where the modern form is a counted list of
    /// <c>BASECLASS_ID</c> names. Converting needs the baseclass database, which has no reader.</item>
    /// <item><b>Special abilities still in the pre-0.921 shape</b> — see
    /// <see cref="SpecabWriter"/>.</item>
    /// </list>
    /// <para>
    /// <b>A missing <c>HitArt</c> or <c>MissileArt</c> is not one of them.</b> Both are absent from
    /// an old-enough record because the reader's gate skipped them, and the reference writes its
    /// default-constructed <c>PIC_DATA</c> there — all zeros and an empty filename, which is what
    /// <see cref="EmptyArt"/> is.
    /// </para>
    /// </remarks>
    public static bool CanWrite(ItemRecord item, out string reason)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Tail.LegacyUsableByClass != 0)
        {
            reason = $"Item '{item.Names.IdName}' carries the pre-0.998101 Usable_by_Class " +
                     "bitmask rather than a baseclass list. Converting it needs baseclass.dat, " +
                     "which has no reader; writing an empty list would make the item usable by " +
                     "nobody.";
            return false;
        }

        if (!SpecabWriter.CanWrite(item.Tail.SpecialAbilities))
        {
            reason = $"Item '{item.Names.IdName}' has special abilities still in the pre-0.921 " +
                     "shape; see SpecabWriter.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <inheritdoc cref="PicDataWriter.Empty"/>
    public static PicRecord EmptyArt => PicDataWriter.Empty;

    /// <summary>Writes one record.</summary>
    /// <exception cref="NotSupportedException">
    /// When the record holds a legacy shape — see <see cref="CanWrite(ItemRecord, out string)"/>.
    /// </exception>
    /// <remarks>
    /// <b><c>RofPerRound</c> is a <c>double</c> among <c>int</c>s</b> — eight bytes where its
    /// neighbours are four, so writing it as anything narrower shifts the whole rest of the record.
    /// It is the item record's counterpart of the monster's <c>float</c> hit dice, and worse,
    /// because the width really does change.
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(item);

        if (!CanWrite(item, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        var names = item.Names;
        ar.WriteInt32(names.PreSpellNameKey);
        ar.WriteString(names.SpellId);                    // verbatim: a SPELL_ID
        WriteDas(ar, names.UniqueName);
        WriteDas(ar, names.IdName);

        // The reference strips each sound's directory as it stores, exactly as it does the icon's.
        WriteDas(ar, PicDataWriter.StripFilenamePath(names.HitSound));
        WriteDas(ar, PicDataWriter.StripFilenamePath(names.MissSound));
        WriteDas(ar, PicDataWriter.StripFilenamePath(names.LaunchSound));

        PicDataWriter.Write(ar, item.HitArt ?? EmptyArt, PicArchiveVariant.Car);
        PicDataWriter.Write(ar, item.MissileArt ?? EmptyArt, PicArchiveVariant.Car);

        var scalars = item.Scalars;
        WriteDas(ar, scalars.AmmoType);
        ar.WriteInt32(scalars.Experience);
        ar.WriteInt32(scalars.Cost);
        ar.WriteInt32(scalars.Encumbrance);
        ar.WriteInt32(scalars.AttackBonus);
        ar.WriteInt32(scalars.Cursed);
        ar.WriteInt32(scalars.BundleQty);
        ar.WriteInt32(scalars.NumCharges);

        var combat = item.Combat;
        ar.WriteUInt32(combat.LocationReadied);
        ar.WriteInt32(combat.HandsToUse);
        ar.WriteInt32(combat.DmgDiceSm);
        ar.WriteInt32(combat.NbrDiceSm);
        ar.WriteInt32(combat.DmgBonusSm);
        ar.WriteInt32(combat.DmgDiceLg);
        ar.WriteInt32(combat.NbrDiceLg);
        ar.WriteInt32(combat.DmgBonusLg);
        ar.WriteDouble(combat.RofPerRound);               // 8 bytes among 4-byte neighbours
        ar.WriteInt32(combat.ProtectionBase);
        ar.WriteInt32(combat.ProtectionBonus);

        var tail = item.Tail;
        ar.WriteInt32(tail.WeaponType);
        ar.WriteInt32(tail.UsageFlags);

        ar.WriteInt32(tail.UsableByBaseclass.Count);
        foreach (string baseclass in tail.UsableByBaseclass)
        {
            ar.WriteString(baseclass);                    // verbatim: a BASECLASS_ID
        }

        ar.WriteInt32(tail.RangeMax);
        ar.WriteUInt32(tail.UseEvent);
        ar.WriteUInt32(tail.ExamineEvent);
        WriteDas(ar, tail.ExamineLabel);
        WriteDas(ar, tail.AttackMessage);
        ar.WriteInt32(tail.RechargeRate);
        ar.WriteInt32(tail.IsNonLethal);

        // HitArt a SECOND time -- see the class remarks. This is the copy the reader's
        // ReadTail consumes, and the record's own HitArt field is the one it keeps.
        PicDataWriter.Write(ar, tail.HitArt ?? EmptyArt, PicArchiveVariant.Car);

        ar.WriteInt32(tail.CanBeHalvedJoined);
        ar.WriteInt32(tail.CanBeTradeDropSoldDep);

        // Both outside the storing branch, and in this order. Unlike a monster, nothing follows.
        SpecabWriter.Write(ar, tail.SpecialAbilities);
        AslWriter.Write(ar, WrittenVersion, AslMaps.ItemData, tail.Attributes);
    }

    /// <summary>
    /// Writes a whole <c>items.dat</c> payload: a count, the records, then the ammo-type list.
    /// </summary>
    /// <remarks>
    /// <b>The ammo list is outside the record loop</b>, the same shape as a monster's item list
    /// sitting after its ASL — a writer that stops after the records leaves the reader to take the
    /// list's count from whatever follows.
    /// </remarks>
    public static void WriteDatabase(IArchiveWriteCursor ar, IReadOnlyList<ItemRecord> items,
                                     IReadOnlyList<string> ammoTypes)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(ammoTypes);

        // Checked before a single byte goes out, as the monster database is.
        foreach (var item in items)
        {
            if (!CanWrite(item, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        ar.WriteInt32(items.Count);
        foreach (var item in items)
        {
            Write(ar, item);
        }

        ar.WriteInt32(ammoTypes.Count);
        foreach (string ammo in ammoTypes)
        {
            ar.WriteString(ammo);
        }
    }

    private static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
