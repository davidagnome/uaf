using UAFcore;

namespace UAFedit.Spells.Tests;

/// <summary>
/// Finds the corpus designs, which are gitignored and usually absent.
/// </summary>
/// <remarks>
/// <b>Every test over these must early-return when the design is missing</b>, and that is exactly
/// how a file of tests comes to pass while proving nothing. The premise tests in
/// <see cref="SpellDatabaseCorpusTests"/> and <see cref="SpecialAbilityCorpusTests"/> are the guard:
/// they assert the design loaded and its databases are the size they are known to be, so a corpus
/// that silently stopped being found fails loudly instead of turning the file green.
/// </remarks>
internal static class Corpus
{
    /// <summary>How many spells <c>SomethingWild.dsn</c> has.</summary>
    public const int SomethingWildSpells = 377;

    /// <summary>How many special abilities <c>SomethingWild.dsn</c> has.</summary>
    public const int SomethingWildAbilities = 441;

    /// <summary>
    /// How many spells <c>Case.dsn</c> has.
    /// </summary>
    /// <remarks>
    /// A different format version — 2.53 against <c>SomethingWild</c>'s 3.55 — which is the point
    /// of testing against it.
    /// </remarks>
    public const int CaseSpells = 318;

    /// <summary>
    /// Opens a corpus design by directory name, or null when it is not on this machine.
    /// </summary>
    /// <remarks>
    /// Walks up from the test binary looking for the repository — a directory holding
    /// <c>src/Shared</c>, the C++ reference tree — because the test's working directory is somewhere
    /// under <c>bin/</c> and the corpus is beside the sources.
    /// <para>
    /// <b>Both decoders are left null</b>, as <c>MainWindowViewModel.Open</c> leaves them: nothing
    /// here draws art or measures text, and passing null keeps SDL out of this test project.
    /// </para>
    /// </remarks>
    public static LoadedDesign? Open(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string root = Path.Combine(dir.FullName, "reference", name);

        return Directory.Exists(root) ? LoadedDesign.Open(root) : null;
    }

    public static LoadedDesign? SomethingWild() => Open("SomethingWild.dsn");

    public static LoadedDesign? Case() => Open("Case.dsn");
}
