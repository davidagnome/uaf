namespace UAF.Serialization;

/// <summary>
/// A reference into one of the databases, by name for the editor and by key for the runtime
/// (<c>class.cpp:1138</c>).
/// </summary>
public sealed record GenericReference(string RefName, sbyte RefType, int RefKey);

/// <summary>
/// One adjustment term of a <see cref="DicePlus"/> expression (<c>class.cpp:1773</c>).
/// </summary>
public sealed record Adjustment(
    short[] Parameters, sbyte[] Operators, GenericReference Reference);

/// <summary>
/// A dice expression: <c>NdS+B</c> with optional clamps and adjustment terms.
/// </summary>
/// <param name="Tag">The form tag actually on the wire — <c>DP0</c>, <c>DP1</c> or <c>DP2</c>.</param>
/// <param name="Text">The expression source. The only payload in the modern <c>DP2</c> form.</param>
/// <param name="Binary">Compiled form; the reference clears it on load to force a recompile.</param>
public sealed record DicePlus(
    string Tag, string Text, string Binary,
    sbyte NumDice, byte NumSides, sbyte Bonus, int Min, int Max, sbyte Sign,
    IReadOnlyList<Adjustment> Adjustments);

/// <summary>
/// Reads <c>DICEPLUS</c> (<c>class.cpp:2494</c>) and the two structures it contains.
/// </summary>
/// <remarks>
/// <para>
/// <b>Self-versioning by string tag.</b> Like the tagged databases, and unlike everything else,
/// a <c>DICEPLUS</c> opens with a string naming its own layout — the surrounding
/// <c>DesignVersion</c> does not select the branch. An unrecognised tag is a soft failure in the
/// reference (it logs and returns non-zero) rather than a throw.
/// </para>
/// <para>
/// <b>The three forms are structurally unrelated.</b> <c>DP2</c> is two strings and nothing else;
/// <c>DP0</c> and <c>DP1</c> are packed numeric fields plus a list of adjustment terms. A reader
/// that assumes the numeric layout will desynchronise badly on a modern design, where every dice
/// expression is <c>DP2</c>.
/// </para>
/// <para>
/// <b>The numeric fields are one byte, not four.</b> <c>m_numDice</c>, <c>m_bonus</c> and
/// <c>m_sign</c> are declared <c>char</c> and <c>m_numSides</c> is a <c>BYTE</c>
/// (<c>class.h:842-847</c>), even though the names read like integers. Only <c>m_min</c> and
/// <c>m_max</c> are <c>int</c> — and in <c>DP0</c> even those are written as <c>BYTE</c> and
/// widened after reading.
/// </para>
/// </remarks>
public static class DicePlusReader
{
    public const string TagLegacy = "DP0";
    public const string TagPacked = "DP1";
    public const string TagText = "DP2";

    /// <summary>True for the modern text form, which carries no numeric fields at all.</summary>
    public static bool IsTextForm(string tag) => tag == TagText;

    public static DicePlus Read(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        string tag = ar.ReadString();
        return tag switch
        {
            TagText => ReadTextForm(ar, tag),
            TagPacked => ReadPacked(ar, tag),
            TagLegacy => ReadLegacy(ar, tag),
            _ => throw new InvalidDataException(
                $"Unknown DICEPLUS format '{tag}'. Expected {TagLegacy}, {TagPacked} or {TagText}; " +
                "an unrecognised tag here almost always means the stream is misaligned."),
        };
    }

    /// <summary>
    /// <c>DP2</c> (<c>class.cpp:2576</c>) — the expression as text, plus its compiled form.
    /// </summary>
    private static DicePlus ReadTextForm(IArchiveCursor ar, string tag)
    {
        string text = ArchiveStringConventions.Decode(ar.ReadString());

        // The reference reads m_Bin and then immediately clears it to force a recompile
        // (class.cpp:2594). Kept here rather than discarded: it is on the wire, and a writer
        // has to put something back.
        string binary = ArchiveStringConventions.Decode(ar.ReadString());

        return new DicePlus(tag, text, binary, 0, 0, 0, 0, 0, 0, []);
    }

    /// <summary><c>DP1</c> (<c>class.cpp:2553</c>) — packed fields with 32-bit clamps.</summary>
    private static DicePlus ReadPacked(IArchiveCursor ar, string tag)
    {
        sbyte numDice = (sbyte)ar.ReadByte();       // char -- signed, and can be negative
        byte numSides = ar.ReadByte();
        sbyte bonus = (sbyte)ar.ReadByte();
        int min = ar.ReadInt32();
        int max = ar.ReadInt32();
        sbyte sign = (sbyte)ar.ReadByte();

        var adjustments = ReadAdjustments(ar);
        return Normalise(tag, numDice, numSides, bonus, min, max, sign, adjustments);
    }

    /// <summary><c>DP0</c> (<c>class.cpp:2530</c>) — clamps stored as bytes and widened.</summary>
    private static DicePlus ReadLegacy(IArchiveCursor ar, string tag)
    {
        sbyte numDice = (sbyte)ar.ReadByte();
        byte numSides = ar.ReadByte();
        sbyte bonus = (sbyte)ar.ReadByte();
        int min = ar.ReadByte();                    // BYTE on the wire, int in the struct
        int max = ar.ReadByte();

        var adjustments = ReadAdjustments(ar);

        // DP0 predates m_sign, so it is derived below rather than read.
        return Normalise(tag, numDice, numSides, bonus, min, max, 0, adjustments);
    }

    /// <summary>
    /// Applies the sign normalisation both numeric forms perform after reading
    /// (<c>class.cpp:2546</c>): a negative dice count becomes a positive count with sign -1.
    /// </summary>
    private static DicePlus Normalise(
        string tag, sbyte numDice, byte numSides, sbyte bonus, int min, int max, sbyte sign,
        IReadOnlyList<Adjustment> adjustments)
    {
        if (numDice < 0)
        {
            sign = -1;
            numDice = (sbyte)Math.Abs(numDice);
        }
        return new DicePlus(tag, string.Empty, string.Empty,
                            numDice, numSides, bonus, min, max, sign, adjustments);
    }

    private static List<Adjustment> ReadAdjustments(IArchiveCursor ar)
    {
        // ReadCount, not a plain int: 2 bytes in a tier-2 archive, 4 in a tier-3 one.
        uint count = ar.ReadCount();

        var adjustments = new List<Adjustment>((int)Math.Min(count, 256));
        for (uint i = 0; i < count; i++)
        {
            adjustments.Add(ReadAdjustment(ar));
        }
        return adjustments;
    }

    /// <summary>Reads one <c>ADJUSTMENT</c> (<c>class.cpp:1773</c>).</summary>
    public static Adjustment ReadAdjustment(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        // MAX_ADJ is 3: three shorts then three chars, so 6 bytes then 3, not 12 then 12.
        short[] parameters = [(short)ar.ReadUInt16(), (short)ar.ReadUInt16(), (short)ar.ReadUInt16()];
        sbyte[] operators = [(sbyte)ar.ReadByte(), (sbyte)ar.ReadByte(), (sbyte)ar.ReadByte()];

        return new Adjustment(parameters, operators, ReadReference(ar));
    }

    /// <summary>Reads a <c>GENERIC_REFERENCE</c> (<c>class.cpp:1138</c>).</summary>
    public static GenericReference ReadReference(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        // Note this decodes "*" inline rather than calling DAS -- same effect, since the blank
        // sentinel IS "*", but it does not consult ArchiveBlank.
        string refName = ArchiveStringConventions.Decode(ar.ReadString());
        sbyte refType = (sbyte)ar.ReadByte();       // char -- one byte, not an enum-sized int
        int refKey = ar.ReadInt32();

        return new GenericReference(refName, refType, refKey);
    }
}

/// <summary>
/// Reads a <c>BASECLASS_LIST</c> (<c>class.cpp:7090</c>): a count then that many names.
/// </summary>
/// <remarks>
/// The names are <c>BASECLASS_ID</c>, which derives from <c>CString</c> — so these are strings, not
/// ordinals. Identical in shape to the inline list inside <c>ITEM_DATA</c>.
/// </remarks>
public static class BaseclassListReader
{
    public static List<string> Read(IArchiveCursor ar)
    {
        ArgumentNullException.ThrowIfNull(ar);

        int count = ar.ReadInt32();                 // a plain int here, NOT ReadCount
        var names = new List<string>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            names.Add(ar.ReadString());
        }
        return names;
    }
}
