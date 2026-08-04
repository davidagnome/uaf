using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers the dice-field expression evaluator.
/// </summary>
/// <remarks>
/// The reference compiles these through 13,146 lines of GPDL. The corpus only ever uses integers,
/// <c>NdM</c>, <c>+ − * /</c>, parentheses and one identifier, so this covers every shipped design
/// and refuses the rest by name.
/// </remarks>
public class DiceFormulaTests
{
    /// <summary>Every die shows its top face, so a total is a fact rather than a range.</summary>
    private static int Max(int count, int sides) => count * sides;

    /// <summary>Every die shows a 1.</summary>
    private static int Min(int count, int _) => count;

    private static int Evaluate(string text, Func<int, int, int>? roll = null, int male = 1)
    {
        bool ok = DiceFormula.TryEvaluate(text, roll ?? Max,
                                          n => n == DiceFormula.MaleSymbol ? male : null,
                                          out int value, out string? why);

        Assert.True(ok, why);
        return value;
    }

    private static string Refused(string text)
    {
        bool ok = DiceFormula.TryEvaluate(text, Max,
                                          n => n == DiceFormula.MaleSymbol ? 1 : null,
                                          out int value, out string? why);

        Assert.False(ok);
        Assert.Equal(0, value);
        Assert.NotNull(why);
        return why!;
    }

    // ---- the shapes the corpus contains --------------------------------------------------------

    [Fact]
    public void An_empty_expression_did_not_roll_rather_than_rolling_zero()
    {
        // Compile fails on an empty string and Roll returns FALSE with its result left at 0 --
        // the same answer AbilityRoll treats as a zero-scoring attempt. Nineteen per cent of the
        // corpus's dice fields are empty, so this is the common case.
        Assert.False(DiceFormula.TryEvaluate("", Max, _ => null, out int value, out string? why));
        Assert.Equal(0, value);
        Assert.Contains("empty", why);

        Assert.False(DiceFormula.TryEvaluate(null, Max, _ => null, out _, out _));
        Assert.False(DiceFormula.TryEvaluate("   ", Max, _ => null, out _, out _));
    }

    [Fact]
    public void A_constant_is_itself()
    {
        Assert.Equal(12, Evaluate("12"));
        Assert.Equal(250, Evaluate("250"));
    }

    [Fact]
    public void A_bare_NdM_rolls()
    {
        Assert.Equal(100, Evaluate("1D100"));
        Assert.Equal(1, Evaluate("1D100", Min));      // one die showing 1
        Assert.Equal(12, Evaluate("2d6"));
        Assert.Equal(2, Evaluate("2d6", Min));        // two dice showing 1
    }

    [Fact]
    public void Arithmetic_follows_the_usual_precedence()
    {
        Assert.Equal(2 + (3 * 4), Evaluate("2+3*4"));
        Assert.Equal((2 + 3) * 4, Evaluate("(2+3)*4"));
        Assert.Equal(1, Evaluate("7-3*2"));
    }

    [Fact]
    public void Dice_bind_tighter_than_multiplication()
    {
        // 2d2*Male is (2d2)*Male, which is what the corpus means by it -- the dice are a primary,
        // not an operator at multiplying precedence.
        Assert.Equal(4, Evaluate("2d2*Male", Max, male: 1));
        Assert.Equal(0, Evaluate("2d2*Male", Max, male: 0));
    }

    [Fact]
    public void The_one_identifier_any_design_uses_resolves()
    {
        // Two dice at 1, plus 34, plus the gender bonus.
        Assert.Equal(37, Evaluate("2d4+34+(1*Male)", Min, male: 1));
        Assert.Equal(36, Evaluate("2d4+34+(1*Male)", Min, male: 0));
    }

    /// <summary>Every distinct shape the corpus's races and classes contain.</summary>
    public static TheoryData<string, int, int> Corpus => new()
    {
        // text, value with every die at 1 and Male=1, value with Male=0
        { "10", 10, 10 },
        { "100", 100, 100 },
        { "12", 12, 12 },
        { "1D100", 1, 1 },
        { "1d100+100", 101, 101 },
        { "1d20+175", 176, 176 },
        { "1d4+18", 19, 19 },
        { "250", 250, 250 },
        { "2d100+250", 252, 252 },
        { "1d10+41+2*Male", 44, 42 },
        { "1d6+36+2*Male", 39, 37 },
        { "2d10+59+1*Male", 62, 61 },
        { "2d19+59+(0*Male)", 61, 61 },
        { "2d4+34+(1*Male)", 37, 36 },
        { "2d4+57+(5*Male)", 64, 59 },
        { "2d5+40+(2d2*Male)", 44, 42 },
        { "2d6+48+(6*Male)", 56, 50 },
        { "2d4+28+((3+1d3)*Male)", 34, 30 },
        { "1d9+59+((1+1d5)*Male)", 62, 60 },
        { "2d12+50+((1+1d5)*Male)", 54, 52 },
        { "2d7+152+((14+1d6)*Male)", 169, 154 },
        { "10d9+100+((40+6d9)*Male)", 156, 110 },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Every_shape_the_corpus_contains_evaluates(string text, int male, int female)
    {
        // With every die showing a 1, so the arithmetic around the dice is what is being checked.
        Assert.Equal(male, Evaluate(text, Min, male: 1));
        Assert.Equal(female, Evaluate(text, Min, male: 0));
    }

    // ---- what is refused, and by name ----------------------------------------------------------

    [Fact]
    public void An_unknown_identifier_is_named_rather_than_guessed()
    {
        // The day a design uses a real reference it says so, rather than quietly rolling a number
        // that is wrong.
        string why = Refused("1d6+CharLevel");

        Assert.Contains("CharLevel", why);
        Assert.Contains("does not resolve", why);
    }

    [Fact]
    public void A_bare_dM_is_refused_rather_than_read_as_one_die()
    {
        // No shipped design writes one, and inventing the implicit 1 would be guessing at a
        // convention rather than transcribing it.
        Assert.Contains("d6", Refused("d6"));
    }

    [Fact]
    public void Malformed_expressions_say_what_and_where()
    {
        Assert.Contains("unclosed", Refused("(2+3"));
        Assert.Contains("ends where a value was expected", Refused("2+"));
        Assert.Contains("unexpected", Refused("2 3"));
        Assert.Contains("unexpected", Refused("2%3"));
    }

    [Fact]
    public void Division_by_zero_is_refused_rather_than_thrown()
    {
        Assert.Contains("division by zero", Refused("6/0"));
    }

    [Fact]
    public void A_refusal_leaves_the_value_at_zero()
    {
        Assert.False(DiceFormula.TryEvaluate("1d6+Nope", Max, _ => null, out int value, out _));
        Assert.Equal(0, value);
    }

    // ---- odds and ends ---------------------------------------------------------------------------

    [Fact]
    public void Whitespace_and_a_leading_minus_are_taken()
    {
        Assert.Equal(-6, Evaluate(" - 2 * 3 "));
        Assert.Equal(4, Evaluate("1d4 + 0"));
    }

    [Fact]
    public void Division_is_integer_division()
    {
        Assert.Equal(3, Evaluate("7/2"));
    }
}
