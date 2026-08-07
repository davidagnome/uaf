namespace UAF.Scripting.Tests;

/// <summary>
/// The Forth VM: that its kernel builds itself, and that the machine underneath behaves.
/// </summary>
public class ForthMachineTests
{
    /// <summary>A machine with its dictionary built.</summary>
    private static ForthMachine Booted()
    {
        var forth = new ForthMachine();

        Assert.True(forth.Bootstrap(),
                    "kernel did not build: " + string.Join("; ", forth.Output));

        return forth;
    }

    /// <summary>Evaluates a line and returns the top of the data stack.</summary>
    private static short Value(ForthMachine forth, string line)
    {
        Assert.True(forth.Evaluate(line), "aborted: " + string.Join("; ", forth.Output));
        return forth.Memory.Stack(0);
    }

    [Fact]
    public void The_kernel_builds_its_own_dictionary()
    {
        var forth = Booted();

        // Words the kernel defines in Forth rather than in C -- if these are present, the outer
        // interpreter, the compiler and the header layout all worked.
        foreach (string word in new[] { ":", ";", "CONSTANT", "2DUP", "IMMEDIATE", "BL", "[']" })
        {
            Assert.True(forth.Lookup(word) != 0, $"the kernel did not define '{word}'");
        }
    }

    [Fact]
    public void The_primitive_table_and_the_kernels_PRIM_lines_are_the_same_list()
    {
        var forth = Booted();

        // +p stamps nextPrim++ into whatever CREATE just made, so the two are bound by position
        // and by nothing else. A word inserted on one side shifts every index after it.
        Assert.Equal(1, forth.Memory.OpcodeOf(forth.Lookup("CREATE")));
        Assert.Equal(2, forth.Memory.OpcodeOf(forth.Lookup("+p")));
        Assert.Equal(21, forth.Memory.OpcodeOf(forth.Lookup("docolon")));
        // docon is created by `PRIM docon` at index 51 and then IMMEDIATELY REBOUND: the
        // kernel line that follows reads its own code field, compiles that index as a literal
        // into a new body, and stamps docolon's index over it. So `docon` ends up a colon word
        // that pushes 51 -- and the primitive at 51 is reachable only through what it compiles.
        Assert.Equal(21, forth.Memory.OpcodeOf(forth.Lookup("docon")));
        Assert.Equal(21, forth.Memory.OpcodeOf(forth.Lookup("docolon")));

        // And the game words sit at the end, in kernel order.
        for (int i = 0; i < ForthMachine.GameWords.Length; i++)
        {
            int link = forth.Lookup(ForthMachine.GameWords[i]);
            Assert.True(link != 0, $"'{ForthMachine.GameWords[i]}' is not in the dictionary");
            Assert.Equal(52 + i, forth.Memory.OpcodeOf(link));
        }
    }

    [Fact]
    public void A_colon_definition_compiles_and_runs()
    {
        var forth = Booted();

        Assert.Equal(49, Value(forth, ": SQUARE DUP UM* DROP ; 7 SQUARE"));
    }

    [Fact]
    public void Arithmetic_and_the_stack_words_behave()
    {
        var forth = Booted();

        Assert.Equal(7, Value(forth, "3 4 +"));
        Assert.Equal(4, Value(forth, "9 5 -"));
        Assert.Equal(6, Value(forth, "6 NEGATE ABS"));
        Assert.Equal(2, Value(forth, "1 2 OVER DROP SWAP DROP"));

        // ROT brings the third item up: 1 2 3 becomes 2 3 1, so the top is 1 and the bottom is 2.
        Assert.Equal(1, Value(forth, "1 2 3 ROT"));
        Assert.Equal(3, Value(forth, "1 2 3 ROT DROP"));
        Assert.Equal(2, Value(forth, "1 2 3 ROT DROP DROP"));
    }

    [Fact]
    public void A_comparison_is_true_as_minus_one_not_as_one()
    {
        var forth = Booted();

        // Forth's flags are -1, which is what makes `A:Type = IF 0 ELSE 1 THEN` in the AI script
        // read the way it does. A port that used 1 would still branch correctly and would get
        // arithmetic on the flag wrong.
        Assert.Equal(-1, Value(forth, "4 4 ="));
        Assert.Equal(0, Value(forth, "4 5 ="));
        Assert.Equal(-1, Value(forth, "4 5 <"));
        Assert.Equal(-1, Value(forth, "5 4 >"));
        Assert.Equal(-1, Value(forth, "4 5 !="));
        Assert.Equal(-1, Value(forth, "0 NOT"));
    }

    [Fact]
    public void Numbers_parse_in_decimal_and_in_the_kernels_hex_notation()
    {
        var forth = Booted();

        Assert.Equal(32, Value(forth, "H'20'"));
        Assert.Equal(255, Value(forth, "H'FF'"));
        Assert.Equal(-12, Value(forth, "-12"));

        // BL is defined in the kernel as H'20' CONSTANT BL, so this exercises both.
        Assert.Equal(32, Value(forth, "BL"));
    }

    [Fact]
    public void A_word_that_is_neither_defined_nor_a_number_aborts_and_names_itself()
    {
        var forth = Booted();

        Assert.False(forth.Evaluate("1 2 NOSUCHWORD 3"));

        // There is no "skip it and carry on" path -- which is why one typo stops a whole
        // AI_Script.BLK rather than quietly changing what the monsters do.
        Assert.Contains("NOSUCHWORD?", forth.Output);
    }

    [Fact]
    public void A_word_that_leaves_the_stack_at_the_wrong_depth_is_refused_by_name()
    {
        var forth = Booted();

        // Declared to leave one value and leaves two. The reference calls die() here.
        var thrown = Assert.Throws<ForthStackException>(
            () => forth.Evaluate(": WRONG 1 2 ; 1 SP+- WRONG"));

        Assert.Contains("WRONG", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("added 1 too many", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_immediate_word_runs_while_compiling()
    {
        var forth = Booted();

        // ['] is defined IMMEDIATE in the kernel; if immediacy did not work, the definition below
        // would compile it instead of running it and the address would never be laid down.
        Assert.NotEqual(0, Value(forth, ": ADDR ['] DUP ; 1 SP+- ADDR"));
    }

    // ---- the machine underneath -------------------------------------------------------------------

    [Fact]
    public void Cells_are_signed_sixteen_bit_and_little_endian()
    {
        var memory = new ForthMemory();

        memory.SetCell(100, 0x1234);

        Assert.Equal(0x34, memory.Bytes[100]);
        Assert.Equal(0x12, memory.Bytes[101]);
        Assert.Equal(0x1234, memory.Cell(100));

        memory.SetCell(100, -2);
        Assert.Equal(-2, memory.Cell(100));
    }

    [Fact]
    public void A_double_cell_is_stored_high_half_first()
    {
        var memory = new ForthMemory();

        // The opposite convention to the byte order inside a cell, which is the sort of thing that
        // works for small numbers and fails once a product exceeds 16 bits.
        memory.SetDoubleCell(100, 0x11223344);

        Assert.Equal(0x1122, memory.Cell(100));
        Assert.Equal(0x3344, (ushort)memory.Cell(102));
        Assert.Equal(0x11223344, memory.DoubleCell(100));
    }

    [Fact]
    public void The_stacks_grow_downwards_from_the_top_of_memory()
    {
        var memory = new ForthMemory();

        memory.Push(11);
        memory.Push(22);

        Assert.Equal(ForthMemory.DataStackBase - 4, memory.Sp);
        Assert.Equal(22, memory.Stack(0));
        Assert.Equal(11, memory.Stack(2));
        Assert.Equal(22, memory.Pop());
        Assert.Equal(11, memory.Pop());
        Assert.Equal(ForthMemory.DataStackBase, memory.Sp);
    }

    [Fact]
    public void The_game_words_are_present_and_refuse_outside_RunThink()
    {
        var forth = Booted();

        // They have to be in the dictionary for the kernel to build -- the PRIM lines that name
        // them hand out the indices -- but nothing in the kernel calls one. Reached outside a
        // RunThink there is no summary to read, and the reader words say so rather than
        // inventing one.
        var thrown = Assert.Throws<InvalidOperationException>(() => forth.Evaluate("C:Distance"));

        Assert.Contains("RunThink", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_kernel_source_is_all_there()
    {
        // 697 words extracted from the live parts of Forth.cpp's initialiser. If this changes,
        // either a comment block moved or the extraction was rerun differently.
        Assert.Equal(697, ForthKernel.Source.Split(' ',
                          StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(697, ForthKernel.WordCount);
    }
}
