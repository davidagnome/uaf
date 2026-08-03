namespace UAF.Common;

/// <summary>
/// The self-delimiting list convention used inside ASL values
/// (<c>SUBSTRINGS</c>, <c>Shared/ASL.cpp:242</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The first character of the string is the delimiter.</b> There is no fixed separator and no
/// escaping: a value beginning <c>|</c> is a <c>|</c>-delimited list, one beginning <c>/</c> is a
/// <c>/</c>-delimited list. That is what lets a design nest one list inside another — the outer
/// list picks a character the inner one does not use — and it is why these look like paths without
/// being paths.
/// </para>
/// <para>
/// A design's <c>WHO_TRIES</c> attempt hook is the clearest example: its ASL value is a list of
/// fields, and each field is itself a list of a script name and two parameters. Reading it needs
/// both operations here, applied in that order.
/// </para>
/// </remarks>
public static class Substrings
{
    /// <summary>
    /// Splits off the first element and everything after it
    /// (<c>SUBSTRINGS::HeadAndTail</c>, <c>ASL.cpp:242</c>).
    /// </summary>
    /// <returns>False when there is nothing to split — fewer than two characters.</returns>
    /// <remarks>
    /// <b>The head loses its leading delimiter and the tail keeps its own.</b> So
    /// <c>/name/rest</c> yields a head of <c>name</c> and a tail of <c>/rest</c>, which is what
    /// lets the tail be split again with no further bookkeeping. A caller wanting the tail's text
    /// rather than a further list has to drop that character itself, and the reference does
    /// exactly that with <c>Right(len - 1)</c>.
    /// </remarks>
    public static bool HeadAndTail(string value, out string head, out string tail)
    {
        ArgumentNullException.ThrowIfNull(value);

        head = string.Empty;
        tail = string.Empty;

        if (value.Length < 2)
        {
            return false;
        }

        char delimiter = value[0];
        int at = value.IndexOf(delimiter, 1);
        if (at < 0)
        {
            at = value.Length;
        }

        head = value[1..at];
        tail = value[at..];
        return true;
    }

    /// <summary>
    /// Reads the next element, advancing <paramref name="column"/>
    /// (<c>SUBSTRINGS::NextField</c>, <c>ASL.cpp:264</c>).
    /// </summary>
    /// <returns>False at the end of the list.</returns>
    /// <remarks>
    /// <para>
    /// <b>The delimiter is re-read from the current position every call</b>, not remembered — so a
    /// list may in principle change delimiter part-way through, and the closing delimiter of one
    /// field is the opening delimiter of the next.
    /// </para>
    /// <para>
    /// <b>The guard is <c>column >= length - 1</c>, not <c>>= length</c>.</b> A single trailing
    /// character cannot start a field, so a list ending in its delimiter stops there rather than
    /// yielding an empty final element.
    /// </para>
    /// </remarks>
    public static bool NextField(string value, ref int column, out string field)
    {
        ArgumentNullException.ThrowIfNull(value);

        field = string.Empty;

        if (column < 0)
        {
            column = 0;
        }

        if (column >= value.Length - 1)
        {
            return false;
        }

        char delimiter = value[column];
        int at = value.IndexOf(delimiter, column + 1);
        if (at < 0)
        {
            at = value.Length;
        }

        field = value[(column + 1)..at];
        column = at;
        return true;
    }

    /// <summary>Every element of a list, in order.</summary>
    public static IEnumerable<string> Fields(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int column = 0;
        while (NextField(value, ref column, out string field))
        {
            yield return field;
        }
    }
}
