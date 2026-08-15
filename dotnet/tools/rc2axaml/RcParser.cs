using System.Globalization;
using System.Text;

namespace Rc2Axaml;

/// <summary>
/// Reads the <c>DIALOG</c> and <c>DIALOGEX</c> resources out of an MFC <c>.rc</c> file.
/// </summary>
/// <remarks>
/// <para>
/// Everything that is not a dialog — <c>STRINGTABLE</c>, <c>MENU</c>, <c>TOOLBAR</c>,
/// <c>BITMAP</c>, the <c>#ifdef APSTUDIO_INVOKED</c> guards and their <c>DESIGNINFO</c> block — is
/// skipped by not matching the dialog header pattern, rather than by tracking the preprocessor.
/// That is safe here because the header pattern is tight: an identifier in column 1, the word
/// <c>DIALOG</c> or <c>DIALOGEX</c>, then four integers. <c>DESIGNINFO</c>'s entries look similar
/// (<c>    IDD_ABOUTBOX, DIALOG</c>) but are indented and comma-separated, so they do not match.
/// </para>
/// <para>
/// <b>The grammar has no line-continuation marker.</b> The resource compiler wraps a statement
/// that runs past ~80 columns onto the next line with no backslash, no trailing comma, nothing —
/// 49 statements in UAFWinEd.rc are wrapped, always just before the window-class string of a
/// <c>CONTROL</c>. The only signal that a line begins a new statement is that its first word is a
/// control keyword, so <see cref="RcKeywords.All"/> is part of the tokeniser, not just the mapper.
/// </para>
/// </remarks>
public static class RcParser
{
    public static RcFile Parse(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var dialogs = new List<RcDialog>();
        var diagnostics = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (!TryParseHeader(lines[i], out string id, out bool extended, out int[]? rect)) { continue; }

            int headerLine = i + 1;
            string? caption = null;
            string? style = null;
            string? exStyle = null;
            RcFont font = RcFont.MsSansSerif8;
            bool sawFont = false;

            int j = i + 1;
            for (; j < lines.Length && lines[j].Trim() != "BEGIN"; j++)
            {
                string head = lines[j].Trim();
                if (head.Length == 0) { continue; }

                if (head.StartsWith("STYLE", StringComparison.Ordinal)) { style = head[5..].Trim(); }
                else if (head.StartsWith("EXSTYLE", StringComparison.Ordinal)) { exStyle = head[7..].Trim(); }
                else if (head.StartsWith("CAPTION", StringComparison.Ordinal)) { caption = Unescape(Unquote(head[7..].Trim())); }
                else if (head.StartsWith("FONT", StringComparison.Ordinal))
                {
                    font = ParseFont(head[4..], id, j + 1, diagnostics);
                    sawFont = true;
                }
                else
                {
                    diagnostics.Add($"{id} (line {j + 1}): unhandled dialog header statement '{head}'");
                }
            }

            if (j >= lines.Length)
            {
                diagnostics.Add($"{id} (line {headerLine}): no BEGIN before end of file");
                break;
            }

            if (!sawFont)
            {
                // Without DS_SETFONT the dialog would use the system font, whose base units are 8x16
                // rather than 6x13. Every dialog in this file sets one, so hitting this means the
                // input is not the file this tool was measured against.
                diagnostics.Add($"{id} (line {headerLine}): no FONT statement — assuming MS Sans Serif 8pt");
            }

            var controls = new List<RcControl>();
            var pending = new StringBuilder();
            int pendingLine = 0;

            for (j++; j < lines.Length && lines[j].Trim() != "END"; j++)
            {
                string body = lines[j].Trim();
                if (body.Length == 0) { continue; }

                if (StartsStatement(body))
                {
                    Flush(pending, pendingLine, id, controls, diagnostics);
                    pending.Append(body);
                    pendingLine = j + 1;
                }
                else if (pending.Length > 0)
                {
                    pending.Append(' ').Append(body);
                }
                else
                {
                    diagnostics.Add($"{id} (line {j + 1}): continuation line with no statement to continue: '{body}'");
                }
            }

            Flush(pending, pendingLine, id, controls, diagnostics);

            dialogs.Add(new RcDialog(
                id, extended, rect![0], rect[1], rect[2], rect[3],
                caption, style, exStyle, font, controls, headerLine));

            i = j;
        }

        return new RcFile(dialogs, diagnostics);
    }

    private static bool StartsStatement(string trimmedLine)
    {
        // A wrapped line always begins with the quoted window class, so a leading quote is by
        // itself proof of continuation and saves worrying about a literal that spells a keyword.
        if (trimmedLine[0] == '"') { return false; }

        int end = 0;
        while (end < trimmedLine.Length && !char.IsWhiteSpace(trimmedLine[end]) && trimmedLine[end] != ',') { end++; }
        return RcKeywords.All.Contains(trimmedLine[..end]);
    }

    private static void Flush(
        StringBuilder pending, int line, string dialogId, List<RcControl> controls, List<string> diagnostics)
    {
        if (pending.Length == 0) { return; }
        string statement = pending.ToString();
        pending.Clear();

        RcControl? control = ParseControl(statement, line, dialogId, diagnostics);
        if (control is not null) { controls.Add(control); }
    }

    private static RcControl? ParseControl(string statement, int line, string dialogId, List<string> diagnostics)
    {
        int split = 0;
        while (split < statement.Length && !char.IsWhiteSpace(statement[split]) && statement[split] != ',') { split++; }
        string keyword = statement[..split];
        List<string> args = SplitArguments(statement[split..]);

        try
        {
            if (keyword == "CONTROL")
            {
                // text, id, class, style, x, y, cx, cy [, exstyle [, helpid]]
                Require(args, 8, keyword);
                return new RcControl(
                    keyword, Unescape(Unquote(args[0])), args[1], Unquote(args[2]),
                    SplitStyle(args[3]), args.Count > 8 ? args[8] : null,
                    Int(args[4]), Int(args[5]), Int(args[6]), Int(args[7]), line);
            }

            if (RcKeywords.TextFirst.Contains(keyword))
            {
                // text, id, x, y, cx, cy [, style [, exstyle]]
                Require(args, 6, keyword);
                return new RcControl(
                    keyword, Unescape(Unquote(args[0])), args[1], null,
                    args.Count > 6 ? SplitStyle(args[6]) : [], args.Count > 7 ? args[7] : null,
                    Int(args[2]), Int(args[3]), Int(args[4]), Int(args[5]), line);
            }

            if (RcKeywords.IdFirst.Contains(keyword))
            {
                // id, x, y, cx, cy [, style [, exstyle]]
                Require(args, 5, keyword);
                return new RcControl(
                    keyword, null, args[0], null,
                    args.Count > 5 ? SplitStyle(args[5]) : [], args.Count > 6 ? args[6] : null,
                    Int(args[1]), Int(args[2]), Int(args[3]), Int(args[4]), line);
            }

            diagnostics.Add($"{dialogId} (line {line}): unhandled control keyword '{keyword}'");
            return null;
        }
        catch (FormatException ex)
        {
            diagnostics.Add($"{dialogId} (line {line}): {ex.Message} in '{statement}'");
            return null;
        }
    }

    private static void Require(List<string> args, int count, string keyword)
    {
        if (args.Count < count)
        {
            throw new FormatException($"{keyword} needs at least {count} arguments, found {args.Count}");
        }
    }

    private static int Int(string token)
    {
        if (!int.TryParse(token.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
        {
            throw new FormatException($"'{token}' is not an integer");
        }

        return value;
    }

    private static bool TryParseHeader(string line, out string id, out bool extended, out int[]? rect)
    {
        id = string.Empty;
        extended = false;
        rect = null;

        if (line.Length == 0 || (!char.IsLetter(line[0]) && line[0] != '_')) { return false; }

        int end = 0;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_')) { end++; }
        if (end == 0 || end >= line.Length || !char.IsWhiteSpace(line[end])) { return false; }

        string name = line[..end];
        string rest = line[end..].TrimStart();

        if (rest.StartsWith("DIALOGEX", StringComparison.Ordinal)) { extended = true; rest = rest[8..]; }
        else if (rest.StartsWith("DIALOG", StringComparison.Ordinal)) { rest = rest[6..]; }
        else { return false; }

        if (rest.Length == 0 || !char.IsWhiteSpace(rest[0])) { return false; }

        List<string> args = SplitArguments(rest);
        // DIALOGEX permits a fifth "helpID" argument. None of the 131 dialogs uses it, but the
        // parse must not reject it if a later .rc does.
        if (args.Count is not (4 or 5)) { return false; }

        var values = new int[4];
        for (int k = 0; k < 4; k++)
        {
            if (!int.TryParse(args[k], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[k]))
            {
                return false;
            }
        }

        id = name;
        rect = values;
        return true;
    }

    private static RcFont ParseFont(string rest, string dialogId, int line, List<string> diagnostics)
    {
        List<string> args = SplitArguments(rest);
        if (args.Count < 2 ||
            !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int points))
        {
            diagnostics.Add($"{dialogId} (line {line}): unreadable FONT statement 'FONT{rest}'");
            return RcFont.MsSansSerif8;
        }

        return new RcFont(points, Unquote(args[1]));
    }

    /// <summary>
    /// Splits a comma-separated argument list, honouring quoted strings.
    /// </summary>
    /// <remarks>
    /// Commas inside string literals are common — <c>"Copyright 2000, DC Development Team\n..."</c>
    /// is the first one in the file — so a plain <c>Split(',')</c> would shear labels in half and
    /// throw the coordinates off by an argument. The doubled-quote escape (<c>""</c>) has to be
    /// recognised here too, or a label containing a quoted word ends the string early.
    /// </remarks>
    public static List<string> SplitArguments(string text)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inString = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (inString && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append("\"\"");
                    i++;
                    continue;
                }

                inString = !inString;
                current.Append(c);
            }
            else if (c == ',' && !inString)
            {
                args.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        args.Add(current.ToString().Trim());
        if (args.Count == 1 && args[0].Length == 0) { args.Clear(); }
        return args;
    }

    /// <summary>
    /// Splits a style expression on <c>|</c>, keeping <c>NOT</c> attached to what it negates.
    /// </summary>
    /// <remarks>
    /// <c>NOT WS_BORDER</c> and <c>NOT WS_TABSTOP</c> both occur (27 statements between them).
    /// Dropping the <c>NOT</c> would turn "this edit box has no border" into "this edit box has a
    /// border", which is exactly backwards and visible on screen.
    /// </remarks>
    public static IReadOnlyList<string> SplitStyle(string expression)
    {
        if (expression.Trim().Length == 0) { return []; }

        var flags = new List<string>();
        foreach (string part in expression.Split('|'))
        {
            string flag = string.Join(' ', part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (flag.Length > 0) { flags.Add(flag); }
        }

        return flags;
    }

    private static string Unquote(string token)
    {
        string trimmed = token.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        return trimmed;
    }

    /// <summary>
    /// Resolves the C-style escapes the resource compiler processes inside string literals.
    /// </summary>
    /// <remarks>
    /// Only <c>\n</c> actually occurs (25 times, in multi-line static labels such as the About
    /// box's copyright notice). The rest are handled because leaving a stray backslash in a label
    /// is worse than handling an escape that never appears, and unknown escapes are passed through
    /// untouched rather than guessed at.
    /// </remarks>
    private static string Unescape(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal)) { return text; }

        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length) { result.Append(text[i]); continue; }

            char next = text[++i];
            switch (next)
            {
                case 'n': result.Append('\n'); break;
                case 'r': result.Append('\r'); break;
                case 't': result.Append('\t'); break;
                case '0': result.Append('\0'); break;
                case '\\': result.Append('\\'); break;
                case '"': result.Append('"'); break;
                default: result.Append('\\').Append(next); break;
            }
        }

        return result.ToString();
    }
}
