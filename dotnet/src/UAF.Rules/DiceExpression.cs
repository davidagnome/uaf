namespace UAF.Rules;

/// <summary>
/// Evaluates a <c>DICEPLUS</c> expression — the little arithmetic language a design writes its
/// numbers in (<c>DICEPLUS::Roll</c>, <c>class.cpp:2193</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every dice expression in a modern design is one of these.</b> The <c>DP2</c> form carries
/// nothing but the source text — all 164 spell-effect expressions in <c>SomethingWild</c> and all
/// 150 in <c>ci-tier3</c> are <c>DP2</c> — so without an evaluator no spell can produce a number.
/// The packed numeric fields of the older <c>DP0</c>/<c>DP1</c> forms are dead in practice.
/// </para>
/// <para>
/// <b>This is a façade, not a second evaluator.</b> It once had a recursive-descent parser of its
/// own, written against the grammar the spell corpus showed — integer literals, <c>NdS</c>, the
/// identifier <c>level</c>, four operators, unary minus and parentheses. That grammar was right
/// about spells and wrong about the rest of the corpus, so the parsing now happens in
/// <see cref="DiceFormula"/>, which transcribes <c>RDRCOMP</c> and <c>RDREXEC</c> directly. What
/// stays here is the spell side's shape: a per-die roller, a nullable answer, and
/// <see cref="Maximum"/>.
/// </para>
/// <para>
/// Two things this used to get wrong, both now the reference's behaviour:
/// <b>trailing text does not fail an expression</b> — the compiler's loop breaks on a token it
/// cannot read, so <c>1.5*level</c> in <c>ci-tier3</c> is <c>1</c> and not "unreadable" — and
/// <b>a name the lookup does not know fails the whole expression</b> rather than counting as zero,
/// because <c>compileDicePlusRDR</c> returns an error and nothing gets compiled at all.
/// </para>
/// </remarks>
public static class DiceExpression
{
    /// <summary>
    /// Evaluates an expression, rolling any dice in it.
    /// </summary>
    /// <param name="text">The expression source, from <c>DicePlus.Text</c>.</param>
    /// <param name="dice">A roller: given sides, returns 1..sides.</param>
    /// <param name="lookup">
    /// Resolves an identifier — in practice only <c>level</c>, the caster's spell-casting level.
    /// Null for a name it does not know, which refuses the whole expression: an unresolvable name
    /// is a compile error in the reference, not a zero term.
    /// </param>
    /// <returns>The value, or null when the expression produced nothing.</returns>
    /// <remarks>
    /// <para>
    /// <b>The arithmetic is integer throughout, and that is not a rounding detail.</b>
    /// <c>RDREXEC::InterpretExpression</c> works on an <c>int</c> stack, so division truncates at
    /// every step. A design writing <c>6-(1/4*LEVEL)</c> gets <c>6</c> at every level, because
    /// <c>1/4</c> is zero before the multiply ever happens. That expression is in <c>ci-tier3</c>.
    /// </para>
    /// <para>
    /// <b>There are no fractional literals, and designs write them anyway.</b> A decimal point is
    /// nothing the tokeniser recognises. Where a term was expected — <c>.5*level</c>, in all four
    /// designs — the compile fails and the expression contributes nothing, which is the null here.
    /// <b>Where an operator was expected it just ends the expression</b>, so <c>1.5*level</c> in
    /// <c>ci-tier3</c> is <c>1</c>. Neither is what the designer meant; only one of them is
    /// nothing.
    /// </para>
    /// </remarks>
    public static int? Evaluate(string text, Func<int, int> dice,
                                Func<string, int?>? lookup = null)
    {
        ArgumentNullException.ThrowIfNull(dice);

        return DiceFormula.TryEvaluate(text, (count, sides) => Roll(count, sides, dice),
                                       lookup ?? (_ => null), out int value, out _)
            ? value
            : null;
    }

    /// <summary>
    /// The largest value an expression can produce, taking every die at its maximum
    /// (<c>DICEPLUS::MaxRoll</c>, <c>class.cpp:2261</c>).
    /// </summary>
    /// <remarks>
    /// The reference runs the identical compiled expression through a second interpreter whose only
    /// difference is that the dice callback returns <c>sides * num</c> instead of rolling. Same
    /// here: a maximising roller.
    /// </remarks>
    public static int? Maximum(string text, Func<string, int?>? lookup = null) =>
        Evaluate(text, sides => sides, lookup);

    /// <summary>
    /// Rolls <paramref name="count"/> dice of <paramref name="sides"/>
    /// (<c>RollDice</c>, <c>Globals.cpp:4925</c>).
    /// </summary>
    /// <remarks>
    /// <b>Zero or fewer sides, or zero or fewer dice, yields nothing at all</b> — the reference
    /// returns the bonus alone before rolling anything. So <c>1d0</c>, which two shipped designs
    /// actually contain, is zero rather than an error or a one.
    /// </remarks>
    public static int Roll(int count, int sides, Func<int, int> dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (sides <= 0 || count <= 0)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < count; i++)
        {
            total += dice(sides);
        }

        return total;
    }
}
