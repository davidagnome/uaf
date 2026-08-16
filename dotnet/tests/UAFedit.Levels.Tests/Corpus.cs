using UAFcore;

namespace UAFedit.Levels.Tests;

/// <summary>
/// Finding the shipped designs the level tests run against.
/// </summary>
/// <remarks>
/// <c>reference/</c> is gitignored, so a clean checkout has none of this and every corpus test
/// returns early. <see cref="LevelCatalogTests.The_corpus_designs_really_loaded"/> is the one
/// assertion that stops that turning the whole suite green while proving nothing.
/// </remarks>
internal static class Corpus
{
    /// <summary>Ten files numbered 001-004, 011-013, 016, 018 and 255 — holes on purpose.</summary>
    internal const string Case = "Case.dsn";

    /// <summary>Eight files numbered 001-008 — no holes, so position and number agree.</summary>
    internal const string SomethingWild = "SomethingWild.dsn";

    /// <summary>The design directory, or null on a checkout without the corpus.</summary>
    /// <remarks>
    /// Walks up for a directory holding <c>src/Shared</c> — the C++ reference tree, which is in the
    /// repo — rather than counting <c>..</c> from the test binary, whose depth depends on the
    /// configuration and the target framework.
    /// </remarks>
    internal static string? Root(string name)
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
        return Directory.Exists(root) ? root : null;
    }

    /// <summary>An opened corpus design, or null.</summary>
    /// <remarks>
    /// Both decoders are left null, as <c>MainWindowViewModel.Open</c> leaves them: nothing in the
    /// level editor draws art or measures text.
    /// </remarks>
    internal static LoadedDesign? Open(string name) =>
        Root(name) is { } root ? LoadedDesign.Open(root) : null;
}
