using System.Diagnostics;

namespace UAFedit.Oracle.Tests;

/// <summary>
/// Runs the real <c>UAFWinEd.exe</c> over a design and reports what its own log said.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only check that answers Phase 5's actual exit criterion.</b> Everything else in
/// the suite asks whether the port agrees with itself; this asks whether the C++ editor can open
/// what the port wrote. It found two defects the day it was first run by hand — a missing
/// <c>specialAbilities.dat</c> and a version stamp moved without the databases under it — and
/// neither was visible from inside the port.
/// </para>
/// <para>
/// <b>Everything about the rig is optional and it skips rather than fails.</b> It needs a Windows
/// runtime (CrossOver here), a bottle, and a built <c>UAFWinEd.exe</c> — none of which a fresh
/// clone or a Linux CI runner has. <see cref="Available"/> is the single gate, and every test
/// returns early without it.
/// </para>
/// <para>
/// <b>Wine is a variable and this is not the authority.</b> The reference's own view
/// initialisation fails under it regardless of design, so only the data-load path is meaningful
/// here. Treat a pass as "the load path is sound on this machine" and let a Windows run be the
/// arbiter if the two ever disagree — the same caution <c>tools/frua-import-oracle.sh</c> gives.
/// </para>
/// </remarks>
public static class ReferenceEditor
{
    private const string CrossOverWine =
        "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine";

    /// <summary>
    /// The bottle to run in.
    /// </summary>
    /// <remarks>
    /// A fuller bottle than the bare oracle one: <c>UAFWinEd</c> is an MFC application and wants a
    /// Windows runtime with more in it than a minimal prefix has.
    /// </remarks>
    private const string Bottle = "SteamBeta";

    /// <summary>The repository root, found by walking up for the C++ sources.</summary>
    public static DirectoryInfo? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        return dir;
    }

    /// <summary>
    /// Whether an executable has the oracle flags compiled into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The check that would have saved a day.</b> The <c>UAFWinEd.exe</c> committed here was
    /// once nine months older than the oracle work and had none of these flags — so
    /// <c>-dumpjson</c> was silently ignored, headless mode never engaged, and every "headless"
    /// run opened a GUI and waited for somebody to dismiss its dialogs. Nothing about that was
    /// visible from the outside: the design still loaded, the log still looked healthy, and the
    /// only clue was the JSON that never appeared.
    /// </para>
    /// <para>
    /// So capability is probed rather than assumed, by looking for the flag the parser matches on
    /// (<c>Globals.cpp:858</c>). A binary without it is reported as unusable instead of being run.
    /// </para>
    /// </remarks>
    public static bool HasOracleFlags(string exe)
    {
        try
        {
            // The flag is an ASCII literal in the binary; reading it whole is cheap enough at
            // ~6 MB and far simpler than parsing PE sections.
            byte[] image = File.ReadAllBytes(exe);
            return Contains(image, "dumpjson"u8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Why the rig cannot run, or null when it can.</summary>
    /// <remarks>
    /// A reason rather than a bool so a skipped run can say what it wanted, which is the
    /// difference between "not applicable here" and "quietly proving nothing".
    /// </remarks>
    public static string? Unavailable()
    {
        if (RepoRoot() is not { } root)
        {
            return "the repository root could not be found";
        }

        if (!File.Exists(CrossOverWine))
        {
            return "CrossOver is not installed";
        }

        if (BottlePath() is null)
        {
            return $"the '{Bottle}' bottle was not found";
        }

        string exe = Path.Combine(root.FullName, "UAFWinEd", "UAFWinEd.exe");

        if (!File.Exists(exe))
        {
            return "UAFWinEd.exe is not built (the Oracle workflow publishes it as "
                   + "uafwined-editor)";
        }

        return HasOracleFlags(exe)
            ? null
            : "the UAFWinEd.exe found has no oracle flags -- it predates them, so -dumpjson would "
              + "be ignored and the editor would open a GUI and wait for a human. Fetch the "
              + "uafwined-editor artifact: gh run download -n uafwined-editor";
    }

    /// <summary>Whether the rig can run at all.</summary>
    public static bool Available => Unavailable() is null;

    /// <summary>
    /// The directory holding the bottles.
    /// </summary>
    /// <remarks>
    /// <b>Ask, do not assume.</b> CrossOver's bottles can sit under the configured
    /// <c>BottleDir</c> — often moved off the boot volume, as it is on this machine — or under the
    /// default support directory, and a machine can have some of each. The configured value is
    /// read first and the default used as a fallback.
    /// </remarks>
    private static string? BottlePath()
    {
        foreach (string candidate in Candidates())
        {
            if (Directory.Exists(Path.Combine(candidate, Bottle)))
            {
                return candidate;
            }
        }

        return null;

        static IEnumerable<string> Candidates()
        {
            string? configured = Run("defaults", ["read", "com.codeweavers.CrossOver", "BottleDir"]);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                yield return configured.Trim();
            }

            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "CrossOver", "Bottles");
        }
    }

    /// <summary>
    /// Assembles a runnable editor: the executable plus the two things it will not start without.
    /// </summary>
    /// <remarks>
    /// <b>The editor resolves its resources from its own directory and will not start standalone.</b>
    /// It needs <c>EditorResources</c> and a <c>TemplateDesign.dsn</c> beside it — and that template
    /// needs a <c>Resources</c> folder of its own, which <c>DefaultDesign</c> does not ship. Without
    /// one the editor stops at "Failed to find required art file … OverArt.png". A corpus design's
    /// art is linked in to satisfy it.
    /// </remarks>
    public static string? Assemble()
    {
        if (RepoRoot() is not { } root)
        {
            return null;
        }

        string editor = Path.Combine(AppContext.BaseDirectory, "reference-editor");
        string exe = Path.Combine(editor, "UAFWinEd.exe");

        Directory.CreateDirectory(editor);

        // Copy when it is missing OR when what is cached has no oracle flags. The second half is
        // not defensive tidiness: this folder survives between runs, so a copy taken before the
        // binary was refreshed goes on being used forever -- which is exactly what happened, and
        // it presents as the test popping GUI dialogs while a hand-run of the same command is
        // silently headless.
        string source = Path.Combine(root.FullName, "UAFWinEd", "UAFWinEd.exe");

        if (!File.Exists(exe) || !HasOracleFlags(exe))
        {
            File.Copy(source, exe, overwrite: true);
        }

        CopyOnce(Path.Combine(root.FullName, "src", "UAFWinEd", "EditorResources"),
                 Path.Combine(editor, "EditorResources"));

        string template = Path.Combine(editor, "TemplateDesign.dsn");
        CopyOnce(Path.Combine(root.FullName, "src", "UAFWinEd", "DefaultDesign.dsn"), template);

        // The template's own art, which it does not ship. Any corpus design's will do -- the
        // editor only needs the files it names to exist.
        string art = Path.Combine(root.FullName, "reference", "SomethingWild.dsn", "Resources");
        if (Directory.Exists(art) && !Directory.Exists(Path.Combine(template, "Resources")))
        {
            Directory.CreateSymbolicLink(Path.Combine(template, "Resources"), art);
        }

        return exe;
    }

    /// <summary>
    /// Loads a design through the reference editor and returns its log.
    /// </summary>
    /// <param name="designRoot">The folder holding <c>Data/</c>.</param>
    /// <returns>
    /// The contents of the editor's own <c>UafErr_Edit.txt</c> for this run, or null when the rig
    /// could not run it.
    /// </returns>
    /// <remarks>
    /// <b><c>-dumpjson</c> is what makes it headless</b>, suppressing the modal dialogs that would
    /// otherwise block forever with no display. <b>Each flag and its value must be one argument</b>:
    /// the reference splits them with a space search inside a single token, so passing them
    /// separately leaves the value empty and the app exits having done nothing.
    /// </remarks>
    public static string? Load(string designRoot, TimeSpan timeout) =>
        Run(designRoot, timeout).Log;

    /// <summary>
    /// What one run produced: the log, and whether the dump actually completed.
    /// </summary>
    /// <param name="Log">This run's portion of the editor's <c>UafErr_Edit.txt</c>.</param>
    /// <param name="Json">The canonical JSON, or null when the dump did not finish.</param>
    /// <remarks>
    /// <b>The JSON is the signal that matters, not the log.</b> <c>-dumpjson</c> deliberately
    /// bypasses <c>CUAFWinEdApp::OpenDesign</c> — that path needs a window and a DirectX device
    /// (<c>DumpJson.cpp</c>) — so the log lines a GUI run ends with, "Finished loading design
    /// data" among them, never appear here. A file on disk is also the one thing a stale binary
    /// cannot fake: it ignored the flag entirely and wrote nothing.
    /// </remarks>
    public sealed record Result(string? Log, string? Json);

    /// <summary>Loads a design through the reference editor and reports what came out.</summary>
    public static Result Run(string designRoot, TimeSpan timeout)
    {
        if (!Available || Assemble() is not { } exe)
        {
            return new Result(null, null);
        }

        string log = Path.Combine(designRoot, "UafErr_Edit.txt");
        long before = File.Exists(log) ? new FileInfo(log).Length : 0;

        var start = new ProcessStartInfo(CrossOverWine)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("--bottle");
        start.ArgumentList.Add(Bottle);
        start.ArgumentList.Add("--cx-app");
        start.ArgumentList.Add(Windows(exe));
        start.ArgumentList.Add($"-config {Windows(designRoot)}");
        start.ArgumentList.Add($"-dumpjson {Windows(Path.Combine(designRoot, "oracle.json"))}");
        start.Environment["CX_BOTTLE_PATH"] = BottlePath()!;

        using (var editor = Process.Start(start))
        {
            if (editor is null)
            {
                return new Result(null, null);
            }

            if (!editor.WaitForExit((int)timeout.TotalMilliseconds))
            {
                editor.Kill(entireProcessTree: true);
            }
        }

        string json = Path.Combine(designRoot, "oracle.json");

        if (!File.Exists(log))
        {
            return new Result(string.Empty, File.Exists(json) ? json : null);
        }

        // Only this run's part of it: the editor appends, so a design loaded twice would otherwise
        // be judged on what happened the first time.
        using var stream = File.OpenRead(log);
        stream.Seek(before, SeekOrigin.Begin);

        return new Result(new StreamReader(stream).ReadToEnd(),
                          File.Exists(json) ? json : null);
    }

    /// <summary>
    /// A macOS path as the bottle sees it.
    /// </summary>
    /// <remarks>
    /// <c>Z:</c> maps to the filesystem root, so nothing has to be copied into the bottle — which
    /// matters because the bottle is the user's, not the test's.
    /// </remarks>
    private static string Windows(string path) =>
        "Z:" + path.Replace('/', '\\');

    private static void CopyOnce(string from, string to)
    {
        if (!Directory.Exists(from) || Directory.Exists(to))
        {
            return;
        }

        foreach (string directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(from, to, StringComparison.Ordinal));
        }

        Directory.CreateDirectory(to);

        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(from, to, StringComparison.Ordinal), overwrite: true);
        }
    }

    private static string? Run(string command, string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }
}
