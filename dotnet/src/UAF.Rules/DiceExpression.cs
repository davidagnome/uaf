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
/// <b>How this differs from the reference's route to the same answer.</b> The original compiles the
/// text to an RDR expression (<c>DICEPLUS::Compile</c>) and runs it through <c>RDREXEC</c>, a small
/// postfix interpreter shared with GPDL, with dice terms dispatched to <c>RollDice</c> and
/// identifiers resolved through <c>GENERIC_REFERENCE::LookupReferenceData</c>. This evaluates the
/// same grammar directly. The compiler and its interpreter are a subsystem of their own and buy
/// nothing here — the language is small, and its operators, precedence and integer arithmetic are
/// what decide the result.
/// </para>
/// <para>
/// The grammar, taken from every distinct expression in the shipped designs: integer literals,
/// <c>NdS</c> dice terms, the identifier <c>level</c> (matched case-insensitively — designs write
/// both <c>level</c> and <c>LEVEL</c>), the four arithmetic operators, unary minus, and
/// parentheses. Real examples: <c>1</c>, <c>-1d8</c>, <c>2d8+1</c>, <c>-(1d6)*level</c>,
/// <c>-(1d4+1)*((level+1)/2)</c>, <c>6-(1/4*LEVEL)</c>.
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
    /// Returns null for a name it does not know, which evaluates to zero.
    /// </param>
    /// <returns>The value, or null when the text will not parse.</returns>
    /// <remarks>
    /// <para>
    /// <b>The arithmetic is integer throughout, and that is not a rounding detail.</b>
    /// <c>RDREXEC::InterpretExpression</c> works on an <c>int</c> stack and its dice callback
    /// returns <c>int</c>, so division truncates at every step. A design writing
    /// <c>6-(1/4*LEVEL)</c> gets <c>6</c> at every level, because <c>1/4</c> is zero before the
    /// multiply ever happens. Reproduced — this is what the shipped designs were balanced against,
    /// and one such expression is in <c>ci-tier3</c>.
    /// </para>
    /// <para>
    /// <b>There are no fractional literals, and designs write them anyway.</b> The reference's
    /// tokeniser treats only <c>'0'</c>–<c>'9'</c> as numeric (<c>GPDLcomp.cpp:4221</c>) and
    /// accumulates digits into an <c>int</c>; a decimal point falls through to the operator table,
    /// matches nothing, and the compile fails. <c>DICEPLUS::Roll</c> then returns false having set
    /// its result to zero — so <b>the expression silently contributes nothing</b>. This is not
    /// hypothetical: <c>.5*level</c> appears in all four designs checked and <c>1.5*level</c> in
    /// <c>ci-tier3</c>, and none of them do anything. Returning null here reproduces that, given a
    /// caller that treats null as no change.
    /// </para>
    /// <para>
    /// A malformed expression returns null rather than throwing. The reference logs and yields
    /// zero; a null lets the caller tell "no change" from "could not read it". <b>One deliberate
    /// difference</b>: trailing rubbish after a complete expression is rejected here, where the
    /// reference stops at the end of what it could parse and ignores the rest. No shipped
    /// expression has a tail, so nothing rests on it, and refusing is the safer default for a
    /// design being edited.
    /// </para>
    /// </remarks>
    public static int? Evaluate(string text, Func<int, int> dice,
                                Func<string, int?>? lookup = null)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parser = new Parser(text, dice, lookup);
        int? value = parser.Expression();
        return parser.Failed || !parser.AtEnd ? null : value;
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

    /// <summary>Recursive descent over the grammar in the class remarks.</summary>
    private sealed class Parser(string text, Func<int, int> dice, Func<string, int?>? lookup)
    {
        private int at;

        public bool Failed { get; private set; }

        public bool AtEnd
        {
            get
            {
                SkipSpace();
                return at >= text.Length;
            }
        }

        /// <summary>Addition and subtraction — the loosest binding.</summary>
        public int? Expression()
        {
            int? left = Term();
            if (left is null)
            {
                return null;
            }

            while (true)
            {
                SkipSpace();
                if (at >= text.Length || (text[at] != '+' && text[at] != '-'))
                {
                    return left;
                }

                char op = text[at++];
                int? right = Term();
                if (right is null)
                {
                    return null;
                }

                left = op == '+' ? left + right : left - right;
            }
        }

        /// <summary>Multiplication and division.</summary>
        private int? Term()
        {
            int? left = Factor();
            if (left is null)
            {
                return null;
            }

            while (true)
            {
                SkipSpace();
                if (at >= text.Length || (text[at] != '*' && text[at] != '/'))
                {
                    return left;
                }

                char op = text[at++];
                int? right = Factor();
                if (right is null)
                {
                    return null;
                }

                if (op == '*')
                {
                    left *= right;
                }
                else
                {
                    // Integer division, truncating -- see the remarks on Evaluate. Division by
                    // zero yields zero rather than faulting, as the interpreter's stack does.
                    left = right == 0 ? 0 : left / right;
                }
            }
        }

        /// <summary>A signed atom, possibly with a dice suffix.</summary>
        /// <remarks>
        /// <b>At most one sign</b>, as the reference is: "We allow only one unary operator. Do you
        /// want more?" (<c>GPDLcomp.cpp:4341</c>). So <c>--5</c> does not parse.
        /// </remarks>
        private int? Factor()
        {
            SkipSpace();

            if (at < text.Length && (text[at] == '-' || text[at] == '+'))
            {
                char sign = text[at++];
                int? inner = Atom();
                return inner is null ? null : sign == '-' ? -inner : inner;
            }

            return Atom();
        }

        /// <summary>An unsigned atom: a group, a number, a dice term or a name.</summary>
        private int? Atom()
        {
            SkipSpace();

            if (at < text.Length && text[at] == '(')
            {
                at++;
                int? inner = Expression();
                SkipSpace();
                if (inner is null || at >= text.Length || text[at] != ')')
                {
                    Failed = true;
                    return null;
                }

                at++;
                return inner;
            }

            if (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                return Number();
            }

            if (at < text.Length && (char.IsAsciiLetter(text[at]) || text[at] == '_'))
            {
                return Identifier();
            }

            Failed = true;
            return null;
        }

        /// <summary>An integer, or the dice count of an <c>NdS</c> term.</summary>
        private int? Number()
        {
            int start = at;
            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            if (!int.TryParse(text.AsSpan(start, at - start), out int value))
            {
                Failed = true;
                return null;
            }

            // A `d` immediately after the count makes this a dice term. Case-insensitive because
            // the designs are inconsistent about it, as they are about `level`.
            if (at < text.Length && (text[at] == 'd' || text[at] == 'D'))
            {
                at++;
                int sidesStart = at;
                while (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    at++;
                }

                if (at == sidesStart || !int.TryParse(text.AsSpan(sidesStart, at - sidesStart),
                                                      out int sides))
                {
                    Failed = true;
                    return null;
                }

                return Roll(value, sides, dice);
            }

            // A decimal point is not part of a number here, and that is not an oversight -- see
            // the remarks on Evaluate. Refusing it is what makes the whole expression fail, which
            // is what the reference does with it.
            if (at < text.Length && text[at] == '.')
            {
                Failed = true;
                return null;
            }

            return value;
        }

        /// <summary>A named value — <c>level</c>, in practice.</summary>
        private int? Identifier()
        {
            int start = at;
            while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] == '_'))
            {
                at++;
            }

            string name = text[start..at];

            // An unknown name is zero, not a failure: the reference logs "Illegal RDR code" and
            // returns 0, so an expression naming something it cannot resolve still produces a
            // number.
            return lookup?.Invoke(name) ?? 0;
        }

        private void SkipSpace()
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }
    }
}
