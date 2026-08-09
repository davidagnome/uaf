namespace UAF.Import.Frua;

/// <summary>
/// A level's compressed string table (<c>UAImportStrings</c>, <c>UAFWinEd/UAImport.cpp:1781</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>FRUA packs text at six bits per character</b>, which is where its all-capitals look comes
/// from. The reference's own comment is the specification: "three bytes being sufficient for four
/// characters… ASCII values of zero and 32-63 are used as is. Values in the range 1-31 have an
/// implicit '1' bit appended to them, shifting the range to 65-95."
/// </para>
/// <para>
/// So a design can write the digits, the space and the common punctuation (32–63) and the upper-case
/// letters (65–90), and nothing else. <b>There are no lower-case letters in the alphabet at all</b>
/// — every line of FRUA dialogue is capitals because the format cannot express anything else.
/// </para>
/// </remarks>
public sealed class FruaStringTable
{
    /// <summary>How many strings a level can hold.</summary>
    public const int Capacity = 400;

    /// <summary>Where the count byte sits in a level file, right after the STRG marker.</summary>
    public const int CountAt = 5799;

    /// <summary>Where the 400 per-string byte counts begin.</summary>
    public const int LengthsAt = 5800;

    /// <summary>
    /// Where the packed bits begin.
    /// </summary>
    /// <remarks>
    /// Confirmed by the reference's own assertion rather than by counting:
    /// <c>ASSERT((index + 6200) &lt; 12961)</c> in <c>GetStrPtr</c>.
    /// </remarks>
    public const int StringsAt = 6200;

    /// <summary>How many packed bytes a level file carries.</summary>
    /// <remarks>
    /// The struct declares <c>Strings[6761]</c> and the reader asks for <b>6,760</b>. The extra
    /// byte is never read or written, so the shorter figure is the real one.
    /// </remarks>
    public const int StringsLength = 6760;

    /// <summary>
    /// The longest string the reference will decode, in characters.
    /// </summary>
    /// <remarks>
    /// 228, and it is a decode-loop bound rather than a format limit — a string whose terminator is
    /// missing stops here instead of running through the whole block.
    /// </remarks>
    public const int MaxCharacters = 228;

    private readonly byte[] lengths;

    private readonly byte[] packed;

    private FruaStringTable(byte count, byte[] lengths, byte[] packed)
    {
        Count = count;
        this.lengths = lengths;
        this.packed = packed;
    }

    /// <summary>
    /// The table's own count byte.
    /// </summary>
    /// <remarks>
    /// <b>Its comment in the reference calls it "total compressed length of all strings" and that
    /// looks wrong</b> — the shipped values are 24 and 19 where the compressed blocks run to
    /// thousands of bytes. Nothing reads it: <c>GetStringAt</c> works entirely from
    /// <c>StringLength</c>, so whatever it means, no import depends on it. Carried across
    /// unexamined rather than reinterpreted.
    /// </remarks>
    public byte Count { get; }

    /// <summary>Reads the table out of a whole level file.</summary>
    public static FruaStringTable Read(ReadOnlySpan<byte> level)
    {
        if (level.Length < StringsAt + StringsLength)
        {
            throw new InvalidDataException(
                $"a level file needs {StringsAt + StringsLength} bytes to hold its strings; "
                + $"this one has {level.Length}");
        }

        return new FruaStringTable(
            level[CountAt],
            level.Slice(LengthsAt, Capacity).ToArray(),
            level.Slice(StringsAt, StringsLength).ToArray());
    }

    /// <summary>
    /// The string at a <b>one-based</b> index, or null when there is none
    /// (<c>GetStringAt</c>, <c>UAImport.cpp:1829</c>).
    /// </summary>
    /// <remarks>
    /// <b>Index 0 means "no string", not "the first string".</b> The reference returns false for
    /// it and for anything above 400, which is what lets an event store 0 to mean silence. The
    /// caller then subtracts one to reach the length table.
    /// </remarks>
    public string? Get(int index)
    {
        if (index <= 0 || index > Capacity)
        {
            return null;
        }

        string text = Decode(Start(index - 1));
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Where string <paramref name="zeroBased"/> begins, in bytes
    /// (<c>GetStrPtr</c>, <c>UAImport.cpp:1792</c>).
    /// </summary>
    /// <remarks>
    /// <b>There is no offset table — a string's position is the sum of every length before it</b>,
    /// so reaching the last string means walking all 400 entries. That is the reference's shape and
    /// it is cheap enough at this size; the lengths are compressed byte counts, not character
    /// counts.
    /// </remarks>
    private int Start(int zeroBased)
    {
        int at = 0;

        for (int i = 0; i < zeroBased; i++)
        {
            at += lengths[i];
        }

        return at;
    }

    /// <summary>
    /// Unpacks six-bit characters, most significant bit first, until a zero group or the bound.
    /// </summary>
    private string Decode(int at)
    {
        var text = new List<char>(MaxCharacters);
        int index = at;
        int bit = 7;
        int pattern = 0;
        int bits = 0;

        while (text.Count < MaxCharacters && index < packed.Length)
        {
            pattern <<= 1;
            if ((packed[index] & (1 << bit)) != 0)
            {
                pattern++;
            }

            bits++;

            if (bits == 6)
            {
                if (pattern == 0)
                {
                    break;
                }

                // 1..31 are the letters, reached by setting the bit the six cannot carry.
                text.Add((char)(pattern <= 31 ? pattern | 0x40 : pattern));
                pattern = 0;
                bits = 0;
            }

            if (bit == 0)
            {
                bit = 7;
                index++;
            }
            else
            {
                bit--;
            }
        }

        return new string([.. text]);
    }
}
