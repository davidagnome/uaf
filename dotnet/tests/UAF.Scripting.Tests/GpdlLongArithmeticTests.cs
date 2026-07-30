using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// Tests for the decimal-string arithmetic behind <c>$PLUS</c> and friends.
/// </summary>
/// <remarks>
/// The division cases are <b>the author's own</b>, transcribed from the commented-out test block at
/// GPDLexec.cpp:8043–8088. That makes them the strongest evidence available on this platform: they
/// are a statement of intended behaviour written by the person who wrote the algorithm, including
/// the cases where it disagrees with ordinary integer division, so they cannot be dismissed as this
/// port's guesses. They also pin the two hard-coded escapes — divide-by-zero and
/// divisor-longer-than-dividend — which no amount of reading the loop would reveal.
/// </remarks>
public class GpdlLongArithmeticTests
{
    [Theory]
    // Transcribed verbatim from GPDLexec.cpp:8047 onwards.
    [InlineData("0", "5", "0", "0")]
    [InlineData("5", "0", "999999", "0")]     // divide by zero is a magic number, not a fault
    [InlineData("9", "5", "1", "4")]
    [InlineData("47", "9", "5", "2")]
    [InlineData("123456", "00987654321", "0", "123456")]
    [InlineData("123456", "00103", "1198", "62")]
    [InlineData("123456", "000183", "674", "114")]
    [InlineData("99", "11", "9", "0")]
    [InlineData("613", "614", "0", "613")]
    [InlineData("-5", "3", "-1", "-2")]
    [InlineData("5", "-3", "-1", "2")]
    [InlineData("5", "-6", "0", "5")]
    [InlineData("-44", "11", "-4", "0")]
    [InlineData("-678", "-22", "30", "-18")]
    public void Divide_matches_the_reference_implementations_own_test_cases(
        string dividend, string divisor, string quotient, string remainder)
    {
        var (q, r) = GpdlLongArithmetic.Divide(dividend, divisor);
        Assert.Equal(quotient, q);
        Assert.Equal(remainder, r);
    }

    [Theory]
    [InlineData("-678", "-22")]
    [InlineData("-5", "3")]
    [InlineData("5", "-3")]
    [InlineData("47", "9")]
    public void Division_truncates_and_the_remainder_takes_the_dividends_sign(
        string dividend, string divisor)
    {
        // The quotient sign is the XOR of the operands' and the remainder's is the dividend's, so
        // this is C's truncating division rather than a flooring one -- $MOD of a negative value is
        // negative. The identity below is what makes that claim checkable rather than asserted.
        var (q, r) = GpdlLongArithmetic.Divide(dividend, divisor);
        Assert.Equal(
            GpdlLongArithmetic.CleanNumber(dividend).TrimStart('+'),
            GpdlLongArithmetic.CleanNumber(
                GpdlLongArithmetic.Add(GpdlLongArithmetic.Multiply(q, divisor), r)).TrimStart('+'));
    }

    [Fact]
    public void A_divisor_longer_than_the_dividend_returns_the_raw_dividend_as_remainder()
    {
        // GPDLexec.cpp:8161 assigns `remainder = dividend`, i.e. the caller's string, so the leading
        // zeros survive. Every other path returns a cleaned remainder.
        var (q, r) = GpdlLongArithmetic.Divide("00123456", "987654321");
        Assert.Equal("0", q);
        Assert.Equal("00123456", r);
    }

    [Theory]
    [InlineData("999", "1", "1000")]
    [InlineData("1", "-1", "0")]
    [InlineData("-5", "-6", "-11")]
    [InlineData("-5", "3", "-2")]
    [InlineData("3", "-5", "-2")]
    [InlineData("", "", "0")]
    public void Add(string x, string y, string expected) =>
        Assert.Equal(expected, GpdlLongArithmetic.Add(x, y));

    [Theory]
    [InlineData("100", "1", "99")]
    [InlineData("1", "100", "-99")]
    [InlineData("-5", "-5", "0")]
    [InlineData("5", "-5", "10")]
    public void Subtract(string x, string y, string expected) =>
        Assert.Equal(expected, GpdlLongArithmetic.Subtract(x, y));

    [Theory]
    [InlineData("12", "12", "144")]
    [InlineData("-3", "4", "-12")]
    [InlineData("-3", "-4", "12")]
    [InlineData("0", "5", "0")]
    [InlineData("-0", "5", "0")]        // a signed zero is never rendered with the sign
    public void Multiply(string x, string y, string expected) =>
        Assert.Equal(expected, GpdlLongArithmetic.Multiply(x, y));

    [Fact]
    public void Multiply_is_genuinely_arbitrary_precision()
    {
        // src/GPDL/language.txt:57 warns that $TIMES "is perfectly capable of multiplying
        // 1000-digit numbers but it will take a while" -- unlike *#, which is hardware 32-bit.
        string big = new('9', 40);
        // (10^40 - 1)^2 = 10^80 - 2*10^40 + 1, i.e. 39 nines, an 8, 39 zeros, a 1.
        string expected = new string('9', 39) + "8" + new string('0', 39) + "1";
        Assert.Equal(80, expected.Length);
        Assert.Equal(expected, GpdlLongArithmetic.Multiply(big, big));
    }

    [Theory]
    [InlineData("3", "03", 0)]          // numerically equal, unlike the == operator
    [InlineData("3", " 3", 0)]
    [InlineData("4", "3", 1)]
    [InlineData("3", "4", -1)]
    [InlineData("-1", "1", -1)]
    public void Compare(string x, string y, int expected) =>
        Assert.Equal(expected, GpdlLongArithmetic.Compare(x, y));

    [Theory]
    [InlineData("12abc34", "+1234")]    // non-digits are dropped, not rejected
    [InlineData("3.75", "+375")]        // the decimal point vanishes; $PLUS("3.75","0") is "375"
    [InlineData("1-2", "+12")]          // a minus after a digit is not a sign
    [InlineData("-1-2", "-12")]
    [InlineData("000", "+")]            // zero cleans to a bare sign with no digits at all
    [InlineData("", "+")]
    [InlineData("0007", "+7")]
    public void CleanNumber_sanitises_rather_than_validates(string input, string expected) =>
        Assert.Equal(expected, GpdlLongArithmetic.CleanNumber(input));

    [Fact]
    public void Garbage_input_produces_a_number_silently()
    {
        // This is the single most surprising property of the family: there is no error path. A
        // typo'd variable holding "N/A" is arithmetic zero.
        Assert.Equal("0", GpdlLongArithmetic.Add("N/A", ""));
        Assert.Equal("7", GpdlLongArithmetic.Add("seven", "7"));
    }
}
