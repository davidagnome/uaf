using System.Globalization;

namespace UAF.Data;

/// <summary>
/// A design's <c>config.txt</c> — the key/value file that carries the screen layout, default art
/// and sound names, and several hundred tunables. Ported from <c>FileParse::ParseFile</c>
/// (<c>Shared/FileParse.cpp:486</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the file that says <i>where</i> things are drawn. <c>VIEWPORT_RECT = 48,54,224,265</c>,
/// <c>HORZ_BAR_TOP = 0,0</c>, <c>TEXTBOX = 18,328</c>: without it, a renderer has the art and the
/// blitter and no idea where anything goes. Several values are also <b>source</b> rectangles rather
/// than destinations — <c>HORZ_BAR_LONG = 0,0,640,14</c> selects the top 14 rows of a 640×42 bar
/// image, which holds three such strips stacked. Blitting whole art files instead is a mistake that
/// looks plausible until the unused padding shows up on screen.
/// </para>
/// <para>
/// A design ships several: <c>config.txt</c> plus <c>config640.txt</c>, <c>config800.txt</c> and
/// <c>config1024.txt</c>, one per resolution.
/// </para>
/// <para>
/// <b>Comments are only stripped at the start of a line.</b> <c>strncmp(buffer, "//", 2)</c>
/// skips the whole line, but a trailing <c>// bottom menu</c> after a value is left in the value
/// string and merely survives because <c>atol</c> stops at the first non-digit
/// (<c>FileParse.cpp:546</c>). That is why <see cref="TryGetInts"/> parses leading integers rather
/// than requiring the field to be numeric.
/// </para>
/// </remarks>
public sealed class DesignConfig
{
    private readonly List<KeyValuePair<string, string>> entries = [];
    private readonly HashSet<int> consumed = [];

    private DesignConfig()
    {
    }

    /// <summary>Every entry in file order, duplicates included.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Entries => entries;

    public int Count => entries.Count;

    public static DesignConfig Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var config = new DesignConfig();
        foreach (string raw in lines)
        {
            // A leading '$' line is skipped by the engine build (FileParse.cpp:539); the editor
            // falls through to parse it. This reader takes the engine's view.
            if (raw.StartsWith('$') || raw.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            int equals = raw.IndexOf('=');

            // A line with no '=' gets " = 0" appended, so a bare token becomes key -> "0"
            // (FileParse.cpp:551). Blank lines would otherwise become an empty key, so they go.
            if (equals < 0)
            {
                string bare = raw.Trim();
                if (bare.Length > 0)
                {
                    config.entries.Add(new(bare, "0"));
                }
                continue;
            }

            // The C++ truncates the key to (index of '=') - 1 characters, which strips the space in
            // "TOKEN = value" but eats the last letter of the key in "TOKEN=value". Trimming is
            // used here instead: it agrees on every well-formed line, and no shipped config omits
            // the space. Reproducing the truncation would only mean silently ignoring a setting a
            // future design wrote without one.
            string key = raw[..equals].Trim();
            string value = raw[(equals + 1)..].Trim();
            if (key.Length > 0)
            {
                config.entries.Add(new(key, value));
            }
        }

        return config;
    }

    public static DesignConfig Load(string path) => Parse(File.ReadLines(path));

    /// <summary>
    /// Finds a key's raw value. Matching is case-insensitive, as <c>CString::CompareNoCase</c>.
    /// </summary>
    /// <param name="consume">
    /// The original's <c>Remove</c> flag, defaulting true. A token found once is skipped by later
    /// lookups (<c>FileParse.cpp:112</c>), so repeated keys are handed out in file order rather
    /// than the first winning every time. Pass false to peek without consuming.
    /// </param>
    public bool TryGetValue(string key, out string value, bool consume = true)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (consumed.Contains(i) ||
                !string.Equals(entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = entries[i].Value;
            if (consume)
            {
                consumed.Add(i);
            }
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Reads a comma-separated integer list, as <c>FILE_PARSE::FindTokens</c>
    /// (<c>Shared/FileParse.cpp:181</c>).
    /// </summary>
    /// <remarks>
    /// Missing fields come back as zero, matching the original's <c>Val1=Val2=Val3=Val4=0</c>
    /// initialisation. The original also returns false outright when the value contains no comma at
    /// all, even if a single number was present — reproduced, because callers use the return value
    /// to decide whether to keep a built-in default.
    /// </remarks>
    public bool TryGetInts(string key, out int[] values, int count = 4, bool consume = true)
    {
        values = new int[count];

        if (!TryGetValue(key, out string raw, consume))
        {
            return false;
        }

        if (!raw.Contains(','))
        {
            return false;
        }

        string[] fields = raw.Split(',');
        for (int i = 0; i < count && i < fields.Length; i++)
        {
            values[i] = ParseLeadingInt(fields[i]);
        }

        return true;
    }

    /// <summary>Reads a two-value point.</summary>
    public bool TryGetPoint(string key, out int x, out int y, bool consume = true)
    {
        bool found = TryGetInts(key, out int[] values, 2, consume);
        x = values[0];
        y = values[1];
        return found;
    }

    /// <summary>Reads a four-value rectangle as left, top, right, bottom.</summary>
    public bool TryGetRect(string key, out int left, out int top, out int right, out int bottom,
                           bool consume = true)
    {
        bool found = TryGetInts(key, out int[] values, 4, consume);
        (left, top, right, bottom) = (values[0], values[1], values[2], values[3]);
        return found;
    }

    /// <summary>Reads a single string value, or a fallback when absent.</summary>
    public string GetString(string key, string fallback = "", bool consume = true) =>
        TryGetValue(key, out string value, consume) && value.Length > 0 ? value : fallback;

    /// <summary>Resets the consume markers, so the file can be read again from the start.</summary>
    public void Rewind() => consumed.Clear();

    /// <summary>
    /// <c>atol</c> semantics: an optional sign then digits, stopping at the first character that is
    /// neither, and yielding zero rather than throwing when there are none.
    /// </summary>
    /// <remarks>
    /// This is what lets <c>16,460 // bottom menu</c> parse as 16 and 460: the trailing comment
    /// rides along on the last field and simply stops the scan.
    /// </remarks>
    private static int ParseLeadingInt(string field)
    {
        ReadOnlySpan<char> span = field.AsSpan().TrimStart();
        int end = 0;

        if (end < span.Length && (span[end] == '-' || span[end] == '+'))
        {
            end++;
        }

        int digitsStart = end;
        while (end < span.Length && char.IsAsciiDigit(span[end]))
        {
            end++;
        }

        return end == digitsStart
            ? 0
            : int.TryParse(span[..end], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                           out int parsed) ? parsed : 0;
    }
}
