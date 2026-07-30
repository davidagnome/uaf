using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// Code-generation tests for <see cref="GpdlCompiler"/>.
/// </summary>
/// <remarks>
/// <para>
/// The expected word sequences here were derived by hand-tracing GPDLcomp.cpp, not by running this
/// implementation and recording what came out — otherwise they would only assert that the code does
/// what it does. Each test names the mechanism it pins down.
/// </para>
/// <para>
/// Word layout reminder: top byte is the <see cref="BinOp"/>, low 24 bits are an address, a
/// sub-opcode, a frame offset or a count. <c>0x06</c> is <c>BINOP_SUBOP</c>, <c>0x02</c> is
/// <c>BINOP_ReferenceGLOBAL</c>.
/// </para>
/// </remarks>
public class GpdlCompilerTests
{
    private static GpdlCompiler CompileOk(string source)
    {
        var compiler = new GpdlCompiler();
        int result = compiler.Compile(source);
        Assert.True(result == 0, "compile failed: " + string.Join("; ", compiler.Errors));
        return compiler;
    }

    private const uint Noop = GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_NOOP;

    [Fact]
    public void Address_zero_is_always_a_NOOP()
    {
        // 0 is INDEX::lookup's "no such function" answer, so nothing may start there
        // (GPDLcomp.cpp:3543).
        var compiler = CompileOk("$PUBLIC $FUNC f() { $RETURN 1; } f;");
        Assert.Equal(Noop, compiler.Code[0]);
        Assert.Equal(0x06000001u, compiler.Code[0]);
    }

    [Fact]
    public void A_minimal_public_function_emits_marker_value_store_and_two_returns()
    {
        var compiler = CompileOk("$PUBLIC $FUNC f() { $RETURN 1; } f;");

        Assert.Equal(
            [
                0x06000001,     // 0: SUBOP NOOP
                0x02000001,     // 1: ReferenceGLOBAL 1 -- the entry marker "f(0)", skipped by CALL
                0x02000002,     // 2: ReferenceGLOBAL 2 -- the constant "1"
                0x04000000,     // 3: STORE_FP 0        -- result slot is FP+numParam, numParam = 0
                0x08000000,     // 4: RETURN  locals=0, params=0
                0x08000000,     // 5: RETURN  -- the unconditional trailing return every function gets
            ],
            compiler.Code);

        // Pool slot 0 is never handed out, so the first real constant is index 1.
        Assert.Equal(["", "f(0)", "1"], compiler.Globals.SerializedValues());
        Assert.Equal([("f", 1u)], compiler.PublicFunctionIndex());
    }

    [Fact]
    public void Integer_literals_become_string_constants()
    {
        // "All variables are strings" (src/GPDL/language.txt:1) starts at the compiler: 7 is
        // interned as the text "7", not as an immediate operand.
        var compiler = CompileOk("$PUBLIC $FUNC f() { $RETURN 7; } f;");
        Assert.Contains("7", compiler.Globals.SerializedValues());
    }

    [Fact]
    public void Adjacent_string_literals_concatenate_into_one_constant()
    {
        var compiler = CompileOk("""$PUBLIC $FUNC f() { $RETURN "ab" "cd"; } f;""");
        Assert.Contains("abcd", compiler.Globals.SerializedValues());
        Assert.DoesNotContain("ab", compiler.Globals.SerializedValues());
    }

    [Fact]
    public void Equal_constants_are_interned_once()
    {
        // GLOBALS::SearchConstant de-duplicates. If the pool grew per occurrence, every index after
        // the first duplicate would shift and the file layout would differ.
        var compiler = CompileOk("""$PUBLIC $FUNC f() { $RETURN "x" + "x"; } f;""");
        Assert.Equal(1, compiler.Globals.SerializedValues().Count(v => v == "x"));
    }

    [Fact]
    public void Constants_and_variables_share_one_pool_and_variables_serialize_as_empty()
    {
        // GLOBALS holds both; GLOBALS::write emits "" for a 'V' entry (GPDLcomp.cpp:1909) because
        // the name is compile-time only.
        var compiler = CompileOk("""$VAR g; $PUBLIC $FUNC s() { g = "x"; } s;""");

        Assert.Equal(
            [
                0x06000001,     // 0: NOOP
                0x02000002,     // 1: entry marker "s(0)" -- pool 1 is the variable g, so this is 2
                0x02000003,     // 2: the constant "x"
                0x02800001,     // 3: ReferenceGLOBAL with bit 23 set = STORE into global slot 1
                0x08000000,     // 4: trailing RETURN
            ],
            compiler.Code);

        Assert.Equal(["", "", "s(0)", "x"], compiler.Globals.SerializedValues());
    }

    [Fact]
    public void While_with_break_threads_the_break_chain_through_the_jump_cells()
    {
        // This is the mechanism worth pinning: addToBreakList ORs the current chain head into the
        // low bits of the cell just emitted, so the JUMPFALSE and the BREAK's JUMP form a linked
        // list that resolveBreaks walks and overwrites. Getting the OR/mask wrong produces jumps to
        // plausible-looking addresses.
        var compiler = CompileOk("$PUBLIC $FUNC w() { $WHILE (1) { $BREAK; }; } w;");

        Assert.Equal(
            [
                0x06000001,     // 0: NOOP
                0x02000001,     // 1: entry marker "w(0)"
                0x02000002,     // 2: constant "1" -- the loop condition
                0x03000006,     // 3: JUMPFALSE 6   (loop exit; also was the break-chain head)
                0x01000006,     // 4: JUMP 6        ($BREAK, resolved to the loop exit)
                0x01000002,     // 5: JUMP 2        (back to the condition)
                0x08000000,     // 6: trailing RETURN
            ],
            compiler.Code);
    }

    [Fact]
    public void If_else_jumps_around_each_arm()
    {
        var compiler = CompileOk("""
            $PUBLIC $FUNC f() { $IF (1) { $RETURN "a"; } $ELSE { $RETURN "b"; }; } f;
            """);

        Assert.Equal(
            [
                0x06000001,     // 0: NOOP
                0x02000001,     // 1: entry marker "f(0)"
                0x02000002,     // 2: constant "1"
                0x03000008,     // 3: JUMPFALSE 8 -- to the else arm
                0x02000003,     // 4: constant "a"
                0x04000000,     // 5: STORE_FP 0
                0x08000000,     // 6: RETURN
                0x0100000b,     // 7: JUMP 11 -- around the else arm
                0x02000004,     // 8: constant "b"
                0x04000000,     // 9: STORE_FP 0
                0x08000000,     // 10: RETURN
                0x08000000,     // 11: trailing RETURN
            ],
            compiler.Code);
    }

    [Fact]
    public void Locals_are_counted_into_one_allocation_cell_and_get_negative_frame_offsets()
    {
        // BINOP_LOCALS must be the function's second cell; each $VAR increments its operand and
        // takes offset -1, -2, ... which the VM sign-extends from 24 bits.
        var compiler = CompileOk("""
            $PUBLIC $FUNC f() { $VAR a; $VAR b; a = "1"; } f;
            """);

        Assert.Equal(0x09000002u, compiler.Code[2]);        // BINOP_LOCALS, count 2
        // Locals are frame-relative, so they take no global-pool slot: "1" is pool index 2, right
        // after the entry marker.
        Assert.Equal(0x02000002u, compiler.Code[3]);        // constant "1"
        Assert.Equal(0x04ffffffu, compiler.Code[4]);        // STORE_FP -1  (a, the first local)
        Assert.Equal(-1, GpdlCode.SignExtend24(GpdlCode.OperandOf(compiler.Code[4])));
    }

    [Fact]
    public void A_local_declared_after_executable_code_is_rejected()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(1, compiler.Compile("""$PUBLIC $FUNC f() { f(); $VAR a; } f;"""));
        Assert.Contains(compiler.Errors, e => e.Contains("before any executable code", StringComparison.Ordinal));
    }

    [Fact]
    public void Parameters_are_numbered_from_the_last_declared()
    {
        // addFormalParam prepends (GPDLcomp.cpp:1070) and defineFormalParameters numbers from the
        // head, so the LAST parameter is frame offset 0. That matches the runtime: actuals are
        // pushed left to right onto a downward stack and FP lands on the last one.
        var compiler = CompileOk("""
            $PUBLIC $FUNC f(first, second) { $RETURN second; } f;
            """);

        // 0 NOOP, 1 marker, 2 FETCH_FP <offset of 'second'>, 3 STORE_FP 2, 4 RETURN, 5 RETURN
        Assert.Equal(0x05000000u, compiler.Code[2]);        // FETCH_FP 0 -> 'second'
        Assert.Equal(0x04000002u, compiler.Code[3]);        // STORE_FP 2 -> result slot at FP+2
        Assert.Equal(0x08000002u, compiler.Code[4]);        // RETURN locals=0, params=2
    }

    [Fact]
    public void A_user_function_call_pushes_a_default_result_before_its_arguments()
    {
        // compileFunctionCall emits SUBOP_FALSE first for a *user* function only
        // (GPDLcomp.cpp:3060) -- that cell becomes the result slot the callee stores into.
        var compiler = CompileOk("""
            $FUNC g(x) { $RETURN x; } g;
            $PUBLIC $FUNC f() { g("q"); } f;
            """);

        int callSite = Array.FindIndex(compiler.Code, w => GpdlCode.OpOf(w) == BinOp.BINOP_CALL);
        Assert.True(callSite > 2);
        Assert.Equal(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_FALSE, compiler.Code[callSite - 2]);
        // ... and the statement's discarded result is popped.
        Assert.Equal(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_POP, compiler.Code[callSite + 1]);
    }

    [Fact]
    public void A_system_function_call_does_not_push_a_default_result()
    {
        var compiler = CompileOk("""$PUBLIC $FUNC f() { $RETURN $LENGTH("abc"); } f;""");
        Assert.Equal(0x02000001u, compiler.Code[1]);   // entry marker
        Assert.Equal(0x02000002u, compiler.Code[2]);   // "abc"
        Assert.Equal(GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_LENGTH, compiler.Code[3]);
    }

    [Fact]
    public void Operator_precedence_follows_operDef()
    {
        // '+' (40) binds tighter than '==' (30), so the concatenation is emitted first.
        var compiler = CompileOk("""$PUBLIC $FUNC f() { $RETURN "a" + "b" == "ab"; } f;""");
        var subops = compiler.Code
            .Where(w => GpdlCode.OpOf(w) == BinOp.BINOP_SUBOP)
            .Select(w => (SubOp)GpdlCode.OperandOf(w))
            .ToList();
        Assert.Equal(
            [SubOp.SUBOP_NOOP, SubOp.SUBOP_PLUS, SubOp.SUBOP_ISEQUAL],
            subops);
    }

    [Fact]
    public void Leading_bang_is_emitted_after_the_first_operand()
    {
        // A unary operator is stacked with priority 999, which forces it out as soon as any binary
        // operator arrives (GPDLcomp.cpp:2663).
        var compiler = CompileOk("""$PUBLIC $FUNC f() { $RETURN !"a" || "b"; } f;""");
        var subops = compiler.Code
            .Where(w => GpdlCode.OpOf(w) == BinOp.BINOP_SUBOP)
            .Select(w => (SubOp)GpdlCode.OperandOf(w))
            .ToList();
        Assert.Equal(
            [SubOp.SUBOP_NOOP, SubOp.SUBOP_NOT, SubOp.SUBOP_LOR],
            subops);
    }

    [Fact]
    public void Assigning_with_hash_equal_forces_the_value_numeric()
    {
        var compiler = CompileOk("""$VAR g; $PUBLIC $FUNC f() { g =# "3.9"; } f;""");
        Assert.Contains(
            GpdlCode.ShiftedSubOp | (uint)SubOp.SUBOP_FORCENUMERIC,
            compiler.Code);
    }

    [Fact]
    public void Switch_duplicates_the_subject_and_chains_the_case_tests()
    {
        var compiler = CompileOk("""
            $PUBLIC $FUNC f() {
              $SWITCH (1) {
                $CASE 1: $RETURN "one";
                $CASE 2: $RETURN "two";
              } $ENDSWITCH;
            } f;
            """);

        var subops = compiler.Code
            .Where(w => GpdlCode.OpOf(w) == BinOp.BINOP_SUBOP)
            .Select(w => (SubOp)GpdlCode.OperandOf(w))
            .ToList();
        // NOOP, then DUP/== per case. No POP of the subject: a switch leaves it on the stack, and
        // the enclosing function's RETURN discards it with SP = FP.
        Assert.Equal(
            [SubOp.SUBOP_NOOP, SubOp.SUBOP_DUP, SubOp.SUBOP_ISEQUAL,
             SubOp.SUBOP_DUP, SubOp.SUBOP_ISEQUAL],
            subops);
    }

    [Fact]
    public void GCASE_swaps_the_operands_before_grepping()
    {
        // $GREP takes (pattern, string) but the switch subject is the string, so the compiler must
        // swap (GPDLcomp.cpp:2133). Without the SWAP the pattern and text are exchanged and every
        // $GCASE silently mismatches.
        var compiler = CompileOk("""
            $PUBLIC $FUNC f() {
              $SWITCH ("hello") {
                $GCASE "ell": $RETURN "yes";
              } $ENDSWITCH;
            } f;
            """);

        var subops = compiler.Code
            .Where(w => GpdlCode.OpOf(w) == BinOp.BINOP_SUBOP)
            .Select(w => (SubOp)GpdlCode.OperandOf(w))
            .ToList();
        Assert.Equal(
            [SubOp.SUBOP_NOOP, SubOp.SUBOP_DUP, SubOp.SUBOP_SWAP, SubOp.SUBOP_GREP],
            subops);
    }

    [Fact]
    public void Respond_expands_to_grep_say_pop_continue()
    {
        var compiler = CompileOk("""
            $PUBLIC $FUNC f() {
              $WHILE (1) { $RESPOND("hi", "hello"); };
            } f;
            """);

        var subops = compiler.Code
            .Where(w => GpdlCode.OpOf(w) == BinOp.BINOP_SUBOP)
            .Select(w => (SubOp)GpdlCode.OperandOf(w))
            .ToList();
        Assert.Equal(
            [SubOp.SUBOP_NOOP, SubOp.SUBOP_LISTENTEXT, SubOp.SUBOP_GREP,
             SubOp.SUBOP_SAY, SubOp.SUBOP_POP],
            subops);
    }

    [Fact]
    public void Nested_functions_are_jumped_around()
    {
        // A function defined inside another must not be executed as part of it, so the enclosing
        // body gets a JUMP over the nested body (GPDLcomp.cpp:3493).
        var compiler = CompileOk("""
            $PUBLIC $FUNC outer() {
              $FUNC inner() { $RETURN "i"; } inner;
              inner();
            } outer;
            """);

        Assert.Equal(BinOp.BINOP_JUMP, GpdlCode.OpOf(compiler.Code[2]));
        uint afterInner = GpdlCode.OperandOf(compiler.Code[2]);
        Assert.True(afterInner > 3, "the jump must land past the nested body");
        Assert.Equal(BinOp.BINOP_CALL, GpdlCode.OpOf(compiler.Code[(int)afterInner + 1]));
    }

    [Fact]
    public void Nested_public_functions_are_indexed_with_an_at_qualified_name()
    {
        var compiler = CompileOk("""
            $PUBLIC $FUNC outer() {
              $PUBLIC $FUNC inner() { $RETURN "i"; } inner;
              inner();
            } outer;
            """);

        var index = compiler.PublicFunctionIndex();
        Assert.Contains(index, e => e.Name == "outer");
        Assert.Contains(index, e => e.Name == "outer@inner");
    }

    [Fact]
    public void Hash_PUBLIC_pragma_makes_every_later_function_public()
    {
        var compiler = CompileOk("""
            #PUBLIC
            $FUNC a() { $RETURN "a"; } a;
            $FUNC b() { $RETURN "b"; } b;
            """);
        Assert.Equal(2, compiler.PublicFunctionIndex().Count);
    }

    [Fact]
    public void A_global_level_prototype_needs_two_semicolons()
    {
        // compileFunctionDecl swallows the terminating semicolon on the prototype path
        // (GPDLcomp.cpp:3478) and then CompileProgram demands one of its own (GPDLcomp.cpp:3562).
        // The single-semicolon form -- the one anybody would write -- is rejected.
        var single = new GpdlCompiler();
        Assert.Equal(1, single.Compile("$FUNC p();\n$PUBLIC $FUNC f() { $RETURN 1; } f;"));
        Assert.Contains(single.Errors,
            e => e.Contains("Expected semi-colon after function definition", StringComparison.Ordinal));

        CompileOk("$FUNC p();;\n$PUBLIC $FUNC f() { $RETURN 1; } f;");
    }

    [Fact]
    public void A_prototype_with_parameters_cannot_be_defined()
    {
        // The consequence of checkProtoParam being called on the wrong DEFINITION
        // (GPDLcomp.cpp:3439). Recorded so that a later "fix" is a visible decision rather than an
        // accident: the reference compiler rejects this source.
        var compiler = new GpdlCompiler();
        Assert.Equal(1, compiler.Compile("""
            $FUNC p(x);;
            $FUNC p(x) { $RETURN x; } p;
            """));
        Assert.Contains(compiler.Errors,
            e => e.Contains("prototype has fewer parameters", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_declarations_are_legal_at_global_level()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(1, compiler.Compile("""$SAY("hello");"""));
        Assert.Contains(compiler.Errors, e => e.Contains("Illegal statement at global level", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mismatched_end_sentinel_is_rejected()
    {
        // The sentinel exists precisely to catch unbalanced braces (src/GPDL/language.txt:12).
        var compiler = new GpdlCompiler();
        Assert.Equal(1, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN 1; } g;"""));
        Assert.Contains(compiler.Errors, e => e.Contains("Expected ending block name", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ACTOR_typed_parameter_must_be_a_single_system_function_call()
    {
        // $Gender takes an ACTOR (systemfunctions[] row: types {0, ACTOR}), so an arbitrary
        // expression is refused -- compileTypedSystemFunctionCall, GPDLcomp.cpp:2787.
        var compiler = new GpdlCompiler();
        Assert.Equal(1, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN $Gender("x"); } f;"""));
        Assert.Contains(compiler.Errors, e => e.Contains("Expected a system function name", StringComparison.Ordinal));

        // $Myself() returns ACTOR, so it is accepted.
        CompileOk("""$PUBLIC $FUNC f() { $RETURN $Gender($Myself()); } f;""");
    }

    [Fact]
    public void A_system_function_returning_ACTOR_cannot_be_used_as_a_string_term()
    {
        var compiler = new GpdlCompiler();
        Assert.Equal(1, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN $Myself(); } f;"""));
        Assert.Contains(compiler.Errors,
            e => e.Contains("function that returns a string value", StringComparison.Ordinal));
    }

    [Fact]
    public void A_required_context_the_hook_does_not_provide_is_rejected()
    {
        // requiredContexts[] maps $Myself to CTX_Myself (GPDLcomp.cpp:1671). A hook that does not
        // supply it must not compile the call.
        var compiler = new GpdlCompiler { AvailableContexts = GpdlContexts.None };
        Assert.Equal(1, compiler.Compile("""$PUBLIC $FUNC f() { $RETURN $Gender($Myself()); } f;"""));
        Assert.Contains(compiler.Errors, e => e.Contains("context that is not provided", StringComparison.Ordinal));
    }

    [Fact]
    public void Parameter_count_is_enforced_exactly()
    {
        var tooFew = new GpdlCompiler();
        Assert.Equal(1, tooFew.Compile("""$PUBLIC $FUNC f() { $RETURN $MIDDLE("abc", 1); } f;"""));
        Assert.Contains(tooFew.Errors, e => e.Contains("Not enough parameters", StringComparison.Ordinal));

        var tooMany = new GpdlCompiler();
        Assert.Equal(1, tooMany.Compile("""$PUBLIC $FUNC f() { $RETURN $LENGTH("a", "b"); } f;"""));
        Assert.Contains(tooMany.Errors, e => e.Contains("Too many parameters", StringComparison.Ordinal));
    }

    [Fact]
    public void True_and_false_are_recognised_only_after_symbol_lookup_fails()
    {
        var compiler = CompileOk("$PUBLIC $FUNC f() { $RETURN true; } f;");
        Assert.Contains("1", compiler.Globals.SerializedValues());

        // A variable named 'true' therefore shadows the literal.
        var shadowed = CompileOk("""$VAR true; $PUBLIC $FUNC f() { $RETURN true; } f;""");
        Assert.Equal(BinOp.BINOP_ReferenceGLOBAL, GpdlCode.OpOf(shadowed.Code[2]));
        Assert.Equal(1u, GpdlCode.OperandOf(shadowed.Code[2]));   // global slot, not a constant
    }

    [Fact]
    public void Only_two_globals_exist_for_a_script_compile_because_markers_are_empty()
    {
        // The embedded-script path suppresses the "name(n)" entry markers (GPDLcomp.cpp:3504), which
        // is why GPDL::BeginExecute(int) cannot validate argument counts on that path.
        var compiler = new GpdlCompiler();
        Assert.Equal(0, compiler.Compile("$PUBLIC $FUNC f() { $RETURN 1; } f;", compilingScript: true));
        Assert.Equal(["", "", "1"], compiler.Globals.SerializedValues());
    }
}
