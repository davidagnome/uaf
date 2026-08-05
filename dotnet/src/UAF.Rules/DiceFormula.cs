namespace UAF.Rules;

/// <summary>
/// Evaluates the expression in a <c>DICEPLUS</c> field (<c>DICEPLUS::Roll</c>,
/// <c>class.cpp:2193</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>These expressions are compiled and interpreted, and this is a transcription of both halves.</b>
/// <c>DICEPLUS::Compile</c> hands the text to <c>RDRCOMP::CompileExpression</c>
/// (<c>GPDLcomp.cpp:4517</c>), which emits a postfix byte string that
/// <c>RDREXEC::InterpretExpression</c> (<c>GPDLexec.cpp:8249</c>) walks over a stack. Those two
/// functions are small and self-contained — the 13,000 lines around them are the <i>script</i>
/// compiler, <c>GPDLCOMP</c>, which a dice field never touches. This class follows
/// <c>RDRCOMP</c>'s tokeniser, its operator table and its precedence loop, then interprets the
/// result the way <c>RDREXEC</c> does.
/// </para>
/// <para>
/// <b>The arithmetic is integer.</b> <c>InterpretExpression</c>'s stack is <c>int stk[40]</c>;
/// <c>DICEPLUS::Roll</c> only widens the answer to a double at the end. So <c>level/2</c> at level
/// three is 1, not 1.5.
/// </para>
/// <para>
/// <b><c>|&lt;</c> and <c>&gt;|</c> are operators, not a bracket syntax.</b> <c>3|&lt;3d6&gt;|18</c>
/// is <c>3 |&lt; 3d6 &gt;| 18</c> — a floor and a ceiling applied by two ordinary binary operators
/// at the table's lowest priority, so each takes everything to its right. That matters, because the
/// corpus writes <c>3|&lt;3d6&gt;|19+(Race_Elf*1)</c>: the ceiling is itself an expression, and a
/// reader that treats the bars as delimiters around a literal drops the racial maximum entirely.
/// </para>
/// <para>
/// <b>An unrecognised character ends the expression silently.</b> <c>m_EvaluateExpression</c>'s
/// loop does <c>if (tokenType == CTKN_NONE) break;</c> — no error — so <c>1.5*level</c> compiles to
/// just <c>1</c>. The same character where a <i>term</i> was expected is an error, so
/// <c>.5*level</c> fails to compile and the roll returns nothing. Both forms are in the shipped
/// corpus and they do not mean the same thing.
/// </para>
/// <para>
/// <b>What the corpus actually contains</b> (8,880 expressions across every field that carries one
/// — ability rolls, class strength bonuses, the five race dice, spell parameters, durations and
/// effect data, in all four designs under <c>reference/</c>): integers, <c>NdM</c>,
/// <c>+ - * /</c>, parentheses, unary minus, the two clamps, and exactly three kinds of name —
/// <c>Male</c>, <c>Race_&lt;name&gt;</c>, and <c>level</c> in either case. Nothing else. The
/// grammar below is wider than that because the reference's is.
/// </para>
/// </remarks>
public static class DiceFormula
{
    /// <summary>1 when the character's gender matches, else 0 (<c>class.cpp:858</c>).</summary>
    /// <remarks>Matched without regard to case, as <c>LookupRefKey</c> does.</remarks>
    public const string MaleSymbol = "Male";

    /// <inheritdoc cref="MaleSymbol"/>
    public const string FemaleSymbol = "Female";

    /// <summary>The character's level (<c>class.cpp:838</c>). Also matched without case.</summary>
    public const string LevelSymbol = "level";

    /// <summary>
    /// The prefix a race test carries: <c>Race_Elf</c> is 1 for an elf and 0 for anyone else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A name with a space or a hyphen is quoted</b> — <c>"Race_Half-Orc"</c> — because the
    /// tokeniser would otherwise stop at the punctuation. <c>compileDicePlusRDR</c> strips the
    /// quotes before looking the name up, so they are punctuation and not part of it.
    /// </para>
    /// <para>
    /// <b>The prefix is not validated against the race database.</b> <c>LookupRefKey</c> checks
    /// only that the name begins <c>Race_</c>, strips those five characters and compares whatever
    /// is left against the character's race — so a name no race has is not an error, it is a term
    /// worth zero. That is load-bearing: see <see cref="RepeatedPrefixBug"/>.
    /// </para>
    /// </remarks>
    public const string RacePrefix = "Race_";

    /// <summary>As <see cref="RacePrefix"/>, for the character's class.</summary>
    public const string ClassPrefix = "Class_";

    /// <summary>
    /// Why shipped designs contain names like <c>Race_Race_Race_Race_Dwarf</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor — and only the editor, the branch is <c>#ifdef UAFEDITOR</c> — rewrites every
    /// <c>DP2</c> expression on load through <c>EncodeOldDicePlusText</c>
    /// (<c>class.cpp:2309</c>, called at <c>class.cpp:2591</c>), which finds bare race names and
    /// prefixes them with <c>Race_</c>. Its identifier scanner is <c>ISALPHANUM</c>
    /// (<c>class.cpp:2307</c>), which is letters and digits and <b>not</b> the underscore — so on
    /// the second pass <c>Race_Dwarf</c> scans as <c>Race</c>, <c>_</c>, <c>Dwarf</c>, and
    /// <c>Dwarf</c> gets prefixed again. Every editor session adds one more <c>Race_</c>.
    /// </para>
    /// <para>
    /// <c>SomethingWild</c> carries up to 41 of them on a single name. The engine never
    /// re-encodes, so it sees the accumulated name, resolves it as a race test against a race that
    /// does not exist, and scores it zero — which means those designs' racial ability adjustments
    /// stopped applying long ago, in the reference too. <c>"Race_Half-Orc"</c> is untouched in the
    /// same files because <c>Half-Orc</c> is not one of the six names the encoder knows.
    /// </para>
    /// </remarks>
    public const string RepeatedPrefixBug =
        "an editor round-trip re-prefixes race names; the engine scores the result zero";

    /// <summary>
    /// Evaluates an expression.
    /// </summary>
    /// <param name="roll">Rolls <c>count</c> dice of <c>sides</c> and totals them.</param>
    /// <param name="symbol">
    /// Resolves a name — after quote stripping, and only for names that are not <c>NdM</c>.
    /// Returning null is <c>compileDicePlusRDR</c>'s <c>return 0</c>: the compile fails and the
    /// whole expression yields nothing, rather than the name being treated as zero.
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

        if (string.IsNullOrEmpty(text))
        {
            unsupported = "the expression is empty";
            return false;
        }

        var code = new List<Step>();
        var compiler = new Compiler(text, symbol, code);

        if (!compiler.Compile(out unsupported))
        {
            return false;
        }

        if (code.Count == 0)
        {
            // Compile() returning an empty m_Bin is a failure at class.cpp:2185, whatever the
            // compiler thought.
            unsupported = $"\"{text}\" compiled to nothing";
            return false;
        }

        value = Interpret(code, roll);
        return true;
    }

    /// <summary>
    /// The operators, with the priorities from <c>CoperDef</c> (<c>GPDLcomp.cpp:4100</c>).
    /// </summary>
    /// <remarks>
    /// <b>Order matters.</b> The tokeniser tries a two-character operator before a one-character
    /// one, so <c>&gt;|</c> must be found before <c>&gt;</c>. Listing the two-character forms first
    /// and searching in order reproduces that without a separate pass.
    /// </remarks>
    private static readonly (string Name, int Priority, Op Op)[] Operators =
    [
        ("|<", 2,  Op.Floor),        // COP_MIN -- the left side is a minimum
        (">|", 2,  Op.Ceiling),      // COP_MAX -- the right side is a maximum
        ("||", 5,  Op.LogicalOr),
        ("^^", 7,  Op.LogicalXor),
        ("&&", 10, Op.LogicalAnd),
        ("==", 30, Op.Equal),
        ("!=", 30, Op.NotEqual),
        ("<=", 35, Op.LessOrEqual),
        (">=", 35, Op.GreaterOrEqual),
        ("|",  15, Op.BitOr),
        ("^",  20, Op.BitXor),
        ("&",  25, Op.BitAnd),
        ("<",  35, Op.Less),
        (">",  35, Op.Greater),
        ("+",  40, Op.Add),
        ("-",  40, Op.Subtract),
        ("*",  45, Op.Multiply),
        ("/",  45, Op.Divide),
        ("%",  45, Op.Remainder),
        ("(",  0,  Op.OpenParen),
        (")",  0,  Op.CloseParen),
    ];

    private enum Op
    {
        None, Negate, Floor, Ceiling, LogicalOr, LogicalXor, LogicalAnd, BitOr, BitXor, BitAnd,
        Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual, Add, Subtract, Multiply,
        Divide, Remainder, OpenParen, CloseParen,
    }

    /// <summary>One entry of the postfix string <c>RDRCOMP</c> emits.</summary>
    /// <remarks>
    /// <b>A roll is flagged rather than inferred from the sides.</b> <c>1d0</c> is in the corpus,
    /// and zero sides is a legal roll that yields nothing — not a literal 1.
    /// </remarks>
    private readonly record struct Step(Op Op, int Value, int Sides, bool IsRoll)
    {
        public static Step Push(int value) => new(Op.None, value, 0, false);

        /// <summary>A <c>DICE_DB</c> reference: rolled at interpret time, not at compile time.</summary>
        public static Step Dice(int count, int sides) => new(Op.None, count, sides, true);

        public static Step Apply(Op op) => new(op, 0, 0, false);

        public bool IsPush => Op == Op.None;
    }

    /// <summary>
    /// <c>RDREXEC::InterpretExpression</c> (<c>GPDLexec.cpp:8249</c>) over an integer stack.
    /// </summary>
    /// <remarks>
    /// <b>Division and remainder by zero give zero</b> rather than throwing — the interpreter tests
    /// the divisor itself. <b><c>|&lt;</c> keeps the larger operand and <c>&gt;|</c> the smaller</b>,
    /// which is what makes the left one a floor and the right one a ceiling.
    /// </remarks>
    private static int Interpret(List<Step> code, Func<int, int, int> roll)
    {
        // stk[0] is seeded with -1, which is what an expression that pushes nothing returns.
        var stack = new List<int> { -1 };

        foreach (var step in code)
        {
            if (step.IsPush)
            {
                // RollDice (Globals.cpp:4925) returns the bonus -- zero here -- when either the
                // sides or the count is not positive, so it never reaches the caller's generator.
                stack.Add(step.IsRoll
                    ? (step.Sides <= 0 || step.Value <= 0 ? 0 : roll(step.Value, step.Sides))
                    : step.Value);
                continue;
            }

            if (step.Op == Op.Negate)
            {
                stack[^1] = -stack[^1];
                continue;
            }

            int right = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            int left = stack[^1];

            stack[^1] = step.Op switch
            {
                Op.Floor => left > right ? left : right,
                Op.Ceiling => left < right ? left : right,
                Op.LogicalOr => (left != 0 || right != 0) ? 1 : 0,
                Op.LogicalAnd => (left != 0 && right != 0) ? 1 : 0,
                Op.LogicalXor => (left != 0) != (right != 0) ? 1 : 0,
                Op.BitOr => left | right,
                Op.BitXor => left ^ right,
                Op.BitAnd => left & right,
                Op.Equal => left == right ? 1 : 0,
                Op.NotEqual => left != right ? 1 : 0,
                Op.Less => left < right ? 1 : 0,
                Op.LessOrEqual => left <= right ? 1 : 0,
                Op.Greater => left > right ? 1 : 0,
                Op.GreaterOrEqual => left >= right ? 1 : 0,
                Op.Add => left + right,
                Op.Subtract => left - right,
                Op.Multiply => left * right,
                Op.Divide => right == 0 ? 0 : left / right,
                Op.Remainder => right == 0 ? 0 : left % right,
                _ => left,
            };
        }

        return stack[^1];
    }

    /// <summary>
    /// <c>RDRCOMP</c>'s tokeniser and precedence loop (<c>GPDLcomp.cpp:4177-4514</c>).
    /// </summary>
    private sealed class Compiler(string text, Func<string, int?> symbol, List<Step> code)
    {
        private int at;
        private Token pushedBack;
        private bool hasPushedBack;

        private enum Kind { None, Name, Dice, Quoted, Integer, Operator }

        private readonly record struct Token(Kind Kind, string Text, int Integer, int Priority,
                                             Op Op);

        public bool Compile(out string? error) => Expression(out error);

        // ---- tokeniser -------------------------------------------------------------------

        // m_whitespace: space and tab only. A newline inside an expression is not skipped.
        private static bool IsSpace(char c) => c is ' ' or '\t';

        // m_initialChar: '$' and '_' as well as letters. '$' is the script language's sigil and no
        // dice field uses one, but the tokeniser accepts it.
        private static bool IsInitial(char c) =>
            c == '$' || c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        // m_moreChar: digits and '@' on top of the initial set. No hyphen -- a name with one has
        // to be quoted, which is why the corpus writes "Race_Half-Orc".
        private static bool IsMore(char c) => (c >= '0' && c <= '9') || c == '@' || IsInitial(c);

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private char Next() => at < text.Length ? text[at++] : Advance();

        private char Advance()
        {
            at++;                                   // m_nextChar steps past the end as well
            return '\0';
        }

        private void Back() => at--;

        private Token Read()
        {
            if (hasPushedBack)
            {
                hasPushedBack = false;
                return pushedBack;
            }

            return Raw();
        }

        private void PushBack(Token token)
        {
            pushedBack = token;
            hasPushedBack = true;
        }

        private Token Raw()
        {
            char c;
            while (IsSpace(c = Next()))
            {
            }

            if (c == '\0')
            {
                return new Token(Kind.None, "", 0, 0, Op.None);
            }

            if (IsInitial(c))
            {
                var name = new System.Text.StringBuilder().Append(c);
                while (IsMore(c = Next()))
                {
                    name.Append(c);
                }
                Back();
                return new Token(Kind.Name, name.ToString(), 0, 0, Op.None);
            }

            if (c == '"')
            {
                // The quotes are kept on the token here and removed by the resolver, exactly as
                // m_getRawToken keeps them and compileDicePlusRDR calls name.Remove('"').
                var quoted = new System.Text.StringBuilder().Append(c);
                while ((c = Next()) != '"' && c != '\0')
                {
                    quoted.Append(c);
                }

                if (c != '\0')
                {
                    quoted.Append('"');
                }
                else
                {
                    Back();
                }

                return new Token(Kind.Quoted, quoted.ToString(), 0, 0, Op.None);
            }

            if (IsDigit(c))
            {
                var number = new System.Text.StringBuilder();
                int integer = 0;
                do
                {
                    number.Append(c);
                    integer = (10 * integer) + (c - '0');
                }
                while (IsDigit(c = Next()));

                if (c is 'd' or 'D')
                {
                    number.Append(c);
                    while (IsDigit(c = Next()))
                    {
                        number.Append(c);
                    }
                    Back();
                    return new Token(Kind.Dice, number.ToString(), integer, 0, Op.None);
                }

                Back();
                return new Token(Kind.Integer, number.ToString(), integer, 0, Op.None);
            }

            // An operator: two characters are tried before one, so ">|" wins over ">".
            string two = c.ToString();
            char second = Next();
            if (second != '\0')
            {
                two += second;
                foreach (var (name, priority, op) in Operators)
                {
                    if (name == two)
                    {
                        return new Token(Kind.Operator, name, 0, priority, op);
                    }
                }
            }

            Back();
            string one = c.ToString();
            foreach (var (name, priority, op) in Operators)
            {
                if (name == one)
                {
                    return new Token(Kind.Operator, name, 0, priority, op);
                }
            }

            // Not an operator at all. This is CTKN_NONE, and it is how "1.5*level" stops at 1.
            return new Token(Kind.None, one, 0, 0, Op.None);
        }

        // ---- compiler --------------------------------------------------------------------

        /// <summary><c>m_EvaluateAtomicElement</c> (<c>GPDLcomp.cpp:4337</c>).</summary>
        private bool Atom(out string? error)
        {
            error = null;

            // One unary operator, no more. A leading '+' is accepted and emits nothing.
            var token = Read();
            Op unary = Op.None;

            if (token.Kind == Kind.Operator && token.Op == Op.Subtract)
            {
                unary = Op.Negate;
                token = Read();
            }
            else if (token.Kind == Kind.Operator && token.Op == Op.Add)
            {
                token = Read();
            }

            if (token.Kind == Kind.Operator && token.Op == Op.OpenParen)
            {
                if (!Expression(out error))
                {
                    return false;
                }

                var close = Read();
                if (close.Kind != Kind.Operator || close.Op != Op.CloseParen)
                {
                    error = $"expected a close parenthesis in \"{text}\"";
                    return false;
                }
            }
            else if (token.Kind == Kind.Integer)
            {
                code.Add(Step.Push(token.Integer));
            }
            else if (token.Kind is Kind.Name or Kind.Dice or Kind.Quoted)
            {
                if (!Reference(token.Text, out error))
                {
                    return false;
                }
            }
            else
            {
                error = $"\"{text}\" has no term where one was expected";
                return false;
            }

            if (unary == Op.Negate)
            {
                code.Add(Step.Apply(Op.Negate));
            }

            return true;
        }

        /// <summary><c>compileDicePlusRDR</c> (<c>class.cpp:1899</c>).</summary>
        private bool Reference(string token, out string? error)
        {
            error = null;
            string name = token.Replace("\"", "", StringComparison.Ordinal);

            if (TryDice(name, out int count, out int sides))
            {
                code.Add(Step.Dice(count, sides));
                return true;
            }

            if (symbol(name) is not int resolved)
            {
                error = $"\"{text}\" references {name}, which this port does not resolve";
                return false;
            }

            code.Add(Step.Push(resolved));
            return true;
        }

        /// <summary><c>decodeNdM</c> (<c>class.cpp:1885</c>), over the token's own text.</summary>
        /// <remarks>
        /// <b>Tried before the name lookup, and on every token kind.</b> A quoted <c>"2d6"</c> is
        /// dice; a bare <c>1d</c> is not, and falls through to the lookup as a name, where it
        /// fails.
        /// </remarks>
        private static bool TryDice(string name, out int count, out int sides)
        {
            count = 0;
            sides = 0;

            int column = 0;
            if (!TryInteger(name, ref column, out count))
            {
                return false;
            }

            if (column >= name.Length || (name[column] != 'd' && name[column] != 'D'))
            {
                return false;
            }

            column++;
            return TryInteger(name, ref column, out sides);
        }

        /// <summary><c>GetInteger</c> (<c>class.cpp:1820</c>), sign and trailing space included.</summary>
        private static bool TryInteger(string value, ref int column, out int result)
        {
            result = 0;
            int start = column;
            int sign = 1;

            while (column < value.Length && IsSpace(value[column]))
            {
                column++;
            }

            if (column < value.Length && (value[column] == '-' || value[column] == '+'))
            {
                sign = value[column] == '-' ? -1 : 1;
                column++;
                while (column < value.Length && IsSpace(value[column]))
                {
                    column++;
                }
            }

            if (column >= value.Length || !IsDigit(value[column]))
            {
                column = start;
                return false;
            }

            while (column < value.Length && IsDigit(value[column]))
            {
                result = (result * 10) + (value[column] - '0');
                column++;
            }

            while (column < value.Length && IsSpace(value[column]))
            {
                column++;
            }

            result *= sign;
            return true;
        }

        /// <summary>
        /// <c>m_EvaluateExpression</c> (<c>GPDLcomp.cpp:4427</c>) — a shunting yard over one
        /// operator stack.
        /// </summary>
        /// <remarks>
        /// <b>Equal priorities associate to the left</b>, because the drain condition is
        /// <c>&gt;=</c> and not <c>&gt;</c>. That is what makes <c>3|&lt;3d6&gt;|18</c> clamp low
        /// first and high second rather than the other way round.
        /// </remarks>
        private bool Expression(out string? error)
        {
            error = null;

            var operators = new List<(int Priority, Op Op)>();

            // A leading unary minus binds looser than anything, so it is applied last.
            var first = Read();
            if (first.Kind == Kind.Operator && first.Op == Op.Subtract)
            {
                operators.Add((999, Op.Negate));
            }
            else
            {
                PushBack(first);
            }

            while (true)
            {
                if (!Atom(out error))
                {
                    return false;
                }

                var token = Read();

                if (token.Kind == Kind.Operator && token.Op == Op.CloseParen)
                {
                    PushBack(token);
                    break;
                }

                // Anything the tokeniser did not recognise ends the expression without complaint.
                if (token.Kind != Kind.Operator)
                {
                    break;
                }

                operators.Add((token.Priority, token.Op));

                while (operators.Count > 1
                       && operators[^2].Priority >= operators[^1].Priority)
                {
                    code.Add(Step.Apply(operators[^2].Op));
                    operators.RemoveAt(operators.Count - 2);
                }
            }

            for (int i = operators.Count - 1; i >= 0; i--)
            {
                code.Add(Step.Apply(operators[i].Op));
            }

            return true;
        }
    }
}
