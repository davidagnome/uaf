using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// The party's journal, as one block of text (<c>FormatJournalText</c>,
/// <c>FormattedText.cpp:1201</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every entry is concatenated into a single passage and then wrapped.</b> The screen pages
/// over <i>lines</i>, not over entries, so a long entry spans boxes and a box can hold the end of
/// one entry and the start of the next.
/// </para>
/// <para>
/// <b>The separator carries a colour reset.</b> <c>"\b\n\n"</c> — the <c>\b</c> is the journal's
/// own tag (<c>CheckJournalColorTag</c>, <c>:343</c>), which clears any colour the previous entry
/// left set, and the two newlines are the blank line between entries. It is stripped before
/// drawing, so it costs no width.
/// </para>
/// </remarks>
public static class JournalScreen
{
    /// <summary>What separates one entry from the next.</summary>
    public const string Separator = "\b\n\n";

    /// <summary>
    /// Joins the journal into the text the screen shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Empty entries are skipped but still counted against the total.</b> The separator goes on
    /// while <c>count &lt; jdata.GetCount()</c>, where <c>count</c> only advances for entries that
    /// had text and <c>GetCount()</c> is the whole list — so a journal ending in empty entries
    /// puts a separator after its last real one, leaving a trailing blank line.
    /// </para>
    /// <para>
    /// <b>An empty journal produces nothing at all</b>, not an empty line: <c>FormatJournalText</c>
    /// returns before formatting when the count is zero.
    /// </para>
    /// </remarks>
    public static string Text(IReadOnlyList<JournalEntry> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (journal.Count == 0)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        int written = 0;

        foreach (var entry in journal)
        {
            if (string.IsNullOrEmpty(entry.Text))
            {
                continue;
            }

            written++;
            text.Append(entry.Text);

            if (written < journal.Count)
            {
                text.Append(Separator);
            }
        }

        return text.ToString();
    }

    /// <summary>The screen's menu (<c>DisplayJournalMenuData</c>).</summary>
    public static readonly (string Label, int Shortcut)[] Menu =
        [("NEXT", 0), ("PREV", 0), ("FIRST", 0), ("LAST", 0), ("EXIT", 1)];

    public const int Next = 0;
    public const int Previous = 1;
    public const int First = 2;
    public const int Last = 3;
    public const int Exit = 4;
}
