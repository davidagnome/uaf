namespace UAFcore;

/// <summary>
/// The two art screens of the character generator (<c>GETCHARICON_MENU_DATA</c> and
/// <c>GETCHARSMALLPIC_MENU_DATA</c>, <c>RunEvent.cpp:3362</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One screen, two directories.</b> Both scan a fixed naming series, show one picture at a
/// time, and offer <c>NEXT</c>, <c>PREV</c> and <c>SELECT</c> — the only differences are the
/// folder, the file-name pattern and which field the answer lands in.
/// </para>
/// <para>
/// <b>The extension search is editor-only.</b> <c>FindImageWithValidExt</c> reads as though it
/// tries every image format for a root name, and under <c>UAFEngine</c> it does not: it checks the
/// exact file and returns FALSE (<c>Globals.cpp:3714</c>). So at play time the series really is
/// literal <c>.png</c>, and a design that shipped its portraits as <c>.pcx</c> shows a player
/// nothing while showing the designer everything.
/// </para>
/// </remarks>
public static class ArtPicker
{
    /// <summary>How far the series is scanned — <c>while (i &lt;= 50)</c>.</summary>
    public const int SeriesLength = 50;

    /// <summary>The small-portrait series (<c>prt_SPic1.png</c> …).</summary>
    public const string SmallPicturePattern = "prt_SPic{0}.png";

    /// <summary>The combat-icon series (<c>cn_Icon1.png</c> …).</summary>
    public const string IconPattern = "cn_Icon{0}.png";

    /// <summary>The menu both screens show.</summary>
    public static readonly (string Label, int Shortcut)[] Menu =
        [("NEXT", 0), ("PREV", 0), ("SELECT", 0)];

    public const int Next = 0;
    public const int Previous = 1;
    public const int Select = 2;

    /// <summary>
    /// The pictures a directory actually has, in series order.
    /// </summary>
    /// <remarks>
    /// <b>The series is scanned, not listed.</b> The engine asks for numbers 1 to 50 by name
    /// rather than enumerating the folder, so a portrait called anything else is invisible however
    /// well formed it is — and a gap in the numbering is skipped rather than ending the scan.
    /// </remarks>
    public static List<string> Available(string? directory, string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var found = new List<string>();
        if (directory is null || !Directory.Exists(directory))
        {
            return found;
        }

        for (int i = 1; i <= SeriesLength; i++)
        {
            string name = string.Format(pattern, i);
            if (File.Exists(Path.Combine(directory, name)))
            {
                found.Add(name);
            }
        }

        return found;
    }

    /// <summary>
    /// Steps the selection, wrapping at both ends.
    /// </summary>
    /// <remarks>
    /// <b>Both directions wrap</b> — <c>NEXT</c> off the end returns to 0 and <c>PREV</c> off the
    /// front goes to the last. That is the opposite of the roster and the inventory, which stop;
    /// this screen is a carousel because there is only ever one picture on it.
    /// </remarks>
    public static int Step(int selected, int count, int delta)
    {
        if (count <= 0)
        {
            return 0;
        }

        return ((selected + delta) % count + count) % count;
    }

    /// <summary>
    /// Whether the paging entries are selectable (<c>OnUpdateUI</c>, <c>RunEvent.cpp:3434</c>).
    /// </summary>
    /// <remarks>
    /// <b>One picture darkens both.</b> The test is <c>numSmallPics &lt;= 1</c>, so a design with
    /// exactly one portrait offers only SELECT — and one with none offers only SELECT too, over
    /// an empty screen.
    /// </remarks>
    public static bool CanStep(int count) => count > 1;
}
