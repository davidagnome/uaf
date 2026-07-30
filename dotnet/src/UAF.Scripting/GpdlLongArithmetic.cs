using System.Text;

namespace UAF.Scripting;

/// <summary>
/// The arbitrary-precision decimal-string arithmetic behind <c>$PLUS</c>, <c>$MINUS</c>,
/// <c>$TIMES</c>, <c>$DIV</c>, <c>$MOD</c>, <c>$EQUAL</c>, <c>$LESS</c> and <c>$GREATER</c>
/// (GPDLexec.cpp:7864–8193, the <c>//ARITHMETIC</c> block).
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> <see cref="System.Numerics.BigInteger"/> with different spelling. The
/// original has behaviour a correct bignum does not, and scripts see it:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Input is sanitised, not validated.</b> <see cref="CleanNumber"/> drops every character that
/// is not a digit, and treats a <c>-</c> as a sign only while no digit has been seen yet. So
/// <c>"12abc34"</c> is 1234, <c>"3.75"</c> is 375, and <c>"1-2"</c> is 12. Nothing ever reports an
/// error.
/// </description></item>
/// <item><description>
/// <b>Division by zero yields 999999.</b> Not an error, not infinity — the literal string
/// <c>"999999"</c> with remainder <c>"0"</c> (GPDLexec.cpp:8159).
/// </description></item>
/// <item><description>
/// <b>The remainder takes the dividend's sign, and quotient/remainder are not consistent.</b> The
/// author's own test cases (left in a comment at GPDLexec.cpp:8043) assert
/// <c>-678 / -22 == 30</c> with remainder <c>-18</c>, i.e. <c>30 * -22 + -18 = -678</c> only if the
/// remainder is read as belonging to the dividend. Those test cases are transcribed into the test
/// suite as ground truth, since they are the author's statement of intent rather than this port's
/// guess.
/// </description></item>
/// <item><description>
/// <b><c>$EQUAL</c> is numeric here, contradicting src/GPDL/language.txt.</b> The spec file says
/// <c>$EQUAL("3","03")</c> is false and that <c>$nEQUAL</c> is the numeric form, but the system
/// function table maps <c>$EQUAL</c> to <c>SUBOP_iEQUAL</c> (GPDLcomp.cpp:1293), which is
/// <see cref="Compare"/> — so it is true, and there is no <c>$nEQUAL</c> function at all. The
/// string comparison the spec describes is the <c>==</c> <i>operator</i>
/// (<c>SUBOP_ISEQUAL</c>). language.txt is stale; the table is what ships.
/// </description></item>
/// </list>
/// <para>
/// The comparison direction is <i>not</i> inverted, despite how <c>SUBOP_iGREATER</c> reads:
/// GPDLexec.cpp:5701 pops the right operand first, so <c>LongCompare(right, left) &lt; 0</c> is
/// <c>left &gt; right</c>. <see cref="Compare"/> follows the plain convention — the operand order
/// is what does the work.
/// </para>
/// </remarks>
public static class GpdlLongArithmetic
{
    /// <summary>
    /// <c>CleanNumber</c> (GPDLexec.cpp:7864). Produces a sign character followed by digits with
    /// leading zeros removed, e.g. <c>"+123"</c> or <c>"-7"</c>; zero becomes <c>"+"</c> with no
    /// digits at all, which the callers rely on.
    /// </summary>
    /// <remarks>
    /// The two conditions are subtle. <c>if ((c &gt;= '1') || (col != 2))</c> is what suppresses
    /// leading zeros: a '0' is only kept once some digit has already been emitted. And
    /// <c>if ((c == '-') &amp;&amp; (col == 2))</c> means a minus is only a sign while no digit has
    /// been seen — <c>"5-3"</c> is +53, not -53.
    /// </remarks>
    public static string CleanNumber(string num)
    {
        var sb = new StringBuilder();
        char sign = '+';
        foreach (char c in num)
        {
            if (c >= '0' && c <= '9')
            {
                if (c >= '1' || sb.Length != 0) { sb.Append(c); }
            }
            if (c == '-' && sb.Length == 0) { sign = '-'; }
        }
        return sign + sb.ToString();
    }

    /// <summary>Magnitude sum of two digit strings (<c>LongPlus</c>, GPDLexec.cpp:7886).</summary>
    private static string MagnitudeAdd(string x, string y)
    {
        var digits = new List<char>();
        int carry = 0;
        int i = x.Length - 1;
        int j = y.Length - 1;
        while (i >= 0)
        {
            carry += x[i--] - '0';
            if (j >= 0) { carry += y[j--] - '0'; }
            digits.Add((char)((carry % 10) + '0'));
            carry /= 10;
        }
        while (j >= 0)
        {
            carry += y[j--] - '0';
            digits.Add((char)((carry % 10) + '0'));
            carry /= 10;
        }
        if (carry != 0) { digits.Add((char)(carry + '0')); }
        if (digits.Count == 0) { digits.Add('0'); }
        digits.Reverse();
        return new string([.. digits]);
    }

    /// <summary>
    /// Magnitude difference (<c>LongMinus</c>, GPDLexec.cpp:7914): <c>x - y</c>, returning the sign
    /// separately. When <c>y &gt; x</c> the original re-complements the buffer in place to recover
    /// the magnitude, which is reproduced here by comparing first and swapping.
    /// </summary>
    private static (char Sign, string Digits) MagnitudeSubtract(string x, string y)
    {
        int cmp = CompareMagnitude(x, y);
        if (cmp == 0) { return ('+', "0"); }
        bool negative = cmp < 0;
        if (negative) { (x, y) = (y, x); }

        var digits = new List<char>();
        int borrow = 0;
        int i = x.Length - 1;
        int j = y.Length - 1;
        while (i >= 0)
        {
            int d = x[i--] - '0' - borrow - (j >= 0 ? y[j--] - '0' : 0);
            if (d < 0) { d += 10; borrow = 1; } else { borrow = 0; }
            digits.Add((char)(d + '0'));
        }
        digits.Reverse();
        int lead = 0;
        while (lead < digits.Count - 1 && digits[lead] == '0') { lead++; }
        return (negative ? '-' : '+', new string([.. digits]).Substring(lead));
    }

    private static int CompareMagnitude(string x, string y)
    {
        if (x.Length != y.Length) { return x.Length < y.Length ? -1 : 1; }
        return string.CompareOrdinal(x, y);
    }

    /// <summary>Formats a signed result the way the original returns it: no '+', but a '-'.</summary>
    private static string Format(char sign, string digits)
    {
        if (digits.Length == 0) { digits = "0"; }
        return sign == '-' && digits != "0" ? "-" + digits : digits;
    }

    private static (char Sign, string Digits) Split(string cleaned) =>
        (cleaned[0], cleaned.Length > 1 ? cleaned[1..] : "0");

    /// <summary><c>LongAdd</c> (GPDLexec.cpp:7954).</summary>
    public static string Add(string x, string y)
    {
        var (xs, xd) = Split(CleanNumber(x));
        var (ys, yd) = Split(CleanNumber(y));
        if (xs == '-' && ys == '-') { return Format('-', MagnitudeAdd(xd, yd)); }
        if (xs == '-')
        {
            var (s, d) = MagnitudeSubtract(yd, xd);
            return Format(s, d);
        }
        if (ys == '-')
        {
            var (s, d) = MagnitudeSubtract(xd, yd);
            return Format(s, d);
        }
        return Format('+', MagnitudeAdd(xd, yd));
    }

    /// <summary><c>LongSubtract</c> (GPDLexec.cpp:7993): <c>x - y</c>.</summary>
    public static string Subtract(string x, string y)
    {
        var (xs, xd) = Split(CleanNumber(x));
        var (ys, yd) = Split(CleanNumber(y));
        if (xs == '-')
        {
            if (ys == '-')
            {
                var (s, d) = MagnitudeSubtract(yd, xd);
                return Format(s, d);
            }
            return Format('-', MagnitudeAdd(yd, xd));
        }
        if (ys == '-') { return Format('+', MagnitudeAdd(xd, yd)); }
        var (s2, d2) = MagnitudeSubtract(xd, yd);
        return Format(s2, d2);
    }

    /// <summary>
    /// <c>LongCompare</c> (GPDLexec.cpp:8032): -1, 0 or 1 by inspecting the first character of
    /// <c>x - y</c>. Note it tests for <c>'0'</c>, so any difference whose first digit is 0 —
    /// impossible after <see cref="MagnitudeSubtract"/> strips leading zeros — would read as equal.
    /// </summary>
    public static int Compare(string x, string y)
    {
        string result = Subtract(x, y);
        if (result.Length == 0) { return 0; }
        if (result[0] == '-') { return -1; }
        if (result[0] == '0') { return 0; }
        return 1;
    }

    /// <summary>
    /// <c>LongMultiply</c> (GPDLexec.cpp:8041). Schoolbook long multiplication over a digit buffer.
    /// The sign logic at the end is <c>if (rd[1] != 0) { *rd='-'; if (!(xneg ^ yneg)) rd++; }</c> —
    /// so a zero product is never signed, and only a genuinely non-empty magnitude can be negative.
    /// </summary>
    public static string Multiply(string s1, string s2)
    {
        var (xs, xd) = Split(CleanNumber(s1));
        var (ys, yd) = Split(CleanNumber(s2));

        int[] acc = new int[xd.Length + yd.Length];
        for (int i = xd.Length - 1; i >= 0; i--)
        {
            for (int j = yd.Length - 1; j >= 0; j--)
            {
                acc[i + j + 1] += (xd[i] - '0') * (yd[j] - '0');
            }
        }
        for (int k = acc.Length - 1; k > 0; k--)
        {
            acc[k - 1] += acc[k] / 10;
            acc[k] %= 10;
        }

        var sb = new StringBuilder();
        int lead = 0;
        while (lead < acc.Length - 1 && acc[lead] == 0) { lead++; }
        for (int k = lead; k < acc.Length; k++) { sb.Append((char)(acc[k] + '0')); }
        string magnitude = sb.ToString();
        if (magnitude.Length == 0) { magnitude = "0"; }

        bool negative = (xs == '-') ^ (ys == '-');
        return negative && magnitude != "0" ? "-" + magnitude : magnitude;
    }

    /// <summary>
    /// <c>LongDivide</c> (GPDLexec.cpp:8148). Truncating division on the magnitudes; the quotient
    /// carries the XOR of the signs and the <b>remainder carries the dividend's sign</b>.
    /// </summary>
    /// <remarks>
    /// Two special cases are not derivable from the algorithm and are transcribed literally:
    /// <list type="bullet">
    /// <item><description>
    /// A divisor that cleans to no digits (zero, or an all-non-digit string) returns quotient
    /// <c>"999999"</c>, remainder <c>"0"</c> (GPDLexec.cpp:8159).
    /// </description></item>
    /// <item><description>
    /// When the divisor has more digits than the dividend the function returns quotient <c>"0"</c>
    /// and remainder <c>= dividend</c> — the <b>original, unsanitised</b> string, not the cleaned
    /// one (GPDLexec.cpp:8161). So <c>$MOD("00123456", "987654321")</c> returns <c>"00123456"</c>
    /// with its leading zeros intact, while a smaller divisor returns a cleaned remainder.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static (string Quotient, string Remainder) Divide(string dividend, string divisor)
    {
        var (xs, xd) = Split(CleanNumber(dividend));
        var (ys, yd) = Split(CleanNumber(divisor));

        if (CleanNumber(divisor).Length == 1) { return ("999999", "0"); }
        if (yd.Length > xd.Length) { return ("0", dividend); }

        var quotient = new StringBuilder();
        string rem = string.Empty;
        foreach (char c in xd)
        {
            rem += c;
            int lead = 0;
            while (lead < rem.Length - 1 && rem[lead] == '0') { lead++; }
            rem = rem[lead..];

            int digit = 0;
            while (CompareMagnitude(rem, yd) >= 0)
            {
                var (_, d) = MagnitudeSubtract(rem, yd);
                rem = d;
                digit++;
            }
            quotient.Append((char)(digit + '0'));
        }

        string q = quotient.ToString();
        int ql = 0;
        while (ql < q.Length - 1 && q[ql] == '0') { ql++; }
        q = q[ql..];

        bool qNegative = (xs == '-') ^ (ys == '-');
        string quotientText = qNegative && q != "0" ? "-" + q : q;
        string remainderText = xs == '-' && rem != "0" ? "-" + rem : rem;
        return (quotientText, remainderText);
    }
}
