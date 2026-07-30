using System.Text;
using UAF.Scripting;

namespace gpdlc;

/// <summary>
/// Cross-platform replacement for <c>GPDLcomp.exe</c> (src/GPDL/GPDL.cpp).
/// </summary>
/// <remarks>
/// <para>
/// The argument shape is the original's, because the editor and the build scripts invoke it that
/// way: <c>gpdlc &lt;input&gt; &lt;output&gt; [listfile]</c>. The output file must be named
/// <c>talk.bin</c> for the engine to find it — src/GPDL/README.txt:12 calls that "an unfortunate
/// lack of foresight".
/// </para>
/// <para>
/// Divergences from the C++ driver, all deliberate:
/// </para>
/// <list type="bullet">
/// <item><description>
/// No "Press Enter" prompts. The original blocks on <c>gets_s</c> after every error message, which
/// makes it unusable from CI.
/// </description></item>
/// <item><description>
/// Exit code is 1 on failure. The original always returns 0 (GPDL.cpp:111), so a broken script
/// looked like a successful build — the same class of mistake as the oracle workflow's
/// <c>::error::</c>-without-exit noted in docs/PORTING-PLAN.md.
/// </description></item>
/// <item><description>
/// The output file is not created when compilation fails. The original opens it first and leaves a
/// zero-length <c>talk.bin</c> behind, which the engine then fails to load with a less useful
/// message.
/// </description></item>
/// </list>
/// </remarks>
internal static class Program
{
    private const string Version = "GPDL compiler (C# port of version 4.7 - 29 Nov 2011)";

    private static int Main(string[] args)
    {
        Console.WriteLine(Version);

        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("   gpdlc <input text file> <output binary file> [listfile]");
            return 1;
        }

        string inputPath = args[0];
        string outputPath = args[1];
        string? listPath = args.Length > 2 ? args[2] : null;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Cannot open input file {inputPath}");
            return 1;
        }

        // Source is single-byte text in a Windows codepage, exactly like the data files. Reading it
        // as UTF-8 would corrupt any non-ASCII literal in the script -- and talk.txt is full of
        // prose, so this is not hypothetical.
        string text = MfcString.Encoding.GetString(File.ReadAllBytes(inputPath));

        var compiler = new GpdlCompiler();
        int result = compiler.Compile(GpdlLexer.SplitLines(text));

        if (result != 0)
        {
            foreach (string error in compiler.Errors) { Console.Error.WriteLine(error); }
            Console.Error.WriteLine("GPDL compilation failed.");
            return 1;
        }

        var program = GpdlProgram.FromCompiler(compiler);

        using (var stream = File.Create(outputPath))
        {
            GpdlBinaryWriter.Write(stream, program);
        }

        if (listPath is not null)
        {
            using var listing = new StreamWriter(listPath, false, MfcString.Encoding);
            GpdlListing.Write(compiler, listing);
        }

        Console.WriteLine(
            $"{program.Code.Length} code words, {program.Globals.Length} globals, " +
            $"{program.Index.Count} public functions -> {outputPath}");
        return 0;
    }
}
