using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>SPELL_DATA</c> (<c>Spell.cpp:3743</c>, the <c>CAR</c> overload) — the third whole
/// record type, and the format's largest.
/// </summary>
/// <remarks>
/// <para>
/// It shares every leaf items and monsters use and adds two of its own — <c>DICEPLUS</c> and
/// <c>SPELL_EFFECTS_DATA</c> — so <see cref="DicePlusWriter"/> and
/// <see cref="SpellEffectsWriter"/> come with it. The same rule governs all three record types:
/// <b>the storing branch carries no version gates</b>, because a design is always saved at the
/// current version and every gate is open by construction. See <see cref="MonsterRecordWriter"/>.
/// </para>
/// <para>
/// <b>The one apparent exception proves it.</b> This storing branch does hold a single test —
/// <c>if (ver &gt;= _VERSION_0840_) CastArt.Serialize(...)</c> (<c>Spell.cpp:3844</c>) — and it is
/// open at every version this writer produces. It is not a shape to mirror; it is a gate that
/// cannot close.
/// </para>
/// <para>
/// <b>The retired scalar blocks are read-only.</b> Thirty-five <c>Target_</c> / <c>Duration_</c> /
/// <c>Range_</c> / <c>Attack_</c> / <c>Damage_</c> / <c>Protection_</c> / <c>Heal_</c> fields are
/// loaded into <i>file-static</i> locals at or below 0.6992 and never stored — the reference itself
/// throws them away. Nothing here writes them, and a reader at
/// <see cref="WrittenVersion"/> does not look for them.
/// </para>
/// </remarks>
public static class SpellRecordWriter
{
    /// <inheritdoc cref="MonsterRecordWriter.WrittenVersion"/>
    /// <remarks>
    /// The same bound as the other two record types, and again set by the embedded
    /// <c>PIC_DATA</c>'s <c>RestartFrame</c> at 5.24. The spell record's own highest gate is lower
    /// but unusually high for a record body: <b>2.6</b>, which is where the
    /// <c>SpellInitiation</c> / <c>SpellTermination</c> pair joins the wire — and it joins
    /// <i>before</i> the 1.0303 saving-throw group rather than after it, so a design between the two
    /// reads a different set of scripts in the same bytes. Writing at anything below 2.6 would put
    /// seven scripts where the reader expects five.
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.V524;

    /// <summary>How many <c>DICEPLUS</c> parameters a modern record carries.</summary>
    /// <remarks>
    /// <c>Duration</c>, <c>P1</c> and <c>P2</c>, then <c>P3</c>‥<c>P5</c> which arrive at 0.999432.
    /// A record read below that has three, and the missing three are written as
    /// <see cref="DicePlusWriter.Empty"/> — which is what the reference's own default-constructed
    /// members write (<c>DICEPLUS::Clear</c> sets both strings empty, <c>class.cpp:2091</c>).
    /// </remarks>
    public const int ParameterCount = 6;

    /// <summary>How many <c>PIC_DATA</c> slots follow <c>CastArt</c>: missile, coverage, hit, linger.</summary>
    public const int ArtCount = 4;

    /// <summary>How many sounds follow the art: missile, coverage, hit, linger.</summary>
    public const int SoundCount = 4;

    /// <summary>
    /// Whether a record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every refusal here is a <c>DICEPLUS</c> or a special-ability block still in a legacy shape —
    /// see <see cref="DicePlusWriter.CanWrite"/> and <see cref="SpecabWriter"/>. The spell record
    /// has no legacy shape of its own that survives reading, which is the difference between it and
    /// <see cref="ItemRecordWriter"/> and <see cref="MonsterRecordWriter"/>:
    /// </para>
    /// <para>
    /// <b>The pre-0.998101 class bitmasks are already converted by the time they get here.</b> An
    /// editor-role record below that version carries a <c>WORD</c> school mask and a <c>WORD</c>
    /// cast mask, and <see cref="SpellRecordReader"/> expands both as it loads — the school into
    /// <c>"Magic User"</c> or <c>"Cleric"</c>, the cast mask into baseclass names — exactly as the
    /// reference does. So the modern form is already in hand and nothing needs
    /// <c>baseclass.dat</c>, which is what makes an old item unwritable but not an old spell.
    /// </para>
    /// <para>
    /// <b>Missing art, sounds, parameters or an effect duration are not refusals either.</b> They
    /// are absent because the reader's gate skipped them, and the reference writes its
    /// default-constructed members there — empty art, blank sounds, empty expressions. That is the
    /// same distinction the monster writer draws between "the reference has nothing to write" and
    /// "the port has lost something".
    /// </para>
    /// </remarks>
    public static bool CanWrite(SpellRecord spell, out string reason)
    {
        ArgumentNullException.ThrowIfNull(spell);

        if (spell.Parameters.Count > ParameterCount)
        {
            reason = $"Spell '{spell.Name}' carries {spell.Parameters.Count} parameters where the " +
                     $"record has {ParameterCount} slots. The surplus has nowhere to go.";
            return false;
        }

        if (spell.Art.Count != ArtCount)
        {
            reason = $"Spell '{spell.Name}' carries {spell.Art.Count} art slots, not {ArtCount}. " +
                     "The count is compile-time in the reference and never written, so a short " +
                     "list would silently truncate the record.";
            return false;
        }

        if (spell.Sounds.Count is not (0 or SoundCount))
        {
            reason = $"Spell '{spell.Name}' carries {spell.Sounds.Count} sounds, not {SoundCount} " +
                     "or none. Below 0.840 the record has no sounds at all and the reference " +
                     "writes four blanks; any other count means the reader lost its place.";
            return false;
        }

        if (spell.Scripts.Count != SpellRecordReader.SpellScriptCount)
        {
            reason = $"Spell '{spell.Name}' carries {spell.Scripts.Count} script slots, not " +
                     $"{SpellRecordReader.SpellScriptCount}. The slots are positional -- see " +
                     "SpellScriptSlot -- so a short list would write the wrong script into the " +
                     "wrong place rather than merely losing one.";
            return false;
        }

        foreach (var parameter in spell.Parameters)
        {
            if (!DicePlusWriter.CanWrite(parameter, out string dice))
            {
                reason = $"Spell '{spell.Name}' has a parameter that cannot be written: {dice}";
                return false;
            }
        }

        if (spell.EffectDuration is { } duration &&
            !DicePlusWriter.CanWrite(duration, out string durationReason))
        {
            reason = $"Spell '{spell.Name}' has an effect duration that cannot be written: " +
                     durationReason;
            return false;
        }

        foreach (var effect in spell.Effects)
        {
            if (!SpellEffectsWriter.CanWrite(effect, out string effectReason))
            {
                reason = $"Spell '{spell.Name}': {effectReason}";
                return false;
            }
        }

        if (!SpecabWriter.CanWrite(spell.SpecialAbilities))
        {
            reason = $"Spell '{spell.Name}' has special abilities still in the pre-0.921 shape; " +
                     "see SpecabWriter.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one record.</summary>
    /// <exception cref="NotSupportedException">
    /// When the record holds a legacy shape — see <see cref="CanWrite(SpellRecord, out string)"/>.
    /// </exception>
    /// <remarks>
    /// <b>The five sound paths are stripped on the way out.</b> <c>SPELL_DATA::PreSerialize</c>
    /// (<c>Spell.cpp:4276</c>) runs <c>StripFilenamePath</c> over the cast sound and the four
    /// effect sounds before a single byte is written, exactly as the item record's does over its
    /// three. The art strips itself inside <see cref="PicDataWriter"/>.
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, SpellRecord spell)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(spell);

        if (!CanWrite(spell, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        ar.WriteInt32(spell.PreSpellNameKey);
        WriteDas(ar, spell.Name);
        WriteDas(ar, PicDataWriter.StripFilenamePath(spell.CastSound));

        ar.WriteString(spell.SchoolId);                   // verbatim: a SCHOOL_ID
        BaseclassListWriter.Write(ar, spell.AllowedBaseclasses);

        ar.WriteInt32(spell.Level);
        ar.WriteInt32(spell.CastingTime);
        ar.WriteInt32(spell.CastingTimeType);
        ar.WriteInt32(spell.CanTargetFriend);
        ar.WriteInt32(spell.CanTargetEnemy);
        ar.WriteInt32(spell.IsCumulative);
        ar.WriteInt32(spell.Restrictions);
        ar.WriteInt32(spell.CanBeDispelled);
        ar.WriteInt32(spell.CanMemorize);
        ar.WriteInt32(spell.AllowScribe);
        ar.WriteInt32(spell.AutoScribe);
        ar.WriteInt32(spell.Lingers);
        ar.WriteInt32(spell.LingerOnceOnly);

        ar.WriteInt32(spell.SaveVersus);
        ar.WriteInt32(spell.SaveResult);
        ar.WriteInt32(spell.Targeting);

        // The retired Target_ block sat between these two on the wire below 0.6992, and the
        // duration rate is what follows it. Nothing is written for it here -- see the class remarks.
        ar.WriteInt32(spell.DurationRate);

        ar.WriteInt32(spell.CastCost);
        ar.WriteInt32(spell.CastPriority);

        // Duration, P1, P2, then P3..P5. A record from below 0.999432 has three and the rest go out
        // as the reference's default-constructed members do.
        for (int i = 0; i < ParameterCount; i++)
        {
            DicePlusWriter.Write(ar, i < spell.Parameters.Count
                ? spell.Parameters[i]
                : DicePlusWriter.Empty);
        }

        ar.WriteInt32(spell.Effects.Count);
        foreach (var effect in spell.Effects)
        {
            SpellEffectsWriter.Write(ar, effect);
        }

        PicDataWriter.Write(ar, spell.CastArt ?? PicDataWriter.Empty, PicArchiveVariant.Car);
        foreach (var art in spell.Art)
        {
            PicDataWriter.Write(ar, art, PicArchiveVariant.Car);
        }

        for (int i = 0; i < SoundCount; i++)
        {
            string sound = i < spell.Sounds.Count ? spell.Sounds[i] : string.Empty;
            WriteDas(ar, PicDataWriter.StripFilenamePath(sound));
        }

        WriteDas(ar, spell.CastMessage);

        // All seven pairs, in slot order. The reference empties every binary as it loads and its
        // CompileScripts is a no-op that empties five of them again (Spell.cpp:5210 -- every
        // compile call in it is commented out), so a file it wrote holds fourteen blanks here.
        foreach (var script in spell.Scripts)
        {
            WriteDas(ar, script.Source);
            WriteDas(ar, script.Binary);
        }

        DicePlusWriter.Write(ar, spell.EffectDuration ?? DicePlusWriter.Empty);

        // Both outside the storing branch, and in this order -- as in every other record.
        SpecabWriter.Write(ar, spell.SpecialAbilities);
        AslWriter.Write(ar, WrittenVersion, AslMaps.SpellData, spell.Attributes);
    }

    /// <summary>
    /// Writes a whole <c>spells.dat</c> payload (<c>SPELL_DATA_TYPE::Serialize</c>,
    /// <c>Spell.cpp:6910</c>): a count then the records.
    /// </summary>
    /// <remarks>
    /// <b>Nothing follows the records</b>, unlike <c>items.dat</c>'s ammo-type list.
    /// </remarks>
    public static void WriteDatabase(IArchiveWriteCursor ar, IReadOnlyList<SpellRecord> spells)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(spells);

        // Checked before a single byte goes out, as the other two databases are.
        foreach (var spell in spells)
        {
            if (!CanWrite(spell, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        ar.WriteInt32(spells.Count);
        foreach (var spell in spells)
        {
            Write(ar, spell);
        }
    }

    private static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
