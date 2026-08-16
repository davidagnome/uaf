using UAFcore;

namespace UAFedit.Databases.Tests;

/// <summary>
/// Finding a shipped design to test against, and admitting when there is not one.
/// </summary>
/// <remarks>
/// <c>reference/</c> is gitignored, so on a fresh checkout none of this exists. Every corpus test
/// returns early rather than failing — and the premise test in each file is what stops the whole
/// file passing while proving nothing, which this codebase has been caught by more than once.
/// </remarks>
internal static class DatabaseCorpus
{
    /// <summary>The design directory, or null on a checkout without the corpus.</summary>
    /// <remarks>
    /// Walks up from the test binary looking for <c>src/Shared</c> — the C++ reference tree, which
    /// is the one directory guaranteed to sit at the repository root. The same walk as
    /// <c>UAFcore.Tests</c> and <c>UAFedit.Map.Tests</c>, deliberately.
    /// </remarks>
    public static string? Root(string name)
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

    /// <summary>The corpus designs, for the tests that want to see both.</summary>
    /// <remarks>
    /// <c>Case</c> earns its place: its levels are numbered with gaps and at least one file
    /// disagrees with its own name, so it is the design most likely to hold something the happy
    /// path does not.
    /// </remarks>
    public static readonly string[] Names = ["SomethingWild.dsn", "Case.dsn"];

    /// <summary>
    /// A design opened with no image decoder and no font rasteriser, or null when absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both decoders are optional and the database editors draw no art — the same reason
    /// <c>MainWindowViewModel.Open</c> passes neither, which is what keeps the editor free of SDL.
    /// </para>
    /// <para>
    /// <b>Nothing is caught.</b> Null means "no corpus on this checkout", and only that. A design
    /// that is present and will not open is a failure, not a skip — swallowing it here would put
    /// the whole file back to passing while proving nothing, which is exactly what the premise
    /// tests exist to prevent.
    /// </para>
    /// </remarks>
    public static LoadedDesign? Open(string name = "SomethingWild.dsn") =>
        Root(name) is { } root ? LoadedDesign.Open(root) : null;
}
