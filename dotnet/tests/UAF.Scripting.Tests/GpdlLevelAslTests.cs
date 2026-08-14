using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// Per-level attributes, and the two game queries beside them.
/// </summary>
/// <remarks>
/// <c>$SET_LEVEL_STATS_ASL</c> and <c>$DELETE_LEVEL_STATS_ASL</c> take a level as their first
/// parameter, unlike the global and party forms — and an empty one means "wherever the party is"
/// (<c>GPDLexec.cpp:5526</c>).
/// </remarks>
public class GpdlLevelAslTests
{
    private sealed class LevelHost : GpdlUnhostedEnvironment
    {
        public override int CurrentLevel => 7;

        public override string GameVersion => "5.24000000";
    }

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

    /// <summary>A value written to a named level lands on that level.</summary>
    [Fact]
    public void A_value_lands_on_the_level_it_names()
    {
        var host = new LevelHost();
        Run("""$SET_LEVEL_STATS_ASL("3", "guard", "asleep");""", host);

        Assert.Equal("asleep", host.GetLevelAsl(3, "guard"));

        // And nowhere else -- not on the current level, and not on its neighbours.
        Assert.Equal(string.Empty, host.GetLevelAsl(7, "guard"));
        Assert.Equal(string.Empty, host.GetLevelAsl(4, "guard"));
    }

    /// <summary>
    /// An empty level means the current one, not level zero.
    /// </summary>
    /// <remarks>
    /// <b>This is the detail the reference spends a line on.</b> It tests the popped string
    /// against <c>""</c> before <c>atoi</c>-ing it — and <c>atoi("")</c> is 0, a level no design
    /// has. A script that omits the level is asking about where it is standing.
    /// </remarks>
    [Fact]
    public void An_empty_level_means_the_current_one()
    {
        var host = new LevelHost();
        Run("""$SET_LEVEL_STATS_ASL("", "torch", "lit");""", host);

        Assert.Equal("lit", host.GetLevelAsl(7, "torch"));
        Assert.Equal(string.Empty, host.GetLevelAsl(0, "torch"));
    }

    /// <summary>Unparseable text means the current level too, rather than zero.</summary>
    /// <remarks>
    /// The reference reaches <c>atoi</c>, which yields 0 for text — the same level-zero trap. The
    /// port refuses it the same way it refuses an empty string, since neither names a level.
    /// </remarks>
    [Fact]
    public void Unparseable_text_means_the_current_level()
    {
        var host = new LevelHost();
        Run("""$SET_LEVEL_STATS_ASL("upstairs", "door", "open");""", host);

        Assert.Equal("open", host.GetLevelAsl(7, "door"));
        Assert.Equal(string.Empty, host.GetLevelAsl(0, "door"));
    }

    /// <summary>Delete takes the level and the key, and leaves other levels alone.</summary>
    [Fact]
    public void Delete_removes_only_the_level_it_names()
    {
        var host = new LevelHost();

        host.SetLevelAsl(2, "key", "value");
        host.SetLevelAsl(3, "key", "value");

        Run("""$DELETE_LEVEL_STATS_ASL("2", "key");""", host);

        Assert.Equal(string.Empty, host.GetLevelAsl(2, "key"));
        Assert.Equal("value", host.GetLevelAsl(3, "key"));
    }

    /// <summary>The current level is one-based.</summary>
    /// <remarks>
    /// The reference pushes <c>currLevel + 1</c>: the stored index is zero-based and the script
    /// sees the number a designer would write.
    /// </remarks>
    [Fact]
    public void The_current_level_is_one_based() =>
        Assert.Equal("7", Run("$RETURN $GET_GAME_CURRLEVEL();", new LevelHost()));

    /// <summary>The version keeps its eight decimal places.</summary>
    /// <remarks>
    /// <c>"%1.8f"</c>. A script comparing it against a literal compares strings, so the trailing
    /// zeroes are part of the answer rather than noise.
    /// </remarks>
    [Fact]
    public void The_version_is_formatted_to_eight_places() =>
        Assert.Equal("5.24000000", Run("$RETURN $GET_GAME_VERSION();", new LevelHost()));
}
