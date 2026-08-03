namespace UAF.Serialization;

/// <summary>
/// A list packed into one attribute value (<c>DELIMITED_STRING</c>, <c>ASL.cpp:102</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Length-prefixed, not separated.</b> Each element is a decimal count, a full stop, then
/// exactly that many characters: <c>5.Dwarf3.Elf</c> is <c>["Dwarf", "Elf"]</c>. So an element may
/// contain any character at all, including a full stop or a digit — which is the point, since
/// these hold names a designer typed.
/// </para>
/// <para>
/// <b>Empty is legal and contains nothing.</b> <c>IsLegal</c> answers true for an empty string
/// while <c>Contains</c> answers false, and callers lean on the difference: a race with no
/// <c>AllowedClass</c> attribute allows every class, and a race with an <i>empty</i> one allows
/// none. Absent and empty are opposite answers.
/// </para>
/// </remarks>
public static class DelimitedString
{
    /// <summary>
    /// Whether a value is a well-formed delimited string (<c>IsLegal</c>).
    /// </summary>
    /// <remarks>
    /// A malformed value is not an error to its callers — they treat it as "no restriction" — so
    /// this answers rather than throws.
    /// </remarks>
    public static bool IsLegal(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        int at = 0;
        while (at < value.Length)
        {
            int length = 0;
            while (value[at] != '.')
            {
                if (value[at] < '0' || value[at] > '9')
                {
                    return false;
                }

                length = (10 * length) + (value[at] - '0');
                at++;

                if (at >= value.Length)
                {
                    return false;
                }
            }

            at += length + 1;         // past the element, and past the full stop
        }

        return at <= value.Length;
    }

    /// <summary>The elements, or an empty list when the value is empty or malformed.</summary>
    public static List<string> Parse(string? value)
    {
        var elements = new List<string>();
        if (string.IsNullOrEmpty(value) || !IsLegal(value))
        {
            return elements;
        }

        int at = 0;
        while (at < value.Length)
        {
            int length = 0;
            while (value[at] != '.')
            {
                length = (10 * length) + (value[at] - '0');
                at++;
            }

            at++;                     // the full stop
            elements.Add(value.Substring(at, Math.Min(length, value.Length - at)));
            at += length;
        }

        return elements;
    }

    /// <summary>Whether the list names <paramref name="element"/> (<c>Contains</c>).</summary>
    /// <remarks>
    /// <b>An empty list contains nothing</b>, where <see cref="IsLegal"/> calls it fine — see the
    /// remarks on the class.
    /// </remarks>
    public static bool Contains(string? value, string element) =>
        Parse(value).Contains(element, StringComparer.Ordinal);

    /// <summary>Packs elements back into one value.</summary>
    public static string Format(IEnumerable<string> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        return string.Concat(elements.Select(e => $"{e.Length}.{e}"));
    }
}
