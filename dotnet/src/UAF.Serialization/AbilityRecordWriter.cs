using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>ability.dat</c> (<c>ABILITY_DATA::Serialize</c>, <c>class.cpp:3996</c>, storing branch,
/// inside <c>ABILITY_DATA_TYPE::Serialize</c>, <c>class.cpp:4381</c>).
/// </summary>
/// <remarks>
/// <para>
/// The smallest of the five databases that had a reader and no writer, and the one whose record has
/// no version fork at all on the way out: <c>Abd0</c> is the only shape that has ever existed, so
/// there is nothing here to refuse for being legacy except what the two leaves refuse for
/// themselves.
/// </para>
/// <para>
/// <b>The record does not write the key its reader may consume.</b> A pre-<c>VersionSpellNames</c>
/// editor stream carries a <c>DWORD</c> between the tag and the name, and the reference reads it
/// into nothing — the matching <c>car &lt;&lt; m_abilityKey</c> in the storing branch is commented
/// out (<c>class.cpp:4002</c>). So the field is readable and unwritable in the reference too, which
/// is why <see cref="AbilityRecord"/> has nowhere to keep it and nothing is lost by writing at
/// <see cref="WrittenVersion"/>.
/// </para>
/// <para>
/// <b>The special-abilities block sits outside the storing branch</b> and is gated on the design's
/// version rather than the record tag (<c>class.cpp:4035</c>). At <see cref="WrittenVersion"/> the
/// gate is open, so it is always written — an ability read from a design below 0.930 gains an empty
/// block, which is what the reference writes there too once the design is saved at a modern version.
/// </para>
/// </remarks>
public static class AbilityRecordWriter
{
    /// <summary>
    /// The earliest design version whose reader reads exactly the shape written here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing in this file records the version, so this is an assumption about
    /// <c>game.dat</c>, not a stamp.</b> A tagged database carries a container tag and a record
    /// count and no version <c>double</c> — the design version comes from beside it. Write these
    /// records into a design whose <c>game.dat</c> says less than this and the reader takes a
    /// different fork with no complaint anywhere.
    /// </para>
    /// <para>
    /// Bound by <c>VersionSpellNames</c>: below it an <b>editor</b>-role reader consumes a
    /// <c>DWORD</c> this writer does not emit (<c>class.cpp:4008</c>), and every field after it in
    /// every record of the file lands four bytes early. The special-abilities gate at 0.930 is the
    /// only other one and it is well below.
    /// </para>
    /// </remarks>
    public static DesignVersion WrittenVersion => DesignVersion.SpellNames;

    /// <summary>
    /// Whether a record can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// Both refusals are inherited from leaves that have their own legacy shapes — a
    /// <c>DICEPLUS</c> still in a numeric form, and a special-abilities block still in the pre-0.921
    /// one. The ability record itself contributes none.
    /// </remarks>
    public static bool CanWrite(AbilityRecord ability, out string reason)
    {
        ArgumentNullException.ThrowIfNull(ability);

        if (!DicePlusWriter.CanWrite(ability.Roll, out string diceReason))
        {
            reason = $"Ability '{ability.Name}' rolls from a legacy dice expression: {diceReason}";
            return false;
        }

        if (!SpecabWriter.CanWrite(ability.SpecialAbilities))
        {
            reason = $"Ability '{ability.Name}' has special abilities still in the pre-0.921 " +
                     "shape; see SpecabWriter.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one <c>ABILITY_DATA</c>.</summary>
    /// <exception cref="NotSupportedException">
    /// When a leaf carries a legacy shape — see <see cref="CanWrite"/>.
    /// </exception>
    /// <remarks>
    /// <b>Name and abbreviation go through the blank sentinel and the tag does not.</b> The
    /// reference does it by hand rather than with the <c>AS</c> macro — assigning <c>"*"</c>,
    /// writing, then assigning back (<c>class.cpp:4003</c>) — which is the same convention spelled
    /// out. An ability whose abbreviation is genuinely empty would otherwise come back as a
    /// zero-length <c>CString</c> the reference's reader would not restore.
    /// </remarks>
    public static void Write(IArchiveWriteCursor ar, AbilityRecord ability)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(ability);

        if (!CanWrite(ability, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        // The per-record tag, before anything else and verbatim -- unlike the race and class
        // databases, an ability's shape is announced by the record rather than by the container.
        ar.WriteString(AbilityRecordReader.SupportedTag);

        ar.WriteString(ArchiveStringConventions.Encode(ability.Name));
        ar.WriteString(ArchiveStringConventions.Encode(ability.Abbreviation));

        DicePlusWriter.Write(ar, ability.Roll);

        // Outside the storing branch in the reference, and unconditional here: see the class
        // remarks for why WrittenVersion leaves the gate open.
        SpecabWriter.Write(ar, ability.SpecialAbilities);
    }

    /// <summary>Writes every record of an <c>ability.dat</c> body, without the count.</summary>
    /// <remarks>
    /// The count belongs to the framing and is written before the compression switch's other side —
    /// see <see cref="TaggedDatabaseWriter.WriteFile"/>. Splitting it out here is what keeps a
    /// caller from writing it twice or in the wrong encoding.
    /// </remarks>
    public static void WriteAll(IArchiveWriteCursor ar, IReadOnlyList<AbilityRecord> abilities)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(abilities);

        // Checked before a single byte goes out, as the item and monster databases are: a refusal
        // half-way through leaves a file that is worse than no file.
        foreach (var ability in abilities)
        {
            if (!CanWrite(ability, out string reason))
            {
                throw new NotSupportedException(reason);
            }
        }

        foreach (var ability in abilities)
        {
            Write(ar, ability);
        }
    }

    /// <summary>Writes a whole <c>ability.dat</c>: tag, compression byte, count, records.</summary>
    public static void WriteFile(Stream stream, IReadOnlyList<AbilityRecord> abilities)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(abilities);

        TaggedDatabaseWriter.WriteFile(stream, TaggedDatabase.Ability, (uint)abilities.Count,
                                       ar => WriteAll(ar, abilities));
    }
}
