using Rc2Axaml;

namespace Rc2Axaml.Tests;

/// <summary>Parsing the shipping resource script, statement for statement.</summary>
public sealed class RcParserTests
{
    private static readonly ResourceScript Script = ResourceScript.Instance;

    [Fact]
    public void ParsesEveryDialogWithoutDiagnostics()
    {
        Assert.Equal(131, Script.Parsed.Dialogs.Count);
        Assert.Empty(Script.Parsed.Diagnostics);
    }

    [Fact]
    public void DialogIdsAreUnique()
    {
        // Names the generated files, so a collision would silently overwrite a dialog.
        Assert.Equal(
            Script.Parsed.Dialogs.Count,
            Script.Parsed.Dialogs.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The counts are external ground truth, taken by grepping the file, not from this parser.
    /// </summary>
    /// <remarks>
    /// This is the "no control keyword is unhandled" check in its strongest form. A keyword the
    /// parser did not recognise, a continuation line mistaken for a statement, or two statements
    /// run together all move one of these numbers.
    /// </remarks>
    [Theory]
    [InlineData("CONTROL", 990)]
    [InlineData("LTEXT", 893)]
    [InlineData("PUSHBUTTON", 532)]
    [InlineData("EDITTEXT", 370)]
    [InlineData("DEFPUSHBUTTON", 117)]
    [InlineData("CTEXT", 111)]
    [InlineData("COMBOBOX", 104)]
    [InlineData("GROUPBOX", 83)]
    [InlineData("SCROLLBAR", 22)]
    [InlineData("RTEXT", 16)]
    [InlineData("LISTBOX", 11)]
    [InlineData("ICON", 1)]
    public void KeywordCountsMatchTheFile(string keyword, int expected)
    {
        int actual = Script.Parsed.Dialogs.SelectMany(d => d.Controls).Count(c => c.Keyword == keyword);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EveryStatementUsesAKnownKeyword()
    {
        IEnumerable<string> keywords = Script.Parsed.Dialogs
            .SelectMany(d => d.Controls)
            .Select(c => c.Keyword)
            .Distinct(StringComparer.Ordinal);

        Assert.All(keywords, keyword => Assert.Contains(keyword, RcKeywords.All));
        Assert.Equal(3250, Script.Parsed.Dialogs.Sum(d => d.Controls.Count));
    }

    /// <summary>
    /// Every window class used by a <c>CONTROL</c> statement, with its count.
    /// </summary>
    /// <remarks>
    /// A new class appearing in a future <c>.rc</c> is exactly the case that must not slip through
    /// as a default-shaped element, so the set is pinned rather than merely counted.
    /// </remarks>
    [Fact]
    public void GenericControlsUseOnlySixWindowClasses()
    {
        Dictionary<string, int> classes = Script.Parsed.Dialogs
            .SelectMany(d => d.Controls)
            .Where(c => c.Keyword == "CONTROL")
            .GroupBy(c => c.WindowClass!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Button"] = 573,
                ["Static"] = 370,
                ["SysListView32"] = 37,
                ["msctls_updown32"] = 7,
                ["SysTabControl32"] = 2,
                ["SysTreeView32"] = 1,
            },
            classes);
    }

    [Fact]
    public void JoinsContinuationLines()
    {
        // The resource compiler wrapped this statement's window class onto its own line
        // (UAFWinEd.rc:324-325). Nothing marks the wrap, so the class is proof the join happened.
        RcControl wrapped = Script.Dialog("IDD_SHOP").Controls
            .Single(c => c.Id == "IDC_BUYITEMSSOLDONLY");

        Assert.Equal("CONTROL", wrapped.Keyword);
        Assert.Equal("Button", wrapped.WindowClass);
        Assert.True(wrapped.HasStyle("BS_AUTOCHECKBOX"));
        Assert.Equal(183, wrapped.X);
        Assert.Equal(20, wrapped.Height);
    }

    [Fact]
    public void KeepsNegatedStyleFlags()
    {
        // "NOT WS_BORDER" is the reference's way of drawing selectable static text. Splitting the
        // style expression on '|' without keeping NOT attached inverts it.
        RcControl status = Script.Dialog("IDD_3DPICDLG_640x480g_640x480e").Controls
            .Single(c => c.Id == "IDC_CellStatus");

        Assert.True(status.NegatesStyle("WS_BORDER"));
        Assert.False(status.HasStyle("WS_BORDER"));
        Assert.True(status.HasStyle("ES_READONLY"));
        Assert.Equal("WS_EX_TRANSPARENT", status.ExStyle);
    }

    [Fact]
    public void ResolvesBackslashEscapesInLabels()
    {
        RcControl copyright = Script.Dialog("IDD_ABOUTBOX").Controls
            .Single(c => c.Text?.StartsWith("Copyright", StringComparison.Ordinal) == true);

        Assert.Equal(
            "Copyright 2000, DC Development Team\nReleased under Gnu Public License",
            copyright.Text);
    }

    [Fact]
    public void KeepsCommasInsideStringLiterals()
    {
        // A naive Split(',') would shear this label and shift every coordinate by one argument.
        RcControl label = Script.Dialog("IDD_ABOUTBOX").Controls
            .Single(c => c.Text?.StartsWith("Copyright", StringComparison.Ordinal) == true);

        Assert.Equal(11, label.X);
        Assert.Equal(37, label.Y);
        Assert.Equal(133, label.Width);
        Assert.Equal(17, label.Height);
    }

    [Fact]
    public void TreatsBareMinusOneAsAnonymous()
    {
        // 11 statements say -1 where the other 883 say IDC_STATIC. Both mean "no identity"; naming
        // the first group would give IDD_QYESNO eight controls called "-1".
        RcDialog dialog = Script.Dialog("IDD_QYESNO");
        Assert.Equal(8, dialog.Controls.Count(c => c.Id == "-1"));
        Assert.All(dialog.Controls.Where(c => c.Id == "-1"), c => Assert.False(c.HasRealId));
    }

    [Fact]
    public void ReadsTheDialogFont()
    {
        Assert.Equal(new RcFont(8, "MS Sans Serif"), Script.Dialog("IDD_ABOUTBOX").Font);
        Assert.Equal(new RcFont(10, "MS Sans Serif"), Script.Dialog("IDD_GAMEVERSION").Font);
        Assert.Equal(new RcFont(8, "MS Shell Dlg"), Script.Dialog("IDD_FlowControl").Font);
    }

    [Fact]
    public void DistinguishesDialogExFromDialog()
    {
        Assert.False(Script.Dialog("IDD_ABOUTBOX").Extended);
        Assert.True(Script.Dialog("IDD_CHARACTER").Extended);
        Assert.Equal(29, Script.Parsed.Dialogs.Count(d => d.Extended));
    }

    [Fact]
    public void IgnoresTheDesignInfoBlock()
    {
        // #ifdef APSTUDIO_INVOKED contains a DESIGNINFO block whose entries read
        // "    IDD_ABOUTBOX, DIALOG". They are indented and comma-separated, which is the only
        // thing keeping them out of the dialog list.
        Assert.Contains("DESIGNINFO", Script.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Script.Parsed.Dialogs, d => d.Controls.Count == 0);
    }
}
