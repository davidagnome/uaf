using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$LOGIC_BLOCK_VALUE</c>, and the three small calls beside it.
/// </summary>
public class GpdlLogicBlockTests
{
    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        Assert.True(compiler.Compile("$PUBLIC $FUNC f() { " + body + " } f;") == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    /// <summary>Each letter reads its own record.</summary>
    [Theory]
    [InlineData("A", "first")]
    [InlineData("B", "second")]
    [InlineData("C", "third")]
    [InlineData("L", "twelfth")]
    public void Each_letter_reads_its_own_record(string letter, string expected)
    {
        var host = new GpdlUnhostedEnvironment
        {
            LogicBlockValues = GpdlLogicBlock.Pack(
                ["first", "second", "third", "", "", "", "", "", "", "", "", "twelfth"]),
        };

        Assert.Equal(expected, Run($"""$RETURN $LOGIC_BLOCK_VALUE("{letter}");""", host));
    }

    /// <summary>
    /// Only the first character is looked at, so a word selects the same record as its initial.
    /// </summary>
    [Fact]
    public void Only_the_first_character_selects()
    {
        var host = new GpdlUnhostedEnvironment
        {
            LogicBlockValues = GpdlLogicBlock.Pack(["alpha", "beta"]),
        };

        Assert.Equal("beta", Run("""$RETURN $LOGIC_BLOCK_VALUE("B");""", host));
        Assert.Equal("beta", Run("""$RETURN $LOGIC_BLOCK_VALUE("Beta");""", host));
    }

    /// <summary>
    /// Twelve is fixed, so the letter after the twelfth is out of range.
    /// </summary>
    /// <remarks>
    /// A logic block always records exactly twelve values — an unused one is written empty rather
    /// than left out — so <c>"M"</c> is not a short blob, it is not a value at all.
    /// </remarks>
    [Theory]
    [InlineData("M")]
    [InlineData("Z")]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("")]
    public void A_letter_outside_the_twelve_is_empty(string letter)
    {
        var host = new GpdlUnhostedEnvironment
        {
            LogicBlockValues = GpdlLogicBlock.Pack(["alpha"]),
        };

        Assert.Equal(string.Empty,
                     Run($"""$RETURN $LOGIC_BLOCK_VALUE("{letter}");""", host));
    }

    /// <summary>An empty record reads as empty, not as missing.</summary>
    [Fact]
    public void An_unused_record_is_empty()
    {
        var host = new GpdlUnhostedEnvironment
        {
            LogicBlockValues = GpdlLogicBlock.Pack(["alpha", "", "gamma"]),
        };

        Assert.Equal(string.Empty, Run("""$RETURN $LOGIC_BLOCK_VALUE("B");""", host));

        // And the record after it still reads correctly, so a zero length was stepped over.
        Assert.Equal("gamma", Run("""$RETURN $LOGIC_BLOCK_VALUE("C");""", host));
    }

    /// <summary>No values at all is empty rather than an error.</summary>
    [Fact]
    public void No_values_at_all_is_empty() =>
        Assert.Equal(string.Empty,
                     Run("""$RETURN $LOGIC_BLOCK_VALUE("A");""", new GpdlUnhostedEnvironment()));

    /// <summary>A blob cut short answers empty rather than reading past its end.</summary>
    [Fact]
    public void A_truncated_blob_does_not_read_past_its_end()
    {
        string packed = GpdlLogicBlock.Pack(["alpha", "beta"]);

        // Cut off mid-record: the first still reads, the second cannot.
        var host = new GpdlUnhostedEnvironment { LogicBlockValues = packed[..12] };

        Assert.Equal("alpha", GpdlLogicBlock.Value(host.LogicBlockValues, "A"));
        Assert.Equal(string.Empty, GpdlLogicBlock.Value(host.LogicBlockValues, "B"));

        // And a blob too short even for one length prefix.
        Assert.Equal(string.Empty, GpdlLogicBlock.Value("ab", "A"));
    }

    /// <summary>A value longer than a byte survives the round trip.</summary>
    /// <remarks>
    /// The length is four bytes little-endian, one character each, so anything past 255 spans more
    /// than one — a packer writing a single byte would work for every short value and fail here.
    /// </remarks>
    [Fact]
    public void A_long_value_survives_the_round_trip()
    {
        string big = new('x', 300);

        Assert.Equal(big, GpdlLogicBlock.Value(GpdlLogicBlock.Pack([big, "after"]), "A"));
        Assert.Equal("after", GpdlLogicBlock.Value(GpdlLogicBlock.Pack([big, "after"]), "B"));
    }

    /// <summary>Packing always produces twelve records, padded or truncated.</summary>
    [Fact]
    public void Packing_always_produces_twelve()
    {
        Assert.Equal(12, GpdlLogicBlock.Count);

        // One value in, and the twelfth still reads (as empty) rather than running off the end.
        string packed = GpdlLogicBlock.Pack(["only"]);
        Assert.Equal("only", GpdlLogicBlock.Value(packed, "A"));
        Assert.Equal(string.Empty, GpdlLogicBlock.Value(packed, "L"));

        // Thirteen in, and the thirteenth is dropped.
        string many = GpdlLogicBlock.Pack(Enumerable.Range(0, 13).Select(i => $"v{i}"));
        Assert.Equal("v11", GpdlLogicBlock.Value(many, "L"));
        Assert.Equal(12 * 4 + Enumerable.Range(0, 12).Sum(i => $"v{i}".Length), many.Length);
    }

    /// <summary>
    /// <c>$CURR_CHANGE_BY_VAL</c> rounds the VM's intermediate result half away from zero.
    /// </summary>
    /// <remarks>
    /// <b>Away from zero, not to even.</b> The reference spells the rule out as
    /// <c>ceil(x - 0.5)</c> below zero and <c>floor(x + 0.5)</c> above it — so −2.5 is −3 where
    /// .NET's default rounding would give −2.
    /// </remarks>
    [Theory]
    [InlineData(2.4, "2")]
    [InlineData(2.5, "3")]
    [InlineData(2.6, "3")]
    [InlineData(-2.4, "-2")]
    [InlineData(-2.5, "-3")]
    [InlineData(0.0, "0")]
    [InlineData(3.5, "4")]
    public void The_intermediate_result_rounds_away_from_zero(double value, string expected)
    {
        var compiler = new GpdlCompiler();
        Assert.True(compiler.Compile(
            "$PUBLIC $FUNC f() { $RETURN $CURR_CHANGE_BY_VAL(); } f;") == 0,
            string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler),
                                        new GpdlUnhostedEnvironment())
        {
            IntermediateResult = value,
        };

        Assert.Equal(expected, vm.Execute("f"));
    }

    /// <summary>Redrawing the screen takes nothing and answers nothing.</summary>
    [Fact]
    public void Drawing_the_adventure_screen_answers_nothing()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal(string.Empty, Run("$RETURN $DrawAdventureScreen();", host));
        Assert.Equal(1, host.AdventureScreenDraws);
    }

    /// <summary>
    /// An event attribute that is not there answers the <c>-?-?-</c> sentinel, not empty.
    /// </summary>
    /// <remarks>
    /// The same marker the ability calls use, so a design can tell "no such attribute" from "an
    /// attribute set to nothing".
    /// </remarks>
    [Fact]
    public void A_missing_event_attribute_answers_the_sentinel()
    {
        Assert.Equal(GpdlScriptContext.NoSuchAbility,
                     Run("""$RETURN $GET_EVENT_Attribute("0", "colour");""",
                         new GpdlUnhostedEnvironment()));
    }
}
