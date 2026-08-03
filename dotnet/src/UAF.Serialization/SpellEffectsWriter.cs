using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes <c>SPELL_EFFECTS_DATA</c> (<c>Spell.cpp:201</c>) — the inverse of
/// <see cref="SpellEffectsReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No version gates, as everywhere else on the write side</b>: the storing branch is a flat run
/// that writes all eleven strings and every scalar, while the loading half admits them in four
/// waves. See <see cref="MonsterRecordWriter"/> for why writing the modern shape unconditionally is
/// the only behaviour the format has.
/// </para>
/// <para>
/// <b><c>changeData</c> is written outside the branch</b> (<c>Spell.cpp:273</c>), after the brace
/// that closes it — so it belongs to every effect at every version, and a writer that puts it
/// inside the modern path would drop eight bytes from an old one. It is the trap
/// <see cref="SpellEffectsReader"/> names, seen from the other side.
/// </para>
/// </remarks>
public static class SpellEffectsWriter
{
    /// <summary>
    /// The nine script strings an effect carries — <c>m_string3</c> through <c>m_string11</c>.
    /// </summary>
    /// <remarks>
    /// Alternating source and compiled binary from <c>m_string4</c> on: activation binary,
    /// modification source and binary, then a saving-throw source/binary pair for each of the plain,
    /// failed and succeeded cases (<c>class.h:2415</c>).
    /// </remarks>
    public const int ScriptCount = 9;

    /// <summary>
    /// Whether an effect can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// The only refusal is a <c>changeData</c> still in a numeric <c>DICEPLUS</c> form — see
    /// <see cref="DicePlusWriter.CanWrite"/>. A <i>short</i> script list is not one: the four waves
    /// are cumulative, so a short list is always a prefix of the nine and the missing tail is what
    /// the reference's own <c>Clear()</c> leaves behind for it to write.
    /// </remarks>
    public static bool CanWrite(SpellEffect effect, out string reason)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.Scripts.Count > ScriptCount)
        {
            reason = $"A spell effect carries {effect.Scripts.Count} scripts where the record has " +
                     $"{ScriptCount} slots. The surplus has nowhere to go and would be dropped.";
            return false;
        }

        if (!DicePlusWriter.CanWrite(effect.ChangeData, out string dice))
        {
            reason = $"Spell effect '{effect.IndexKey}' has a changeData that cannot be written: " +
                     dice;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one effect.</summary>
    /// <exception cref="NotSupportedException">
    /// When the effect holds a shape that cannot go out — see <see cref="CanWrite"/>.
    /// </exception>
    public static void Write(IArchiveWriteCursor ar, SpellEffect effect)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(effect);

        if (!CanWrite(effect, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        WriteDas(ar, effect.IndexKey);
        ar.WriteUInt32(effect.Flags);
        ar.WriteDouble(effect.ChangeResult);           // 8 bytes among DWORDs
        WriteDas(ar, effect.String2);
        ar.WriteUInt32(effect.SourceOfEffect);
        ar.WriteUInt32(effect.Parent);

        // Nine slots whatever the list holds. An effect read below 0.910 has a short list, and the
        // reference writes its own empty members there -- so padding is exact, not a guess.
        for (int i = 0; i < ScriptCount; i++)
        {
            WriteDas(ar, i < effect.Scripts.Count ? effect.Scripts[i] : string.Empty);
        }

        ar.WriteUInt32(effect.StopTime);
        ar.WriteUInt32(effect.Data);

        // Outside the branch in the reference, and therefore outside it here.
        DicePlusWriter.Write(ar, effect.ChangeData);
    }

    private static void WriteDas(IArchiveWriteCursor ar, string value) =>
        ar.WriteString(ArchiveStringConventions.Encode(value));
}
