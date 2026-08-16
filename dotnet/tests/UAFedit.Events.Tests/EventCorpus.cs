using UAF.Serialization;
using UAFcore;

namespace UAFedit.Events.Tests;

/// <summary>
/// The reference designs, when they are present.
/// </summary>
/// <remarks>
/// <para>
/// <c>reference/</c> is gitignored, so every test that wants a real design must survive its
/// absence. The walk-up-for-<c>src/Shared</c> discovery is the same one
/// <c>UAFcore.Tests/GameScriptHostRosterTests</c> uses; sharing the shape matters more than
/// sharing the code, because the failure mode is a suite that passes by doing nothing.
/// </para>
/// <para>
/// <b>That failure mode is guarded explicitly.</b>
/// <see cref="EventEditorCorpusTests.The_corpus_loads_a_level_with_events"/> asserts a level really
/// arrived with events in it, so a run where the corpus is present but unreadable fails rather
/// than quietly skipping.
/// </para>
/// </remarks>
public static class EventCorpus
{
    /// <summary>The designs these tests read, in the order the histogram reports them.</summary>
    public static readonly string[] Designs = ["SomethingWild.dsn", "Case.dsn"];

    /// <summary>The repository root, found by walking up for <c>src/Shared</c>.</summary>
    public static string? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName;
    }

    /// <summary>
    /// Opens a reference design, or null when it is not on this machine.
    /// </summary>
    /// <remarks>
    /// The <c>Data/</c> check rather than a bare directory check is deliberate: some names under
    /// <c>reference/</c> are extracted archives with no design in them, and
    /// <see cref="LoadedDesign.Open"/> throws on those rather than returning null.
    /// </remarks>
    public static LoadedDesign? Open(string name)
    {
        if (Root() is not { } root)
        {
            return null;
        }

        string path = Path.Combine(root, "reference", name);

        return Directory.Exists(Path.Combine(path, "Data")) ? LoadedDesign.Open(path) : null;
    }

    /// <summary>Every level of a design that reads, with its file index.</summary>
    public static IEnumerable<(int Index, LevelFile Level)> Levels(LoadedDesign design)
    {
        for (int i = 0; i < design.LevelFiles.Count; i++)
        {
            if (design.Level(i) is { } level)
            {
                yield return (i, level);
            }
        }
    }

    /// <summary>
    /// The first level of a design that has events with chains in it.
    /// </summary>
    /// <remarks>
    /// Chain navigation cannot be tested on a level whose events are all islands, and designs do
    /// ship such levels. Picking by content rather than by index keeps the tests from depending on
    /// a level number that a future corpus update could renumber.
    /// </remarks>
    public static LevelFile? LevelWithChains(LoadedDesign design) =>
        Levels(design)
            .Select(pair => pair.Level)
            .FirstOrDefault(level => level.Events.Any(e => EventChainLinks.Of(e).Count > 0));
}
