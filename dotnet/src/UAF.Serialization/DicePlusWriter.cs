namespace UAF.Serialization;

/// <summary>
/// Writes <c>DICEPLUS</c> (<c>class.cpp:2494</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is only one form on the write side, and it is <c>DP2</c>.</b> The storing branch writes
/// the tag and two strings; the whole numeric path — dice, sides, bonus, clamps, sign and the
/// adjustment list — is <i>commented out</i> beneath it (<c>class.cpp:2505</c>). So <c>DP0</c> and
/// <c>DP1</c> are shapes the reference can read and has never been able to produce, and neither
/// <c>ADJUSTMENT</c> nor <c>GENERIC_REFERENCE</c> has a reachable writer at all. This is the same
/// rule as <see cref="SpecabWriter"/>'s and <see cref="MonsterRecordWriter"/>'s, arrived at from a
/// third direction: the loading branch understands more shapes than the storing one emits.
/// </para>
/// <para>
/// <b>The compiled form is written back as it was read.</b> The reference clears <c>m_Bin</c> on
/// load to force a recompile (<c>class.cpp:2589</c>), so a file it wrote holds an empty binary and
/// the two agree; <see cref="DicePlusReader"/> keeps the field precisely so this writer has
/// something to put back. An editor that edits <see cref="DicePlus.Text"/> must clear
/// <see cref="DicePlus.Binary"/> beside it, or the pair goes out inconsistent.
/// </para>
/// </remarks>
public static class DicePlusWriter
{
    /// <summary>
    /// Whether an expression can be written as it stands, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// A <c>DP0</c> or <c>DP1</c> read by this port has <b>no text</b>: the reference synthesises
    /// one from the numeric fields as it loads (<c>EncodeOldDicePlusText</c>), and that conversion
    /// is unported. Writing such a record as <c>DP2</c> anyway would emit an empty expression — a
    /// file that reads back cleanly with every legacy dice roll silently gone.
    /// </remarks>
    public static bool CanWrite(DicePlus dice, out string reason)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (!DicePlusReader.IsTextForm(dice.Tag))
        {
            reason = $"A DICEPLUS in the {dice.Tag} form carries packed numeric fields, and only " +
                     $"{DicePlusReader.TagText} can be written. Converting needs " +
                     "EncodeOldDicePlusText (class.cpp), which is unported; writing it as " +
                     $"{DicePlusReader.TagText} would emit an empty expression.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Writes one expression.</summary>
    /// <exception cref="NotSupportedException">
    /// When the expression is still in a numeric form — see <see cref="CanWrite"/>.
    /// </exception>
    public static void Write(IArchiveWriteCursor ar, DicePlus dice)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(dice);

        if (!CanWrite(dice, out string reason))
        {
            throw new NotSupportedException(reason);
        }

        // Verbatim, not DAS: the reference writes the tag with a plain `car <<`, and "DP2" is
        // never empty so the blank sentinel never comes into it.
        ar.WriteString(DicePlusReader.TagText);

        ar.WriteString(ArchiveStringConventions.Encode(dice.Text));
        ar.WriteString(ArchiveStringConventions.Encode(dice.Binary));
    }

    /// <summary>The expression the reference's default-constructed <c>DICEPLUS</c> writes as.</summary>
    /// <remarks>
    /// Both strings empty, so both go out as the blank sentinel. Used where a record's member is
    /// absent because the reader's version gate skipped it — the reference has a
    /// default-constructed one to write there, which is not the same thing as the port having lost
    /// an expression.
    /// </remarks>
    public static DicePlus Empty { get; } =
        new(DicePlusReader.TagText, string.Empty, string.Empty, 0, 0, 0, 0, 0, 0, []);
}

/// <summary>
/// Writes a <c>BASECLASS_LIST</c> (<c>class.cpp:7090</c>): a count then that many names.
/// </summary>
/// <remarks>
/// <b>The count is a plain <c>int</c>, not <c>WriteCount</c></b> — four bytes at every tier, where
/// a collection count would be two in a plain archive. The same shape as the inline list inside
/// <c>ITEM_DATA</c>, and the names are <c>BASECLASS_ID</c>, which derives from <c>CString</c>, so
/// they go out verbatim rather than through the blank sentinel.
/// <para>
/// The <c>CArchive</c> overload is <c>die("BASECLASS_LIST Serialize(CArchive&amp;)")</c>
/// (<c>class.cpp:7115</c>) — this list only ever travels through <c>CAR</c>.
/// </para>
/// </remarks>
public static class BaseclassListWriter
{
    public static void Write(IArchiveWriteCursor ar, IReadOnlyList<string> baseclasses)
    {
        ArgumentNullException.ThrowIfNull(ar);
        ArgumentNullException.ThrowIfNull(baseclasses);

        ar.WriteInt32(baseclasses.Count);
        foreach (string baseclass in baseclasses)
        {
            ar.WriteString(baseclass);
        }
    }
}
