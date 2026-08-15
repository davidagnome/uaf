using System.Text;
using Rc2Axaml;

namespace Rc2Axaml.Tests;

/// <summary>
/// Loads the real <c>src/UAFWinEd/UAFWinEd.rc</c> once for the whole test class run.
/// </summary>
/// <remarks>
/// The tests deliberately run against the shipping resource script rather than a hand-written
/// fixture. A fixture would only ever contain the constructs its author already thought of, and
/// what makes a transpiler for this file hard is the parts nobody would think to write down —
/// unmarked continuation lines, <c>-1</c> used where <c>IDC_STATIC</c> was meant, a combo box
/// whose height is its drop-down's.
/// </remarks>
public sealed class ResourceScript
{
    public static readonly ResourceScript Instance = new();

    private ResourceScript()
    {
        RepositoryRoot = FindRepositoryRoot();
        Path = System.IO.Path.Combine(RepositoryRoot, "src", "UAFWinEd", "UAFWinEd.rc");
        Text = Encoding.Latin1.GetString(File.ReadAllBytes(Path));
        Parsed = RcParser.Parse(Text);
    }

    public string RepositoryRoot { get; }

    public string Path { get; }

    public string Text { get; }

    public RcFile Parsed { get; }

    public RcDialog Dialog(string id) =>
        Parsed.Dialogs.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException($"{id} is not in {Path}");

    private static string FindRepositoryRoot()
    {
        // The tests must find the C++ tree from the test binary's location, and the depth of that
        // relationship changes with configuration and target framework, so it is searched for
        // rather than assumed.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "src", "UAFWinEd", "UAFWinEd.rc")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"src/UAFWinEd/UAFWinEd.rc not found above {AppContext.BaseDirectory}");
    }
}
