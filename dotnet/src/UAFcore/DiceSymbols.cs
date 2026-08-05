using UAF.Rules;

namespace UAFcore;

/// <summary>
/// Resolves the names a <c>DICEPLUS</c> expression can contain, against one character.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>GENERIC_REFERENCE::LookupRefKey</c> (<c>class.cpp:826</c>) followed by
/// <c>LookupReferenceData</c> (<c>class.cpp:1000</c>) — the compiler decides what kind of name it
/// is, the interpreter turns it into a number, and both halves are here because the answer never
/// depends on when it is asked.
/// </para>
/// <para>
/// <b>The order is the reference's, and the length guards are too.</b> <c>Class_</c> is tested
/// before <c>Race_</c>, and each requires at least one character after the prefix — so a bare
/// <c>Race_</c> is not a race test, it is an unknown name.
/// </para>
/// <para>
/// <b>Neither prefix is checked against a database.</b> <c>Race_Nonesuch</c> is a well-formed race
/// test that no character matches, which is exactly why designs corrupted by the editor's
/// re-encoding bug still load — see <see cref="DiceFormula.RepeatedPrefixBug"/>.
/// </para>
/// <para>
/// <b>Where this stops.</b> The reference goes on to try the ability, spellgroup and trait
/// databases. Those all compile, and then <c>interpretDicePlusRDRB</c> (<c>class.cpp:1978</c>)
/// has their cases commented out, so each falls through to <c>default</c>, logs "Illegal RDR code"
/// and yields 0. Telling such a name from a misspelling needs the design's databases, and no
/// expression in any shipped design uses one, so this returns null for them: the expression is
/// refused by name rather than quietly scoring zero.
/// </para>
/// </remarks>
/// <param name="Male">The character's gender, after adjustment.</param>
/// <param name="RaceId">
/// The character's race. <c>RACE_ID</c> derives from <c>CString</c> (<c>Externs.h:1297</c>) and
/// adds no comparison of its own, so the match is case-sensitive — both sides come out of the same
/// database, so it is exact or it is a different race.
/// </param>
/// <param name="ClassId">The character's class, matched the same way.</param>
/// <param name="Level">
/// The character's level. <c>LEVEL_UNKNOWN</c> reads as 0 in the reference, and so does a level
/// that was never set here.
/// </param>
public readonly record struct DiceSymbols(bool Male, string? RaceId, string? ClassId, int Level)
{
    /// <summary>Resolves one name, or null for a name this port does not know.</summary>
    public int? Resolve(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (string.Equals(name, DiceFormula.LevelSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(Level, 0);
        }

        if (string.Equals(name, DiceFormula.MaleSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return Male ? 1 : 0;
        }

        if (string.Equals(name, DiceFormula.FemaleSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return Male ? 0 : 1;
        }

        if (name.Length > DiceFormula.ClassPrefix.Length
            && name.StartsWith(DiceFormula.ClassPrefix, StringComparison.Ordinal))
        {
            return Matches(name[DiceFormula.ClassPrefix.Length..], ClassId);
        }

        if (name.Length > DiceFormula.RacePrefix.Length
            && name.StartsWith(DiceFormula.RacePrefix, StringComparison.Ordinal))
        {
            return Matches(name[DiceFormula.RacePrefix.Length..], RaceId);
        }

        return null;
    }

    private static int Matches(string wanted, string? actual) =>
        string.Equals(wanted, actual, StringComparison.Ordinal) ? 1 : 0;

    /// <summary>The resolver as a callback, for <see cref="DiceFormula.TryEvaluate"/>.</summary>
    public Func<string, int?> Resolver => Resolve;
}
