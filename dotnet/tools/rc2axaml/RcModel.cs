namespace Rc2Axaml;

/// <summary>
/// The twelve control statements that appear in <c>src/UAFWinEd/UAFWinEd.rc</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the complete set for that file, counted, not guessed: CONTROL 990, LTEXT 893,
/// PUSHBUTTON 532, EDITTEXT 370, DEFPUSHBUTTON 117, CTEXT 111, COMBOBOX 104, GROUPBOX 83,
/// SCROLLBAR 22, RTEXT 16, LISTBOX 11, ICON 1. The resource grammar has more (LTEXT's siblings
/// AUTO3STATE, RADIOBUTTON, STATE3, and so on) but they never occur here, so they are not
/// modelled — <see cref="RcParser"/> reports any statement it does not recognise instead of
/// silently dropping it, which is what the "no control keyword is unhandled" test checks.
/// </para>
/// <para>
/// <b>The set is load-bearing for parsing, not just for mapping.</b> The resource compiler wraps
/// long statements onto continuation lines with no continuation marker of any kind, so the only
/// way to tell a new statement from the tail of the previous one is that a new statement starts
/// with one of these words. See <see cref="RcParser"/>.
/// </para>
/// </remarks>
public static class RcKeywords
{
    /// <summary>Statements whose first argument is a text literal, then the control id.</summary>
    public static readonly IReadOnlySet<string> TextFirst = new HashSet<string>(StringComparer.Ordinal)
    {
        "LTEXT", "CTEXT", "RTEXT", "PUSHBUTTON", "DEFPUSHBUTTON", "GROUPBOX", "ICON",
    };

    /// <summary>Statements whose first argument is the control id — they carry no text.</summary>
    public static readonly IReadOnlySet<string> IdFirst = new HashSet<string>(StringComparer.Ordinal)
    {
        "EDITTEXT", "COMBOBOX", "LISTBOX", "SCROLLBAR",
    };

    /// <summary>Every recognised control statement, including the generic <c>CONTROL</c>.</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(TextFirst.Concat(IdFirst).Append("CONTROL"), StringComparer.Ordinal);
}

/// <summary>
/// A dialog's <c>FONT</c> statement. Only the point size and face matter here — the optional
/// weight, italic and charset fields exist in the file but do not affect layout.
/// </summary>
/// <remarks>
/// The font is not decoration: it determines the dialog base units, and therefore every
/// coordinate in the dialog. See <see cref="DialogUnits"/>.
/// </remarks>
public sealed record RcFont(int PointSize, string Face)
{
    /// <summary>The overwhelmingly common case in UAFWinEd.rc — 101 of 131 dialogs verbatim.</summary>
    public static readonly RcFont MsSansSerif8 = new(8, "MS Sans Serif");
}

/// <summary>
/// One control statement, normalised across the three argument orders the grammar uses.
/// </summary>
/// <param name="Keyword">The statement word, e.g. <c>LTEXT</c> or <c>CONTROL</c>.</param>
/// <param name="Text">
/// The label. <see langword="null"/> for the id-first statements, which have no text argument.
/// For <c>ICON</c> this is a resource identifier rather than a literal — the one <c>ICON</c> in
/// the file is <c>IDR_MAINFRAME</c>, unquoted.
/// </param>
/// <param name="Id">
/// The control identifier as written. <c>IDC_STATIC</c> and the bare literal <c>-1</c> both mean
/// "no identity"; see <see cref="HasRealId"/>.
/// </param>
/// <param name="WindowClass">The window class of a <c>CONTROL</c> statement; null otherwise.</param>
/// <param name="Style">
/// Style flags, split on <c>|</c> and trimmed. A flag may be negated — <c>NOT WS_BORDER</c> and
/// <c>NOT WS_TABSTOP</c> both occur — and is stored with the <c>NOT</c> attached.
/// </param>
/// <param name="ExStyle">The optional extended-style argument, unsplit. Rare: 12 occurrences.</param>
/// <param name="SourceLine">1-based line of the statement's first line, for provenance comments.</param>
public sealed record RcControl(
    string Keyword,
    string? Text,
    string Id,
    string? WindowClass,
    IReadOnlyList<string> Style,
    string? ExStyle,
    int X,
    int Y,
    int Width,
    int Height,
    int SourceLine)
{
    /// <summary>
    /// Whether the control is addressable from code, and so deserves an <c>x:Name</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IDC_STATIC</c> is <c>-1</c>, and this file writes it both ways — 883 statements say
    /// <c>IDC_STATIC</c> and 11 say <c>-1</c>. Checking only for the symbol would have named
    /// eleven controls after a negative number, and two dialogs (IDD_REMOVENPC, IDD_QYESNO) would
    /// then have contained duplicate names.
    /// </para>
    /// <para>
    /// A numeric id that is not <c>-1</c> still counts as real. There is exactly one — <c>1119</c>
    /// in IDD_FILEOPENPREVIEW (UAFWinEd.rc:1398) — and it is not an oversight: 1119 is
    /// <c>stc32</c> from the Windows SDK's <c>dlgs.h</c>, the placeholder the common file dialog
    /// looks for to work out where to put its own controls around a custom preview pane. The
    /// symbol is not in resource.h, which is why it is written as a bare number.
    /// </para>
    /// </remarks>
    public bool HasRealId =>
        Id.Length > 0 &&
        !string.Equals(Id, "IDC_STATIC", StringComparison.Ordinal) &&
        !string.Equals(Id, "-1", StringComparison.Ordinal);

    /// <summary>True when <paramref name="flag"/> is present and not negated.</summary>
    public bool HasStyle(string flag) => Style.Contains(flag, StringComparer.Ordinal);

    /// <summary>True when the style list carries <c>NOT <paramref name="flag"/></c>.</summary>
    public bool NegatesStyle(string flag) => Style.Contains("NOT " + flag, StringComparer.Ordinal);
}

/// <summary>
/// One <c>DIALOG</c> or <c>DIALOGEX</c> resource.
/// </summary>
/// <param name="Extended">
/// True for <c>DIALOGEX</c>. 30 of the 131 are extended; the difference that matters here is that
/// <c>DIALOGEX</c> permits a trailing extended-style argument on control statements.
/// </param>
/// <param name="Style">The <c>STYLE</c> flags, unsplit.</param>
/// <param name="ExStyle">The <c>EXSTYLE</c> line, present on 5 dialogs.</param>
/// <param name="SourceLine">1-based line of the header, for the generated file's provenance comment.</param>
public sealed record RcDialog(
    string Id,
    bool Extended,
    int X,
    int Y,
    int Width,
    int Height,
    string? Caption,
    string? Style,
    string? ExStyle,
    RcFont Font,
    IReadOnlyList<RcControl> Controls,
    int SourceLine);

/// <summary>The parse of a whole <c>.rc</c> file: every dialog, plus anything not understood.</summary>
/// <remarks>
/// Diagnostics are collected rather than thrown so a single run reports every problem in the file
/// at once. An empty list is the contract the tests assert against.
/// </remarks>
public sealed record RcFile(IReadOnlyList<RcDialog> Dialogs, IReadOnlyList<string> Diagnostics);
