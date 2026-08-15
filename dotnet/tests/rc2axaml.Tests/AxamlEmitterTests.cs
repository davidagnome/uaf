using System.Xml.Linq;
using Rc2Axaml;

namespace Rc2Axaml.Tests;

/// <summary>Emitting AXAML for every dialog in the shipping resource script.</summary>
public sealed class AxamlEmitterTests
{
    private static readonly ResourceScript Script = ResourceScript.Instance;
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Lazy<IReadOnlyDictionary<string, XDocument>> Generated = new(() =>
    {
        var diagnostics = new List<string>();
        Dictionary<string, XDocument> documents = Script.Parsed.Dialogs.ToDictionary(
            dialog => dialog.Id,
            dialog => XDocument.Parse(AxamlEmitter.Emit(dialog, "src/UAFWinEd/UAFWinEd.rc", diagnostics)),
            StringComparer.Ordinal);

        Assert.Empty(diagnostics);
        return documents;
    });

    private static IEnumerable<XElement> Controls(string dialogId) =>
        Generated.Value[dialogId].Root!.Element(XName.Get("Canvas", "https://github.com/avaloniaui"))!.Elements();

    private static string? Name(XElement element) => (string?)element.Attribute(Xaml + "Name");

    [Fact]
    public void EveryDialogEmitsWellFormedXamlWithNothingUnmapped()
    {
        // Lazy<T> does the work and asserts the diagnostics list is empty; XDocument.Parse is the
        // well-formedness check, and it is why attribute escaping is not left to inspection.
        Assert.Equal(131, Generated.Value.Count);
    }

    [Fact]
    public void EveryControlStatementBecomesExactlyOneElement()
    {
        foreach (RcDialog dialog in Script.Parsed.Dialogs)
        {
            Assert.Equal(dialog.Controls.Count, Controls(dialog.Id).Count());
        }
    }

    [Fact]
    public void NothingIsEmittedWithoutCanvasCoordinates()
    {
        foreach (XElement control in Script.Parsed.Dialogs.SelectMany(d => Controls(d.Id)))
        {
            Assert.NotNull(control.Attribute("Canvas.Left"));
            Assert.NotNull(control.Attribute("Canvas.Top"));
            Assert.NotNull(control.Attribute("Width"));
            Assert.NotNull(control.Attribute("Height"));
        }
    }

    [Fact]
    public void GeneratedFilesHaveNoCodeBehind()
    {
        // An x:Class would demand a partial class that does not exist and break any project that
        // globs these files.
        Assert.All(Generated.Value.Values, document => Assert.Null(document.Root!.Attribute(Xaml + "Class")));
    }

    [Fact]
    public void AnonymousControlsAreLeftUnnamed()
    {
        List<string?> names = Script.Parsed.Dialogs.SelectMany(d => Controls(d.Id)).Select(Name).ToList();
        Assert.DoesNotContain("IDC_STATIC", names);
        Assert.DoesNotContain("-1", names);
        Assert.All(names.Where(n => n is not null), n => Assert.True(char.IsLetter(n![0]) || n[0] == '_'));

        // 3250 controls, of which 883 are IDC_STATIC and 11 are the bare literal -1.
        Assert.Equal(2356, names.Count(n => n is not null));
    }

    /// <summary>
    /// The one control identified by a bare number keeps its identity.
    /// </summary>
    /// <remarks>
    /// 1119 is <c>stc32</c> from the Windows SDK's <c>dlgs.h</c> — the placeholder the common file
    /// dialog looks for when arranging itself around a custom preview pane. It is written as a
    /// number because the symbol is not in resource.h, and it is the only id in the file that is
    /// not a valid XAML name as written.
    /// </remarks>
    [Fact]
    public void NumericControlIdIsPrefixedRatherThanDropped()
    {
        Assert.Contains("Id1119", Controls("IDD_FILEOPENPREVIEW").Select(Name));
    }

    [Fact]
    public void NamesAreUniqueWithinEachDialog()
    {
        foreach (RcDialog dialog in Script.Parsed.Dialogs)
        {
            List<string> names = Controls(dialog.Id).Select(Name).OfType<string>().ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void NamesEveryControlThatHasARealId()
    {
        foreach (RcDialog dialog in Script.Parsed.Dialogs)
        {
            int expected = dialog.Controls.Count(c => c.HasRealId);
            Assert.Equal(expected, Controls(dialog.Id).Count(e => Name(e) is not null));
        }
    }

    /// <summary>
    /// IDD_ABOUTBOX in full: seven controls, one of each interesting shape and small enough to pin.
    /// </summary>
    [Fact]
    public void AboutBoxProducesTheExpectedControls()
    {
        List<XElement> controls = Controls("IDD_ABOUTBOX").ToList();

        Assert.Equal(
            ["Image", "TextBlock", "Button", "TextBlock", "TextBlock", "TextBlock", "TextBlock"],
            controls.Select(c => c.Name.LocalName));

        // Only IDOK and IDC_VersionText have real ids; the other five are IDC_STATIC.
        Assert.Equal([null, null, "IDOK", null, "IDC_VersionText", null, null], controls.Select(Name));

        XElement ok = controls[2];
        Assert.Equal("OK", (string?)ok.Attribute("Content"));
        Assert.Equal("True", (string?)ok.Attribute("IsDefault"));
        Assert.Equal("78", (string?)ok.Attribute("Canvas.Left"));
        Assert.Equal("147", (string?)ok.Attribute("Canvas.Top"));
        Assert.Equal("75", (string?)ok.Attribute("Width"));
        Assert.Equal("23", (string?)ok.Attribute("Height"));

        // CTEXT is centred; the dialog's own extent is 155 x 109 DLU = 232 x 177 px.
        Assert.Equal("Center", (string?)controls[1].Attribute("TextAlignment"));
        Assert.Equal("232", (string?)Generated.Value["IDD_ABOUTBOX"].Root!.Attribute("Width"));
        Assert.Equal("177", (string?)Generated.Value["IDD_ABOUTBOX"].Root!.Attribute("Height"));

        // The \n survived escaping, unescaping and XML attribute-value normalisation.
        Assert.Equal(
            "Copyright 2000, DC Development Team\nReleased under Gnu Public License",
            (string?)controls[3].Attribute("Text"));
    }

    /// <summary>
    /// IDD_REMOVENPC: nine controls covering every statement whose first argument is the id, plus
    /// the anonymous <c>-1</c> labels and the Cancel button.
    /// </summary>
    [Fact]
    public void RemoveNpcProducesTheExpectedControls()
    {
        List<XElement> controls = Controls("IDD_REMOVENPC").ToList();

        Assert.Equal(
            [
                "TextBlock", "Button", "ComboBox", "TextBlock", "TextBox",
                "TextBlock", "Button", "Button", "Button",
            ],
            controls.Select(c => c.Name.LocalName));

        Assert.Equal(
            [null, "IDC_SEECHAR", "IDC_DISTANCE", null, "IDC_TEXT", null, "IDC_REMOVECHAR", "IDOK", "IDCANCEL"],
            controls.Select(Name));

        // ES_MULTILINE | WS_VSCROLL, with no ES_AUTOHSCROLL, so it wraps.
        XElement text = controls[4];
        Assert.Equal("True", (string?)text.Attribute("AcceptsReturn"));
        Assert.Equal("Wrap", (string?)text.Attribute("TextWrapping"));

        Assert.Equal("True", (string?)controls[7].Attribute("IsDefault"));
        Assert.Equal("True", (string?)controls[8].Attribute("IsCancel"));
    }

    /// <summary>
    /// A combo box is emitted one line tall, not as tall as its drop-down.
    /// </summary>
    /// <remarks>
    /// <c>IDC_DISTANCE</c> in IDD_REMOVENPC is written <c>128,7,65,59</c> — 59 DLU, or 95 px, is
    /// the drop-down. Taking it literally would cover the multi-line edit box below it.
    /// </remarks>
    [Fact]
    public void ComboBoxHeightIgnoresTheDropDownExtent()
    {
        RcControl resource = Script.Dialog("IDD_REMOVENPC").Controls.Single(c => c.Id == "IDC_DISTANCE");
        Assert.Equal(59, resource.Height);

        XElement emitted = Controls("IDD_REMOVENPC").Single(e => Name(e) == "IDC_DISTANCE");
        Assert.Equal("23", (string?)emitted.Attribute("Height"));
    }

    /// <summary>
    /// The <c>CONTROL</c> dispatch, one dialog per interesting window class and button style.
    /// </summary>
    [Theory]
    [InlineData("IDD_SHOP", "IDC_BUYITEMSSOLDONLY", "CheckBox")]              // Button, BS_AUTOCHECKBOX
    [InlineData("IDD_ADDSPECIALOBJECT", "IDC_QUEST", "RadioButton")]          // Button, BS_AUTORADIOBUTTON
    [InlineData("IDD_3DPICDLG_640x480g_640x480e", "IDC_SELECT1", "ToggleButton")] // Button, BS_PUSHLIKE
    [InlineData("IDD_FILEOPENPREVIEW", "IDC_IMAGE", "Border")]                // Static, SS_BLACKFRAME
    [InlineData("IDD_CHOOSEMONSTERS", "IDC_AVAILLIST", "ListBox")]            // SysListView32
    [InlineData("IDD_EVENTVIEWER", "IDC_EVENTTREE", "TreeView")]              // SysTreeView32
    [InlineData("IDD_WALLSETPIC", "IDC_SLOTTAB", "TabControl")]               // SysTabControl32
    [InlineData("IDD_TAVERN", "IDC_DRINKSPIN", "NumericUpDown")]              // msctls_updown32
    public void GenericControlsDispatchOnWindowClass(string dialogId, string controlId, string element)
    {
        XElement emitted = Controls(dialogId).Single(e => Name(e) == controlId);
        Assert.Equal(element, emitted.Name.LocalName);
    }

    [Fact]
    public void RadioButtonsAreGroupedByTheWsGroupRun()
    {
        // Win32 bounds a radio group with WS_GROUP on its first control; Avalonia needs a name.
        List<XElement> radios = Controls("IDD_ADDSPECIALOBJECT")
            .Where(e => e.Name.LocalName == "RadioButton")
            .ToList();

        Assert.Equal(3, radios.Count);
        Assert.Single(radios.Select(r => (string?)r.Attribute("GroupName")).Distinct(StringComparer.Ordinal));
        Assert.StartsWith("IDD_ADDSPECIALOBJECT_Group", (string)radios[0].Attribute("GroupName")!, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapedAmpersandSurvives()
    {
        // UAFWinEd.rc:3197 reads "In AD&&D Terms" — Win32's escape for a literal ampersand.
        XElement label = Script.Parsed.Dialogs
            .SelectMany(d => Controls(d.Id))
            .Single(e => ((string?)e.Attribute("Text"))?.StartsWith("In AD", StringComparison.Ordinal) == true);

        Assert.Equal("In AD&D Terms, the defaults are:", (string?)label.Attribute("Text"));
    }

    [Fact]
    public void ProvenanceCommentCitesTheSourceLine()
    {
        string axaml = AxamlEmitter.Emit(Script.Dialog("IDD_ABOUTBOX"), "src/UAFWinEd/UAFWinEd.rc", []);
        Assert.Contains("src/UAFWinEd/UAFWinEd.rc:288", axaml, StringComparison.Ordinal);
        Assert.Contains("About Dungeon Craft Editor", axaml, StringComparison.Ordinal);
    }
}
