namespace UAF.Media;

/// <summary>
/// Where the event text box sits and how much text fits in it, as
/// <c>LoadConfigFile</c> derives it (<c>Shared/Globals.cpp:2761-2795</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two config forms, and the newer one wins.</b> <c>TEXTBOX = x,y</c> gives a position and takes
/// the width from the screen, centring the box by symmetry; <c>TEXTBOX_RECT = x,y,x2,y2</c> gives
/// all four edges. Both are read, in that order, so a design carrying both gets the rect.
/// </para>
/// <para>
/// <b>The line count is settled twice, from different numbers.</b> Config computes it against a
/// hardcoded 16-pixel line, then <see cref="ForFont"/> recomputes it from the font actually
/// loaded (<c>GetTextBoxCharHeight</c>, <c>FormattedText.cpp:694</c>). Only the second matters for
/// layout — but the first is what <c>TEXTBOX_HEIGHT</c> is built from in the rect-less form, so
/// dropping it changes the box height.
/// </para>
/// </remarks>
public sealed record TextBoxMetrics(int X, int Y, int Width, int Height, int Lines)
{
    /// <summary>The engine's default box for a 640×480 screen (<c>Globals.cpp:185</c>).</summary>
    public static readonly TextBoxMetrics Default = new(18, 328, 640 - (18 * 2), 16 * 5, 5);

    /// <summary>
    /// Derives the box from a design's config values.
    /// </summary>
    /// <param name="screenWidth">
    /// <c>Screen_Width</c>, default 640. Only used by the <c>TEXTBOX</c> form, which takes the
    /// width as the screen less a margin equal to the left inset on both sides.
    /// </param>
    /// <param name="lines">
    /// <c>TextBox_Lines</c>. Matched case-insensitively by the reference's <c>FindToken</c>, which
    /// is what lets every shipped design spell it <c>TEXTBOX_LINES</c> and still be read.
    /// </param>
    public static TextBoxMetrics FromConfig(int screenWidth = 640,
                                            (int X, int Y)? textbox = null,
                                            int? lines = null,
                                            (int Left, int Top, int Right, int Bottom)? rect = null)
    {
        int x = Default.X;
        int y = Default.Y;
        int width = Default.Width;

        if (textbox is (int tx, int ty))
        {
            x = tx;
            y = ty;
            width = screenWidth - (x * 2);
        }

        int lineCount = lines ?? 5;
        int height = 16 * lineCount;

        if (rect is (int left, int top, int right, int bottom))
        {
            x = left;
            y = top;

            // The -1 is the reference's, not a transcription slip: the rect's far edge is treated
            // as exclusive AND one further pixel is dropped. Reproduced, because a design's text
            // wraps at this width today.
            width = right - left - 1;
            height = bottom - top - 1;
            lineCount = height / 16;
        }

        return new TextBoxMetrics(x, y, width, height, lineCount);
    }

    /// <summary>
    /// Narrows the box to what the given font can actually use
    /// (<c>GetTextBoxCharWidth</c>/<c>Height</c>, <c>FormattedText.cpp:674,694</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The usable width is the box less half the widest glyph — a right margin, so a full line does
    /// not sit flush against the edge. The height is unchanged; it is the <i>line count</i> that is
    /// re-derived, from the font's tallest glyph rather than the config's 16.
    /// </para>
    /// <para>
    /// This is the width to wrap at. Wrapping at <see cref="Width"/> instead overruns by half a
    /// character, which shows up only on the occasional line that happens to end near the edge.
    /// </para>
    /// </remarks>
    public TextBoxMetrics ForFont(BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);

        int maxWidth = font.Atlas.MaxCharWidth;
        int maxHeight = font.Atlas.MaxCharHeight;

        int usableWidth = Width - (maxWidth / 2);
        int lineCount = maxHeight > 0 ? Height / maxHeight : Lines;

        return this with { Width = usableWidth, Lines = lineCount };
    }
}
