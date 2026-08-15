namespace UAF.Scripting;

/// <summary>
/// A string that carries its own delimiter (<c>STR_SUM</c>, <c>Shared/Globals.cpp:5357</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The first character IS the delimiter.</b> There is no separate argument saying how the string
/// is split — <c>"|a|b|c"</c> holds three fields separated by <c>|</c>, and <c>",a,b"</c> holds two
/// separated by a comma. So two strings can only be compared field-by-field if they were written
/// with the same leading character, and nothing checks that they were.
/// </para>
/// <para>
/// The same convention appears in <c>$GET_SPELLBOOK</c>, where the separators are passed in
/// instead — these are two different answers to the same problem in one engine.
/// </para>
/// </remarks>
public static class GpdlDelimitedString
{
    /// <summary>
    /// The fields of a delimited string, in order.
    /// </summary>
    /// <remarks>
    /// <b>A trailing delimiter produces an empty last field</b>, because the split is driven by
    /// the separators rather than by the content: <c>"|a|"</c> is <c>["a", ""]</c>, not
    /// <c>["a"]</c>. An empty string has no fields at all — there is not even a delimiter to read.
    /// </remarks>
    public static List<string> Fields(string text)
    {
        var fields = new List<string>();

        if (string.IsNullOrEmpty(text))
        {
            return fields;
        }

        char delimiter = text[0];
        int col = 1;

        while (true)
        {
            int start = col;
            while (col < text.Length && text[col] != delimiter)
            {
                col++;
            }

            fields.Add(text[start..col]);

            if (col >= text.Length)
            {
                return fields;
            }

            // Step over the delimiter and begin the next field.
            col++;
        }
    }

    /// <summary>
    /// <c>$DelimitedStringFilter(source, filter, function)</c>
    /// (<c>Shared/Globals.cpp:5393</c>).
    /// </summary>
    /// <param name="source">
    /// The string to filter. Its first character is the delimiter, and the result is written with
    /// that same character — so the answer can be filtered again.
    /// </param>
    /// <param name="filter">
    /// The fields to remove, delimited by <b>its own</b> first character. The two strings need not
    /// agree on a delimiter and nothing checks that they do.
    /// </param>
    /// <param name="function">
    /// <c>"AndNot"</c> is the only operation there is.
    /// <b>Anything else is echoed back as the result</b> — the reference's last line is
    /// <c>return function;</c>, so <c>$DelimitedStringFilter("|a", "|b", "Or")</c> answers
    /// <c>"Or"</c>. Not an error code a script could distinguish from data.
    /// </param>
    /// <returns>
    /// The source's fields that do not appear in the filter, delimited as the source was. An empty
    /// source answers empty <b>whatever the function is</b> — the length check comes first, so the
    /// echo above does not happen for one.
    /// </returns>
    public static string Filter(string source, string filter, string function)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source ?? string.Empty;
        }

        if (function != AndNot)
        {
            return function ?? string.Empty;
        }

        var remove = Fields(filter);
        char delimiter = source[0];
        var result = new System.Text.StringBuilder();

        foreach (string field in Fields(source))
        {
            // Ordinal, and case-sensitive: the reference compares with memcmp.
            if (!remove.Contains(field, StringComparer.Ordinal))
            {
                result.Append(delimiter).Append(field);
            }
        }

        return result.ToString();
    }

    /// <summary>The one operation <see cref="Filter"/> recognises.</summary>
    /// <remarks>
    /// <b>Case-sensitive.</b> <c>"andnot"</c> is not it, and a script writing that gets
    /// <c>"andnot"</c> back as the answer rather than a filtered string.
    /// </remarks>
    public const string AndNot = "AndNot";
}
