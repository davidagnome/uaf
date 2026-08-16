namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// One design on disk, and the files in it this harness knows how to read and write back.
/// </summary>
/// <param name="Name">How the design is named in a failure message.</param>
/// <param name="Root">The design folder — <c>Data/</c> and <c>Saves/</c> hang off it.</param>
/// <param name="IsTracked">
/// True when the design is committed rather than sitting under the gitignored <c>reference/</c>.
/// The one tracked design is what stops a checkout without <c>reference/</c> running an empty
/// suite and calling it proof.
/// </param>
public sealed record CorpusDesign(string Name, string Root, bool IsTracked)
{
    /// <summary>The <c>Data/</c> folder, where every database and level lives.</summary>
    public string DataDirectory => Path.Combine(Root, "Data");

    public override string ToString() => Name;
}

/// <summary>
/// Finds the designs the round trip runs over.
/// </summary>
/// <remarks>
/// <para>
/// Two of the three live under <c>reference/</c>, which is gitignored, so every case that needs
/// one has to return early when it is absent — the pattern the rest of the test suite uses
/// (<c>UAFcore.Tests/GameScriptHostRosterTests.cs</c>).
/// </para>
/// <para>
/// <b><c>DefaultDesign</c> is deliberately in the list even though it cannot be written.</b> It is
/// committed to the repository, so it always runs, and what it proves is the other half of the
/// claim: that a design the port <i>cannot</i> save is refused with a reason rather than silently
/// mangled. Without it a checkout lacking <c>reference/</c> would run nothing at all and report a
/// green suite, which is the failure mode this file is written to avoid.
/// </para>
/// </remarks>
public static class DesignCorpus
{
    /// <summary>
    /// The repository root, found by walking up for the folder holding the C++ reference.
    /// </summary>
    /// <remarks>
    /// <c>src/Shared</c> rather than <c>.git</c>: a worktree or a submodule checkout has no
    /// <c>.git</c> directory, and the C++ sources are what every fixture path is relative to.
    /// </remarks>
    public static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        return dir;
    }

    /// <summary>Every design present on this machine, tracked or not.</summary>
    public static IReadOnlyList<CorpusDesign> Present()
    {
        if (RepoRoot() is not { } root)
        {
            return [];
        }

        CorpusDesign[] candidates =
        [
            new("DefaultDesign", Path.Combine(root.FullName, "src", "UAFWinEd", "DefaultDesign.dsn"),
                IsTracked: true),
            new("SomethingWild", Path.Combine(root.FullName, "reference", "SomethingWild.dsn"),
                IsTracked: false),
            new("Case", Path.Combine(root.FullName, "reference", "Case.dsn"),
                IsTracked: false),
        ];

        return [.. candidates.Where(d => File.Exists(Path.Combine(d.DataDirectory, "game.dat")))];
    }

    /// <summary>The design of that name, or null when it is not on this machine.</summary>
    public static CorpusDesign? Find(string name) =>
        Present().FirstOrDefault(d => d.Name == name);

    /// <summary>
    /// Names for <c>[Theory]</c> data. Deliberately the full list rather than only what is
    /// present, so a design that vanished shows up as a case that returned early rather than as a
    /// case that was never generated.
    /// </summary>
    public static TheoryData<string> Names => ["DefaultDesign", "SomethingWild", "Case"];

    /// <summary>
    /// Every file in a design this harness has a reader and a writer for, in a stable order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The list is every database a design carries.</b> Five of them —
    /// <c>ability.dat</c>, <c>baseclass.dat</c>, <c>classes.dat</c>, <c>races.dat</c> and
    /// <c>specialAbilities.dat</c> — had readers and no writers, which is what stopped an editor
    /// saving a design it had opened; the writers exist now and the round trip covers them here.
    /// </para>
    /// <para>
    /// The four tagged databases are read at the version <c>game.dat</c> declares, because a
    /// tagged file carries a tag and a count and no version of its own — see
    /// <see cref="DesignFiles.GlobalVersion"/>.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Files(CorpusDesign design)
    {
        var files = new List<string>();
        string data = design.DataDirectory;

        foreach (string name in new[]
                 {
                     "game.dat", "items.dat", "monsters.dat", "spells.dat",
                     "ability.dat", "baseclass.dat", "classes.dat", "races.dat",
                     "specialAbilities.dat",
                 })
        {
            if (File.Exists(Path.Combine(data, name)))
            {
                files.Add(Path.Combine(data, name));
            }
        }

        files.AddRange(Directory.EnumerateFiles(data, "Level*.lvl").OrderBy(p => p, StringComparer.Ordinal));

        // Characters travel in two places: a design can ship one beside its databases, and the
        // pregenerated party sits under Saves/. Both are the same format.
        files.AddRange(Directory.EnumerateFiles(data, "*.CHAR").OrderBy(p => p, StringComparer.Ordinal));

        string saves = Path.Combine(design.Root, "Saves");
        if (Directory.Exists(saves))
        {
            files.AddRange(Directory.EnumerateFiles(saves, "*.chr").OrderBy(p => p, StringComparer.Ordinal));
        }

        return files;
    }

    /// <summary>
    /// The five databases that used to have a reader and no writer.
    /// </summary>
    /// <remarks>
    /// <b>They are all writable now</b>, and the list survives only so a test can assert the
    /// designs really carry them — a round trip that covered a file no design ships would be
    /// covering nothing. <c>baseclass.dat</c> and <c>classes.dat</c> carry the rules a design's
    /// character generation runs on, which is why an editor could not ship without them.
    /// </remarks>
    public static IReadOnlyList<string> LateDatabases =>
        ["ability.dat", "baseclass.dat", "classes.dat", "races.dat", "specialAbilities.dat"];
}
