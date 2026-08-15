using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$DelimitedStringFilter</c>, and the self-delimiting strings it works on.
/// </summary>
/// <remarks>
/// Pure string work — the only one of the remaining calls that needs nothing from the game at all.
/// </remarks>
public class GpdlDelimitedStringTests
{
    private static string Run(string body)
    {
        var compiler = new GpdlCompiler();
        Assert.True(compiler.Compile("$PUBLIC $FUNC f() { " + body + " } f;") == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler),
                                        new GpdlUnhostedEnvironment());
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    /// <summary>
    /// The first character is the delimiter, not part of the content.
    /// </summary>
    /// <remarks>
    /// <b>There is no separate argument saying how to split.</b> A string states its own delimiter
    /// by leading with it, so two strings can only be compared field-by-field if they happen to
    /// agree — and nothing checks that they do.
    /// </remarks>
    [Theory]
    [InlineData("|a|b|c", new[] { "a", "b", "c" })]
    [InlineData(",a,b", new[] { "a", "b" })]
    [InlineData("|solo", new[] { "solo" })]
    [InlineData("xaxbxc", new[] { "a", "b", "c" })]
    public void The_first_character_is_the_delimiter(string text, string[] expected) =>
        Assert.Equal(expected, GpdlDelimitedString.Fields(text));

    /// <summary>
    /// A trailing delimiter produces an empty last field.
    /// </summary>
    /// <remarks>
    /// The split is driven by the separators, not the content — so <c>"|a|"</c> is two fields, one
    /// of them empty, and a design writing a trailing separator has an extra field it may not
    /// expect.
    /// </remarks>
    [Fact]
    public void A_trailing_delimiter_leaves_an_empty_field()
    {
        Assert.Equal(["a", ""], GpdlDelimitedString.Fields("|a|"));
        Assert.Equal(["", "", ""], GpdlDelimitedString.Fields("|||"));
        Assert.Equal([""], GpdlDelimitedString.Fields("|"));
    }

    /// <summary>An empty string has no fields — there is not even a delimiter to read.</summary>
    [Fact]
    public void An_empty_string_has_no_fields() =>
        Assert.Empty(GpdlDelimitedString.Fields(string.Empty));

    /// <summary>The filter removes the fields it names, and keeps the rest.</summary>
    [Fact]
    public void AndNot_keeps_what_the_filter_does_not_name()
    {
        Assert.Equal("|a|c",
                     Run("""$RETURN $DelimitedStringFilter("|a|b|c", "|b", "AndNot");"""));

        // Several at once, and order follows the source rather than the filter.
        Assert.Equal("|b",
                     Run("""$RETURN $DelimitedStringFilter("|a|b|c", "|c|a", "AndNot");"""));
    }

    /// <summary>
    /// The result carries the source's delimiter, so it can be filtered again.
    /// </summary>
    [Fact]
    public void The_result_is_delimited_like_the_source()
    {
        Assert.Equal(",a,c",
                     Run("""$RETURN $DelimitedStringFilter(",a,b,c", ",b", "AndNot");"""));

        // Even when the filter used a different one -- the two need not agree.
        Assert.Equal(",a,c",
                     Run("""$RETURN $DelimitedStringFilter(",a,b,c", "|b", "AndNot");"""));
    }

    /// <summary>Removing everything answers the empty string, not the bare delimiter.</summary>
    [Fact]
    public void Removing_everything_answers_empty() =>
        Assert.Equal(string.Empty,
                     Run("""$RETURN $DelimitedStringFilter("|a|b", "|a|b", "AndNot");"""));

    /// <summary>Comparison is exact — case included, and no partial matches.</summary>
    [Fact]
    public void Fields_are_compared_exactly()
    {
        // The reference compares with memcmp, so case matters and nothing is trimmed.
        Assert.Equal("|Fire|fire ",
                     Run("""$RETURN $DelimitedStringFilter("|Fire|fire|fire ", "|fire", "AndNot");"""));
    }

    /// <summary>
    /// An unrecognised function is echoed back as the answer.
    /// </summary>
    /// <remarks>
    /// <b>Not an error code a script could tell from data.</b> The reference's last line is
    /// <c>return function;</c>, so <c>$DelimitedStringFilter("|a", "|b", "Or")</c> answers
    /// <c>"Or"</c> — and a design that mistypes <c>AndNot</c> gets its own typo back looking like a
    /// one-field result.
    /// </remarks>
    [Theory]
    [InlineData("Or")]
    [InlineData("andnot")]
    [InlineData("And Not")]
    public void An_unrecognised_function_is_echoed_back(string function) =>
        Assert.Equal(function,
                     Run($"""$RETURN $DelimitedStringFilter("|a|b", "|b", "{function}");"""));

    /// <summary>
    /// An empty source answers empty whatever the function is.
    /// </summary>
    /// <remarks>
    /// The length check comes before the function is looked at, so the echo above does not happen
    /// for one — the only case where a bad function name is not visible in the answer.
    /// </remarks>
    [Fact]
    public void An_empty_source_answers_empty_even_for_a_bad_function()
    {
        Assert.Equal(string.Empty,
                     Run("""$RETURN $DelimitedStringFilter("", "|b", "AndNot");"""));
        Assert.Equal(string.Empty,
                     Run("""$RETURN $DelimitedStringFilter("", "|b", "Or");"""));
    }

    /// <summary>An empty filter removes nothing, and the source comes back unchanged.</summary>
    [Fact]
    public void An_empty_filter_removes_nothing() =>
        Assert.Equal("|a|b",
                     Run("""$RETURN $DelimitedStringFilter("|a|b", "", "AndNot");"""));
}
