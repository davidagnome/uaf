using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// End-to-end tests: compile a script, run it, check the returned string.
/// </summary>
/// <remarks>
/// These exercise the compiler and the VM against each other, which catches disagreements about the
/// frame protocol and the stack discipline that neither side's unit tests would notice — a
/// consistently wrong parameter numbering, for instance, compiles and runs and gives the wrong
/// answer only when a function has more than one parameter.
/// </remarks>
public class GpdlVirtualMachineTests
{
    /// <summary>A host whose <c>$RANDOM</c> is a fixed sequence, so traces are reproducible.</summary>
    private sealed class ScriptedHost(params int[] randoms) : GpdlUnhostedEnvironment
    {
        private int _next;

        public override int Random(int sides) => randoms[_next++ % randoms.Length] % sides;

        public override bool Grep(string pattern, string text) =>
            text.Contains(pattern, StringComparison.Ordinal);
    }

    private static string Run(string source, string entry = "f", IGpdlHost? host = null)
    {
        var compiler = new GpdlCompiler();
        int result = compiler.Compile(source);
        Assert.True(result == 0, "compile failed: " + string.Join("; ", compiler.Errors));
        var program = GpdlProgram.FromCompiler(compiler);
        var vm = new GpdlVirtualMachine(program, host);
        string value = vm.Execute(entry);
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    [Fact]
    public void A_function_returns_its_value()
    {
        Assert.Equal("hello", Run("""$PUBLIC $FUNC f() { $RETURN "hello"; } f;"""));
    }

    [Fact]
    public void A_function_with_no_return_yields_the_empty_string()
    {
        // The default result pushed before the parameters is "" and the trailing RETURN never
        // overwrites it.
        Assert.Equal("", Run("""$PUBLIC $FUNC f() { $VAR a; } f;"""));
    }

    [Fact]
    public void An_unknown_entry_point_returns_empty_without_executing()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN "x"; } f;"""));
        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler));
        Assert.Equal("", vm.Execute("nosuch"));
        Assert.Equal(0, vm.InstructionCount);
    }

    [Fact]
    public void String_concatenation_uses_the_plus_operator()
    {
        Assert.Equal("abcd", Run("""$PUBLIC $FUNC f() { $RETURN "ab" + "cd"; } f;"""));
    }

    [Theory]
    [InlineData("3 +# 4", "7")]
    [InlineData("10 -# 3", "7")]
    [InlineData("6 *# 7", "42")]
    [InlineData("43 /# 6", "7")]
    [InlineData("43 %# 6", "1")]
    [InlineData("12 &# 10", "8")]
    [InlineData("12 |# 3", "15")]
    [InlineData("12 ^# 10", "6")]
    public void Hardware_integer_operators(string expression, string expected)
    {
        Assert.Equal(expected, Run($"$PUBLIC $FUNC f() {{ $RETURN {expression}; }} f;"));
    }

    [Fact]
    public void Division_by_zero_in_the_numeric_operator_uses_one_as_the_divisor()
    {
        // GPDLexec.cpp:5032 -- a warning and a divisor of 1, so 5 /# 0 is 5. Note $DIV would give
        // "999999" for the same inputs; the two families do not agree.
        Assert.Equal("5", Run("$PUBLIC $FUNC f() { $RETURN 5 /# 0; } f;"));
        Assert.Equal("999999", Run("""$PUBLIC $FUNC f() { $RETURN $DIV("5", "0"); } f;"""));
    }

    [Fact]
    public void The_bignum_family_is_reached_through_the_dollar_functions()
    {
        Assert.Equal("42", Run("""$PUBLIC $FUNC f() { $RETURN $PLUS("2", "40"); } f;"""));
        string big = new('9', 30);
        Assert.Equal(
            GpdlLongArithmetic.Multiply(big, big),
            Run("$PUBLIC $FUNC f() { $RETURN $TIMES(\"" + big + "\", \"" + big + "\"); } f;"));
    }

    [Fact]
    public void Equality_operator_is_textual_but_the_EQUAL_function_is_numeric()
    {
        // This pair is the trap src/GPDL/language.txt describes backwards. The operator '==' is
        // SUBOP_ISEQUAL (byte comparison); $EQUAL is SUBOP_iEQUAL (LongCompare).
        Assert.Equal("", Run("""$PUBLIC $FUNC f() { $RETURN "3" == "03"; } f;"""));
        Assert.Equal("1", Run("""$PUBLIC $FUNC f() { $RETURN $EQUAL("3", "03"); } f;"""));
    }

    [Fact]
    public void Truth_is_the_string_1_and_falsehood_is_the_empty_string()
    {
        // src/GPDL/language.txt:61. Note "0" is also false to JUMPFALSE but is not what a
        // comparison produces.
        Assert.Equal("1", Run("""$PUBLIC $FUNC f() { $RETURN "b" > "a"; } f;"""));
        Assert.Equal("", Run("""$PUBLIC $FUNC f() { $RETURN "a" > "b"; } f;"""));
    }

    [Fact]
    public void Zero_and_empty_are_both_false_to_a_conditional_but_other_strings_are_true()
    {
        Assert.Equal("no", Run("""$PUBLIC $FUNC f() { $IF ("0") { $RETURN "yes"; }; $RETURN "no"; } f;"""));
        Assert.Equal("no", Run("""$PUBLIC $FUNC f() { $IF ("") { $RETURN "yes"; }; $RETURN "no"; } f;"""));
        Assert.Equal("yes", Run("""$PUBLIC $FUNC f() { $IF ("00") { $RETURN "yes"; }; $RETURN "no"; } f;"""));
        Assert.Equal("yes", Run("""$PUBLIC $FUNC f() { $IF (" ") { $RETURN "yes"; }; $RETURN "no"; } f;"""));
    }

    [Fact]
    public void Parameters_bind_positionally_across_the_reversed_formal_list()
    {
        // The compiler stores formals last-first and the VM's frame pointer lands on the last
        // actual. If either side were changed alone this test would swap the two values.
        Assert.Equal(
            "first=A second=B",
            Run("""
                $FUNC g(a, b) { $RETURN "first=" + a + " second=" + b; } g;
                $PUBLIC $FUNC f() { $RETURN g("A", "B"); } f;
                """));
    }

    [Fact]
    public void Locals_survive_assignment_and_do_not_collide_with_parameters()
    {
        Assert.Equal(
            "p:1 x:2 y:3",
            Run("""
                $FUNC g(p) {
                  $VAR x;
                  $VAR y;
                  x = "2";
                  y = "3";
                  $RETURN "p:" + p + " x:" + x + " y:" + y;
                } g;
                $PUBLIC $FUNC f() { $RETURN g("1"); } f;
                """));
    }

    [Fact]
    public void Global_variables_persist_across_calls_within_one_run()
    {
        Assert.Equal(
            "ab",
            Run("""
                $VAR g;
                $FUNC append(s) { g = g + s; $RETURN g; } append;
                $PUBLIC $FUNC f() { append("a"); $RETURN append("b"); } f;
                """));
    }

    [Fact]
    public void While_and_break_terminate()
    {
        Assert.Equal(
            "1111111111",
            Run("""
                $VAR out;
                $VAR n;
                $PUBLIC $FUNC f() {
                  n = "0";
                  out = "";
                  $WHILE (1) {
                    $IF (n ==# 10) { $BREAK; };
                    out = out + "1";
                    n = n +# 1;
                  };
                  $RETURN out;
                } f;
                """));
    }

    [Fact]
    public void Continue_jumps_back_to_the_loop_condition()
    {
        Assert.Equal(
            "13579",
            Run("""
                $VAR out;
                $VAR n;
                $PUBLIC $FUNC f() {
                  n = "0";
                  out = "";
                  $WHILE (n <# 10) {
                    n = n +# 1;
                    $IF (n %# 2 ==# 0) { $CONTINUE; };
                    out = out + n;
                  };
                  $RETURN out;
                } f;
                """));
    }

    [Fact]
    public void Switch_selects_a_case_and_break_leaves_it()
    {
        Assert.Equal(
            "two",
            Run("""
                $PUBLIC $FUNC f() {
                  $SWITCH ("2") {
                    $CASE "1": $RETURN "one";
                    $CASE "2": $RETURN "two";
                    $DEFAULT:  $RETURN "other";
                  } $ENDSWITCH;
                } f;
                """));
    }

    [Fact]
    public void Switch_falls_through_when_a_case_omits_break()
    {
        // The fall-through jump lands past the following case's test, so the next case's body runs
        // without its condition being evaluated.
        Assert.Equal(
            "ab",
            Run("""
                $VAR out;
                $PUBLIC $FUNC f() {
                  out = "";
                  $SWITCH ("1") {
                    $CASE "1": out = out + "a";
                    $CASE "9": out = out + "b";
                  } $ENDSWITCH;
                  $RETURN out;
                } f;
                """));
    }

    [Fact]
    public void Switch_default_runs_when_nothing_matches()
    {
        Assert.Equal(
            "other",
            Run("""
                $PUBLIC $FUNC f() {
                  $SWITCH ("zzz") {
                    $CASE "1": $RETURN "one";
                    $DEFAULT:  $RETURN "other";
                  } $ENDSWITCH;
                } f;
                """));
    }

    [Fact]
    public void Recursion_works_through_a_prototype()
    {
        // Two things about this declaration are not typos. A zero-parameter prototype is the only
        // kind the reference compiler accepts (see the checkProtoParam note in
        // GpdlCompiler.CompileFunctionDecl), and a prototype at global level needs *two* semicolons:
        // compileFunctionDecl consumes one on the prototype path (GPDLcomp.cpp:3478) and
        // CompileProgram then demands another (GPDLcomp.cpp:3562).
        Assert.Equal(
            "3",
            Run("""
                $VAR n;
                $FUNC down();;
                $FUNC down() {
                  $IF (n ==# 0) { $RETURN "0"; };
                  n = n -# 1;
                  $RETURN down();
                } down;
                $PUBLIC $FUNC f() { n = "3"; down(); $RETURN "3"; } f;
                """));
    }

    [Theory]
    [InlineData("""$LENGTH("abcde")""", "5")]
    [InlineData("""$MIDDLE("abcdef", 1, 3)""", "bcd")]
    [InlineData("""$MIDDLE("abc", 5, 2)""", "")]
    [InlineData("""$UpCase("aBc")""", "ABC")]
    [InlineData("""$DownCase("aBc")""", "abc")]
    [InlineData("""$Capitalize("hello wide world")""", "Hello Wide World")]
    [InlineData("""$NOT("")""", "1")]
    [InlineData("""$NOT("x")""", "")]
    [InlineData("""$NUMERIC("42")""", "1")]
    [InlineData("""$NUMERIC("4x2")""", "")]
    [InlineData("""$NUMERIC("3.75")""", "1")]
    public void String_and_predicate_system_functions(string call, string expected)
    {
        Assert.Equal(expected, Run($"$PUBLIC $FUNC f() {{ $RETURN {call}; }} f;"));
    }

    [Fact]
    public void Out_of_range_MIDDLE_indices_clamp_instead_of_faulting()
    {
        // CString::Mid clamps; Substring would throw. Scripts pass computed indices freely.
        // A negative start clamps to 0 rather than counting from the end, so this is Left(2).
        Assert.Equal("ab", Run("""$PUBLIC $FUNC f() { $RETURN $MIDDLE("abc", -5, 2); } f;"""));
        Assert.Equal("bc", Run("""$PUBLIC $FUNC f() { $RETURN $MIDDLE("abc", 1, 99); } f;"""));
        // A start past the end yields nothing rather than throwing.
        Assert.Equal("", Run("""$PUBLIC $FUNC f() { $RETURN $MIDDLE("abc", 9, 2); } f;"""));
    }

    [Theory]
    // The leading character of a delimited string IS its delimiter, so these all use ','.
    [InlineData("""$DelimitedStringCount(",a,b,c")""", "3")]
    [InlineData("""$DelimitedStringCount("")""", "0")]
    [InlineData("""$DelimitedStringSubstring(",a,b,c", 1)""", "b")]
    [InlineData("""$DelimitedStringSubstring(",a,b,c", 9)""", "")]
    [InlineData("""$DelimitedStringHead(",a,b,c")""", "a")]
    [InlineData("""$DelimitedStringTail(",a,b,c")""", ",b,c")]
    [InlineData("""$DelimitedStringAdd(",b,c", "a", "")""", ",a,b,c")]
    public void Delimited_string_family(string call, string expected)
    {
        Assert.Equal(expected, Run($"$PUBLIC $FUNC f() {{ $RETURN {call}; }} f;"));
    }

    [Fact]
    public void DelimitedStringAdd_falls_back_to_hash_when_no_delimiter_is_available()
    {
        // GPDLexec.cpp:2686 -- an empty source and an empty delimiter hint yield '#'.
        Assert.Equal("#a", Run("""$PUBLIC $FUNC f() { $RETURN $DelimitedStringAdd("", "a", ""); } f;"""));
        Assert.Equal("|a", Run("""$PUBLIC $FUNC f() { $RETURN $DelimitedStringAdd("", "a", "|"); } f;"""));
    }

    [Fact]
    public void Random_goes_through_the_host()
    {
        Assert.Equal("3", Run(
            "$PUBLIC $FUNC f() { $RETURN $RANDOM(10); } f;",
            host: new ScriptedHost(3)));
    }

    [Fact]
    public void Random_with_a_non_positive_bound_is_an_illegal_parameter()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile("$PUBLIC $FUNC f() { $RETURN $RANDOM(0); } f;"));
        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), new ScriptedHost(1));
        Assert.Equal("", vm.Execute("f"));
        Assert.Equal(GpdlState.GPDL_ILLPARAM, vm.Status);
    }

    [Fact]
    public void An_unported_subop_throws_with_a_citation_rather_than_returning_a_plausible_value()
    {
        // The whole point of the boundary: a script reaching an unimplemented engine call must fail
        // loudly, not quietly answer "0". The example has to be something still unported, so this
        // test moves as the port advances -- it has named $PARTYSIZE, $GET_CHAR_EFFAC and
        // $VisualDistance in turn, and each time that call landed the test failed and had to be
        // repointed. That failure is the mechanism working, not a nuisance: pick another unported
        // call from the measurement in the plan's sub-opcode item.
        //
        // $AddCombatant needs a whole combat-spawning path, so it should outlast most of them.
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile(
            """$PUBLIC $FUNC f() { $RETURN $AddCombatant("orc", "1"); } f;"""));
        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler));
        var ex = Assert.Throws<NotSupportedException>(() => vm.Execute("f"));
        Assert.Contains("$AddCombatant", ex.Message, StringComparison.Ordinal);
        Assert.Contains("GPDLexec.cpp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Grep_is_not_available_without_a_host_that_supplies_it()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN $GREP("a", "abc"); } f;"""));
        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler));
        var ex = Assert.Throws<NotSupportedException>(() => vm.Execute("f"));
        Assert.Contains("regexp.cpp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_runaway_loop_is_cut_off_rather_than_hanging()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile("$PUBLIC $FUNC f() { $WHILE (1) { }; } f;"));
        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler));
        Assert.Equal("", vm.Execute("f"));
        Assert.Equal(GpdlState.GPDL_EXCESSCPU, vm.Status);
        Assert.True(vm.InstructionCount > GpdlVirtualMachine.InterpretLimit);
    }

    [Fact]
    public void Execute_with_arguments_checks_the_count_against_the_entry_marker()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile("""
            $PUBLIC $FUNC f(a, b) { $RETURN a + "/" + b; } f;
            """));
        var program = GpdlProgram.FromCompiler(compiler);

        var ok = new GpdlVirtualMachine(program);
        Assert.Equal("x/y", ok.Execute("f", ["x", "y"]));

        var wrong = new GpdlVirtualMachine(program);
        Assert.Equal("", wrong.Execute("f", ["x"]));
        Assert.Equal(GpdlState.GPDL_NOSUCHNAME, wrong.Status);
    }

    [Fact]
    public void A_trace_records_one_line_per_instruction_with_the_data_stack()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN "a" + "b"; } f;"""));
        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler)) { Trace = [] };
        Assert.Equal("ab", vm.Execute("f"));

        Assert.NotNull(vm.Trace);
        Assert.Equal(vm.InstructionCount, vm.Trace!.Count);
        // Entry: PC 1, the entry-marker fetch. The stack holds only the default result, an empty
        // string, so the bracket renders as "[]".
        Assert.StartsWith("000001 02000001 []", vm.Trace[0], StringComparison.Ordinal);
        // By the concatenation both operands are stacked, top first, above the marker string that
        // this entry path pushes and the empty default result below it.
        Assert.Contains(vm.Trace, line => line.EndsWith("[b|a|f(0)|]", StringComparison.Ordinal));
    }
}
