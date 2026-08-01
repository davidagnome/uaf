namespace UAF.Media;

/// <summary>
/// Wraps marked-up text into lines that fit a given pixel width
/// (<c>FormatMultiLineText</c>, <c>UAFWin/FormattedText.cpp:1025</c>).
/// </summary>
/// <remarks>
/// <para>
/// The measurement comes from the font, not from a character count, because the faces designs ask
/// for are proportional. <see cref="FormattedTextScanner"/> supplies the characters and keeps the
/// tag state; this loop only accumulates width and decides where to cut.
/// </para>
/// <para>
/// <b>Wrapping is decided one character past the edge.</b> The width is accumulated first and
/// tested after, so the character that overflows is what triggers the break — and it is then
/// rewound to the last space rather than dropped. A line therefore ends at the last word that
/// <i>fits</i>, which is the intent, but the arithmetic is off by that one character's width and a
/// word ending exactly at the boundary still wraps.
/// </para>
/// </remarks>
public static class TextFormatter
{
    /// <summary>
    /// The scanner's index cap. The original's literal; the terminator ends the scan long first.
    /// </summary>
    private const int NoIndexCap = 99999;

    /// <summary>
    /// Wraps <paramref name="text"/> to <paramref name="lineWidth"/> pixels.
    /// </summary>
    /// <param name="lineWidth">
    /// Usable width in pixels — <see cref="TextBoxMetrics.ForFont"/>'s <c>Width</c>, not the raw
    /// box width.
    /// </param>
    public static TextDisplayData Format(byte[] text, int lineWidth, BitmapFont font,
                                         TextDisplayData? into = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        var data = into ?? new TextDisplayData();
        data.RemoveAll();

        var scanner = new FormattedTextScanner();
        scanner.Initialize(text, NoIndexCap, (byte)'W', FontColor.White, 0, 0);

        byte[] preamble = [];
        int currentWidth = 0;

        FormattedTextStatus status;
        while ((status = scanner.NextChar()) != FormattedTextStatus.EndOfText)
        {
            switch (status)
            {
                case FormattedTextStatus.Printable:
                    currentWidth += font.GetCharacterWidth(scanner.CurrentCharacter);
                    if (currentWidth > lineWidth)
                    {
                        scanner.Backup();
                        data.Add(new TextLine(scanner.GetString(ref preamble), false));
                        scanner.SkipSpace();
                        currentWidth = 0;
                    }

                    break;

                // Only a carriage return ends a line. A bare '\n' is skipped -- see the scanner.
                case FormattedTextStatus.CarriageReturn:
                    data.Add(new TextLine(scanner.GetString(ref preamble), false));
                    currentWidth = 0;
                    break;

                case FormattedTextStatus.Wait:
                    data.Add(new TextLine(scanner.GetString(ref preamble), true));
                    currentWidth = 0;
                    break;

                // Tags consume input but no width, and the scanner has already applied them.
                case FormattedTextStatus.Escape:
                case FormattedTextStatus.Color:
                case FormattedTextStatus.Digit:
                case FormattedTextStatus.FontTag:
                case FormattedTextStatus.SkipNextColor:
                case FormattedTextStatus.CrNl:
                case FormattedTextStatus.NlCr:
                case FormattedTextStatus.NewLine:
                    break;

                default:
                    throw new InvalidOperationException(
                        $"die(0x4a744): unexpected {status} while wrapping text.");
            }
        }

        // Whatever is left after the last break is a line, unless it is empty -- which it is when
        // the text ended exactly on a wrap or a carriage return.
        byte[] tail = scanner.GetString(ref preamble);
        if (tail.Length > 0)
        {
            data.Add(new TextLine(tail, false));
        }

        PostProcess(data);
        return data;
    }

    /// <inheritdoc cref="Format(byte[], int, BitmapFont, TextDisplayData?)"/>
    public static TextDisplayData Format(string text, int lineWidth, BitmapFont font,
                                         TextDisplayData? into = null) =>
        Format(BitmapFont.Encode(text ?? string.Empty), lineWidth, font, into);

    /// <summary>
    /// Truncates over-long lines and strips the control characters that survived wrapping
    /// (<c>PostProcessText</c>, <c>FormattedText.cpp:987</c>).
    /// </summary>
    /// <param name="screenWidth">
    /// <c>SCREEN_WIDTH</c> (<c>Globals.cpp:529</c>), default 640. The cap is
    /// <c>screenWidth / 5</c> characters, on the original's reasoning that no font averages under
    /// five pixels a character — a backstop against a runaway line, not a layout rule.
    /// </param>
    public static void PostProcess(TextDisplayData data, int screenWidth = 640)
    {
        ArgumentNullException.ThrowIfNull(data);

        int maxChars = screenWidth / 5;

        for (int i = 0; i < data.NumLines; i++)
        {
            var line = data.Lines[i];
            int max = Math.Min(line.Text.Length, maxChars);

            var kept = new List<byte>(max);
            for (int l = 0; l < max; l++)
            {
                byte c = line.Text[l];
                if (c is not ((byte)'\n' or (byte)'\r' or (byte)'\b' or (byte)'\t'))
                {
                    kept.Add(c);
                }
            }

            data.Replace(i, line with { Text = [.. kept] });
        }
    }

    /// <summary>
    /// Replaces byte 0x80 with a space (<c>StripInvalidChars</c>,
    /// <c>FormattedText.cpp:773</c>).
    /// </summary>
    /// <remarks>
    /// <b>The original means to strip a range and reaches exactly one byte.</b> It tests
    /// <c>*pChar &lt; -127 || *pChar &gt; 255</c> on a <i>signed</i> <c>char</c>, so the second
    /// test can never fire and the first matches only −128 — 0x80, which Windows-1252 maps to the
    /// euro sign. Every other high byte passes through. Transcribed as it behaves rather than as it
    /// reads, because designs' text is full of high bytes that must survive.
    /// </remarks>
    public static void StripInvalidChars(Span<byte> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == 0x80)
            {
                text[i] = (byte)' ';
            }
        }
    }
}
