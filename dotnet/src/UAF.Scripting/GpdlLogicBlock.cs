namespace UAF.Scripting;

/// <summary>
/// The twelve values a logic block leaves behind for scripts to read
/// (<c>RecordLogicBlockValues</c>, <c>UAFWin/RunEvent.cpp:14333</c>).
/// </summary>
/// <remarks>
/// <para>
/// A logic block matches the player's typed words and records what it captured. The captures are
/// packed into a single global attribute — <b>exactly twelve of them</b>, each a four-byte length
/// followed by that many characters — and <c>$LOGIC_BLOCK_VALUE("A")</c> through <c>"L"</c> read
/// them back out.
/// </para>
/// <para>
/// <b>Twelve is fixed, not a maximum.</b> The writer always emits twelve records; an unused one is
/// written with length zero rather than left out. So the blob is self-describing only in its
/// lengths, and a reader has to trust the count.
/// </para>
/// </remarks>
public static class GpdlLogicBlock
{
    /// <summary>How many values a logic block records.</summary>
    public const int Count = 12;

    /// <summary>The first letter, and the one that selects record zero.</summary>
    public const char FirstLetter = 'A';

    /// <summary>
    /// One value out of the packed blob.
    /// </summary>
    /// <param name="values">The global attribute's contents.</param>
    /// <param name="letter">
    /// <c>"A"</c>–<c>"L"</c>. Only the first character is looked at, so <c>"Beta"</c> selects the
    /// same record as <c>"B"</c>.
    /// </param>
    /// <returns>The value, or empty for a letter outside the range or a blob too short to hold it.</returns>
    /// <remarks>
    /// <para>
    /// <b>The reference's version of this loop cannot work, and the port implements the intent
    /// instead.</b> Its selector variable <c>k</c> holds the letter's ordinal <i>and</i> is
    /// reassigned to each record's length inside the loop (<c>GPDLexec.cpp:4630</c>) — so the loop
    /// bound changes as it runs, and the match test <c>if (i != k)</c> compares the loop counter
    /// against a byte length rather than against the requested index. A value comes back only when
    /// a record's length happens to equal its position, and the loop does not stop when it does.
    /// </para>
    /// <para>
    /// Nothing could depend on that, and the intent is unambiguous from the writer: twelve
    /// length-prefixed records, the letter picks one. That is what this does.
    /// </para>
    /// </remarks>
    public static string Value(string values, string letter)
    {
        if (string.IsNullOrEmpty(values) || string.IsNullOrEmpty(letter))
        {
            return string.Empty;
        }

        int wanted = letter[0] - FirstLetter;

        if (wanted < 0 || wanted >= Count)
        {
            return string.Empty;
        }

        int at = 0;

        for (int i = 0; i <= wanted; i++)
        {
            if (at + sizeof(int) > values.Length)
            {
                return string.Empty;
            }

            int length = ReadLength(values, at);
            at += sizeof(int);

            if (length < 0 || at + length > values.Length)
            {
                return string.Empty;
            }

            if (i == wanted)
            {
                return values.Substring(at, length);
            }

            at += length;
        }

        return string.Empty;
    }

    /// <summary>
    /// Packs values the way the logic-block writer does, for tests and for a host that has to
    /// produce the blob.
    /// </summary>
    /// <remarks>
    /// <b>Always twelve records.</b> Fewer values than that are padded with empty ones and extra
    /// ones are dropped, because a reader counts on the twelve being there.
    /// </remarks>
    public static string Pack(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var padded = values.Take(Count).ToList();
        while (padded.Count < Count)
        {
            padded.Add(string.Empty);
        }

        var packed = new System.Text.StringBuilder();

        foreach (string value in padded)
        {
            WriteLength(packed, value.Length);
            packed.Append(value);
        }

        return packed.ToString();
    }

    /// <summary>
    /// A four-byte little-endian length, one character per byte.
    /// </summary>
    /// <remarks>
    /// <b>The blob is bytes held in a string</b>, because the reference stores it in a
    /// <c>CString</c> and reads the length with a pointer cast. Each character here is one byte of
    /// the original, so a length above 255 spans several characters exactly as it does there.
    /// </remarks>
    private static int ReadLength(string values, int at) =>
        (values[at] & 0xFF)
        | ((values[at + 1] & 0xFF) << 8)
        | ((values[at + 2] & 0xFF) << 16)
        | ((values[at + 3] & 0xFF) << 24);

    /// <inheritdoc cref="ReadLength"/>
    private static void WriteLength(System.Text.StringBuilder packed, int length)
    {
        packed.Append((char)(length & 0xFF))
              .Append((char)((length >> 8) & 0xFF))
              .Append((char)((length >> 16) & 0xFF))
              .Append((char)((length >> 24) & 0xFF));
    }
}
