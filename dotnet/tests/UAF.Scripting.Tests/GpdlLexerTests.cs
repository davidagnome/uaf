using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// Token-level behaviour of <see cref="GpdlLexer"/>, concentrating on the places where the
/// original's rules are surprising enough that a reasonable reimplementation would get them wrong.
/// </summary>
public class GpdlLexerTests
{
    private static List<(GpdlTokenType Type, string Text)> Lex(string source)
    {
        var lexer = new GpdlLexer(GpdlLexer.SplitLines(source));
        var result = new List<(GpdlTokenType, string)>();
        GpdlTokenType t;
        while ((t = lexer.NextToken()) != GpdlTokenType.TKN_NONE)
        {
            result.Add((t, lexer.Token));
        }
        return result;
    }

    private static List<GpdlTokenType> Types(string source) => [.. Lex(source).Select(x => x.Type)];

    [Fact]
    public void Dollar_and_underscore_start_a_name()
    {
        Assert.Equal(
            [GpdlTokenType.TKN_NAME, GpdlTokenType.TKN_NAME, GpdlTokenType.TKN_NAME],
            Types("$SAY _local Name9"));
        Assert.Equal("$SAY", Lex("$SAY _local")[0].Text);
    }

    [Fact]
    public void At_sign_continues_a_name_so_qualified_lookups_are_one_token()
    {
        // localLookup splits on '@' itself (GPDLcomp.cpp:1718), so "outer@inner" must arrive whole.
        var tokens = Lex("outer@inner");
        Assert.Single(tokens);
        Assert.Equal("outer@inner", tokens[0].Text);
    }

    [Fact]
    public void Hex_and_decimal_integers_are_converted_by_the_lexer()
    {
        var lexer = new GpdlLexer(GpdlLexer.SplitLines("0x1F 42 0\n"));
        Assert.Equal(GpdlTokenType.TKN_INTEGER, lexer.NextToken());
        Assert.Equal(31, lexer.Integer);
        Assert.Equal(GpdlTokenType.TKN_INTEGER, lexer.NextToken());
        Assert.Equal(42, lexer.Integer);
        Assert.Equal(GpdlTokenType.TKN_INTEGER, lexer.NextToken());
        Assert.Equal(0, lexer.Integer);
    }

    [Fact]
    public void Minus_directly_before_a_digit_is_the_numeric_unary_minus()
    {
        // "-5" is TKN_nMINUS then TKN_INTEGER 5: the digit is pushed back (GPDLcomp.cpp:561). This
        // is how a negative literal gets a SUBOP_nNEGATE rather than becoming part of the number.
        var tokens = Lex("-5");
        Assert.Equal(GpdlTokenType.TKN_nMINUS, tokens[0].Type);
        Assert.Equal("-", tokens[0].Text);
        Assert.Equal(GpdlTokenType.TKN_INTEGER, tokens[1].Type);
    }

    [Fact]
    public void Minus_followed_by_space_is_plain_minus()
    {
        // The distinction is purely lexical -- whitespace changes the operator. "a-5" and "a - 5"
        // therefore compile to different code.
        Assert.Equal(GpdlTokenType.TKN_MINUS, Lex("a - 5")[1].Type);
        Assert.Equal(GpdlTokenType.TKN_nMINUS, Lex("a -5")[1].Type);
    }

    [Theory]
    [InlineData("+#", GpdlTokenType.TKN_nPLUS)]
    [InlineData("-#", GpdlTokenType.TKN_nMINUS)]
    [InlineData("*#", GpdlTokenType.TKN_nGEAR)]
    [InlineData("/#", GpdlTokenType.TKN_nSLASH)]
    [InlineData("%#", GpdlTokenType.TKN_nPERCENT)]
    [InlineData("&#", GpdlTokenType.TKN_nAND)]
    [InlineData("|#", GpdlTokenType.TKN_nOR)]
    [InlineData("^#", GpdlTokenType.TKN_nXOR)]
    [InlineData("=#", GpdlTokenType.TKN_nEQUAL)]
    [InlineData("==#", GpdlTokenType.TKN_nISEQUAL)]
    [InlineData("!=#", GpdlTokenType.TKN_nNOTEQUAL)]
    [InlineData("<#", GpdlTokenType.TKN_nLESS)]
    [InlineData("<=#", GpdlTokenType.TKN_nLESSEQUAL)]
    [InlineData(">#", GpdlTokenType.TKN_nGREATER)]
    [InlineData(">=#", GpdlTokenType.TKN_nGREATEREQUAL)]
    [InlineData("==", GpdlTokenType.TKN_ISEQUAL)]
    [InlineData("!=", GpdlTokenType.TKN_NOTEQUAL)]
    [InlineData("&&", GpdlTokenType.TKN_LAND)]
    [InlineData("||", GpdlTokenType.TKN_LOR)]
    [InlineData("<=", GpdlTokenType.TKN_LESSEQUAL)]
    [InlineData(">=", GpdlTokenType.TKN_GREATEREQUAL)]
    [InlineData("!", GpdlTokenType.TKN_NOT)]
    public void Numeric_and_logical_operator_spellings(string source, GpdlTokenType expected)
    {
        Assert.Equal(expected, Lex(source)[0].Type);
    }

    [Fact]
    public void A_lone_ampersand_or_caret_is_not_a_token_at_all()
    {
        // GPDLcomp.cpp:586 and :630 fall through with no return, so control reaches the final
        // "return TKN_NONE" and the character is silently dropped as a syntax error. A port that
        // helpfully returned TKN_nAND here would accept source the real compiler rejects.
        Assert.Empty(Lex("&"));
        Assert.Empty(Lex("^"));
    }

    [Fact]
    public void A_lone_pipe_lexes_as_NOT()
    {
        // Almost certainly a copy/paste slip at GPDLcomp.cpp:620, but it is what ships.
        Assert.Equal(GpdlTokenType.TKN_NOT, Lex("|")[0].Type);
    }

    [Fact]
    public void Double_slash_comments_run_to_end_of_line()
    {
        Assert.Equal(
            [GpdlTokenType.TKN_NAME, GpdlTokenType.TKN_NAME],
            Types("alpha // beta gamma\ndelta\n"));
    }

    [Fact]
    public void Only_backslash_n_is_a_real_escape()
    {
        // Every other backslash escape yields the character itself, which is why a $GREP pattern in
        // talk.txt has to be written "\\bhi\\b" to reach the regex engine as \bhi\b.
        Assert.Equal("a\nb", Lex("\"a\\nb\"")[0].Text);
        Assert.Equal("\\bhi", Lex("\"\\\\bhi\"")[0].Text);
        Assert.Equal("q\"z", Lex("\"q\\\"z\"")[0].Text);
    }

    [Fact]
    public void An_unterminated_string_ends_the_token_stream()
    {
        // Strings cannot span lines; m_getString returns TKN_NONE, which the parser reads as EOF.
        Assert.Empty(Lex("\"no closing quote\n"));
    }

    [Fact]
    public void Pragma_carries_the_hash_in_its_text()
    {
        var tokens = Lex("#PUBLIC");
        Assert.Equal(GpdlTokenType.TKN_PRAGMA, tokens[0].Type);
        Assert.Equal("#PUBLIC", tokens[0].Text);
    }

    [Fact]
    public void Hash_not_followed_by_a_name_character_is_a_bare_pound()
    {
        Assert.Equal(GpdlTokenType.TKN_POUND, Lex("# ")[0].Type);
    }

    [Fact]
    public void A_token_at_end_of_line_is_reported_on_the_following_line()
    {
        // The name scanner has to look one character past the token to know it ended, and that
        // character is the newline -- which increments the counter. Pushback restores the character
        // but not the count (GPDLcomp.cpp:288 vs :296), so a name in the last column reports the
        // next line. This is why a compiler diagnostic sometimes points one line too far, and it is
        // load-bearing for comparing error text against the reference.
        var lexer = new GpdlLexer(GpdlLexer.SplitLines("a\nb\nc\n"));
        lexer.NextToken();
        Assert.Equal(2, lexer.LineNumber);
        lexer.NextToken();
        Assert.Equal(3, lexer.LineNumber);
        lexer.NextToken();
        Assert.Equal(4, lexer.LineNumber);
    }

    [Fact]
    public void A_token_followed_by_more_text_reports_its_own_line()
    {
        // The common case: nothing has consumed the newline yet.
        var lexer = new GpdlLexer(GpdlLexer.SplitLines("alpha(\nbeta\n"));
        lexer.NextToken();
        Assert.Equal(1, lexer.LineNumber);
    }

    [Fact]
    public void SplitLines_drops_carriage_returns_and_keeps_newlines()
    {
        Assert.Equal(["a\n", "b\n"], GpdlLexer.SplitLines("a\r\nb\r\n"));
        // Trailing text with no newline still becomes a line, matching PROGRAM_TEXT::Initialize.
        Assert.Equal(["a\n", "b"], GpdlLexer.SplitLines("a\nb"));
    }

    [Fact]
    public void Backspacing_two_tokens_is_a_compiler_bug_and_throws()
    {
        var lexer = new GpdlLexer(GpdlLexer.SplitLines("a b\n"));
        lexer.NextToken();
        lexer.BackspaceToken();
        Assert.Throws<InvalidOperationException>(lexer.BackspaceToken);
    }
}
