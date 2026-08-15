namespace UAF.Scripting;

/// <summary>
/// The drawing state the eleven <c>$Gr*</c> calls share
/// (<c>GR_CONTROL</c>, <c>UAFWin/CharStatsForm.cpp:1347</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is how the character sheet is drawn.</b> The engine looks for a special ability called
/// <c>Global_Display</c> and runs its script; failing that it runs a built-in one
/// (<c>defaultCharStats</c>). Either way the script draws the sheet with these eleven calls, so a
/// design can replace the whole layout without touching the engine.
/// </para>
/// <para>
/// <b>Two cursors, not one.</b> An <i>anchor</i> marks where the current line begins and a
/// <i>cursor</i> is where the next glyph goes. <c>$GrTab</c> moves the cursor relative to the
/// anchor — which is what makes a column line up down the sheet — while <c>$GrPrtLF</c> advances
/// the anchor by the linefeed and drags the cursor back to it. Collapsing them into one position
/// would leave every column after the first drifting right by the width of what was printed.
/// </para>
/// <para>
/// <b>Named points do double duty as coordinates and as offsets.</b> The same table holds
/// "where the status column is" and "how far a line feed moves", and <c>$GrSet</c> can define one
/// point in terms of another. There is no separate notion of a vector.
/// </para>
/// </remarks>
public sealed class GpdlGraphics
{
    private readonly Dictionary<string, Point> points = new(StringComparer.Ordinal);

    /// <summary>One named point (<c>GRDEF</c>).</summary>
    private struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// How far <c>$GrPrtLF</c> moves the anchor.
    /// </summary>
    /// <remarks>
    /// <b>Twelve pixels down and none across, until a script says otherwise.</b> That default is
    /// set by <c>Clear</c>, so a sheet that never calls <c>$GrSetLinefeed</c> still advances a line
    /// at a time.
    /// </remarks>
    public int LinefeedX { get; private set; }

    /// <inheritdoc cref="LinefeedX"/>
    public int LinefeedY { get; private set; } = DefaultLinefeedY;

    /// <summary>The default line height.</summary>
    public const int DefaultLinefeedY = 12;

    /// <summary>Where the current line starts.</summary>
    public int AnchorX { get; private set; }

    /// <inheritdoc cref="AnchorX"/>
    public int AnchorY { get; private set; }

    /// <summary>Where the next glyph goes.</summary>
    public int CursorX { get; private set; }

    /// <inheritdoc cref="CursorX"/>
    public int CursorY { get; private set; }

    /// <summary>
    /// The colour text is drawn in, as a <c>FONT_COLOR_NUM</c> ordinal.
    /// </summary>
    /// <remarks>
    /// An ordinal rather than a typed colour because the enum lives in the media layer, which the
    /// scripting layer does not depend on. The host is what turns it into a colour.
    /// </remarks>
    public int Color { get; private set; } = (int)GpdlFontColor.White;

    /// <summary>
    /// What <c>$GrFormat</c> was last given.
    /// </summary>
    /// <remarks>
    /// <b>Written and never read.</b> <c>GrFormat</c> assigns <c>grc.format</c> and nothing in the
    /// engine looks at it again (<c>CharStatsForm.cpp:1525</c>) — so the call is inert, and it is
    /// kept only so a script using it still balances the stack. Recorded here rather than
    /// discarded so that the next person to look does not have to re-derive that it does nothing.
    /// </remarks>
    public string Format { get; private set; } = string.Empty;

    /// <summary>
    /// Resets for a new sheet (<c>GR_CONTROL::Clear</c>).
    /// </summary>
    /// <remarks>
    /// <b>The reference's <c>Clear</c> leaves the cursor and the colour alone</b>, so both carry
    /// over from the previous sheet. In practice the built-in script sets them before it draws
    /// anything, which is why nobody noticed. This resets them too: leaving a sheet's appearance
    /// depending on what was rendered before it is not behaviour worth reproducing, and no design
    /// can rely on it without also relying on which character was looked at last.
    /// </remarks>
    public void Clear()
    {
        points.Clear();
        LinefeedX = 0;
        LinefeedY = DefaultLinefeedY;
        AnchorX = 0;
        AnchorY = 0;
        Format = string.Empty;

        // Not in the reference's Clear -- see the remarks.
        CursorX = 0;
        CursorY = 0;
        Color = (int)GpdlFontColor.White;
    }

    /// <summary>A named point, or null when no script has defined it.</summary>
    public (int X, int Y)? Point_(string name) =>
        points.TryGetValue(name, out var point) ? (point.X, point.Y) : null;

    /// <summary>
    /// Defines a point (<c>$GrSet</c>).
    /// </summary>
    /// <param name="name">The point to define, created if it does not exist.</param>
    /// <param name="x">
    /// A number, <b>or the name of another point</b> — in which case this point takes that one's x.
    /// The two are told apart by whether the name is already defined, so a point called
    /// <c>"12"</c> would shadow the number 12.
    /// </param>
    /// <param name="y">The same, for y. The x and y sources are looked up independently.</param>
    /// <remarks>
    /// <b>An unrecognised name is not an error, it is zero.</b> The fallback is <c>atoi</c>, which
    /// reads text with no leading digits as 0 — so a typo'd point name silently places the point at
    /// the origin rather than complaining.
    /// </remarks>
    public void Set(string name, string x, string y)
    {
        ArgumentNullException.ThrowIfNull(name);

        var point = points.TryGetValue(name, out var existing) ? existing : default;

        point.X = points.TryGetValue(x, out var fromX) ? fromX.X : MfcString.Atoi(x);
        point.Y = points.TryGetValue(y, out var fromY) ? fromY.Y : MfcString.Atoi(y);

        points[name] = point;
    }

    /// <summary>
    /// Takes the linefeed from a point (<c>$GrSetLinefeed</c>).
    /// </summary>
    /// <remarks>A name no script defined does nothing at all — the linefeed keeps its old value.</remarks>
    public void SetLinefeed(string name)
    {
        if (Point_(name) is { } point)
        {
            LinefeedX = point.X;
            LinefeedY = point.Y;
        }
    }

    /// <summary>
    /// Starts a new line at a point (<c>$GrMoveTo</c>).
    /// </summary>
    /// <remarks>Sets both anchor and cursor, which is what makes it a <i>move to</i> rather than a tab.</remarks>
    public void MoveTo(string name)
    {
        if (Point_(name) is { } point)
        {
            AnchorX = CursorX = point.X;
            AnchorY = CursorY = point.Y;
        }
    }

    /// <summary>
    /// Shifts the anchor by a point and returns the cursor to it (<c>$GrMove</c>).
    /// </summary>
    /// <remarks>
    /// Relative where <see cref="MoveTo"/> is absolute — the point is read as an offset, not a
    /// position.
    /// </remarks>
    public void Move(string name)
    {
        if (Point_(name) is { } point)
        {
            AnchorX += point.X;
            AnchorY += point.Y;
            CursorX = AnchorX;
            CursorY = AnchorY;
        }
    }

    /// <summary>
    /// Moves the cursor to an offset from the anchor (<c>$GrTab</c>).
    /// </summary>
    /// <remarks>
    /// <b>The anchor does not move</b>, so tabbing twice on one line both times measures from the
    /// start of the line rather than from wherever the last print left off. This is what keeps a
    /// column straight.
    /// </remarks>
    public void Tab(string name)
    {
        if (Point_(name) is { } point)
        {
            CursorX = AnchorX + point.X;
            CursorY = AnchorY + point.Y;
        }
    }

    /// <summary>
    /// Records the cursor's position under a name (<c>$GrMark</c>).
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="Tab"/>, and the one call that <i>creates</i> a point besides
    /// <see cref="Set"/> — so a script can lay a column out by printing a label and marking where
    /// it ended.
    /// </remarks>
    public void Mark(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        points[name] = new Point { X = CursorX, Y = CursorY };
    }

    /// <summary>Stores the format string (<c>$GrFormat</c>). See <see cref="Format"/>.</summary>
    public void SetFormat(string name) => Format = name ?? string.Empty;

    /// <summary>
    /// Selects the drawing colour (<c>$GrColor</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Case-sensitive, and anything unrecognised is white.</b> <c>GrColor</c> compares with
    /// <c>CString::operator==</c> rather than the case-insensitive <c>CompareNoCase</c> that
    /// <c>ASCII_TO_COLOR</c> uses elsewhere — so <c>$GrColor("Red")</c> draws in <b>white</b>,
    /// silently. The engine's own built-in sheet writes every name in capitals, which is why this
    /// has never bitten anyone in the reference.
    /// </para>
    /// <para>
    /// <b>Kept as-is deliberately.</b> Making it case-insensitive would be a kindness to a designer
    /// writing new scripts and a visible change to every existing design that has a lower-case
    /// colour name in it — text that renders white today would start rendering red. This is a
    /// rendering contract, not a defect that stops a design loading.
    /// </para>
    /// <para>
    /// <b>Ten names for eleven colours:</b> <c>BRIGHTORANGE</c> has no name here, so a script cannot
    /// select it however it is spelt.
    /// </para>
    /// </remarks>
    public void SetColor(string name) => Color = (int)ColorOf(name);

    /// <inheritdoc cref="SetColor"/>
    public static GpdlFontColor ColorOf(string name) => name switch
    {
        "WHITE" => GpdlFontColor.White,
        "GREEN" => GpdlFontColor.Green,
        "YELLOW" => GpdlFontColor.Yellow,
        "ORANGE" => GpdlFontColor.Orange,
        "RED" => GpdlFontColor.Red,
        "CYAN" => GpdlFontColor.Cyan,
        "MAGENTA" => GpdlFontColor.Magenta,
        "SILVER" => GpdlFontColor.Silver,
        "BLACK" => GpdlFontColor.Black,
        "BLUE" => GpdlFontColor.Blue,
        _ => GpdlFontColor.White,
    };

    /// <summary>
    /// Draws text at the cursor and advances it (<c>$GrPrint</c>).
    /// </summary>
    /// <param name="draw">
    /// Draws the text and answers how wide it was. The width is what advances the cursor, so a host
    /// that cannot measure text should answer zero rather than guess — everything on the line would
    /// then overprint, which is at least visibly wrong.
    /// </param>
    public void Print(string text, Func<string, int, int, int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        CursorX += draw(text ?? string.Empty, CursorX, CursorY, Color);
    }

    /// <summary>
    /// Draws text and then starts the next line (<c>$GrPrtLF</c>).
    /// </summary>
    /// <remarks>
    /// <b>The cursor does not advance by the text's width first.</b> Unlike <see cref="Print"/>,
    /// this discards the measurement entirely and jumps to the next line — so the two are not
    /// "print" and "print then newline", and following a <c>$GrPrtLF</c> with a <c>$GrPrint</c>
    /// starts at the new line's beginning rather than after the text.
    /// </remarks>
    public void PrintLine(string text, Func<string, int, int, int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        draw(text ?? string.Empty, CursorX, CursorY, Color);

        AnchorX += LinefeedX;
        AnchorY += LinefeedY;
        CursorX = AnchorX;
        CursorY = AnchorY;
    }

}

/// <summary>
/// The font colours a <c>$GrColor</c> name selects (<c>FONT_COLOR_NUM</c>,
/// <c>Shared/GlobalData.h:683</c>).
/// </summary>
/// <remarks>
/// The same ordinals as the media layer's <c>FontColor</c>, repeated here because the scripting
/// layer does not depend on that assembly. <see cref="GpdlGraphics.Color"/> crosses the host seam
/// as the bare ordinal, which is what the two have in common.
/// </remarks>
public enum GpdlFontColor
{
    White = 0,
    Yellow = 1,
    Orange = 2,
    BrightOrange = 3,
    Red = 4,
    Green = 5,
    Blue = 6,
    Cyan = 7,
    Black = 8,
    Magenta = 9,
    Silver = 10,
}
