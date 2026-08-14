using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// The ten map-override calls — <c>$GetWall</c>/<c>$SetWall</c> and their four siblings.
/// </summary>
/// <remarks>
/// These are how a script repaints the map while the game runs: a level, a square and a side, and
/// one byte per layer. All ten go through <c>GetMapOverride</c>/<c>SetMapOverride</c>
/// (<c>GlobalData.cpp:2513</c>) and differ only in the layer they name.
/// </remarks>
public class GpdlMapOverrideTests
{
    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        string source = "$PUBLIC $FUNC f() { " + body + " } f;";
        Assert.True(compiler.Compile(source) == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    /// <summary>The five layers, by the names a script writes.</summary>
    public static TheoryData<string, string, GpdlMapOverrideKind> Layers => new()
    {
        { "$GetWall", "$SetWall", GpdlMapOverrideKind.Wall },
        { "$GetDoor", "$SetDoor", GpdlMapOverrideKind.Door },
        { "$GetBackground", "$SetBackground", GpdlMapOverrideKind.Background },
        { "$GetOverlay", "$SetOverlay", GpdlMapOverrideKind.Overlay },
        { "$GetBlockage", "$SetBlockage", GpdlMapOverrideKind.Blockage },
    };

    /// <summary>Each layer's pair writes and reads back its own square.</summary>
    [Theory]
    [MemberData(nameof(Layers))]
    public void A_layer_writes_and_reads_back(string get, string set, GpdlMapOverrideKind kind)
    {
        var host = new GpdlUnhostedEnvironment();

        Run($"""{set}("2", "3", "4", "1", "17");""", host);

        // Through the host...
        Assert.Equal(17, host.GetMapOverride(kind, 2, 3, 4, 1));

        // ...and through the script's own reader.
        Assert.Equal("17", Run($"""$RETURN {get}("2", "3", "4", "1");""", host));
    }

    /// <summary>
    /// The five layers are separate stores, not one.
    /// </summary>
    /// <remarks>
    /// A square carries a byte for each layer on each side. Writing a wall must not show up as a
    /// door — which a single keyed store that forgot the layer would do, and which would pass
    /// every single-layer test above.
    /// </remarks>
    [Fact]
    public void One_layer_does_not_show_through_another()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""$SetWall("1", "0", "0", "0", "9");""", host);

        Assert.Equal("9", Run("""$RETURN $GetWall("1", "0", "0", "0");""", host));

        foreach (string other in new[] { "$GetDoor", "$GetBackground", "$GetOverlay",
                                         "$GetBlockage" })
        {
            Assert.Equal("255", Run($"""$RETURN {other}("1", "0", "0", "0");""", host));
        }
    }

    /// <summary>
    /// The four sides of a square are separate too.
    /// </summary>
    [Fact]
    public void Each_side_of_a_square_is_its_own()
    {
        var host = new GpdlUnhostedEnvironment();

        for (int facing = 0; facing < 4; facing++)
        {
            Run($"""$SetWall("1", "5", "5", "{facing}", "{facing + 20}");""", host);
        }

        for (int facing = 0; facing < 4; facing++)
        {
            Assert.Equal((facing + 20).ToString(),
                         Run($"""$RETURN $GetWall("1", "5", "5", "{facing}");""", host));
        }
    }

    /// <summary>
    /// An unset square answers 255, and so does a level that does not exist.
    /// </summary>
    /// <remarks>
    /// <b>A script cannot tell the two apart.</b> <c>GetMapOverride</c> turns every failure — no
    /// such level, a row never allocated, a column short of the read — into the same -1, which the
    /// wrapper reports as 255. Worth pinning because it is a limit on what a design can ask.
    /// </remarks>
    [Fact]
    public void Nothing_there_and_no_such_level_both_answer_255()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal("255", Run("""$RETURN $GetWall("1", "0", "0", "0");""", host));
        Assert.Equal("255", Run("""$RETURN $GetWall("999", "0", "0", "0");""", host));
        Assert.Equal("255", Run("""$RETURN $GetWall("0", "0", "0", "0");""", host));
        Assert.Equal("255", Run("""$RETURN $GetWall("-4", "0", "0", "0");""", host));
    }

    /// <summary>
    /// Levels are one-based, so level 1 is the first and level 0 is no level at all.
    /// </summary>
    /// <remarks>
    /// The reference comments this twice over: <c>parameters[0]</c> is the level "starting with
    /// level 1!! There is no level 0". An off-by-one here would put every script's writes on the
    /// wrong level.
    /// </remarks>
    [Fact]
    public void Level_zero_is_not_a_level()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""$SetWall("0", "1", "1", "0", "42");""", host);
        Run("""$SetWall("256", "1", "1", "0", "42");""", host);

        // Neither write landed anywhere.
        Assert.Empty(host.MapOverrides);
    }

    /// <summary>
    /// Writing 255 clears the square rather than storing it, and larger values clamp down to it.
    /// </summary>
    /// <remarks>
    /// <b>One value doing two jobs.</b> A square holds a byte, so 255 is both the top of the range
    /// and the "nothing here" marker; <c>SetMapOverride</c> clamps anything above it and then
    /// refuses to allocate storage for it. A script writing 300 erases the square.
    /// </remarks>
    [Fact]
    public void Writing_255_clears_the_square()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""$SetWall("1", "2", "2", "0", "7");""", host);
        Assert.Equal("7", Run("""$RETURN $GetWall("1", "2", "2", "0");""", host));

        Run("""$SetWall("1", "2", "2", "0", "255");""", host);
        Assert.Equal("255", Run("""$RETURN $GetWall("1", "2", "2", "0");""", host));

        // And a value too large for a byte does the same, rather than wrapping to 44.
        Run("""$SetWall("1", "2", "2", "0", "7");""", host);
        Run("""$SetWall("1", "2", "2", "0", "300");""", host);
        Assert.Equal("255", Run("""$RETURN $GetWall("1", "2", "2", "0");""", host));
    }

    /// <summary>A facing outside 0–3 folds onto one of the four sides.</summary>
    /// <remarks>
    /// <c>facing % 4</c>, with negatives folded back up. So side 5 is side 1 and side -1 is side 3
    /// — the same square, not a miss.
    /// </remarks>
    [Theory]
    [InlineData("5", "1")]
    [InlineData("-1", "3")]
    [InlineData("8", "0")]
    public void A_facing_outside_the_four_folds_onto_one(string written, string read)
    {
        var host = new GpdlUnhostedEnvironment();

        Run($"""$SetWall("1", "0", "0", "{written}", "11");""", host);

        Assert.Equal("11", Run($"""$RETURN $GetWall("1", "0", "0", "{read}");""", host));
    }

    /// <summary>
    /// A setter answers nothing, so it is a statement rather than a value.
    /// </summary>
    /// <remarks>
    /// The reference pushes an empty string from all five setters — which in GPDL is false. A
    /// script writing <c>$IF $SetWall(...)</c> is always taking the else arm.
    /// </remarks>
    [Fact]
    public void A_setter_answers_nothing() =>
        Assert.Equal(string.Empty,
                     Run("""$RETURN $SetWall("1", "0", "0", "0", "3");""",
                         new GpdlUnhostedEnvironment()));

    /// <summary>
    /// <c>$GetDoor</c> takes the same four parameters as its four siblings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A divergence, and a deliberate one.</b> In the reference <c>$GetDoor</c> declares four
    /// parameters (<c>GPDLcomp.cpp:1613</c>) but its handler pops <b>five</b>
    /// (<c>GPDLexec.cpp:6247</c>) — the only one of the ten that disagrees with itself. The extra
    /// pop shifts every argument one place along, so the call reads whatever happened to be under
    /// the arguments as its level, the caller's level as x, x as y, y as facing, and drops facing
    /// entirely. It also removes a value that belonged to the caller.
    /// </para>
    /// <para>
    /// So <c>$GetDoor</c> cannot ever have worked, and no design can depend on what it returned.
    /// The port pops four, like the declaration and like the other nine.
    /// </para>
    /// </remarks>
    [Fact]
    public void GetDoor_takes_four_parameters_like_its_siblings()
    {
        var host = new GpdlUnhostedEnvironment();

        Run("""$SetDoor("3", "6", "7", "2", "88");""", host);

        // Read back at the square it was written to -- not one shifted along.
        Assert.Equal("88", Run("""$RETURN $GetDoor("3", "6", "7", "2");""", host));

        // And the call leaves the stack as it found it: a value pushed before the arguments
        // survives, which is what the reference's fifth pop would have eaten as its level.
        Assert.Equal("keep88", Run("""$RETURN "keep" + $GetDoor("3", "6", "7", "2");""", host));
    }

    /// <summary>
    /// Every one of the ten declares the arity the port dispatches.
    /// </summary>
    /// <remarks>
    /// The check that would have caught the <c>$GetDoor</c> defect: getters take four and setters
    /// five, and nothing in the family is exempt.
    /// </remarks>
    [Fact]
    public void The_ten_declare_four_and_five()
    {
        foreach (string name in new[] { "$GetWall", "$GetDoor", "$GetBackground", "$GetOverlay",
                                        "$GetBlockage" })
        {
            var entry = Assert.Single(GpdlSystemFunctions.Table, f => f.Name == name);
            Assert.Equal(4, entry.ParameterCount);
        }

        foreach (string name in new[] { "$SetWall", "$SetDoor", "$SetBackground", "$SetOverlay",
                                        "$SetBlockage" })
        {
            var entry = Assert.Single(GpdlSystemFunctions.Table, f => f.Name == name);
            Assert.Equal(5, entry.ParameterCount);
        }
    }
}
