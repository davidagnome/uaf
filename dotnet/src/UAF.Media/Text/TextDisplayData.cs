namespace UAF.Media;

/// <summary>One wrapped line, and whether the player must press return after it.</summary>
/// <remarks>
/// <c>Text</c> is bytes for the same reason everything else in this layer is, and it carries its
/// own leading tag preamble, so a line renders correctly without replaying the lines above it.
/// </remarks>
public sealed record TextLine(byte[] Text, bool WaitForReturn);

/// <summary>
/// Wrapped lines plus a cursor over them, paged a box at a time
/// (<c>TEXT_DISPLAY_DATA</c>, <c>UAFWin/FormattedText.h:1358</c>).
/// </summary>
/// <remarks>
/// <para>
/// A box is <see cref="LinesPerBox"/> lines, or fewer if a line demands a wait. Events show one
/// box, wait for the player, then advance — which is why paging lives with the text rather than
/// with whatever draws it.
/// </para>
/// <para>
/// <b><see cref="LinesPerBox"/> is per-instance here and global in the original.</b> The engine has
/// one text box, so <c>TEXTBOX_LINES</c> is a global that <c>GetTextBoxCharHeight</c> recomputes
/// from the live font before each format. The journal reader gets round that with a parallel set of
/// <c>*JournalBox</c> methods differing only in using <c>JOURNAL_TEXTBOX_LINES</c> (20) instead —
/// six near-duplicate methods that collapse to one field.
/// </para>
/// </remarks>
public sealed class TextDisplayData
{
    private readonly List<TextLine> lines = [];

    /// <summary>Lines shown at once. <c>TEXTBOX_LINES</c>; 20 for the journal.</summary>
    public int LinesPerBox { get; set; } = 5;

    /// <summary>The first line of the box currently displayed.</summary>
    public int CurrentLine { get; private set; }

    /// <summary>
    /// The line count the paging arithmetic uses.
    /// </summary>
    /// <remarks>
    /// The original keeps this as a separate <c>numLines</c> counter incremented alongside every
    /// <c>Add</c>, which is the same as the array length in every path that survives — so it is one
    /// number here.
    /// </remarks>
    public int NumLines => lines.Count;

    public IReadOnlyList<TextLine> Lines => lines;

    /// <summary>Whether text is revealed a character at a time on this box.</summary>
    public bool SlowText { get; set; }

    /// <summary>Draw every line inverted.</summary>
    public bool HighlightAll { get; set; }

    /// <summary>True until the current box has been drawn once; slow text applies only then.</summary>
    public bool InitialBoxDisplay { get; set; } = true;

    /// <summary>Set by the caller when the event itself demands a keypress, whatever the text says.</summary>
    public bool EventRequiresReturn { get; set; }

    public bool PauseText { get; set; }

    /// <summary>Slow text and paused text both have to draw to the visible surface.</summary>
    public bool NeedsFrontBuffer => UseSlowText || PauseText;

    /// <summary>Slow text applies only to a box's first showing, not to a redraw.</summary>
    public bool UseSlowText => InitialBoxDisplay && SlowText;

    public void Add(TextLine line) => lines.Add(line);

    /// <summary>Rewrites a line in place, for the post-wrap cleanup pass.</summary>
    public void Replace(int index, TextLine line) => lines[index] = line;

    public void RemoveAll()
    {
        lines.Clear();
        CurrentLine = 0;
    }

    /// <summary>Resets everything but the lines themselves (<c>ClearFormattedText</c>, :734).</summary>
    public void Clear()
    {
        InitialBoxDisplay = true;
        SlowText = true;
        CurrentLine = 0;
        PauseText = false;
        lines.Clear();
    }

    public bool IsFirstBox => CurrentLine == 0;

    /// <summary>
    /// Whether any line in the current box asks for a keypress.
    /// </summary>
    public bool WaitForReturn()
    {
        for (int i = 0; i < LinesPerBox; i++)
        {
            if (CurrentLine + i >= NumLines)
            {
                break;
            }

            if (lines[CurrentLine + i].WaitForReturn)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the current box is the last one (<c>IsLastBox</c>, :498).
    /// </summary>
    /// <remarks>
    /// A <c>/N</c> inside the box means more follows even when the remaining lines would have fit,
    /// which is what makes the wait meaningful.
    /// </remarks>
    public bool IsLastBox()
    {
        int i;
        for (i = 0; i < LinesPerBox; i++)
        {
            if (CurrentLine + i + 1 >= NumLines)
            {
                return true;
            }

            if (lines[CurrentLine + i].WaitForReturn)
            {
                return false;
            }
        }

        return CurrentLine + i >= NumLines;
    }

    /// <summary>
    /// Advances to the next box, stopping early at a <c>/N</c> (<c>NextBox</c>, :514).
    /// </summary>
    public void NextBox()
    {
        InitialBoxDisplay = true;

        int i;
        for (i = 0; i < LinesPerBox; i++)
        {
            if (CurrentLine + i > NumLines)
            {
                CurrentLine = NumLines;
                return;
            }

            // A line that waits ends the box after itself, not before.
            if (lines[CurrentLine + i].WaitForReturn)
            {
                CurrentLine = Math.Min(CurrentLine + i + 1, NumLines);
                return;
            }
        }

        CurrentLine += i;
    }

    /// <summary>
    /// Steps back a box (<c>PrevBox</c>, :544).
    /// </summary>
    /// <remarks>
    /// <para>
    /// On plain text this is the exact inverse of <see cref="NextBox"/>: the initial step of two
    /// plus the loop's <see cref="LinesPerBox"/>−2 decrements is <see cref="LinesPerBox"/> lines.
    /// </para>
    /// <para>
    /// <b>It drifts across a wait, though, and that is transcribed rather than fixed.</b> The two
    /// lines it steps back over are never checked for <see cref="TextLine.WaitForReturn"/> — the
    /// loop starts at 2, after the decrement — so a box that ended one line above the current one
    /// because of a <c>/N</c> is stepped straight past, landing a line early and re-showing the tail
    /// of the previous box. Paging back through event text in the reference engine really does do
    /// this.
    /// </para>
    /// </remarks>
    public void PrevBox()
    {
        if (CurrentLine <= 0)
        {
            return;
        }

        CurrentLine -= 2;
        for (int i = 2; i < LinesPerBox; i++)
        {
            if (CurrentLine < 0)
            {
                break;
            }

            if (lines[CurrentLine].WaitForReturn)
            {
                CurrentLine++;
                break;
            }

            CurrentLine--;
        }

        if (CurrentLine < 0)
        {
            CurrentLine = 0;
        }

        InitialBoxDisplay = true;
    }

    /// <summary>
    /// Returns to the first box. <b>Deliberately does not reset
    /// <see cref="InitialBoxDisplay"/></b> — the original's assignment is commented out (:568), so
    /// re-showing the first box does not replay the slow-text reveal.
    /// </summary>
    public void FirstBox() => CurrentLine = 0;

    /// <summary>The lines of the box currently displayed, stopping at a line that waits.</summary>
    public IEnumerable<TextLine> CurrentBox()
    {
        for (int i = 0; i < LinesPerBox && CurrentLine + i < NumLines; i++)
        {
            var line = lines[CurrentLine + i];
            yield return line;

            if (line.WaitForReturn)
            {
                yield break;
            }
        }
    }
}
