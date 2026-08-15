using System.Text;

namespace Rc2Axaml;

/// <summary>
/// One-shot transpiler from an MFC resource script to Avalonia AXAML, per Phase 5 of
/// docs/PORTING-PLAN.md (§7, "write a one-shot .rc → .axaml transpiler").
/// </summary>
/// <remarks>
/// <para>
/// Usage: <c>rc2axaml &lt;UAFWinEd.rc&gt; &lt;output-dir&gt; [--source-path &lt;path&gt;]</c>.
/// One <c>.axaml</c> per dialog, named after the dialog's resource id.
/// </para>
/// <para>
/// The exit code is 1 if anything was unhandled, because a transpiler that quietly drops a control
/// is worse than one that fails: the dropped control does not come back, and nobody diffs 131
/// generated files against a resource script by eye. The same rule the GPDL driver adopted
/// (tools/gpdlc/Program.cs) for the same reason.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: rc2axaml <input.rc> <output-dir> [--source-path <path-for-comments>]");
            return 1;
        }

        string inputPath = args[0];
        string outputDir = args[1];
        string sourcePath = "src/UAFWinEd/UAFWinEd.rc";

        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--source-path") { sourcePath = args[i + 1]; }
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Cannot open input file {inputPath}");
            return 1;
        }

        // Resource scripts are single-byte text in a Windows codepage, like every other data file
        // in this tree. Reading as UTF-8 would mangle any non-ASCII character in a label.
        string text = Encoding.Latin1.GetString(File.ReadAllBytes(inputPath));

        RcFile parsed = RcParser.Parse(text);
        var diagnostics = new List<string>(parsed.Diagnostics);

        Directory.CreateDirectory(outputDir);

        int controls = 0;
        foreach (RcDialog dialog in parsed.Dialogs)
        {
            string axaml = AxamlEmitter.Emit(dialog, sourcePath, diagnostics);
            File.WriteAllText(Path.Combine(outputDir, dialog.Id + ".axaml"), axaml, new UTF8Encoding(false));
            controls += dialog.Controls.Count;
        }

        Console.WriteLine($"{parsed.Dialogs.Count} dialogs, {controls} controls -> {outputDir}");

        if (diagnostics.Count > 0)
        {
            foreach (string diagnostic in diagnostics) { Console.Error.WriteLine(diagnostic); }
            Console.Error.WriteLine($"{diagnostics.Count} unhandled or lossy conversions.");
            return 1;
        }

        return 0;
    }
}
