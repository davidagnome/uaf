using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>CHARACTER</c> (<c>Char.cpp:2540</c>, the <c>CAR</c> overload) — the fourth record
/// type, and the format's largest.
/// </summary>
/// <remarks>
/// <para>
/// It draws on more leaves than anything else in the format: <c>PIC_DATA</c> twice, an
/// <c>ITEM_LIST</c>, a <c>MONEY_SACK</c>, a spellbook, a blockage list, three tagged adjustment
/// lists, a list of <c>SPELL_EFFECTS_DATA</c>, special abilities and an ASL. The rule that governs
/// the other three record types governs this one too: <b>the storing branch carries no version
/// gates</b> — see <see cref="MonsterRecordWriter"/>.
/// </para>
/// <para>
/// <b>The opener is a constant, not the record's own version.</b> The reference writes
/// <c>CHARACTER_VERSION</c> — <c>0x80000001</c> (<c>ProjectVersion.h:6</c>) — whatever the record
/// was read as. That is what makes the field a discriminator on the way back in: the high bit says
/// "a version follows", and its absence says the first <c>int</c> was a legacy index. The index
/// itself the reference discards on load (<c>//uniqueKey = temp;</c>), so writing the constant over
/// it loses nothing that was ever kept.
/// </para>
/// <para>
/// <b>One loading-side fixup is deliberately not mirrored.</b> A character whose opener was an
/// index has its armour class reduced by the protection its readied items give
/// (<c>Char.cpp:3015</c>), because old versions folded that in. This port reads the raw
/// <c>m_AC</c>, which is what lets a record be written back byte-exact; the cost is that the port's
/// in-memory AC differs from the reference's for such a character. §10a of
/// <c>SERIALIZATION.md</c> states the trade-off — it has to be made per field, and this is the
/// field.
/// </para>
/// </remarks>
public static class CharacterRecordWriter
{
    /// <inheritdoc cref="MonsterRecordWriter.WrittenVersion"/>
    /// <remarks>
    /// 5.24 again, and again set by the embedded <c>PIC_DATA</c>'s <c>RestartFrame</c> — this
    /// record carries two of them. The highest gate in the record body proper is <b>0.999702</b>,
    /// where the seven ability scores widen from <c>BYTE</c> to <c>int</c>: 21 bytes appearing in
    /// the middle of the record, and the reason a writer cannot target an arbitrary version even
    /// if every other field would survive it.
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.V524;

    /// <summary>
    /// The opener the reference writes for every character — <c>CHARACTER_VERSION</c>.
    /// </summary>
    public const uint CharacterVersion = 0x80000001;

    /// <summary>
    /// Whether a record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three legacy shapes survive reading with no modern form to go out as, and all three are the
    /// same shapes the monster writer refuses — which is the useful part: a record type this much
    /// larger added no new kind of refusal, only more places for the known ones to appear.
    /// </para>
    /// <list type="bullet">
    /// <item><b>No icon.</b> Below 0.640 the record holds a bare filename and a discarded
    /// <c>int</c>; building a <c>PIC_DATA</c> from it needs <c>SetDefaults()</c>, unported.</item>
    /// <item><b>No money sack.</b> Below 0.661 the coins are loose <c>int</c>s that the reference
    /// folds into a sack as it reads; this port discards them, so writing an empty sack would take
    /// the character's money. Contrast a <i>monster</i>'s missing sack, which is absent because
    /// the reference had nothing to write — there the empty sack is exact.</item>
    /// <item><b>A carried item with a legacy numeric id</b>, and <b>special abilities in the
    /// pre-0.921 shape</b> — see <see cref="MonsterRecordWriter"/> and
    /// <see cref="SpecabWriter"/>.</item>
    /// </list>
    /// <para>
    /// <b>An empty spellbook, no blockages and no adjustments are not refusals.</b> Each is a
    /// count of zero on the wire, which is exactly what the reference's default-constructed
    /// members write.
    /// </para>
    /// </remarks>
    public static bool CanWrite(CharacterRecord character, out string reason)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (character.Icon is null)
        {
            reason = $"Character '{character.Name}' was read from a design below 0.640 and has " +
                     "only an icon filename. Building a PIC_DATA from it needs " +
                     "PIC_DATA::SetDefaults (PicData.cpp), which is not ported.";
            return false;
        }

        if (character.Money is null)
        {
            reason = $"Character '{character.Name}' was read from a design below 0.661, where the " +
                     "coins are loose ints rather than a MONEY_SACK. The reference folds them " +
                     "into the sack as it loads; this port drops them, so writing an empty sack " +
                     "would take the character's money.";
            return false;
        }

        if (character.Items.Items.Any(i => i.LegacyItemId != 0))
        {
            reason = $"Character '{character.Name}' carries an item held by the pre-0.998101 " +
                     "numeric id, which resolves against the item database. Writing an empty " +
                     "ITEM_ID would leave the character holding nothing.";
            return false;
        }

        foreach (var effect in character.SpellEffects)
        {
            if (!SpellEffectsWriter.CanWrite(effect, out string effectReason))
            {
                reason = $"Character '{character.Name}': {effectReason}";
                return false;
            }
        }

        if (!SpecabWriter.CanWrite(character.SpecialAbilities))
        {
            reason = $"Character '{character.Name}' has special abilities still in the pre-0.921 " +
                     "shape; see SpecabWriter.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one record.</summary>
    /// <exception cref="NotSupportedException">
    /// When the record holds a legacy shape — see <see cref="CanWrite(CharacterRecord, out string)"/>.
    /// </exception>
    /// <remarks>
    /// <b>The width traps are denser here than anywhere else in the format.</b>
    /// <c>nbrHitDice</c> is a <c>double</c> and <c>NbrAttacks</c> a <c>float</c> — eight bytes and
    /// four, neither of them the <c>int</c> its neighbours are — while <c>type</c>,
    /// <c>maxMovement</c>, <c>uniquePartyID</c>, <c>openDoors</c>, <c>openMagicDoors</c> and
    /// <c>BB_LG</c> are single <c>BYTE</c>s among <c>int</c>s. The seven ability scores are
    /// <c>int</c>s here because <see cref="WrittenVersion"/> is past 0.999702; below it they are
    /// <c>BYTE</c>s, which is the widest single divergence in the record.
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, CharacterRecord character)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(character);

        if (!CanWrite(character, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteUInt32(CharacterVersion);                // the constant, not character.CharacterVersion
        ar.WriteInt32(character.PreSpellNamesKey);

        ar.WriteByte(character.Type);                    // BYTE
        ar.WriteString(character.Race);                  // verbatim: a RACE_ID
        ar.WriteInt32(character.Gender);
        ar.WriteString(character.ClassId);               // verbatim: a CLASS_ID
        ar.WriteInt32(character.Alignment);
        ar.WriteInt32(character.AllowInCombat);
        ar.WriteInt32(character.Status);
        ar.WriteString(character.UndeadType);            // verbatim, and a name rather than an ordinal
        ar.WriteInt32(character.CreatureSize);

        WriteDas(ar, character.Name);
        ar.WriteString(character.CharacterId);           // verbatim: a CHARACTER_ID

        ar.WriteInt32(character.Thac0);
        ar.WriteInt32(character.Morale);
        ar.WriteInt32(character.Encumbrance);
        ar.WriteInt32(character.MaxEncumbrance);
        ar.WriteInt32(character.ArmorClass);
        ar.WriteInt32(character.HitPoints);
        ar.WriteInt32(character.MaxHitPoints);
        ar.WriteDouble(character.NumberOfHitDice);       // 8 bytes among 4-byte neighbours

        ar.WriteInt32(character.Age);
        ar.WriteInt32(character.MaxAge);
        ar.WriteInt32(character.Birthday);
        ar.WriteInt32(character.MaxCureDisease);

        ar.WriteInt32(character.UnarmedDieSmall);
        ar.WriteInt32(character.UnarmedNumberDieSmall);
        ar.WriteInt32(character.UnarmedBonus);
        ar.WriteInt32(character.UnarmedDieLarge);
        ar.WriteInt32(character.UnarmedNumberDieLarge);

        ar.WriteByte(character.MaxMovement);             // BYTE
        ar.WriteInt32(character.ReadyToTrain);
        ar.WriteInt32(character.CanTradeItems);

        // GetPermStr() and its six siblings all return int (Char.h:926), so the modern width is
        // what goes out -- which is only correct because WrittenVersion is past 0.999702.
        var abilities = character.Abilities;
        ar.WriteInt32(abilities.Strength);
        ar.WriteInt32(abilities.StrengthMod);
        ar.WriteInt32(abilities.Intelligence);
        ar.WriteInt32(abilities.Wisdom);
        ar.WriteInt32(abilities.Dexterity);
        ar.WriteInt32(abilities.Constitution);
        ar.WriteInt32(abilities.Charisma);

        ar.WriteByte(character.OpenDoors);               // three BYTEs in a row
        ar.WriteByte(character.OpenMagicDoors);
        ar.WriteByte(character.BendBarsLiftGates);

        ar.WriteInt32(character.HitBonus);
        ar.WriteInt32(character.DamageBonus);
        ar.WriteInt32(character.MagicResistance);

        // Three string-tagged lists, each opening with its own version tag.
        CharacterLeafWriters.WriteBaseclassStats(ar, character.BaseclassStats);
        CharacterLeafWriters.WriteSkillAdjustments(ar, character.SkillAdjustments);
        CharacterLeafWriters.WriteSpellAdjustments(ar, character.SpellAdjustments);

        ar.WriteInt32(character.IsPreGenerated);
        ar.WriteInt32(character.CanBeSaved);
        ar.WriteInt32(character.HasLayedOnHandsToday);

        MonsterLeafWriters.WriteMoneySack(ar, character.Money!);
        ar.WriteSingle(character.NumberOfAttacks);       // float, not int

        PicDataWriter.Write(ar, character.Icon!, PicArchiveVariant.Car);
        ar.WriteInt32(character.IconIndex);
        ar.WriteInt32(character.OriginalIndex);
        ar.WriteByte(character.UniquePartyId);           // BYTE

        ar.WriteInt32(character.DisableTalkIfDead);
        ar.WriteUInt32(character.TalkEvent);
        WriteDas(ar, character.TalkLabel);
        ar.WriteUInt32(character.ExamineEvent);
        WriteDas(ar, character.ExamineLabel);

        CharacterLeafWriters.WriteSpellBook(ar, character.SpellBook);

        ar.WriteInt32(character.DetectingInvisible);
        ar.WriteInt32(character.DetectingTraps);

        ar.WriteInt32(character.SpellEffects.Count);
        foreach (var effect in character.SpellEffects)
        {
            SpellEffectsWriter.Write(ar, effect);
        }

        CharacterLeafWriters.WriteBlockages(ar, character.Blockages);

        // Four leaves outside the storing branch -- one more than any other record has.
        PicDataWriter.Write(ar, character.SmallPic, PicArchiveVariant.Car);
        MonsterLeafWriters.WriteItemList(ar, character.Items);
        SpecabWriter.Write(ar, character.SpecialAbilities);
        AslWriter.Write(ar, WrittenVersion, AslMaps.Character, character.Attributes);
    }

    /// <summary>
    /// Writes a <c>CHAR_LIST</c> (<c>Char.cpp:9531</c>): a count then the characters.
    /// </summary>
    public static void WriteList(IArchiveWriteCursor ar, IReadOnlyList<CharacterRecord> characters)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(characters);

        // Checked before a single byte goes out, as the three databases are.
        foreach (var character in characters)
        {
            if (!CanWrite(character, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        ar.WriteInt32(characters.Count);
        foreach (var character in characters)
        {
            Write(ar, character);
        }
    }

    private static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
