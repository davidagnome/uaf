using UAF.Rules;

namespace UAF.Rules.Tests;

/// <summary>Covers the little arithmetic language a design writes its dice expressions in.</summary>
public class DiceExpressionTests
{
    /// <summary>A roller that always shows the same face, so an expression is deterministic.</summary>
    private static Func<int, int> Face(int face) => _ => face;

    /// <summary>A roller that always shows the maximum, matching the reference's MaxRoll.</summary>
    private static Func<int, int> Max => sides => sides;

    private static int? Eval(string text, int face = 3, int level = 5) =>
        DiceExpression.Evaluate(text, Face(face),
                                name => name.Equals("level", StringComparison.OrdinalIgnoreCase)
                                    ? level : null);

    // ---- literals and dice ---------------------------------------------------------------------

    [Theory]
    [InlineData("1", 1)]
    [InlineData("0", 0)]
    [InlineData("200", 200)]
    [InlineData("-11", -11)]
    [InlineData("-1", -1)]
    public void A_plain_number_is_itself(string text, int expected)
    {
        Assert.Equal(expected, Eval(text));
    }

    [Fact]
    public void A_dice_term_rolls_once_per_die()
    {
        Assert.Equal(4 * 3, Eval("4d8", face: 3));
    }

    [Fact]
    public void A_dice_term_takes_a_bonus()
    {
        Assert.Equal((2 * 3) + 1, Eval("2d8+1", face: 3));
    }

    [Fact]
    public void A_leading_minus_negates_the_whole_dice_term()
    {
        Assert.Equal(-(1 * 3), Eval("-1d8", face: 3));
    }

    [Fact]
    public void A_zero_sided_die_yields_nothing()
    {
        // RollDice returns the bonus alone when sides or count is not positive. Two shipped designs
        // contain 1d0.
        Assert.Equal(0, Eval("1d0"));
    }

    [Fact]
    public void Dice_are_case_insensitive_because_the_designs_are()
    {
        Assert.Equal(Eval("2d8"), Eval("2D8"));
    }

    // ---- operators and precedence --------------------------------------------------------------

    [Fact]
    public void Multiplication_binds_tighter_than_addition()
    {
        Assert.Equal(1 + (2 * 3), Eval("1+2*3"));
    }

    [Fact]
    public void Parentheses_override_precedence()
    {
        Assert.Equal((1 + 2) * 3, Eval("(1+2)*3"));
    }

    [Fact]
    public void A_negated_parenthesised_group_negates_everything_inside_it()
    {
        // -(2d8+1) is one of the commonest forms in the shipped designs.
        Assert.Equal(-((2 * 3) + 1), Eval("-(2d8+1)", face: 3));
    }

    [Fact]
    public void Subtraction_is_left_associative()
    {
        Assert.Equal(10 - 3 - 2, Eval("10-3-2"));
    }

    [Fact]
    public void Whitespace_is_ignored()
    {
        Assert.Equal(Eval("2d8+1"), Eval("  2d8 + 1 "));
    }

    // ---- the level identifier ------------------------------------------------------------------

    [Fact]
    public void Level_resolves_through_the_lookup()
    {
        Assert.Equal(5, Eval("level", level: 5));
        Assert.Equal(5, Eval("LEVEL", level: 5));
    }

    [Fact]
    public void A_name_the_lookup_does_not_know_is_zero_rather_than_a_failure()
    {
        // The reference logs "Illegal RDR code" and returns 0, so the expression still produces a
        // number.
        Assert.Equal(7, DiceExpression.Evaluate("7+something", Face(3)));
    }

    [Fact]
    public void A_dice_term_scaled_by_level_is_the_commonest_scaling_form()
    {
        Assert.Equal(-(1 * 3) * 5, Eval("-(1d6)*level", face: 3, level: 5));
    }

    [Fact]
    public void Nested_parentheses_around_a_level_expression_evaluate_inside_out()
    {
        // -(1d4+1)*((level+1)/2) at level 5 with a face of 3: -(3+1) * (6/2) = -12.
        Assert.Equal(-12, Eval("-(1d4+1)*((level+1)/2)", face: 3, level: 5));
    }

    // ---- integer arithmetic --------------------------------------------------------------------

    [Fact]
    public void Division_truncates_at_every_step()
    {
        Assert.Equal(0, Eval("1/4"));
        Assert.Equal(1, Eval("7/4"));
    }

    [Fact]
    public void A_design_that_divides_before_multiplying_loses_the_whole_term()
    {
        // 6-(1/4*LEVEL) is in ci-tier3. The 1/4 truncates to zero before the multiply, so the
        // expression is 6 at every level -- almost certainly not what its author meant, and what
        // the shipped engine does.
        Assert.Equal(6, Eval("6-(1/4*LEVEL)", level: 1));
        Assert.Equal(6, Eval("6-(1/4*LEVEL)", level: 20));
    }

    [Fact]
    public void Dividing_by_zero_yields_zero_rather_than_faulting()
    {
        Assert.Equal(0, Eval("5/0"));
    }

    // ---- failure -------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("(1+2")]
    [InlineData("1+")]
    [InlineData("*3")]
    [InlineData("2d")]
    public void A_malformed_expression_is_null_rather_than_a_throw(string text)
    {
        Assert.Null(Eval(text));
    }

    [Theory]
    [InlineData(".5*level")]
    [InlineData("1.5*level")]
    public void A_fractional_literal_does_not_parse_and_the_expression_does_nothing(string text)
    {
        // The reference's tokeniser has no decimal point: digits accumulate into an int and a '.'
        // falls through to the operator table, matching nothing. The compile fails, and
        // DICEPLUS::Roll returns false with its result already zeroed -- so the expression
        // silently contributes nothing. Both of these are in shipped designs.
        Assert.Null(Eval(text));
    }

    [Fact]
    public void Only_one_unary_sign_is_allowed()
    {
        // "We allow only one unary operator. Do you want more?" -- GPDLcomp.cpp:4341.
        Assert.Null(Eval("--5"));
        Assert.Equal(-5, Eval("-5"));
    }

    [Fact]
    public void Trailing_rubbish_is_a_failure_not_a_silent_truncation()
    {
        Assert.Null(Eval("1+2)"));
    }

    // ---- the maximum ---------------------------------------------------------------------------

    [Fact]
    public void The_maximum_takes_every_die_at_its_top_face()
    {
        Assert.Equal((2 * 8) + 1, DiceExpression.Maximum("2d8+1"));
    }

    [Fact]
    public void The_maximum_is_the_same_expression_with_a_maximising_roller()
    {
        Assert.Equal(DiceExpression.Evaluate("-(3d8+3)", Max),
                     DiceExpression.Maximum("-(3d8+3)"));
    }

    // ---- rolling -------------------------------------------------------------------------------

    [Fact]
    public void Rolling_no_dice_or_no_sides_yields_nothing()
    {
        Assert.Equal(0, DiceExpression.Roll(0, 8, Face(3)));
        Assert.Equal(0, DiceExpression.Roll(3, 0, Face(3)));
        Assert.Equal(0, DiceExpression.Roll(-2, 8, Face(3)));
    }
}
