using System.Text;

namespace UAF.Scripting;

/// <summary>
/// Port of <c>INPUTFILE</c> (GPDLcomp.cpp:70–745): the GPDL tokeniser.
/// </summary>
/// <remarks>
/// <para>
/// The original is fed one line at a time by a caller-supplied <c>GetOneLine</c> callback and
/// returns 0 from <c>m_nextChar</c> at end of input. This port keeps that shape (an
/// <see cref="IEnumerator{T}"/> of lines) rather than taking the whole text, because the line
/// boundary is observable: <see cref="ReadStringLiteral"/> refuses to cross one.
/// </para>
/// <para>
/// Several behaviours here look like bugs and are reproduced deliberately. They are called out at
/// the site. The important structural one: <b>character pushback is a single slot</b>
/// (<c>m_savedChar</c>), and the original calls it twice in a row on the <c>#</c> path. The second
/// call is a no-op, so exactly one character comes back — code that "fixes" this by making
/// pushback a stack changes how <c>#</c> at the end of a token lexes.
/// </para>
/// </remarks>
public sealed class GpdlLexer
{
    private readonly IEnumerator<string> _lines;
    private string _line = string.Empty;
    private int _lineIndex;
    private int _lineNumber = 1;
    private char _prevChar;
    private char _savedChar;
    private bool _eof;

    /// <summary>Set when a <c>//</c> comment was seen; the next raw token skips to end of line.</summary>
    private bool _skipLine;

    private GpdlTokenType _backspaceTkn = GpdlTokenType.TKN_NONE;
    private GpdlTokenType _latestTkn = GpdlTokenType.TKN_NONE;

    private readonly StringBuilder _token = new();
    private int _integer;

    public GpdlLexer(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _lines = lines.GetEnumerator();
    }

    /// <summary>
    /// Splits source text the way <c>PROGRAM_TEXT::Initialize</c> (GPDLcomp.cpp:3774) does: drop
    /// every <c>\r</c>, keep the <c>\n</c> at the end of each line, and emit a final line for any
    /// trailing text with no newline.
    /// </summary>
    public static List<string> SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new List<string>();
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (c == '\r') { continue; }
            sb.Append(c);
            if (c == '\n')
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0) { result.Add(sb.ToString()); }
        return result;
    }

    /// <summary>The text of the most recent token.</summary>
    public string Token => _token.ToString();

    /// <summary>The value of the most recent <see cref="GpdlTokenType.TKN_INTEGER"/>.</summary>
    public int Integer => _integer;

    /// <summary>1-based line number, incremented as newlines are consumed.</summary>
    public int LineNumber => _lineNumber;

    /// <summary>Errors reported by the lexer and by the compiler that drives it.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>
    /// Mirrors <c>INPUTFILE::error</c> (GPDLcomp.cpp:311): after 12 messages the original stops
    /// reporting and emits a bare "." per further error. Kept because a caller counting messages
    /// would otherwise see a different total.
    /// </summary>
    public void Error(string text)
    {
        if (Errors.Count >= 12)
        {
            Errors.Add(".");
            return;
        }
        Errors.Add($"{text}  (line {_lineNumber}, last token '{Token}')");
    }

    private static bool IsWhitespace(char c) =>
        // GPDLcomp.cpp:225. 0x10 and 0x12 are in the original list; they are not whitespace by any
        // other definition, but a source file containing them would tokenise differently without.
        c is ' ' or '\r' or '\n' or '\t' or (char)0x10 or (char)0x12;

    private static bool IsInitialChar(char c) =>
        // GPDLcomp.cpp:367. '$' leads every system function and keyword.
        c == '$' || c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static bool IsMoreChar(char c) =>
        (c >= '0' && c <= '9') || c == '@' || IsInitialChar(c);

    private char NextChar()
    {
        if (_savedChar != 0)
        {
            char temp = _savedChar;
            _savedChar = (char)0;
            return temp;
        }
        if (_eof) { return (char)0; }
        while (true)
        {
            if (_lineIndex >= _line.Length)
            {
                if (!_lines.MoveNext())
                {
                    _eof = true;
                    return (char)0;
                }
                _line = _lines.Current;
                _lineIndex = 0;
                if (_line.Length == 0) { continue; }
            }
            _prevChar = _line[_lineIndex++];
            if (_prevChar == '\n') { _lineNumber++; }
            return _prevChar;
        }
    }

    /// <summary>
    /// <c>INPUTFILE::m_backspace</c> (GPDLcomp.cpp:296) — one slot, and it re-pushes
    /// <c>m_prevChar</c> rather than remembering what was consumed. Calling it twice pushes back
    /// the same character once.
    /// </summary>
    /// <remarks>
    /// <b>The guard on <c>_eof</c> is a deliberate divergence, and the only one in this class.</b>
    /// The C++ pushes back unconditionally, but at end of input <c>m_prevChar</c> still holds the
    /// last real character — and the <c>m_savedChar</c> test at the top of <c>m_nextChar</c> runs
    /// before the <c>m_eof</c> test, so that character is handed out again. Every scanner that
    /// backspaces after a failed lookahead (<c>m_convertDecimal</c>, <c>m_convertHex</c>, and each
    /// two-character operator) therefore loops forever on a source file whose final character is a
    /// digit or an operator. GPDLcomp.exe hangs on such a file rather than compiling it; real files
    /// end with a newline, so it has never been hit. Reproducing a hang has no value, and it makes
    /// the port unusable from a test.
    /// </remarks>
    private void Backspace()
    {
        if (_eof) { return; }
        _savedChar = _prevChar;
    }

    /// <summary>
    /// <c>INPUTFILE::backspaceToken</c> (GPDLcomp.cpp:300). The original pops a message box and
    /// <c>exit(1)</c>s on a double backspace; here it throws, because that state is a compiler bug
    /// and silently dropping it would mis-compile.
    /// </summary>
    public void BackspaceToken()
    {
        if (_backspaceTkn != GpdlTokenType.TKN_NONE)
        {
            throw new InvalidOperationException("Internal error in backspaceToken (GPDLcomp.cpp:302)");
        }
        _backspaceTkn = _latestTkn;
    }

    private void ConvertHex()
    {
        _integer = 0;
        while (true)
        {
            char c = NextChar();
            if (c >= '0' && c <= '9') { _integer = _integer * 16 + c - '0'; }
            else if (c >= 'A' && c <= 'F') { _integer = _integer * 16 + c - 'A' + 10; }
            else if (c >= 'a' && c <= 'f') { _integer = _integer * 16 + c - 'a' + 10; }
            else { Backspace(); return; }
        }
    }

    private void ConvertDecimal(char c)
    {
        _integer = 0;
        while (c >= '0' && c <= '9')
        {
            _integer = _integer * 10 + c - '0';
            c = NextChar();
        }
        Backspace();
    }

    /// <summary>
    /// <c>INPUTFILE::m_getString</c> (GPDLcomp.cpp:411). Only <c>\n</c> is a real escape; every
    /// other backslash escape yields the character itself, which is how <c>$GREP</c> patterns get
    /// their <c>\b</c> through to the regex engine as a literal <c>b</c>... no: as the character
    /// <c>b</c>. A pattern written <c>"\\bhi\\b"</c> in talk.txt therefore reaches the compiler as
    /// <c>\bhi\b</c>, which is what the regex engine wants — the doubling is required.
    /// </summary>
    private GpdlTokenType ReadStringLiteral()
    {
        _token.Clear();
        char c;
        while ((c = NextChar()) != '\n')
        {
            if (c == (char)0) { break; }
            if (c == '\\')
            {
                c = NextChar();
                if (c == '\n' || c == (char)0) { return GpdlTokenType.TKN_NONE; }
                _token.Append(c == 'n' ? '\n' : c);
                continue;
            }
            if (c == '"') { return GpdlTokenType.TKN_STRING; }
            _token.Append(c);
        }
        return GpdlTokenType.TKN_NONE;   // ran into end-of-line: strings cannot span lines
    }

    private GpdlTokenType GetRawToken()
    {
        _token.Clear();
        if (_skipLine)
        {
            _skipLine = false;
            while (true)
            {
                char sc = NextChar();
                if (sc == (char)0) { return GpdlTokenType.TKN_NONE; }
                if (sc == '\n') { break; }
            }
            Backspace();
        }

        while (true)
        {
            char c = NextChar();
            char nextc;
            if (c == (char)0) { return GpdlTokenType.TKN_NONE; }
            if (IsWhitespace(c)) { continue; }
            _token.Append(c);

            if (IsInitialChar(c))
            {
                while (true)
                {
                    c = NextChar();
                    if (c == (char)0) { return GpdlTokenType.TKN_NAME; }
                    if (IsMoreChar(c)) { _token.Append(c); }
                    else { Backspace(); return GpdlTokenType.TKN_NAME; }
                }
            }

            if (c >= '0' && c <= '9')
            {
                if (c == '0')
                {
                    c = NextChar();
                    if (c is 'X' or 'x') { ConvertHex(); }
                    else { Backspace(); ConvertDecimal('0'); }
                }
                else { ConvertDecimal(c); }
                return GpdlTokenType.TKN_INTEGER;
            }

            switch (c)
            {
                case '(': return GpdlTokenType.TKN_OPENPAREN;
                case ')': return GpdlTokenType.TKN_CLOSEPAREN;
                case ';': return GpdlTokenType.TKN_SEMICOLON;
                case '{': return GpdlTokenType.TKN_OPENBRACE;
                case '}': return GpdlTokenType.TKN_CLOSEBRACE;
                case ',': return GpdlTokenType.TKN_COMMA;
                case '[': return GpdlTokenType.TKN_OPENBRACKET;
                case ']': return GpdlTokenType.TKN_CLOSEBRACKET;
                case ':': return GpdlTokenType.TKN_COLON;
                case '"': return ReadStringLiteral();
            }

            if (c == '*')
            {
                nextc = NextChar();
                if (nextc == '#') { Set("*#"); return GpdlTokenType.TKN_nGEAR; }
                Backspace();
                return GpdlTokenType.TKN_GEAR;
            }
            if (c == '/')
            {
                nextc = NextChar();
                if (nextc == '/') { _token.Append('/'); return GpdlTokenType.TKN_DOUBLESLASH; }
                if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nSLASH; }
                Backspace();
                return GpdlTokenType.TKN_SLASH;
            }
            if (c == '%')
            {
                nextc = NextChar();
                if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nPERCENT; }
                Backspace();
                return GpdlTokenType.TKN_PERCENT;
            }
            if (c == '+')
            {
                nextc = NextChar();
                if (nextc == '#') { Set("+#"); return GpdlTokenType.TKN_nPLUS; }
                Backspace();
                return GpdlTokenType.TKN_PLUS;
            }
            if (c == '-')
            {
                nextc = NextChar();
                if (nextc == '#') { Set("-#"); return GpdlTokenType.TKN_nMINUS; }
                if (nextc >= '0' && nextc <= '9')
                {
                    // A digit directly after '-' makes it a *unary numeric* minus and the digit is
                    // pushed back, so "-5" lexes as TKN_nMINUS then TKN_INTEGER 5
                    // (GPDLcomp.cpp:561). "a - 5" still lexes as TKN_MINUS because of the space.
                    Set("-");
                    Backspace();
                    return GpdlTokenType.TKN_nMINUS;
                }
                Backspace();
                return GpdlTokenType.TKN_MINUS;
            }
            if (c == '&')
            {
                nextc = NextChar();
                if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nAND; }
                if (nextc == '&') { _token.Append('&'); return GpdlTokenType.TKN_LAND; }
                Backspace();
                // NOTE: no return here in the original (GPDLcomp.cpp:586) -- control falls through
                // the remaining tests and a lone '&' ends up returning TKN_NONE, i.e. a syntax
                // error. Same for '^' below. Do not "fix" this into TKN_nAND.
            }
            if (c == '!')
            {
                nextc = NextChar();
                if (nextc == '=')
                {
                    _token.Append('=');
                    nextc = NextChar();
                    if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nNOTEQUAL; }
                    Backspace();
                    return GpdlTokenType.TKN_NOTEQUAL;
                }
                Backspace();
                return GpdlTokenType.TKN_NOT;
            }
            if (c == '|')
            {
                nextc = NextChar();
                if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nOR; }
                if (nextc == '|') { _token.Append('|'); return GpdlTokenType.TKN_LOR; }
                Backspace();
                // A lone '|' returns TKN_NOT, not TKN_nOR -- almost certainly a copy/paste slip in
                // GPDLcomp.cpp:620, but it is what the shipped compiler does.
                return GpdlTokenType.TKN_NOT;
            }
            if (c == '^')
            {
                nextc = NextChar();
                if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nXOR; }
                Backspace();
                // Falls through, as with '&'.
            }
            if (c == '=')
            {
                nextc = NextChar();
                if (nextc == '=')
                {
                    _token.Append('=');
                    nextc = NextChar();
                    if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nISEQUAL; }
                    Backspace();
                    return GpdlTokenType.TKN_ISEQUAL;
                }
                if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nEQUAL; }
                Backspace();
                return GpdlTokenType.TKN_EQUAL;
            }
            if (c == '<')
            {
                nextc = NextChar();
                if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nLESS; }
                if (nextc == '=')
                {
                    _token.Append('=');
                    nextc = NextChar();
                    if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nLESSEQUAL; }
                    Backspace();
                    return GpdlTokenType.TKN_LESSEQUAL;
                }
                Backspace();
                return GpdlTokenType.TKN_LESS;
            }
            if (c == '>')
            {
                nextc = NextChar();
                if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nGREATER; }
                if (nextc == '=')
                {
                    _token.Append('=');
                    nextc = NextChar();
                    if (nextc == '#') { _token.Append('#'); return GpdlTokenType.TKN_nGREATEREQUAL; }
                    Backspace();
                    return GpdlTokenType.TKN_GREATEREQUAL;
                }
                Backspace();
                return GpdlTokenType.TKN_GREATER;
            }
            if (c == '#')
            {
                nextc = NextChar();
                Backspace();
                if (!IsInitialChar(nextc))
                {
                    // The original backspaces a *second* time here (GPDLcomp.cpp:709). Because
                    // pushback is a single slot holding m_prevChar, that is a no-op: only nextc
                    // comes back. Reproduced by simply not backspacing again.
                    return GpdlTokenType.TKN_POUND;
                }
                // The '#' is already in _token, and the pragma name is appended to it, so
                // "#PUBLIC" arrives as the token text "#PUBLIC".
                while (true)
                {
                    c = NextChar();
                    if (c == (char)0) { return GpdlTokenType.TKN_PRAGMA; }
                    if (IsMoreChar(c)) { _token.Append(c); }
                    else { Backspace(); return GpdlTokenType.TKN_PRAGMA; }
                }
            }
            return GpdlTokenType.TKN_NONE;
        }
    }

    private void Set(string text)
    {
        _token.Clear();
        _token.Append(text);
    }

    /// <summary>
    /// <c>INPUTFILE::NextToken</c> (GPDLcomp.cpp:730): honours a pending
    /// <see cref="BackspaceToken"/> and swallows <c>//</c> comments.
    /// </summary>
    public GpdlTokenType NextToken()
    {
        if (_backspaceTkn != GpdlTokenType.TKN_NONE)
        {
            _latestTkn = _backspaceTkn;
            _backspaceTkn = GpdlTokenType.TKN_NONE;
            return _latestTkn;
        }
        do
        {
            _latestTkn = GetRawToken();
            if (_latestTkn == GpdlTokenType.TKN_DOUBLESLASH) { _skipLine = true; }
        }
        while (_latestTkn == GpdlTokenType.TKN_DOUBLESLASH);
        return _latestTkn;
    }
}
