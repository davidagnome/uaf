using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>MONSTER_DATA</c> (<c>Monster.cpp:629</c>) — the first whole record this port can
/// produce.
/// </summary>
/// <remarks>
/// <para>
/// Monsters first because the corpus proves their records carry real content in every leaf the
/// other databases share: an ASL block on all 195 monsters in <c>SomethingWild</c>, special
/// abilities, an embedded <c>PIC_DATA</c>, an item list, a money sack. Items and spells reuse most
/// of that.
/// </para>
/// <para>
/// <b>One write path, whatever the version</b> — the same rule <see cref="SpecabWriter"/> follows,
/// and for the same reason. The reference's storing branch is a flat run of writes with no version
/// tests in it: every gate in <see cref="MonsterRecordReader"/> lives in the loading half only.
/// That is not an oversight. A design is always saved at the <i>current</i> version, so on the way
/// out every gate is open by construction; the loading gates exist to read what older builds left
/// behind. Mirroring them here would emit an old shape into a file stamped new, which is the one
/// combination nothing can read.
/// </para>
/// <para>
/// The consequence is that a record still holding a legacy shape cannot be written at all, and
/// <see cref="CanWrite(MonsterRecord, out string)"/> says so rather than writing a file that reads
/// back clean with the content quietly gone. See its remarks for the four cases.
/// </para>
/// <para>
/// <b>Either encoding.</b> This writes through <see cref="IArchiveWriteCursor"/>, so the same
/// record walk produces a plain stream or a compressed <c>CAR</c> depending on what it is handed —
/// which is how a shipped <c>monsters.dat</c>, every one of which is LZW-compressed, can be written
/// back in the form it arrived in.
/// </para>
/// </remarks>
public static class MonsterRecordWriter
{
    /// <summary>
    /// The earliest version whose reader reads exactly the shape written here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bound by <c>RestartFrame</c> inside the icon, which arrives at <c>_VERSION_524</c> — 5.24,
    /// not 0.524, despite the unpadded name. Nothing is added to the monster record between it and
    /// <see cref="DesignVersion.Product"/>, so anything in that range reads this identically; the
    /// reference stamps what it saves with <c>Product</c>.
    /// </para>
    /// <para>
    /// Read this back at a lower version and the mismatch is silent: the gates simply stop early
    /// and the remaining fields land in the next record.
    /// </para>
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.V524;

    /// <summary>
    /// Whether a record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four legacy shapes survive reading and have no modern form to go out as. Three are
    /// unresolved identifiers the reference resolves during load — through databases this port does
    /// not have at hand — and the fourth needs a conversion that is not ported:
    /// </para>
    /// <list type="bullet">
    /// <item><b>No icon.</b> Below 0.640 the record holds a bare filename, which the reference
    /// turns into a whole <c>PIC_DATA</c> via <c>SetDefaults()</c>. Writing zeros for the animation
    /// parameters would give every old monster a still frame of nothing.</item>
    /// <item><b>An attack with a legacy spell id.</b> The number indexes a spell table by ordinal;
    /// the modern field is a name.</item>
    /// <item><b>A carried item with a legacy item id.</b> Same shape, same problem.</item>
    /// <item><b>Special abilities still in the legacy shape</b> — see
    /// <see cref="SpecabWriter"/>.</item>
    /// </list>
    /// <para>
    /// <b>A missing item list or money sack is not one of them.</b> Those are absent below 0.694
    /// and 0.906 respectively, and the reference writes its default-constructed members there — an
    /// empty list with twelve zeroed slots, ten zeroed coin types. Writing empties is therefore
    /// exact, not a guess.
    /// </para>
    /// </remarks>
    public static bool CanWrite(MonsterRecord monster, out string reason)
    {
        ArgumentNullException.ThrowIfNull(monster);

        if (monster.Icon is null)
        {
            reason = $"Monster '{monster.Name}' was read from a design below 0.640 and has only " +
                     $"the icon filename '{monster.LegacyIconFile}'. Building a PIC_DATA from it " +
                     "needs PIC_DATA::SetDefaults (PicData.cpp), which is not ported.";
            return false;
        }

        if (monster.Attacks.Any(a => a.LegacySpellId != 0))
        {
            reason = $"Monster '{monster.Name}' has an attack carrying the pre-0.998101 numeric " +
                     "spell id, which resolves against the spell database. The modern field is a " +
                     "SPELL_ID name; writing an empty one would drop the attack's spell.";
            return false;
        }

        if (monster.Items is not null && monster.Items.Items.Any(i => i.LegacyItemId != 0))
        {
            reason = $"Monster '{monster.Name}' carries an item held by the pre-0.998101 numeric " +
                     "id, which resolves against the item database. Writing an empty ITEM_ID " +
                     "would leave the monster holding nothing.";
            return false;
        }

        if (!SpecabWriter.CanWrite(monster.SpecialAbilities))
        {
            reason = $"Monster '{monster.Name}' has special abilities still in the pre-0.921 " +
                     "shape; see SpecabWriter.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one record.</summary>
    /// <exception cref="NotSupportedException">
    /// When the record holds a legacy shape — see <see cref="CanWrite(MonsterRecord, out string)"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b><c>HitDice</c> is a <c>float</c> among <c>int</c>s</b> (<c>Monster.h:410</c>). Writing it
    /// as an integer is four bytes either way, so it never desynchronises — it just gives every
    /// monster in the file a nonsense hit-die count, which no reader will complain about.
    /// </para>
    /// <para>
    /// <b>The DAS blank convention applies to six strings and not to the rest.</b> Name, the four
    /// sounds and the icon filename go through it; <c>classID</c>, <c>undeadType</c>, the item ids
    /// and the spell ids are written verbatim. The reference marks the difference only by which
    /// macro it uses at the call site.
    /// </para>
    /// <para>
    /// <b>Two things the reference does here that this deliberately does not.</b> It rewrites
    /// <c>'/'</c> to <c>'|'</c> in the name when saving at or below 0.830 — unreachable, since it
    /// saves at <see cref="WrittenVersion"/> and above. And it re-derives the <c>$SYS$Race</c>
    /// attribute from its in-memory <c>raceID</c> before writing the ASL
    /// (<c>StoreStringAsASL</c>, <c>Monster.cpp:659</c>); this port never splits the two apart, so
    /// the attribute is written back exactly as it was read — the same bytes for any file the
    /// reference produced, and unaltered for any it did not.
    /// </para>
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, MonsterRecord monster)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(monster);

        if (!CanWrite(monster, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteInt32(monster.PreSpellNameKey);
        WriteDas(ar, monster.Name);

        PicDataWriter.Write(ar, monster.Icon!, PicArchiveVariant.Car);

        WriteDas(ar, monster.HitSound);
        WriteDas(ar, monster.MissSound);
        WriteDas(ar, monster.MoveSound);
        WriteDas(ar, monster.DeathSound);

        ar.WriteInt32(monster.Intelligence);
        ar.WriteInt32(monster.ArmorClass);
        ar.WriteInt32(monster.Movement);
        ar.WriteSingle(monster.HitDice);         // float, not int
        ar.WriteInt32(monster.UseHitDice);
        ar.WriteInt32(monster.HitDiceBonus);
        ar.WriteInt32(monster.Thac0);

        MonsterLeafWriters.WriteAttackData(ar, monster.Attacks);

        ar.WriteInt32(monster.MagicResistance);
        ar.WriteInt32(monster.Size);
        ar.WriteString(monster.ClassId);         // verbatim
        ar.WriteInt32(monster.Morale);
        ar.WriteInt32(monster.ExperienceValue);

        ar.WriteUInt32(monster.FormType);
        ar.WriteUInt32(monster.PenaltyType);
        ar.WriteUInt32(monster.ImmunityType);
        ar.WriteUInt32(monster.MiscOptionsType);
        ar.WriteString(monster.UndeadType);      // verbatim; a name, never the legacy ordinal

        SpecabWriter.Write(ar, monster.SpecialAbilities);
        AslWriter.Write(ar, WrittenVersion, AslMaps.MonsterData, monster.Attributes);

        // Both AFTER the attribute list. MONSTER_DATA is the only record that continues past its
        // ASL, so a writer modelled on ITEM_DATA stops here and the reader runs into the next
        // record's key looking for an item count.
        MonsterLeafWriters.WriteItemList(ar, monster.Items ?? EmptyItemList);
        MonsterLeafWriters.WriteMoneySack(ar, monster.Money ?? EmptyMoneySack);
    }

    /// <summary>
    /// Writes a whole <c>monsters.dat</c> payload (<c>MONSTER_DATA_TYPE::Serialize</c>,
    /// <c>Monster.cpp:1023</c>): a count then the records, and nothing after.
    /// </summary>
    /// <remarks>
    /// <b>Every record is checked before the first byte goes out.</b> A database that fails
    /// half-way leaves a truncated file whose count promises records that are not there — and the
    /// reader will read past the end of it into whatever follows.
    /// </remarks>
    public static void WriteDatabase(IArchiveWriteCursor ar, IReadOnlyList<MonsterRecord> monsters)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(monsters);

        foreach (var monster in monsters)
        {
            if (!CanWrite(monster, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        ar.WriteInt32(monsters.Count);
        foreach (var monster in monsters)
        {
            Write(ar, monster);
        }
    }

    /// <summary>What the reference's default-constructed <c>myItems</c> writes as.</summary>
    private static ItemList EmptyItemList { get; } =
        new([], new ReadyItems(new int[MonsterLeafReaders.ReadySlotCount]));

    /// <summary>What the reference's default-constructed <c>money</c> writes as.</summary>
    private static MoneySack EmptyMoneySack { get; } =
        new(new int[MonsterLeafReaders.MaxCoinTypes], [], []);

    private static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
