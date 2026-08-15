namespace Rc2Axaml;

/// <summary>The Avalonia element a control statement becomes, with its attributes already chosen.</summary>
/// <param name="Element">Avalonia type name, e.g. <c>TextBlock</c>.</param>
/// <param name="Attributes">Attribute name/value pairs, in emission order, values not yet escaped.</param>
/// <param name="Note">
/// A remark to emit as an XML comment beside the element, or null. Used only where the mapping
/// loses information a later hand pass will need — a Win32 common control with no Avalonia
/// equivalent, a bitmap label, an editable combo.
/// </param>
public sealed record MappedControl(string Element, IReadOnlyList<(string Name, string Value)> Attributes, string? Note);

/// <summary>
/// Chooses the Avalonia control for each <c>.rc</c> control statement.
/// </summary>
/// <remarks>
/// <para>
/// The interesting half is <c>CONTROL</c>, which is the resource compiler's escape hatch: it names
/// a window class and a style word and can therefore express any control at all. UAFWinEd.rc uses
/// six classes — <c>Button</c> (573), <c>Static</c> (370), <c>SysListView32</c> (37),
/// <c>msctls_updown32</c> (7), <c>SysTabControl32</c> (2) and <c>SysTreeView32</c> (1) — and
/// within <c>Button</c> the style word decides between four different widgets. Dispatching on the
/// class alone would turn 573 checkboxes, radio buttons and toggles into 573 push buttons: not one
/// of them is a plain push button.
/// </para>
/// <para>
/// <b>Every <c>Static</c> here is <c>SS_BLACKFRAME</c>.</b> Not one is a text label: the editor
/// used the generic statement only for the framed rectangles it blits pictures into (tile
/// previews, the 3D view, the file-open preview). They become <c>Border</c>, which is the honest
/// mapping — a box waiting for something to be drawn in it.
/// </para>
/// </remarks>
public static class ControlMap
{
    /// <summary>
    /// A closed combo box is one line tall regardless of what the resource says.
    /// </summary>
    /// <remarks>
    /// <b>The <c>cy</c> of a <c>COMBOBOX</c> statement is the height of the dropped-down list, not
    /// of the control.</b> The dialog editor writes it that way so the designer can size the
    /// drop-down. Taking it at face value produces combo boxes 40 to 240 pixels tall that swallow
    /// whatever is beneath them — <c>IDC_EVENT_TYPE</c> would be 239 DLU, most of its dialog. So
    /// the emitted height comes from this constant instead, and the drop-down extent is discarded:
    /// Avalonia sizes its own popup.
    /// </remarks>
    public const int ClosedComboHeightDlu = 14;

    public static MappedControl Map(RcControl control, string dialogId, int radioGroup, IList<string> diagnostics)
    {
        var attributes = new List<(string, string)>();

        switch (control.Keyword)
        {
            case "LTEXT": return Text(control, "Left");
            case "CTEXT": return Text(control, "Center");
            case "RTEXT": return Text(control, "Right");

            case "PUSHBUTTON":
            case "DEFPUSHBUTTON":
                return PushButton(control, control.Keyword == "DEFPUSHBUTTON");

            case "GROUPBOX":
                attributes.Add(("Header", Mnemonics.ForContent(control.Text ?? string.Empty)));
                return new MappedControl("HeaderedContentControl", attributes, null);

            case "EDITTEXT":
                return EditText(control);

            case "COMBOBOX":
                return new MappedControl(
                    "ComboBox", attributes,
                    control.HasStyle("CBS_DROPDOWN")
                        ? "CBS_DROPDOWN: the original accepted typed text; Avalonia's ComboBox does not"
                        : null);

            case "LISTBOX":
                return new MappedControl("ListBox", attributes, null);

            case "SCROLLBAR":
                attributes.Add(("Orientation", control.HasStyle("SBS_VERT") ? "Vertical" : "Horizontal"));
                return new MappedControl("ScrollBar", attributes, null);

            case "ICON":
                // The argument is a resource identifier (IDR_MAINFRAME), not a path. Nothing in the
                // port resolves those yet, so the Image is emitted without a Source and the name is
                // left in a comment for whoever wires the icons up.
                return new MappedControl("Image", attributes, $"ICON resource {control.Text}");

            case "CONTROL":
                return MapGeneric(control, dialogId, radioGroup, diagnostics);

            default:
                diagnostics.Add(
                    $"{dialogId} (line {control.SourceLine}): no Avalonia mapping for '{control.Keyword}'");
                return new MappedControl("Border", attributes, $"unmapped {control.Keyword}");
        }
    }

    private static MappedControl MapGeneric(
        RcControl control, string dialogId, int radioGroup, IList<string> diagnostics)
    {
        var attributes = new List<(string, string)>();
        string content = Mnemonics.ForContent(control.Text ?? string.Empty);

        switch (control.WindowClass)
        {
            case "Button":
                if (control.HasStyle("BS_AUTORADIOBUTTON") || control.HasStyle("BS_RADIOBUTTON"))
                {
                    attributes.Add(("Content", content));
                    // Avalonia's RadioButton has no notion of the WS_GROUP run that Win32 uses to
                    // bound a group, so the run index is materialised into a name here. It is
                    // prefixed with the dialog id because GroupName is matched by string.
                    attributes.Add(("GroupName", $"{dialogId}_Group{radioGroup}"));
                    return new MappedControl("RadioButton", attributes, null);
                }

                bool threeState = control.HasStyle("BS_AUTO3STATE") || control.HasStyle("BS_3STATE");
                bool check = threeState || control.HasStyle("BS_AUTOCHECKBOX") || control.HasStyle("BS_CHECKBOX");

                if (check && control.HasStyle("BS_PUSHLIKE"))
                {
                    // BS_PUSHLIKE is a checkbox drawn as a button that stays down — a ToggleButton.
                    // 180 of them, all in the 3D-picture dialogs' wall-slot grids.
                    attributes.Add(("Content", content));
                    return new MappedControl("ToggleButton", attributes, null);
                }

                if (check)
                {
                    attributes.Add(("Content", content));
                    if (threeState) { attributes.Add(("IsThreeState", "True")); }
                    return new MappedControl(
                        "CheckBox", attributes,
                        control.HasStyle("BS_BITMAP") ? "BS_BITMAP: the label was a bitmap, not text" : null);
                }

                if (control.HasStyle("BS_GROUPBOX"))
                {
                    attributes.Add(("Header", content));
                    return new MappedControl("HeaderedContentControl", attributes, null);
                }

                attributes.Add(("Content", content));
                if (control.HasStyle("BS_DEFPUSHBUTTON")) { attributes.Add(("IsDefault", "True")); }
                return new MappedControl("Button", attributes, null);

            case "Static":
                if (control.HasStyle("SS_BITMAP") || control.HasStyle("SS_ICON"))
                {
                    return new MappedControl("Image", attributes, null);
                }

                if (control.HasStyle("SS_BLACKFRAME") || control.HasStyle("SS_GRAYFRAME") ||
                    control.HasStyle("SS_WHITEFRAME") || control.HasStyle("SS_ETCHEDFRAME") ||
                    control.HasStyle("SS_ETCHEDHORZ") || control.HasStyle("SS_ETCHEDVERT") ||
                    control.HasStyle("SS_BLACKRECT") || control.HasStyle("SS_GRAYRECT") ||
                    control.HasStyle("SS_WHITERECT") || control.HasStyle("SS_SUNKEN"))
                {
                    // A literal brush rather than a DynamicResource: nothing here is themed yet,
                    // and an unresolvable resource key would leave the frame invisible — the one
                    // failure mode that looks like the transpiler dropped the control.
                    attributes.Add(("BorderBrush", "Gray"));
                    attributes.Add(("BorderThickness", "1"));
                    return new MappedControl("Border", attributes, null);
                }

                attributes.Add(("Text", Mnemonics.ForLabel(control.Text ?? string.Empty)));
                return new MappedControl("TextBlock", attributes, null);

            // The three Win32 common controls below have close Avalonia relatives but no shared
            // API, so the class is left in a comment: the hand pass has to supply columns, a
            // buddy control or an item template anyway.
            case "SysListView32":
                return new MappedControl(
                    "ListBox", attributes,
                    "SysListView32 " + (control.HasStyle("LVS_REPORT") ? "LVS_REPORT (multi-column)" : "(icon view)"));

            case "SysTreeView32":
                return new MappedControl("TreeView", attributes, "SysTreeView32");

            case "SysTabControl32":
                return new MappedControl("TabControl", attributes, "SysTabControl32");

            case "msctls_updown32":
                // UDS_AUTOBUDDY attached the spinner to the preceding EDITTEXT; NumericUpDown is
                // the edit and the spinner in one, so the buddy edit above it is now redundant.
                return new MappedControl("NumericUpDown", attributes, "msctls_updown32 (UDS_AUTOBUDDY spinner)");

            default:
                diagnostics.Add(
                    $"{dialogId} (line {control.SourceLine}): " +
                    $"no Avalonia mapping for CONTROL class '{control.WindowClass}'");
                return new MappedControl("Border", attributes, $"unmapped window class {control.WindowClass}");
        }
    }

    private static MappedControl Text(RcControl control, string alignment)
    {
        var attributes = new List<(string, string)>
        {
            ("Text", Mnemonics.ForLabel(control.Text ?? string.Empty)),
        };

        if (alignment != "Left") { attributes.Add(("TextAlignment", alignment)); }

        // Win32 statics wrap at the control's width unless SS_LEFTNOWORDWRAP says otherwise, and
        // several labels here rely on it — the paragraph at UAFWinEd.rc:1160 is 245 DLU wide and
        // three lines tall.
        if (!control.HasStyle("SS_LEFTNOWORDWRAP")) { attributes.Add(("TextWrapping", "Wrap")); }

        return new MappedControl("TextBlock", attributes, null);
    }

    private static MappedControl PushButton(RcControl control, bool isDefault)
    {
        var attributes = new List<(string, string)>
        {
            ("Content", Mnemonics.ForContent(control.Text ?? string.Empty)),
        };

        if (isDefault) { attributes.Add(("IsDefault", "True")); }

        // IDCANCEL is the Escape key's button by convention in every MFC dialog; Avalonia spells
        // that IsCancel, and it costs nothing to carry across.
        if (string.Equals(control.Id, "IDCANCEL", StringComparison.Ordinal))
        {
            attributes.Add(("IsCancel", "True"));
        }

        return new MappedControl("Button", attributes, null);
    }

    private static MappedControl EditText(RcControl control)
    {
        var attributes = new List<(string, string)>();

        if (control.HasStyle("ES_MULTILINE"))
        {
            attributes.Add(("AcceptsReturn", "True"));
            // ES_AUTOHSCROLL on a multi-line edit means "scroll sideways instead of wrapping".
            if (!control.HasStyle("ES_AUTOHSCROLL")) { attributes.Add(("TextWrapping", "Wrap")); }
        }

        if (control.HasStyle("ES_READONLY")) { attributes.Add(("IsReadOnly", "True")); }
        if (control.HasStyle("ES_CENTER")) { attributes.Add(("TextAlignment", "Center")); }
        else if (control.HasStyle("ES_RIGHT")) { attributes.Add(("TextAlignment", "Right")); }
        if (control.HasStyle("ES_PASSWORD")) { attributes.Add(("PasswordChar", "*")); }

        // A borderless read-only edit is the reference's way of drawing selectable static text —
        // IDC_CellStatus in the 3D-picture dialogs does exactly this.
        if (control.NegatesStyle("WS_BORDER")) { attributes.Add(("BorderThickness", "0")); }

        return new MappedControl("TextBox", attributes, null);
    }
}

/// <summary>
/// Translates Win32 <c>&amp;</c> mnemonics into Avalonia's <c>_</c> mnemonics, or removes them.
/// </summary>
/// <remarks>
/// The two conventions collide in both directions: Win32 escapes a literal ampersand as
/// <c>&amp;&amp;</c> and treats <c>_</c> as an ordinary character, while Avalonia's content
/// presenters treat <c>_</c> as the marker and want it doubled when literal. Converting one
/// without the other silently eats characters — UAFWinEd.rc:3197 reads "In AD&amp;&amp;D Terms",
/// which must come out as "In AD&amp;D Terms" and not "In ADD Terms".
/// </remarks>
public static class Mnemonics
{
    /// <summary>Stands in for an escaped ampersand while the marker pass runs; never occurs in a .rc label.</summary>
    private const char Sentinel = '\u0001';

    /// <summary>For controls whose content presenter recognises access keys.</summary>
    public static string ForContent(string text) =>
        text.Replace("_", "__", StringComparison.Ordinal)
            .Replace("&&", Sentinel.ToString(), StringComparison.Ordinal)
            .Replace('&', '_')
            .Replace(Sentinel, '&');

    /// <summary>
    /// For <c>TextBlock</c>, which has no access-key handling: the marker is simply dropped, and
    /// underscores stay as they are.
    /// </summary>
    public static string ForLabel(string text) =>
        text.Replace("&&", Sentinel.ToString(), StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace(Sentinel, '&');
}
