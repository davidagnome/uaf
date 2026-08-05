using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>
/// Covers the dice-field expression evaluator.
/// </summary>
/// <remarks>
/// A transcription of <c>RDRCOMP</c> and <c>RDREXEC</c>, so these are tests of the reference's
/// behaviour rather than of a subset chosen from the corpus — including the parts of it that are
/// clearly bugs, because a design's numbers were balanced against them. <c>DiceCorpusTests</c>
/// runs the same evaluator over every expression the shipped designs actually contain.
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
    public void A_missing_term_is_an_error_but_a_missing_operator_is_not()
    {
        // m_EvaluateAtomicElement errors when it finds no term; m_EvaluateExpression's loop just
        // breaks on anything that is not an operator. So the first two are refused and the third
        // quietly evaluates its prefix.
        Assert.Contains("close parenthesis", Refused("(2+3"));
        Assert.Contains("no term", Refused("2+"));
        Assert.Equal(2, Evaluate("2 3"));
    }

    [Fact]
    public void Division_and_remainder_by_zero_give_zero()
    {
        // InterpretExpression tests the divisor itself (GPDLexec.cpp:8274) rather than dividing --
        // so this is a real answer, not a refusal and not a throw.
        Assert.Equal(0, Evaluate("6/0"));
        Assert.Equal(0, Evaluate("6%0"));
        Assert.Equal(1, Evaluate("7%2"));
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

    // ---- the clamps, which are operators and not a bracket syntax ------------------------------

    /// <summary>
    /// Resolves the three kinds of name the corpus writes: <c>Male</c>, <c>level</c> and a race
    /// test, the last of which matches only <c>Elf</c>.
    /// </summary>
    private static int? Symbols(string name) => name switch
    {
        "Male" => 1,
        "level" => 4,
        "Race_Elf" => 1,
        _ => name.StartsWith("Race_", StringComparison.Ordinal) ? 0 : null,
    };

    private static int WithSymbols(string text, Func<int, int, int> roll)
    {
        Assert.True(DiceFormula.TryEvaluate(text, roll, Symbols, out int value, out string? why),
                    why);
        return value;
    }

    [Fact]
    public void The_left_clamp_is_a_floor_and_the_right_one_a_ceiling()
    {
        // 3 |< 3d6 is max(3, roll); >| 18 is min(that, 18).
        Assert.Equal(3, WithSymbols("3|<3d6>|18", Min));       // three ones, floored at 3
        Assert.Equal(18, WithSymbols("3|<3d6>|18", Max));      // eighteen, at the ceiling
        Assert.Equal(12, WithSymbols("3|<2d6>|18", Max));      // between the two, untouched
    }

    [Fact]
    public void Equal_priorities_associate_to_the_left()
    {
        // The drain condition is >=, not >, so this is (20 |< 1d6) >| 10 and not
        // 20 |< (1d6 >| 10) -- which would answer 20.
        Assert.Equal(10, WithSymbols("20|<1d6>|10", Min));
    }

    [Fact]
    public void The_ceiling_is_an_expression_and_not_a_literal()
    {
        // The corpus writes racial ability maxima this way. Reading the bars as delimiters around
        // an integer parses the "19" and silently drops the rest of the cap.
        Assert.Equal(20, WithSymbols("3|<4d6>|19+(Race_Elf*1)", Max));
        Assert.Equal(19, WithSymbols("3|<4d6>|19+(Race_Gnome*1)", Max));
    }

    // ---- what the reference does with text it cannot read ---------------------------------------

    [Fact]
    public void An_unreadable_character_ends_the_expression_without_complaint()
    {
        // m_EvaluateExpression breaks on CTKN_NONE with no error, so a decimal point truncates the
        // expression rather than failing it. Both of these are in the shipped corpus.
        Assert.Equal(1, WithSymbols("1.5*level", Max));
        Assert.Contains("no term", Refused(".5*level"));
    }

    [Fact]
    public void A_quoted_name_is_the_name_without_its_quotes()
    {
        // compileDicePlusRDR does name.Remove('"') before looking it up. The quotes are there
        // because the tokeniser stops at the hyphen, not because they are part of the name.
        Assert.Equal(0, WithSymbols("\"Race_Half-Orc\"*2", Max));
        Assert.Equal(2, WithSymbols("\"Race_Elf\"*2", Max));
    }

    [Fact]
    public void A_die_with_no_sides_rolls_nothing()
    {
        // RollDice returns the bonus -- zero -- when the sides or the count is not positive, so
        // 1d0 never reaches the generator. SomethingWild's spell effects contain one.
        Assert.Equal(0, WithSymbols("1d0", (_, _) => 999));
        Assert.Equal(5, WithSymbols("1d0+5", (_, _) => 999));
    }

    [Fact]
    public void A_race_name_no_race_has_is_a_zero_and_not_a_failure()
    {
        // LookupRefKey checks only the Race_ prefix and never consults the race database, so an
        // accumulated name from the editor's re-encoding bug compiles and scores nothing. Every
        // ability in SomethingWild depends on this: refusing here would make each one roll zero.
        Assert.Equal(6, WithSymbols("2d3+(Race_Race_Race_Dwarf*-1)", Max));
    }

    [Fact]
    public void Level_reads_the_character_and_not_a_die()
    {
        Assert.Equal(4, WithSymbols("1*level", Max));
        Assert.Equal(2, WithSymbols("level/2", Max));
        Assert.Equal(2, WithSymbols("(level+1)/2", Max));    // integer division, so 5/2 is 2
    }
}
