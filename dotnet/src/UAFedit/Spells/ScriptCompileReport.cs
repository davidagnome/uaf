namespace UAFedit.Spells;

/// <summary>One script that would not compile.</summary>
/// <param name="Owner">The spell or special ability it belongs to.</param>
/// <param name="Script">Which of that owner's scripts — a slot name, or an entry name.</param>
public sealed record ScriptFailure(string Owner, string Script, string Errors);

/// <summary>
/// What compiling a whole database's scripts found.
/// </summary>
/// <remarks>
/// <b>Shared by both editors because the reference has the sweep on only one of them.</b>
/// <c>Test All Special Abilities</c> exists (<c>ID_TEST_SPECIAL_ABILITIES</c>,
/// <c>UAFWinEd/MainFrm.cpp:1926</c>); there is no equivalent for spell scripts, which could only be
/// checked one at a time through the script dialog's <c>Test Syntax</c> button. Running the same
/// sweep over the spells costs nothing extra and answers the same question.
/// </remarks>
/// <param name="Owners">How many spells or abilities carried at least one script.</param>
/// <param name="Scripts">How many scripts were compiled in total.</param>
public sealed record ScriptCompileReport(
    int Owners, int Scripts, IReadOnlyList<ScriptFailure> Failures)
{
    public static ScriptCompileReport Empty { get; } = new(0, 0, []);

    public bool AllCompiled => Failures.Count == 0;

    public string Summary(string ownerNoun) =>
        Scripts == 0
            ? "No scripts to compile."
            : $"{Scripts} scripts in {Owners} {ownerNoun}; "
              + (AllCompiled ? "all compiled." : $"{Failures.Count} failed.");
}
