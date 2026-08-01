using System.Globalization;

namespace UAF.Media;

/// <summary>
/// What <see cref="FormattedTextScanner.NextChar"/> just consumed (<c>FORMATTED_TEXT::FTStatus</c>,
/// <c>UAFWin/FormattedText.h:1376</c>).
/// </summary>
/// <remarks>
/// The header's order is kept. These are not serialized, so the ordinals carry no format weight —
/// but the state machine dispatches on the previous status, and reading the two side by side is
/// how the dispatch gets checked.
/// </remarks>
public enum FormattedTextStatus
{
    /// <summary>A character that draws. <see cref="FormattedTextScanner.CurrentCharacter"/> holds it.</summary>
    Printable = 0,

    /// <summary>Nothing fetched yet.</summary>
    Start,

    /// <summary>A <c>/</c>. The tag it introduces is decoded on the following call.</summary>
    Escape,

    /// <summary>A <c>\r</c>. This is the one that ends a line — see the remarks on the scanner.</summary>
    CarriageReturn,

    /// <summary>A <c>\n</c>.</summary>
    NewLine,

    /// <summary>A <c>\n</c> directly after a <c>\r</c> — the second half of a CRLF, ignored.</summary>
    CrNl,

    /// <summary>A <c>\r</c> directly after a <c>\n</c>. <b>Fatal in the reference</b> — see the remarks.</summary>
    NlCr,

    /// <summary>A colour tag was applied.</summary>
    Color,

    /// <summary>The first digit of a two-digit font tag.</summary>
    Digit,

    /// <summary>A complete two-digit font tag.</summary>
    FontTag,

    /// <summary>No character remains.</summary>
    EndOfText,

    /// <summary>A <c>/N</c> — end the line here and wait for the player.</summary>
    Wait,

    /// <summary>A <c>/#</c> — swallow the next colour tag instead of applying it.</summary>
    SkipNextColor,
}

/// <summary>
/// Walks text one character at a time, applying the <c>/</c> markup as it goes
/// (<c>FORMATTED_TEXT</c>, <c>UAFWin/FormattedText.cpp:1339</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the front half of the text layer: it turns a byte string into a stream of printable
/// characters plus the colour and font state each one is drawn with. Wrapping
/// (<see cref="TextFormatter"/>) and the slow-text renderer both drive it; neither has to know
/// anything about the markup.
/// </para>
/// <para>
/// <b>Text is bytes, not <see cref="string"/>.</b> Same reason the fonts are — the engine is an
/// MBCS build and indexes glyphs by <c>unsigned char</c>. The scanner never decodes, so a
/// Windows-1252 byte above 127 passes through as itself.
/// </para>
/// <para>
/// <b>The buffer has a moving base.</b> The original holds <c>const char *m_pText</c> and advances
/// the pointer itself in <c>GetString</c>, leaving the index to restart at 0 for each line. That is
/// modelled here as <see cref="TextStart"/> into a fixed array, because several of the quirks below
/// are consequences of the base moving while other members do not.
/// </para>
/// <para>
/// <b>Only <c>\r</c> ends a line, not <c>\n</c>.</b> The wrap loop acts on
/// <see cref="FormattedTextStatus.CarriageReturn"/> and explicitly ignores
/// <see cref="FormattedTextStatus.NewLine"/> ("We only process FTCR",
/// <c>FormattedText.cpp:1071</c>). Text arriving with Unix line endings therefore does not break at
/// all — it wraps only when it runs out of width.
/// </para>
/// <para>
/// <b>A <c>\n\r</c> sequence kills the reference engine.</b> <c>TestNextChar</c> produces
/// <c>FTNLCR</c> for a carriage return following a newline, and <c>NextChar</c>'s dispatch has no
/// case for it, so it reaches <c>die(0x551b0a)</c> — a message box and <c>abort()</c>
/// (<c>RunEvent.cpp:148</c>), in every build, not a debug assert. CRLF is fine; it is only the
/// reversed pair. This port throws rather than inventing a recovery, on the same reasoning as the
/// serialization layer's <c>die()</c> mirrors: a hard stop is locatable, and silently succeeding
/// where the reference aborts would hide a real difference in what designs can contain.
/// </para>
/// </remarks>
public sealed class FormattedTextScanner
{
    private byte[] text = [];
    private int textStart;
    private int length;
    private int currentCharIndex;
    private int lastLineBreakIndex;
    private byte currentChar;
    private FormattedTextStatus prevStatus;

    private FontColor startingColorNum;
    private FontColor currentColorNum;
    private int startingFontNum;
    private int currentFontNum;
    private byte startingColorChar;
    private byte currentColorChar;
    private bool customColorActive;
    private bool skipNextColor;

    /// <summary>
    /// The character <see cref="FormattedTextStatus.Printable"/> just yielded.
    /// </summary>
    public byte CurrentCharacter => currentChar;

    /// <summary>The colour tags seen so far select this colour.</summary>
    public FontColor CurrentColor => currentColorNum;

    /// <summary>The font tags seen so far select this font number; 0 is the design's default.</summary>
    public int CurrentFont => currentFontNum;

    /// <summary>
    /// Whether a <c>/C</c> is in force, overriding the current font's colour with the design's
    /// custom one.
    /// </summary>
    public bool IsCustomColorActive => customColorActive;

    /// <summary>Index of the character last returned, relative to <see cref="TextStart"/>.</summary>
    public int CharIndex => currentCharIndex - 1;

    /// <summary>Where the current line begins in the buffer. <c>GetString</c> advances it.</summary>
    public int TextStart => textStart;

    /// <summary>
    /// Points the scanner at <paramref name="source"/>.
    /// </summary>
    /// <param name="cap">
    /// The original's <c>length</c>: an upper bound on the index, not on the buffer.
    /// <see cref="TextFormatter"/> passes 99999 and lets the terminator do the work;
    /// <see cref="Backup"/> passes a real bound to re-scan a prefix. <b>It is not adjusted when
    /// <see cref="GetString"/> moves the base</b>, so the reachable extent grows line by line —
    /// harmless at 99999, which is why the original never noticed.
    /// </param>
    public void Initialize(byte[] source, int cap, byte colorChar, FontColor colorNum,
                           int fontNum, int index, int start = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        text = source;
        textStart = start;
        length = cap;
        startingFontNum = fontNum;
        currentFontNum = fontNum;
        startingColorNum = colorNum;
        currentColorNum = colorNum;
        customColorActive = false;
        startingColorChar = colorChar;
        currentColorChar = colorChar;
        currentCharIndex = index;
        lastLineBreakIndex = -1;
        currentChar = 0;
        prevStatus = FormattedTextStatus.Start;
        skipNextColor = false;
    }

    /// <summary>
    /// Reads the byte at <paramref name="index"/> from the current base, treating anything past the
    /// end of the array as the terminator the original's <c>char*</c> would have found.
    /// </summary>
    private byte At(int index)
    {
        int absolute = textStart + index;
        return (uint)absolute < (uint)text.Length ? text[absolute] : (byte)0;
    }

    /// <summary>
    /// Classifies the next raw character (<c>TestNextChar</c>, <c>FormattedText.cpp:1365</c>).
    /// </summary>
    /// <remarks>
    /// <b>Only space and tab register as line breaks here.</b> The original calls
    /// <c>IsWhiteSpace</c>, which also accepts <c>\n</c>, <c>\r</c> and the two-character
    /// <c>/n</c> — but all three are handled by the branches above it and can never reach the test.
    /// (It is also called on the address of a one-byte member, so its second-character lookahead
    /// reads whatever follows in the struct; that path is unreachable for the same reason.)
    /// </remarks>
    private void TestNextChar()
    {
        if (At(currentCharIndex) == 0 || currentCharIndex >= length)
        {
            prevStatus = FormattedTextStatus.EndOfText;
            return;
        }

        if (At(currentCharIndex) == (byte)'/')
        {
            prevStatus = FormattedTextStatus.Escape;
            currentCharIndex++;
            return;
        }

        if (At(currentCharIndex) == (byte)'\r')
        {
            prevStatus = prevStatus == FormattedTextStatus.NewLine
                ? FormattedTextStatus.NlCr
                : FormattedTextStatus.CarriageReturn;
            currentCharIndex++;
            return;
        }

        if (At(currentCharIndex) == (byte)'\n')
        {
            prevStatus = prevStatus == FormattedTextStatus.CarriageReturn
                ? FormattedTextStatus.CrNl
                : FormattedTextStatus.NewLine;
            currentCharIndex++;
            return;
        }

        currentChar = At(currentCharIndex);
        if (currentChar == (byte)' ' || currentChar == (byte)'\t')
        {
            // The break index points AT the whitespace, before the increment.
            lastLineBreakIndex = currentCharIndex;
        }

        currentCharIndex++;
        prevStatus = FormattedTextStatus.Printable;
    }

    /// <summary>
    /// Advances one step (<c>NextChar</c>, <c>FormattedText.cpp:1418</c>).
    /// </summary>
    /// <remarks>
    /// Decoding a tag takes two calls: the first sees the <c>/</c> and returns
    /// <see cref="FormattedTextStatus.Escape"/>, the second decodes what follows. A font tag takes
    /// three, one per digit.
    /// </remarks>
    public FormattedTextStatus NextChar()
    {
        switch (prevStatus)
        {
            case FormattedTextStatus.Start:
            case FormattedTextStatus.Color:
            case FormattedTextStatus.FontTag:
            case FormattedTextStatus.Printable:
            case FormattedTextStatus.CarriageReturn:
            case FormattedTextStatus.CrNl:
            case FormattedTextStatus.NewLine:
                TestNextChar();
                return prevStatus;

            case FormattedTextStatus.Escape:
                return ReadEscape();

            case FormattedTextStatus.Digit:
                return ReadFontDigit();

            case FormattedTextStatus.EndOfText:
                return prevStatus;

            default:
                // NlCr lands here, and so would Wait and SkipNextColor if they were ever stored --
                // they are not, because the escape branch returns them without touching prevStatus.
                throw new InvalidOperationException(
                    $"die(0x551b0a): FORMATTED_TEXT has no transition from {prevStatus}. " +
                    "A '\\n\\r' sequence reaches this in the reference engine too, where it is a " +
                    "message box and abort().");
        }
    }

    /// <summary>Decodes the character after a <c>/</c>.</summary>
    /// <remarks>
    /// <para>
    /// The status is set to <see cref="FormattedTextStatus.Color"/> up front — the original's
    /// "Just a guess" — and the two early returns (<c>/N</c> and <c>/#</c>) deliberately leave it
    /// that way, which is what keeps them out of the dispatch above.
    /// </para>
    /// <para>
    /// <b>An unrecognised tag is not an error.</b> The index steps back and the <c>/</c> is
    /// re-issued as a printable character, so prose containing a date or a path survives intact.
    /// </para>
    /// </remarks>
    private FormattedTextStatus ReadEscape()
    {
        currentChar = At(currentCharIndex++);
        prevStatus = FormattedTextStatus.Color;

        if (skipNextColor)
        {
            // The tag is consumed and discarded; /# arms this for exactly one tag.
            skipNextColor = false;
            return prevStatus;
        }

        switch ((char)currentChar)
        {
            case 'W' or 'w': SetColor(FontColor.White, (byte)'W'); break;
            case 'Y' or 'y': SetColor(FontColor.Yellow, (byte)'Y'); break;
            case 'O' or 'o': SetColor(FontColor.Orange, (byte)'O'); break;
            case 'T' or 't': SetColor(FontColor.BrightOrange, (byte)'T'); break;
            case 'R' or 'r': SetColor(FontColor.Red, (byte)'R'); break;
            case 'G' or 'g': SetColor(FontColor.Green, (byte)'G'); break;
            case 'B' or 'b': SetColor(FontColor.Blue, (byte)'B'); break;
            case 'V' or 'v': SetColor(FontColor.Cyan, (byte)'V'); break;
            case 'K' or 'k': SetColor(FontColor.Black, (byte)'K'); break;
            case 'M' or 'm': SetColor(FontColor.Magenta, (byte)'M'); break;
            case 'S' or 's': SetColor(FontColor.Silver, (byte)'S'); break;

            // Highlight is a draw-time inversion rather than a colour; the original reaches into
            // the graphics manager for it and clears it once the string has been drawn. Here it is
            // reported through the colour char, which is also what the preamble carries.
            case 'H' or 'h':
                currentColorChar = (byte)'H';
                break;

            // Ends the line and waits for the player. Note prevStatus stays Color.
            case 'N' or 'n':
                return FormattedTextStatus.Wait;

            case '#':
                skipNextColor = true;
                return FormattedTextStatus.SkipNextColor;

            // Overrides the current font's colour with the design's custom one. It survives a font
            // change and is cleared by any explicit colour.
            case 'C' or 'c':
                customColorActive = true;
                break;

            case >= '0' and <= '9':
                prevStatus = FormattedTextStatus.Digit;
                return prevStatus;

            default:
                currentCharIndex -= 1;
                currentChar = (byte)'/';
                prevStatus = FormattedTextStatus.Printable;
                return prevStatus;
        }

        return prevStatus;
    }

    private void SetColor(FontColor color, byte colorChar)
    {
        customColorActive = false;
        currentColorNum = color;
        currentColorChar = colorChar;
    }

    /// <summary>
    /// Decodes the second digit of a font tag; <c>/26</c> selects font 26.
    /// </summary>
    /// <remarks>
    /// <b>Both digits are required.</b> A <c>/</c> followed by one digit and a non-digit rewinds
    /// two characters and emits the <c>/</c> as printable, so "3/4 of the way" is not eaten.
    /// </remarks>
    private FormattedTextStatus ReadFontDigit()
    {
        currentChar = At(currentCharIndex++);

        if (currentChar is >= (byte)'0' and <= (byte)'9')
        {
            currentFontNum = (10 * (At(currentCharIndex - 2) - '0'))
                             + (At(currentCharIndex - 1) - '0');
            prevStatus = FormattedTextStatus.FontTag;
            return prevStatus;
        }

        currentCharIndex -= 2;
        currentChar = (byte)'/';
        prevStatus = FormattedTextStatus.Printable;
        return prevStatus;
    }

    /// <summary>
    /// Rewinds to the last whitespace, so an over-long line breaks at a word boundary
    /// (<c>Backup</c>, <c>FormattedText.cpp:1589</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rewind can cross tags, so the colour and font at the break point are recovered by
    /// re-scanning the prefix from the line's starting state rather than by remembering them. That
    /// re-scan is why the scanner has to be cheap to construct.
    /// </para>
    /// <para>
    /// <b>Declining to rewind means "cut here", not "do not cut".</b> The caller breaks the line
    /// either way — this only decides where. So a run with no whitespace in it, or one whose only
    /// break is at index 0, is hard-cut mid-word at the character that overflowed: a 10-character
    /// word in a 4-character box becomes three lines, not one long one. The <c>&lt;= 0</c> guard
    /// (not <c>&lt; 0</c>) is what stops a line beginning with a space from rewinding to the start
    /// and wrapping forever.
    /// </para>
    /// <para>
    /// <b><see cref="lastLineBreakIndex"/> is not cleared here or by <see cref="GetString"/>.</b>
    /// It survives into the next line, still measured from the previous base. It is only ever read
    /// after a fresh line has recorded its own break, except for a line with no whitespace at all —
    /// where the stale value applies and cuts at an arbitrary point. Reproduced rather than fixed:
    /// it only shows up on unbroken runs longer than the box, and "fixing" it would change where
    /// existing designs' text breaks.
    /// </para>
    /// </remarks>
    public void Backup()
    {
        if (lastLineBreakIndex <= 0)
        {
            return;
        }

        int prefix = lastLineBreakIndex;

        var rescan = new FormattedTextScanner();
        rescan.Initialize(text, prefix, startingColorChar, startingColorNum, startingFontNum, 0,
                          textStart);
        while (rescan.NextChar() != FormattedTextStatus.EndOfText)
        {
        }

        currentColorNum = rescan.currentColorNum;
        currentColorChar = rescan.currentColorChar;
        currentFontNum = rescan.currentFontNum;

        // The original gets this for free by holding the flag in the graphics manager, where the
        // re-scan writes to the same global. Copying it explicitly is the same result: the prefix
        // is exactly the text up to the break, so its ending flag is the state to resume with.
        // Note customColorActive is deliberately NOT copied -- the original does not either.
        skipNextColor = rescan.skipNextColor;

        currentCharIndex = prefix;
    }

    /// <summary>
    /// Steps over one leading space so a wrapped line does not begin with the space it broke on
    /// (<c>SkipSpace</c>, <c>FormattedText.cpp:1610</c>).
    /// </summary>
    /// <remarks>
    /// <b>One space, and only at the very start of a line.</b> The guard returns unless the index
    /// is 0, and a run of spaces loses only the first — so text padded with several spaces after a
    /// full stop keeps the rest, indented.
    /// </remarks>
    public void SkipSpace()
    {
        if (currentCharIndex != 0)
        {
            return;
        }

        if (At(0) == (byte)' ')
        {
            textStart++;
        }
    }

    /// <summary>
    /// Cuts the line accumulated so far, advances the base past it, and rewrites
    /// <paramref name="preamble"/> with the tags the next line must open with
    /// (<c>GetString</c>, <c>FormattedText.cpp:1619</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The preamble is how colour survives a line break: a line that begins mid-colour is emitted
    /// with the tag re-stated at its head, so each line renders correctly on its own. It is empty
    /// only when both colour and font are at their defaults.
    /// </para>
    /// <para>
    /// <b>Trailing control characters are trimmed from the text but still consumed.</b> The length
    /// is walked back over anything below <c>' '</c> while the base advances by the untrimmed
    /// index, so the newline that ended the line does not reappear at the head of the next one.
    /// </para>
    /// </remarks>
    public byte[] GetString(ref byte[] preamble)
    {
        ArgumentNullException.ThrowIfNull(preamble);

        int trimmed = currentCharIndex;
        while (trimmed > 0 && At(trimmed - 1) < (byte)' ')
        {
            trimmed--;
        }

        var result = new byte[preamble.Length + trimmed];
        preamble.CopyTo(result, 0);
        Array.Copy(text, textStart, result, preamble.Length, trimmed);

        textStart += currentCharIndex;
        currentCharIndex = 0;
        startingColorChar = currentColorChar;
        startingColorNum = currentColorNum;
        startingFontNum = currentFontNum;

        preamble = BuildPreamble(startingColorNum, startingColorChar, startingFontNum);
        return result;
    }

    /// <summary>
    /// The tag prefix that re-establishes <paramref name="color"/> and <paramref name="font"/> at
    /// the head of a continuation line.
    /// </summary>
    /// <remarks>
    /// White is the default and needs no tag, so a white line carries only a font tag if the font
    /// is non-default. The font is always two digits, matching the scanner's two-digit rule.
    /// </remarks>
    private static byte[] BuildPreamble(FontColor color, byte colorChar, int font)
    {
        string tag = (color, font) switch
        {
            (FontColor.White, 0) => string.Empty,
            (FontColor.White, _) => $"/{font.ToString("00", CultureInfo.InvariantCulture)}",
            (_, 0) => $"/{(char)colorChar}",
            _ => $"/{(char)colorChar}/{font.ToString("00", CultureInfo.InvariantCulture)}",
        };

        return tag.Length == 0 ? [] : BitmapFont.Encode(tag);
    }
}
