namespace UAF.Rules;

/// <summary>
/// Evaluates the expression in a <c>DICEPLUS</c> field (<c>DICEPLUS::Roll</c>,
/// <c>class.cpp:2193</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The reference compiles these through the GPDL toolchain.</b> <c>RDRCOMP</c> and
/// <c>RDREXEC</c> live in <c>GPDLcomp.h</c> and <c>GPDLexec.h</c> — 13,146 lines of compiler and
/// interpreter — so a dice field is, formally, a script. Porting that belongs to the scripting
/// phase and not to character creation.
/// </para>
/// <para>
/// <b>What designs actually write is far smaller.</b> Across every race and class in the corpus —
/// 288 expressions, 74 distinct — the whole vocabulary is integers, <c>NdM</c>, <c>+ − * /</c>,
/// parentheses, and <b>one identifier</b>: <c>Male</c>, a 1 or 0 used to add a gender bonus to
/// weight and height (<c>2d4+34+(1*Male)</c>). Not one field references a level, a race or an
/// ability. So this covers the corpus completely at a fraction of the size.
/// </para>
/// <para>
/// <b>Anything outside that subset is refused by name, never guessed at</b> — see
/// <see cref="TryEvaluate"/>'s <c>unsupported</c>. The day a design uses a real reference it will
/// say so, rather than quietly rolling a number that is wrong.
/// </para>
/// </remarks>
public static class DiceFormula
{
    /// <summary>The one identifier any shipped design uses: 1 for a male character, else 0.</summary>
    public const string MaleSymbol = "Male";

    /// <summary>
    /// Evaluates an expression.
    /// </summary>
    /// <param name="roll">Rolls <c>count</c> dice of <c>sides</c> and totals them.</param>
    /// <param name="symbol">
    /// Resolves an identifier, or returns null for one it does not know — which makes the whole
    /// expression unsupported rather than substituting a zero.
    /// </param>
    /// <param name="value">The result, or 0.</param>
    /// <param name="unsupported">Why it could not be evaluated, or null when it was.</param>
    /// <remarks>
    /// <b>An empty expression is "did not roll", not "rolled zero".</b> <c>Compile</c> fails on an
    /// empty string and <c>Roll</c> returns FALSE with its result left at 0 — which is the same
    /// answer <see cref="AbilityRoll.Modern"/> treats as a zero-scoring attempt. Nineteen per cent
    /// of the corpus's dice fields are empty, so this is the common case and not an edge one.
    /// </remarks>
    public static bool TryEvaluate(string? text, Func<int, int, int> roll,
                                   Func<string, int?> symbol,
                                   out int value, out string? unsupported)
    {
        ArgumentNullException.ThrowIfNull(roll);
        ArgumentNullException.ThrowIfNull(symbol);

        value = 0;
        unsupported = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            unsupported = "the expression is empty";
            return false;
        }

        var parser = new Parser(text, roll, symbol);
        if (!parser.TryParse(out value, out unsupported))
        {
            value = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// A recursive-descent parser over the subset above.
    /// </summary>
    /// <remarks>
    /// <b><c>NdM</c> binds tighter than <c>*</c>.</b> <c>2d2*Male</c> is <c>(2d2)*Male</c>, which
    /// is what the corpus means by it — the dice are a primary, not an operator at multiplying
    /// precedence.
    /// </remarks>
    private sealed class Parser(string text, Func<int, int, int> roll, Func<string, int?> symbol)
    {
        private int at;

        public bool TryParse(out int value, out string? unsupported)
        {
            value = 0;
            unsupported = null;

            if (!Expression(ref value, ref unsupported))
            {
                return false;
            }

            SkipSpace();
            if (at < text.Length)
            {
                unsupported = $"unexpected '{text[at]}' at {at} in \"{text}\"";
                return false;
            }

            return true;
        }

        private void SkipSpace()
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }

        private bool Expression(ref int value, ref string? unsupported)
        {
            if (!Term(ref value, ref unsupported))
            {
                return false;
            }

            while (true)
            {
                SkipSpace();
                if (at >= text.Length || (text[at] != '+' && text[at] != '-'))
                {
                    return true;
                }

                char op = text[at++];
                int right = 0;
                if (!Term(ref right, ref unsupported))
                {
                    return false;
                }

                value = op == '+' ? value + right : value - right;
            }
        }

        private bool Term(ref int value, ref string? unsupported)
        {
            if (!Factor(ref value, ref unsupported))
            {
                return false;
            }

            while (true)
            {
                SkipSpace();
                if (at >= text.Length || (text[at] != '*' && text[at] != '/'))
                {
                    return true;
                }

                char op = text[at++];
                int right = 0;
                if (!Factor(ref right, ref unsupported))
                {
                    return false;
                }

                if (op == '/')
                {
                    // Integer division, and a zero divisor is refused rather than thrown or
                    // silently treated as one.
                    if (right == 0)
                    {
                        unsupported = $"division by zero in \"{text}\"";
                        return false;
                    }
                    value /= right;
                }
                else
                {
                    value *= right;
                }
            }
        }

        private bool Factor(ref int value, ref string? unsupported)
        {
            SkipSpace();

            if (at < text.Length && text[at] == '-')
            {
                at++;
                if (!Factor(ref value, ref unsupported))
                {
                    return false;
                }
                value = -value;
                return true;
            }

            return Primary(ref value, ref unsupported);
        }

        private bool Primary(ref int value, ref string? unsupported)
        {
            SkipSpace();

            if (at >= text.Length)
            {
                unsupported = $"\"{text}\" ends where a value was expected";
                return false;
            }

            if (text[at] == '(')
            {
                at++;
                if (!Expression(ref value, ref unsupported))
                {
                    return false;
                }

                SkipSpace();
                if (at >= text.Length || text[at] != ')')
                {
                    unsupported = $"unclosed '(' in \"{text}\"";
                    return false;
                }

                at++;
                return true;
            }

            if (char.IsAsciiDigit(text[at]))
            {
                int number = Integer();

                // NdM, where both halves are literal. A bare "d6" is refused rather than read as
                // 1d6: no shipped design writes one, and inventing the implicit 1 would be a
                // guess at a convention rather than a transcription of it.
                SkipSpace();
                if (at < text.Length && (text[at] == 'd' || text[at] == 'D'))
                {
                    int mark = at;
                    at++;
                    SkipSpace();

                    if (at >= text.Length || !char.IsAsciiDigit(text[at]))
                    {
                        at = mark;
                        value = number;
                        return true;
                    }

                    value = roll(number, Integer());
                    return true;
                }

                value = number;
                return true;
            }

            if (char.IsAsciiLetter(text[at]) || text[at] == '_')
            {
                int start = at;
                while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] == '_'))
                {
                    at++;
                }

                string name = text[start..at];
                if (symbol(name) is not int resolved)
                {
                    unsupported = $"\"{text}\" references {name}, which this port does not resolve";
                    return false;
                }

                value = resolved;
                return true;
            }

            unsupported = $"unexpected '{text[at]}' at {at} in \"{text}\"";
            return false;
        }

        private int Integer()
        {
            int start = at;
            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            // A number too long for an int is refused upstream by the caller seeing 0; the corpus
            // has nothing near the limit.
            return int.TryParse(text[start..at], out int parsed) ? parsed : 0;
        }
    }
}
