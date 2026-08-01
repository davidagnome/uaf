using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The one piece of real GPDL script text in the repository: <c>src/GPDL/talk.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>talk.txt does not compile.</b> It calls four functions that the shipped
/// <c>systemfunctions[]</c> table no longer defines, so the reference compiler rejects it too — it is
/// a stale sample, not a conformance suite. The exact names and sites are asserted below so that the
/// claim is checked rather than merely recorded, and so that a future edit to either the corpus or
/// the table shows up as a failure here.
/// </para>
/// <para>
/// To still get coverage from ~800 lines of genuine script, <see cref="RepairedSource"/> applies a
/// documented four-substitution patch that swaps each dead name for a live one of the same arity and
/// parameter typing. The patch is applied in-memory from the committed file so it cannot drift, and
/// it is chosen to preserve the <i>shape</i> of the generated code (same opcode classes, same
/// parameter handling) rather than the semantics — the point is to exercise the compiler over real
/// text, not to make the script mean the same thing.
/// </para>
/// </remarks>
public class TalkCorpusTests
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

    private static string TalkPath => Path.Combine(RepoRoot(), "src", "GPDL", "talk.txt");

    private static string TalkSource() =>
        MfcString.Encoding.GetString(File.ReadAllBytes(TalkPath));

    /// <summary>
    /// The four dead calls in talk.txt and the live replacements used for the compile fixture.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// <c>$GET_CHAR_CHA</c> / <c>$SET_CHAR_CHA</c> — the table now spells these
    /// <c>$GET_CHAR_PERM_CHA</c> (1 param) and <c>$SET_CHAR_PERM_CHA</c> (2 params), alongside
    /// <c>LIMITED</c> and <c>ADJ</c> variants that did not exist when talk.txt was written. Same
    /// arity, same untyped parameters.
    /// </description></item>
    /// <item><description>
    /// <c>$Race</c> / <c>$Class</c> — removed outright. <c>GPDL::m_Race</c> and <c>GPDL::m_Class</c>
    /// still exist in GPDLexec.h:353–354, so the implementations outlived their table rows. The
    /// replacements <c>$Status</c> and <c>$Alignment</c> are the surviving members of exactly the
    /// same family: one ACTOR parameter, string result, an attribute of the actor. Substituting a
    /// plain-string function such as <c>$GET_CHAR_CLASS</c> would <i>not</i> work — the argument at
    /// talk.txt:465 is <c>$Name(qn)</c>, which returns ACTOR, and an ACTOR value cannot be used
    /// where a string is expected (GPDLcomp.cpp:2576).
    /// </description></item>
    /// </list>
    /// </remarks>
    private static readonly (string Dead, string Live)[] Repairs =
    [
        ("$GET_CHAR_CHA(", "$GET_CHAR_PERM_CHA("),
        ("$SET_CHAR_CHA(", "$SET_CHAR_PERM_CHA("),
        ("$Race(", "$Status("),
        ("$Class(", "$Alignment("),
    ];

    private static string RepairedSource()
    {
        string text = TalkSource();
        foreach (var (dead, live) in Repairs) { text = text.Replace(dead, live, StringComparison.Ordinal); }
        return text;
    }

    [Fact]
    public void The_corpus_file_is_present_and_is_the_expected_size()
    {
        // A guard on the fixture itself: the assertions below cite line numbers.
        //
        // Measured on normalised text, not on FileInfo.Length. There is no .gitattributes in this
        // tree, so a Windows checkout rewrites this file's 466 LF endings as CRLF and the byte
        // count rises to 20,595 — which failed here on windows-latest only. What the guard is
        // actually for is the content changing, not how git chose to write it out.
        Assert.True(File.Exists(TalkPath));
        string text = TalkSource().ReplaceLineEndings("\n");
        Assert.Equal(20129, text.Length);
        Assert.Equal(466, text.Count(c => c == '\n'));
    }

    [Fact]
    public void talk_txt_calls_four_functions_the_compiler_no_longer_defines()
    {
        // If any of these ever appears in systemfunctions[] again, this test fails and the repair
        // table above becomes wrong.
        foreach (string dead in new[] { "$GET_CHAR_CHA", "$SET_CHAR_CHA", "$Race", "$Class" })
        {
            Assert.Null(GpdlSystemFunctions.Find(dead));
            Assert.Contains(dead + "(", TalkSource(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void talk_txt_is_rejected_at_the_first_dead_call()
    {
        // CompileProgram aborts on the first error, so the diagnostic names the earliest of the four
        // — $SET_CHAR_CHA at talk.txt:357. This message and line number are directly comparable
        // against the reference compiler's stderr, which makes it the cheapest available oracle
        // check for the whole front end.
        var compiler = new GpdlCompiler();
        Assert.Equal(1, compiler.Compile(TalkSource()));

        string joined = string.Join("\n", compiler.Errors);
        Assert.Contains("Undefined name '$SET_CHAR_CHA' at start of statement", joined, StringComparison.Ordinal);
        Assert.Contains("line 357", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void The_repaired_corpus_compiles_without_diagnostics()
    {
        var compiler = new GpdlCompiler();
        int result = compiler.Compile(RepairedSource());
        Assert.True(result == 0, "compile failed: " + string.Join("; ", compiler.Errors));
        Assert.Empty(compiler.Errors);
    }

    [Fact]
    public void The_repaired_corpus_produces_a_stable_shape()
    {
        // Not a golden hash of the bytes -- that would freeze this port's output as if it were
        // authoritative, which it is not until the Windows oracle has confirmed it (see
        // GpdlOracleDiffTests). These are coarse invariants that a codegen regression would break.
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile(RepairedSource()));
        var program = GpdlProgram.FromCompiler(compiler);

        Assert.Equal(860, program.Code.Length);
        Assert.Equal(153, program.Globals.Length);
        Assert.Equal(14, program.Index.Count);

        // Address 0 is the reserved NOOP, and no public function may start there.
        Assert.Equal(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_NOOP, program.Code[0]);
        Assert.All(program.Index, e => Assert.True(e.Address > 0));

        // Every index entry must point at a cell that is a global reference -- the entry marker.
        Assert.All(program.Index, e =>
            Assert.Equal(BinOp.BINOP_ReferenceGLOBAL, GpdlCode.OpOf(program.Code[e.Address])));
    }

    [Fact]
    public void Every_index_entry_has_a_matching_entry_marker_naming_the_function()
    {
        // The marker is "name(paramCount)". If the compiler ever emitted the wrong constant here,
        // GPDL::BeginExecute would validate argument counts against the wrong number and the frame
        // would be built one slot out.
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile(RepairedSource()));
        var program = GpdlProgram.FromCompiler(compiler);

        foreach (var (name, address) in program.Index)
        {
            string marker = program.Globals[GpdlCode.OperandOf(program.Code[address])];
            // Nested publics are indexed as outer@inner but the marker holds the bare name.
            string bare = name[(name.LastIndexOf('@') + 1)..];
            Assert.StartsWith(bare + "(", marker, StringComparison.Ordinal);
            Assert.EndsWith(")", marker, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_jump_and_call_target_is_inside_the_code_segment()
    {
        // Cheap corruption check over 860 real words: a back-patching bug usually shows up as a
        // target past the end or at address 0.
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile(RepairedSource()));
        uint[] code = compiler.Code;

        for (int i = 0; i < code.Length; i++)
        {
            var op = GpdlCode.OpOf(code[i]);
            if (op is BinOp.BINOP_JUMP or BinOp.BINOP_JUMPFALSE or BinOp.BINOP_CALL)
            {
                uint target = GpdlCode.OperandOf(code[i]);
                Assert.True(target > 0 && target < code.Length,
                    $"word {i} ({op}) targets {target}, outside 1..{code.Length - 1}");
            }
        }
    }

    [Fact]
    public void Every_global_reference_is_inside_the_pool()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile(RepairedSource()));
        uint[] code = compiler.Code;
        int poolSize = compiler.Globals.Used;

        for (int i = 0; i < code.Length; i++)
        {
            if (GpdlCode.OpOf(code[i]) != BinOp.BINOP_ReferenceGLOBAL) { continue; }
            uint slot = GpdlCode.OperandOf(code[i]) & 0x7fffff;
            Assert.True(slot > 0 && slot < poolSize,
                $"word {i} references global {slot}, outside 1..{poolSize - 1}");
        }
    }

    [Fact]
    public void The_repaired_corpus_round_trips_through_the_binary_container()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile(RepairedSource()));
        var program = GpdlProgram.FromCompiler(compiler);

        byte[] bytes = GpdlBinaryWriter.ToBytes(program);
        using var ms = new MemoryStream(bytes);
        var reloaded = GpdlBinaryWriter.Read(ms);

        Assert.Equal(bytes.Length, ms.Position);
        Assert.Equal(program.Code, reloaded.Code);
        Assert.Equal(program.Globals, reloaded.Globals);
        Assert.Equal(program.Index, reloaded.Index);
    }

    [Fact]
    public void A_listing_is_produced_for_every_code_word()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile(RepairedSource()));
        string listing = GpdlListing.ToText(compiler);

        // One line per word, plus one label line per user function the disassembler can name.
        int wordLines = listing.Split('\n').Count(l => l.StartsWith("        ", StringComparison.Ordinal));
        Assert.Equal(compiler.Code.Length, wordLines);
        Assert.Contains("Noop", listing, StringComparison.Ordinal);
        Assert.Contains("$RETURN", listing, StringComparison.Ordinal);
        // No word should disassemble as unknown.
        Assert.DoesNotContain("?????", listing, StringComparison.Ordinal);
    }
}
