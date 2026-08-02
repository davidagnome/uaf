using UAF.Common;

namespace UAF.Serialization;

/// <summary>
/// Writes the special-abilities block that precedes every record's ASL
/// (<c>SPECIAL_ABILITIES::Serialize</c>, <c>Specab.cpp:1153</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is only one write path, whatever the version.</b> The reference's legacy branch is
/// gated on <c>version &lt;= 0.920 <i>&amp;&amp; !ar.IsStoring()</i></b> — reading only — so an old
/// design is read in the old shape and written back in the new one. Mirroring the reader's fork
/// here would produce files the reference cannot read; see <see cref="SpecabReader"/>, whose
/// remarks call this out as the trap it is.
/// </para>
/// <para>
/// The whole legacy branch is additionally inside <c>#ifdef UAFEDITOR</c>, so the engine never even
/// reads it. Writing the modern <c>A_CStringPAIR_L</c> unconditionally is therefore not a
/// simplification — it is the only behaviour the format has.
/// </para>
/// </remarks>
public static class SpecabWriter
{
    /// <summary>
    /// Writes a block in the modern pair form.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// When the block holds legacy slots or ordinals. See the remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Two contrasts with the sibling ASL block</b>, which is easy to conflate with this since
    /// both live in <c>ASL.cpp</c>: the count is a 32-bit <c>int</c> where ASL uses a <c>WORD</c>,
    /// and the strings are written <b>verbatim</b> where ASL's legacy path wraps them in the
    /// <c>DAS</c> blank convention. There is no map-name marker and no flags byte either, so a
    /// desynchronised stream has nothing here to announce itself with.
    /// </para>
    /// <para>
    /// <b>A block read from a pre-0.921 design cannot be written yet, and refusing is deliberate.</b>
    /// The reference converts legacy slots into modern pairs as it reads them
    /// (<c>Specab.cpp:1196</c>); this port keeps the legacy shape unconverted, because consuming
    /// the right number of bytes is what lets the stream advance and discarding the contents would
    /// make the conversion unverifiable. Until that conversion is ported there is no honest
    /// modern form to write — and emitting an empty block instead would produce a file that reads
    /// back cleanly with every special ability silently gone.
    /// </para>
    /// </remarks>
    public static void Write(MfcArchiveWriter ar, SpecabBlock block)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(block);

        if (block.LegacySlots.Count > 0 || block.LegacyOrdinals.Count > 0)
        {
            throw new NotSupportedException(
                "This special-abilities block was read from a design at or below 0.920 and is " +
                "still in the legacy shape. Writing it needs the legacy-to-modern conversion " +
                "(Specab.cpp:1196), which is not ported. Writing an empty block instead would " +
                "lose every ability without any sign that it had.");
        }

        WritePairs(ar, block.Pairs);
    }

    /// <summary>Writes an <c>A_CStringPAIR_L</c> (<c>ASL.cpp:1848</c>).</summary>
    public static void WritePairs(MfcArchiveWriter ar, IEnumerable<SpecabPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(pairs);

        var list = pairs as IReadOnlyList<SpecabPair> ?? [.. pairs];

        ar.WriteInt32(list.Count);              // int, not WORD

        foreach (var pair in list)
        {
            // Verbatim: no DAS here, so an empty value stays empty and "*" stays "*".
            ar.WriteString(pair.Key);
            ar.WriteString(pair.Value);
        }
    }

    /// <summary>
    /// Whether a block can be written as it stands.
    /// </summary>
    /// <remarks>
    /// A caller walking a whole database will want to know before it starts, rather than part-way
    /// through a file it has already begun.
    /// </remarks>
    public static bool CanWrite(SpecabBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.LegacySlots.Count == 0 && block.LegacyOrdinals.Count == 0;
    }
}
