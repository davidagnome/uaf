namespace UAFcore;

/// <summary>
/// The editor's template design, and making a new design out of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The port copies the template; the reference edits it in place.</b> <c>UAFWinEd</c>'s File &gt;
/// New points the runtime's folders at <c>ede.TemplateDir()</c> and relies on the user doing a Save
/// As before saving anything (<c>UAFWinEd.cpp:579</c>). This editor's Save writes back to the
/// folder it opened, so doing the same would quietly overwrite the template the first time
/// somebody pressed Ctrl+S — and there is only one of it.
/// </para>
/// <para>
/// <b>Only <c>Data</c> and <c>Resources</c> are copied.</b> The template folder also carries a
/// <c>CVS</c> directory, a readme and a copy of <c>UAFWin.exe</c>, none of which is part of a
/// design.
/// </para>
/// </remarks>
public static class DesignTemplate
{
    /// <summary>The folder name every design template and copy uses for its records.</summary>
    private static readonly string[] DesignFolders = ["Data", "Resources"];

    /// <summary>
    /// Where the template lives, or null when it cannot be found.
    /// </summary>
    /// <remarks>
    /// Beside the executable first, which is where a built editor would ship it; then up the tree
    /// for the copy in the source, which is where it is during development. A design is identified
    /// by having <c>Data/game.dat</c>, not by its name, so a folder that merely happens to be
    /// called <c>DefaultDesign.dsn</c> is not mistaken for one.
    /// </remarks>
    public static string? Locate()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "DefaultDesign.dsn");
        if (IsDesign(beside))
        {
            return beside;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "UAFWinEd", "DefaultDesign.dsn");
            if (IsDesign(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// The name a copied file takes, which is its own except for the template's one level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The template ships <c>Level000.lvl</c>, and no design can reach it.</b> A level file is
    /// named for its index <b>plus one</b> (<c>Shared/Level.cpp:3643</c>), so <c>Level000</c> would
    /// be index -1 — a level that cannot exist. The template's <c>startLevel</c> is 0, so both the
    /// reference and this port look for <c>Level001.lvl</c>, find nothing, and give up: the
    /// reference says "Cannot open file … Level001.lvl" and then "Failed to load start level for
    /// design", and opens an empty editor.
    /// </para>
    /// <para>
    /// <b>So the copy renames it.</b> A new design's one level becomes index 0, which is what its
    /// own <c>startLevel</c> points at. Renaming rather than rewriting <c>startLevel</c> because
    /// the level is the thing that is misnamed — every other design in the corpus starts at
    /// <c>Level001.lvl</c>.
    /// </para>
    /// </remarks>
    private static string LevelName(string name) =>
        name.Equals("Level000.lvl", StringComparison.OrdinalIgnoreCase)
            ? "Level001.lvl"
            : name;

    /// <summary>Whether a folder holds a design.</summary>
    public static bool IsDesign(string root) =>
        !string.IsNullOrWhiteSpace(root)
        && File.Exists(Path.Combine(root, "Data", "game.dat"));

    /// <summary>
    /// Copies the template into <paramref name="destination"/>, which must be empty or absent.
    /// </summary>
    /// <returns>The new design's root — the same path, for chaining into an open.</returns>
    /// <exception cref="InvalidOperationException">
    /// When the template cannot be found, or the destination already holds something. <b>Refusing
    /// a non-empty folder is the whole safety of this</b>: a new design that merged itself into an
    /// existing one would be indistinguishable from a corrupted design afterwards.
    /// </exception>
    public static string CreateAt(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        if (Locate() is not { } template)
        {
            throw new InvalidOperationException(
                "The template design could not be found. It should sit beside the editor as "
                + "'DefaultDesign.dsn'.");
        }

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new InvalidOperationException(
                $"'{destination}' is not empty. A new design needs a folder of its own.");
        }

        foreach (string folder in DesignFolders)
        {
            string from = Path.Combine(template, folder);
            if (!Directory.Exists(from))
            {
                continue;                        // the template ships no Resources of its own
            }

            string to = Path.Combine(destination, folder);
            Directory.CreateDirectory(to);

            foreach (string file in Directory.EnumerateFiles(from))
            {
                File.Copy(file, Path.Combine(to, LevelName(Path.GetFileName(file))));
            }
        }

        if (!IsDesign(destination))
        {
            throw new InvalidOperationException(
                $"The copy of the template into '{destination}' did not produce a design.");
        }

        return destination;
    }
}
