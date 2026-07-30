using System.Text;

namespace UAF.Scripting;

/// <summary>
/// The <c>CStringA</c> operations that GPDL's semantics actually depend on, reproduced exactly.
/// </summary>
/// <remarks>
/// <para>
/// Both halves of GPDL are written against MFC's <c>CString</c>, which in these projects is
/// <c>CStringA</c> (<c>CharacterSet=MultiByte</c>). Three of its behaviours differ from the
/// obvious .NET equivalent, and each one changes observable script results:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Ordering compares bytes, not UTF-16 code units.</b> <c>CStringA::operator&lt;</c> is
/// <c>strcmp</c>, which the C standard defines over <c>unsigned char</c>. Decoding a legacy design
/// into UTF-16 reorders anything above 0x7F: byte 0x80 in Windows-1252 is U+20AC, which sorts
/// <i>above</i> byte 0xFF (U+00FF). <c>$LESS</c>/<c>$GREATER</c> (GPDLexec.cpp:4326, 4541) would
/// therefore disagree with the original on any non-ASCII text. <see cref="CompareBytes"/> encodes
/// back to the codepage and compares there.
/// </description></item>
/// <item><description>
/// <b><c>Mid</c>/<c>Left</c>/<c>Right</c> clamp instead of throwing.</b> <c>$MIDDLE</c>
/// (GPDLexec.cpp:4815) feeds them script-supplied integers that are routinely out of range, and
/// the original quietly returns a short or empty string. <c>Substring</c> would throw.
/// </description></item>
/// <item><description>
/// <b><c>MakeUpper</c>/<c>MakeLower</c> are ASCII-only here.</b> They call <c>_strupr</c>, which
/// is CRT-locale sensitive; the engine never calls <c>setlocale</c>, so the locale is "C" and only
/// a-z/A-Z are mapped. .NET's <c>ToUpperInvariant</c> would additionally fold accented Latin-1
/// letters, changing <c>$UpCase</c> (GPDLexec.cpp:6048) and the <c>$GREP</c> case-folding.
/// </description></item>
/// </list>
/// </remarks>
public static class MfcString
{
    private static readonly Encoding Cp1252;

    static MfcString()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1252 = Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// The single-byte codepage every GPDL string is really in. Shared with
    /// <c>UAF.Serialization.MfcArchiveReader.DefaultEncoding</c>; kept separate only so
    /// UAF.Scripting need not depend on UAF.Serialization.
    /// </summary>
    public static Encoding Encoding => Cp1252;

    /// <summary>
    /// <c>strcmp</c> semantics: byte-wise unsigned comparison of the codepage encodings.
    /// Returns a negative number, zero, or a positive number like <c>strcmp</c> does.
    /// </summary>
    public static int CompareBytes(string a, string b)
    {
        byte[] x = Cp1252.GetBytes(a);
        byte[] y = Cp1252.GetBytes(b);
        int n = Math.Min(x.Length, y.Length);
        for (int i = 0; i < n; i++)
        {
            if (x[i] != y[i])
            {
                return x[i] < y[i] ? -1 : 1;
            }
        }
        return x.Length.CompareTo(y.Length);
    }

    /// <summary>
    /// <c>CStringT::Mid(iFirst, nCount)</c>, clamps transcribed from ATL's <c>cstringt.h</c>.
    /// Note the order of the clamps: when <c>iFirst</c> is past the end, the third clamp makes
    /// <c>nCount</c> negative and the fourth resets it to zero — reordering them changes results.
    /// </summary>
    public static string Mid(string s, int iFirst, int nCount)
    {
        int len = s.Length;
        if (iFirst < 0) { iFirst = 0; }
        if (nCount < 0) { nCount = 0; }
        if (iFirst + nCount > len) { nCount = len - iFirst; }
        if (iFirst > len) { nCount = 0; }
        if (nCount <= 0) { return string.Empty; }
        return s.Substring(iFirst, nCount);
    }

    /// <summary><c>CStringT::Left(nCount)</c>.</summary>
    public static string Left(string s, int nCount)
    {
        if (nCount < 0) { nCount = 0; }
        if (nCount >= s.Length) { return s; }
        return s[..nCount];
    }

    /// <summary><c>CStringT::Right(nCount)</c>.</summary>
    public static string Right(string s, int nCount)
    {
        if (nCount < 0) { nCount = 0; }
        if (nCount >= s.Length) { return s; }
        return s[(s.Length - nCount)..];
    }

    /// <summary>
    /// <c>CStringT::Find(ch, iStart)</c> — returns an absolute index, or -1. A start index past
    /// the end yields -1 rather than throwing, which the delimited-string subops rely on.
    /// </summary>
    public static int Find(string s, char ch, int iStart)
    {
        if (iStart < 0) { iStart = 0; }
        if (iStart >= s.Length) { return -1; }
        return s.IndexOf(ch, iStart);
    }

    /// <summary>ASCII-only upcase, matching <c>_strupr</c> in the "C" locale.</summary>
    public static string MakeUpper(string s)
    {
        char[] c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
        {
            if (c[i] >= 'a' && c[i] <= 'z') { c[i] = (char)(c[i] - 32); }
        }
        return new string(c);
    }

    /// <summary>ASCII-only downcase, matching <c>_strlwr</c> in the "C" locale.</summary>
    public static string MakeLower(string s)
    {
        char[] c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
        {
            if (c[i] >= 'A' && c[i] <= 'Z') { c[i] = (char)(c[i] + 32); }
        }
        return new string(c);
    }
}
