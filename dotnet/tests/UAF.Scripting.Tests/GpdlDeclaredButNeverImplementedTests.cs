using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The seven calls the reference declares and never implements.
/// </summary>
/// <remarks>
/// <para>
/// Each has a row in the system-function table, so a design can write one and it <b>compiles</b> —
/// but there is no handler anywhere in <c>GPDLexec.cpp</c>, so it falls through the interpreter's
/// own default and stops the script with "Illegal subop code" (<c>GPDLexec.cpp:6600</c>).
/// </para>
/// <para>
/// <b>Halting is what the reference does, so it is ported rather than refused.</b> Throwing the
/// port's "not ported" exception would say something different and untrue — that this port has not
/// got to it yet. Keeping the two apart is what keeps the remaining-work count honest, and the
/// count has been wrong often enough in this area to be worth a test.
/// </para>
/// </remarks>
public class GpdlDeclaredButNeverImplementedTests
{
    /// <summary>The seven, with a call the compiler will accept.</summary>
    public static TheoryData<string, string> Calls => new()
    {
        { "$AbilityContext", """$AbilityContext()""" },
        { "$SpellgroupContext", """$SpellgroupContext()""" },
        { "$TraitContext", """$TraitContext()""" },
        { "$SET_CHAR_CLASS", """$SET_CHAR_CLASS("hero", "Fighter")""" },
        { "$TESTKEY", """$TESTKEY("k")""" },
        { "$LAST_HITTER_OF", """$LAST_HITTER_OF()""" },
        { "$LAST_TARGETER_OF", """$LAST_TARGETER_OF()""" },
    };

    /// <summary>
    /// Each one compiles and then stops the script, rather than being refused as unported.
    /// </summary>
    [Theory]
    [MemberData(nameof(Calls))]
    public void A_declared_but_unimplemented_call_halts_the_script(string name, string call)
    {
        var compiler = new GpdlCompiler();

        // It compiles: the design author gets no warning at all.
        Assert.True(compiler.Compile($"$PUBLIC $FUNC f() {{ {call}; $RETURN \"after\"; }} f;") == 0,
                    $"{name} did not compile: " + string.Join("; ", compiler.Errors));

        var host = new GpdlUnhostedEnvironment();
        host.Context.Push();
        host.Context.Set(GpdlContext.Attacker, "hero");

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);

        // Not an exception -- the port only throws for calls IT has not implemented.
        string result = vm.Execute("f");

        // The script stopped where the call was, so what followed never ran.
        Assert.Equal(GpdlState.GPDL_ILLPARAM, vm.Status);
        Assert.NotEqual("after", result);
    }

    /// <summary>
    /// None of the seven appears in the reference's interpreter.
    /// </summary>
    /// <remarks>
    /// <b>The measurement this rests on, kept as a test rather than a claim in a document.</b> An
    /// earlier note recorded five of these and named <c>$GET_SPELLBOOK</c> among them, which was
    /// wrong twice over: that one maps to <c>SUBOP_GetSpellbook</c> and does have a handler, while
    /// <c>$AbilityContext</c>, <c>$SpellgroupContext</c> and <c>$TraitContext</c> are dead and were
    /// not listed. Skipped when the reference tree is not present.
    /// </remarks>
    [Fact]
    public void The_seven_have_no_handler_in_the_reference()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? path = dir is null
            ? null
            : Path.Combine(dir.FullName, "src", "Shared", "GPDLexec.cpp");

        if (path is null || !File.Exists(path))
        {
            return;
        }

        string source = File.ReadAllText(path);
        Assert.True(source.Length > 100_000, "GPDLexec.cpp read short -- the check would be vacuous");

        foreach (var row in Calls)
        {
            string name = (string)row[0];
            var entry = Assert.Single(GpdlSystemFunctions.Table, f => f.Name == name);

            Assert.DoesNotContain($"case {entry.SubOp}:", source, StringComparison.Ordinal);
        }

        // And the counterexample: $GET_SPELLBOOK is NOT one of them.
        var spellbook = Assert.Single(GpdlSystemFunctions.Table,
                                      f => f.Name == "$GET_SPELLBOOK");
        Assert.Contains($"case {spellbook.SubOp}:", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two <c>NOT_USED_FOR_ANYTHING</c> sub-opcodes are what their names say.
    /// </summary>
    /// <remarks>
    /// <c>$LAST_HITTER_OF</c> and <c>$LAST_TARGETER_OF</c> are not merely unimplemented — they are
    /// wired to placeholder opcodes, so the names were reserved and never connected to anything.
    /// <b>Both take no parameters at all</b>, which is another sign they were never finished: the
    /// names read as though they take an actor.
    /// </remarks>
    [Fact]
    public void The_two_last_actor_calls_are_wired_to_placeholders()
    {
        Assert.Equal(SubOp.SUBOP_NOT_USED_FOR_ANYTHING1,
                     Assert.Single(GpdlSystemFunctions.Table,
                                   f => f.Name == "$LAST_HITTER_OF").SubOp);

        Assert.Equal(SubOp.SUBOP_NOT_USED_FOR_ANYTHING2,
                     Assert.Single(GpdlSystemFunctions.Table,
                                   f => f.Name == "$LAST_TARGETER_OF").SubOp);
    }
}
