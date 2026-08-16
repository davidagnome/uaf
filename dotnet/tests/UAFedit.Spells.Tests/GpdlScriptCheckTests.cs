using UAF.Scripting;

namespace UAFedit.Spells.Tests;

/// <summary>The compile check, which is the only part of these editors that runs real code.</summary>
public class GpdlScriptCheckTests
{
    /// <remarks>
    /// The point of the wrapper: what a design stores is a bare body, which is not a compilable
    /// unit. Without the wrapper every script in every design fails.
    /// </remarks>
    [Fact]
    public void A_bare_body_compiles_only_because_it_is_wrapped()
    {
        const string body = "$RETURN 1;";

        Assert.True(GpdlScriptCheck.SpecialAbility(body).Succeeded);
        Assert.True(GpdlScriptCheck.Spell(body).Succeeded);

        // ...and the same text on its own does not.
        var raw = new GpdlCompiler();
        Assert.NotEqual(0, raw.Compile(body));
    }

    [Fact]
    public void The_two_wrappers_use_the_editors_own_entry_points()
    {
        Assert.Contains("$PUBLIC $FUNC spelltest()",
                        GpdlScriptCheck.Spell("$RETURN 1;").Wrapped);
        Assert.Contains("$PUBLIC $FUNC SpecAbTest()",
                        GpdlScriptCheck.SpecialAbility("$RETURN 1;").Wrapped);
    }

    /// <remarks>
    /// A body whose last line is a comment would swallow the closing brace without the newline the
    /// reference's wrappers both carry.
    /// </remarks>
    [Fact]
    public void A_body_ending_in_a_comment_still_closes()
    {
        Assert.True(GpdlScriptCheck.Spell("$RETURN 1; // done").Succeeded);
    }

    [Fact]
    public void A_broken_body_reports_errors()
    {
        var result = GpdlScriptCheck.SpecialAbility("$IF (");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        Assert.NotEmpty(result.Summary);
    }

    /// <remarks>
    /// The right answer for a slot the designer left blank, and it saves every caller a special
    /// case — the callers themselves decide not to bother.
    /// </remarks>
    [Fact]
    public void An_empty_body_compiles()
    {
        Assert.True(GpdlScriptCheck.Spell(string.Empty).Succeeded);
    }

    /// <remarks>
    /// <c>GpdlLexer.Error</c> stops recording after twelve and appends a bare full stop, so a badly
    /// broken script reports a dozen problems rather than hundreds.
    /// </remarks>
    [Fact]
    public void The_error_list_is_capped_by_the_lexer()
    {
        var result = GpdlScriptCheck.Spell(string.Join("\n", Enumerable.Repeat("$IF (", 60)));

        Assert.False(result.Succeeded);
        Assert.True(result.Errors.Count <= 13, $"got {result.Errors.Count} errors");
    }
}
