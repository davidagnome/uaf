using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// Byte-for-byte diff of this compiler's output against the reference <c>GPDLcomp.exe</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the test that decides whether the port is correct, and it cannot run on macOS.</b>
/// Everything else in this suite checks the port against a reading of the C++ source; only this
/// checks it against the C++ <i>binary</i>. Until the Oracle workflow drops reference output into
/// <c>oracle/golden/gpdl/</c>, these tests return early — so a green suite does <b>not</b> mean
/// byte-identity has been demonstrated.
/// </para>
/// <para>
/// To produce the goldens, add a step to <c>.github/workflows/oracle-cpp.yml</c> after the
/// GPDLcomp build:
/// </para>
/// <code>
/// GPDLcomp.exe &lt;script&gt;.txt oracle\golden\gpdl\&lt;script&gt;.bin oracle\golden\gpdl\&lt;script&gt;.lst
/// </code>
/// <para>
/// Two cautions carried over from docs/PORTING-PLAN.md's Phase 0 notes. GPDLcomp is a console app so
/// it does block PowerShell, unlike UAFWinEd — but it calls <c>gets_s</c> after every error message
/// and after its usage banner, so a script that fails to compile will <b>hang the runner</b> rather
/// than fail. Redirect stdin from NUL. And it always exits 0 (GPDL.cpp:111), so the step must check
/// that the <c>.bin</c> exists and is non-empty rather than trusting the exit code.
/// </para>
/// <para>
/// The corpus to compile is <b>not</b> <c>src/GPDL/talk.txt</c>: it does not compile with the shipped
/// table either (see <see cref="TalkCorpusTests"/>). Use the small scripts under
/// <c>oracle/golden/gpdl/</c> alongside the expected output, so the input the oracle saw is committed
/// with what it produced.
/// </para>
/// </remarks>
public class GpdlOracleDiffTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string GoldenDir => Path.Combine(RepoRoot(), "oracle", "golden", "gpdl");

    /// <summary>
    /// Every <c>.txt</c> in the golden directory that has a matching <c>.bin</c>. Empty until the
    /// oracle produces them.
    /// </summary>
    private static List<(string Source, string Binary, string? Listing)> Pairs()
    {
        var result = new List<(string, string, string?)>();
        if (!Directory.Exists(GoldenDir)) { return result; }
        foreach (string source in Directory.GetFiles(GoldenDir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            string binary = Path.ChangeExtension(source, ".bin");
            if (!File.Exists(binary)) { continue; }
            string listing = Path.ChangeExtension(source, ".lst");
            result.Add((source, binary, File.Exists(listing) ? listing : null));
        }
        return result;
    }

    [Fact]
    public void Every_corpus_script_compiles_cleanly()
    {
        // Guards the corpus itself, and runs whether or not goldens exist. A script that stops
        // compiling here would produce no .bin in the oracle workflow, and the diff tests -- which
        // skip an unpaired .txt -- would go quiet rather than fail.
        var sources = Directory.Exists(GoldenDir)
            ? Directory.GetFiles(GoldenDir, "*.txt").OrderBy(p => p, StringComparer.Ordinal).ToList()
            : [];
        Assert.NotEmpty(sources);

        foreach (string path in sources)
        {
            string source = MfcString.Encoding.GetString(File.ReadAllBytes(path));
            var compiler = new GpdlCompiler();
            int result = compiler.Compile(source);
            Assert.True(result == 0,
                $"{Path.GetFileName(path)}: " + string.Join("; ", compiler.Errors));
            Assert.Empty(compiler.Errors);

            var program = GpdlProgram.FromCompiler(compiler);
            Assert.NotEmpty(program.Index);
            // A round trip through the container proves the segment lengths agree with the data.
            byte[] bytes = GpdlBinaryWriter.ToBytes(program);
            using var ms = new MemoryStream(bytes);
            var reloaded = GpdlBinaryWriter.Read(ms);
            Assert.Equal(bytes.Length, ms.Position);
            Assert.Equal(program.Code, reloaded.Code);
        }
    }

    [Fact]
    public void The_corpus_covers_the_constructs_it_claims_to()
    {
        // Cheap check that a future edit does not quietly drop coverage: between them the four
        // scripts must generate every primary opcode the talk.bin path can contain.
        var seen = new HashSet<BinOp>();
        foreach (string path in Directory.GetFiles(GoldenDir, "*.txt"))
        {
            var compiler = new GpdlCompiler();
            Assert.Equal(0, compiler.Compile(MfcString.Encoding.GetString(File.ReadAllBytes(path))));
            foreach (uint word in compiler.Code) { seen.Add(GpdlCode.OpOf(word)); }
        }

        Assert.Equal(
            [
                BinOp.BINOP_JUMP,
                BinOp.BINOP_ReferenceGLOBAL,
                BinOp.BINOP_JUMPFALSE,
                BinOp.BINOP_STORE_FP,
                BinOp.BINOP_FETCH_FP,
                BinOp.BINOP_SUBOP,
                BinOp.BINOP_CALL,
                BinOp.BINOP_RETURN,
                BinOp.BINOP_LOCALS,
            ],
            seen.OrderBy(o => (int)o));
    }

    [Fact]
    public void Compiled_bytecode_is_byte_identical_to_the_reference_compiler()
    {
        var pairs = Pairs();
        if (pairs.Count == 0) { return; }   // no oracle output yet -- see class remarks

        foreach (var (sourcePath, binaryPath, _) in pairs)
        {
            string source = MfcString.Encoding.GetString(File.ReadAllBytes(sourcePath));
            var compiler = new GpdlCompiler();
            int result = compiler.Compile(source);
            Assert.True(result == 0,
                $"{Path.GetFileName(sourcePath)}: " + string.Join("; ", compiler.Errors));

            byte[] mine = GpdlBinaryWriter.ToBytes(GpdlProgram.FromCompiler(compiler));
            byte[] theirs = File.ReadAllBytes(binaryPath);

            // Report the first divergence rather than just "arrays differ": with three concatenated
            // segments and no framing, the offset says which segment drifted.
            int limit = Math.Min(mine.Length, theirs.Length);
            for (int i = 0; i < limit; i++)
            {
                Assert.True(mine[i] == theirs[i],
                    $"{Path.GetFileName(binaryPath)}: first difference at offset {i} " +
                    $"(ours 0x{mine[i]:x2}, reference 0x{theirs[i]:x2})");
            }
            Assert.Equal(theirs.Length, mine.Length);
        }
    }

    [Fact]
    public void Assembly_listings_match_the_reference_compiler()
    {
        var pairs = Pairs().Where(p => p.Listing is not null).ToList();
        if (pairs.Count == 0) { return; }

        foreach (var (sourcePath, _, listingPath) in pairs)
        {
            string source = MfcString.Encoding.GetString(File.ReadAllBytes(sourcePath));
            var compiler = new GpdlCompiler();
            Assert.Equal(0, compiler.Compile(source));

            string[] mine = GpdlListing.ToText(compiler).Split('\n');
            string[] theirs = MfcString.Encoding.GetString(File.ReadAllBytes(listingPath!))
                .Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            int limit = Math.Min(mine.Length, theirs.Length);
            for (int i = 0; i < limit; i++)
            {
                Assert.True(string.Equals(mine[i], theirs[i], StringComparison.Ordinal),
                    $"{Path.GetFileName(listingPath)}: line {i + 1} differs\n" +
                    $"  ours:      {mine[i]}\n  reference: {theirs[i]}");
            }
            Assert.Equal(theirs.Length, mine.Length);
        }
    }

    /// <summary>
    /// Scripts the <b>reference</b> compiler cannot produce a golden for, and why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>prototypes.txt</c> hangs <c>GPDLcomp.exe</c> — a use-after-free, measured not
    /// guessed.</b> <c>addDictionary</c> links the new dictionary into its parent's
    /// <c>m_offspring</c> list (<c>GPDLcomp.cpp:1117</c>) and <c>discardCurrent</c> deletes it
    /// without unlinking (<c>:2375</c>). A forward declaration takes exactly that path, so the
    /// parent's list head is left dangling; the definition that follows splices it back in with
    /// <c>dict-&gt;m_next = m_offspring</c>, and <c>m_countFunctions</c> then walks the corrupted
    /// chain (<c>:1171</c>).
    /// </para>
    /// <para>
    /// <b>The symptoms identify the loop's location exactly.</b> Runs 31263750742 and 31289965658
    /// both timed out with <b>zero bytes of stderr</b> and a <b>zero-byte <c>.bin</c></b>. No
    /// stderr means the compile itself succeeded — errors print before they prompt. An empty
    /// <c>.bin</c> despite a successful compile means <c>outarchive.Close()</c> never ran to flush
    /// the buffer, so the loop is inside <c>WriteDictionary</c>, after <c>WriteCode</c> and
    /// <c>WriteConstants</c> had already written into it.
    /// </para>
    /// <para>
    /// <b>This port compiles the script correctly</b>, which is why it stays in the corpus:
    /// <see cref="Every_corpus_script_compiles_cleanly"/> still covers it, so the construct is
    /// tested even though no reference bytecode for it can exist. Removing the script would delete
    /// the only record of the bug along with the coverage.
    /// </para>
    /// </remarks>
    private static readonly string[] ReferenceCannotCompile = ["prototypes.txt"];

    [Fact]
    public void Goldens_are_either_complete_or_absent_but_never_partial()
    {
        // xUnit 2.9 has no Assert.Skip, so the diff tests above cannot distinguish "matched" from
        // "nothing to match": an unpaired .txt is silently skipped. That makes a PARTIAL set the
        // dangerous state -- the oracle compiled some scripts and not others, and the suite stays
        // green over whatever it missed. All-or-nothing is checkable, so check it.
        //
        // The all-absent case is today's expected state and is deliberately not a failure; the
        // .NET workflow should emit a warning when oracle/golden/gpdl/*.bin is missing, exactly as
        // it does for oracle/golden/DefaultDesign.json.
        string[] sources = Directory.Exists(GoldenDir)
            ? [.. Directory.GetFiles(GoldenDir, "*.txt")
                           .Where(s => !ReferenceCannotCompile.Contains(Path.GetFileName(s)))]
            : [];
        var withGolden = sources.Where(s => File.Exists(Path.ChangeExtension(s, ".bin"))).ToList();

        Assert.True(
            withGolden.Count == 0 || withGolden.Count == sources.Length,
            $"{withGolden.Count} of {sources.Length} corpus scripts have a .bin. The missing ones " +
            "are skipped by the diff tests, so a partial set hides failures: " +
            string.Join(", ", sources
                .Where(s => !File.Exists(Path.ChangeExtension(s, ".bin")))
                .Select(Path.GetFileName)));
    }

    /// <summary>
    /// The excluded scripts are real, and still compile here.
    /// </summary>
    /// <remarks>
    /// Guards the exclusion list against becoming a graveyard: a name that no longer matches a file
    /// would silently weaken the completeness check above, and a script listed here that this port
    /// <i>also</i> failed on would mean the exclusion is hiding a defect on our side rather than
    /// documenting one on theirs.
    /// </remarks>
    [Fact]
    public void Every_excluded_script_exists_and_compiles_here()
    {
        foreach (string name in ReferenceCannotCompile)
        {
            string path = Path.Combine(GoldenDir, name);
            Assert.True(File.Exists(path), $"{name} is excluded but no longer in the corpus");

            var compiler = new GpdlCompiler();
            Assert.True(compiler.Compile(MfcString.Encoding.GetString(File.ReadAllBytes(path))) == 0,
                        $"{name}: " + string.Join("; ", compiler.Errors));

            // And it must genuinely have no golden -- otherwise the exclusion is stale.
            Assert.False(File.Exists(Path.ChangeExtension(path, ".bin")),
                         $"{name} now has a .bin; remove it from ReferenceCannotCompile");
        }
    }
}
