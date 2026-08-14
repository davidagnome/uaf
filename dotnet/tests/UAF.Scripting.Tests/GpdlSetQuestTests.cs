using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$SET_QUEST</c>, whose value argument is scanned rather than parsed.
/// </summary>
/// <remarks>
/// The reference walks every character of the value string: a <c>-</c> anywhere sets the sign to
/// minus, a <c>+</c> anywhere sets it to plus, and every digit is accumulated wherever it sits
/// (<c>GPDLexec.cpp:5622</c>). Nothing is rejected, so there is no such thing as a malformed value
/// — only a surprising one.
/// </remarks>
public class GpdlSetQuestTests
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

    private static string Set(string value, GpdlUnhostedEnvironment host) =>
        Run($"""$RETURN $SET_QUEST("rescue", "{value}");""", host);

    /// <summary>A plain number is an assignment.</summary>
    [Fact]
    public void A_plain_number_assigns()
    {
        var host = new GpdlUnhostedEnvironment();
        host.SetQuestStage("rescue", 7);

        Assert.Equal("4", Set("4", host));
        Assert.Equal(4, host.QuestStage("rescue"));
    }

    /// <summary>
    /// A sign makes it relative, which is why "5" and "+5" differ.
    /// </summary>
    [Fact]
    public void A_sign_makes_it_relative()
    {
        var host = new GpdlUnhostedEnvironment();
        host.SetQuestStage("rescue", 10);

        Assert.Equal("15", Set("+5", host));
        Assert.Equal("12", Set("-3", host));

        // And without one it lands on the number itself, not ten plus five.
        Assert.Equal("5", Set("5", host));
    }

    /// <summary>
    /// The scan ignores where the sign and digits sit.
    /// </summary>
    /// <remarks>
    /// <b>This is the behaviour, not a bug being reproduced.</b> <c>"1-2"</c> is minus twelve —
    /// the digits accumulate into 12 and the <c>-</c> makes the whole thing relative — rather than
    /// one minus two. A script written expecting arithmetic gets something else.
    /// </remarks>
    [Theory]
    [InlineData("1-2", 100, 88)]     // digits 1 then 2 = 12, sign '-', so 100 - 12
    [InlineData("2+3", 100, 123)]    // 23 added, not 2 plus 3
    [InlineData("+-5", 100, 95)]     // the LATER sign wins
    [InlineData("-+5", 100, 105)]    // and so it does the other way round
    [InlineData("x9y", 100, 9)]      // no sign anywhere: an assignment of 9
    [InlineData("", 100, 0)]         // nothing at all assigns zero
    public void The_scan_ignores_position(string value, int start, int expected)
    {
        var host = new GpdlUnhostedEnvironment();
        host.SetQuestStage("rescue", start);

        Assert.Equal(expected.ToString(), Set(value, host));
        Assert.Equal(expected, host.QuestStage("rescue"));
    }

    /// <summary>
    /// The answer is read back from the store, not echoed.
    /// </summary>
    /// <remarks>
    /// The reference sets the stage and then calls <c>GetStage</c> again before pushing — so a
    /// store that clamps tells the script what the quest actually is, not what it asked for.
    /// </remarks>
    [Fact]
    public void The_answer_comes_from_the_store()
    {
        var host = new ClampingHost();

        // Asked for 99, and the store keeps 10.
        Assert.Equal("10", Set("99", host));
        Assert.Equal(10, host.QuestStage("rescue"));
    }

    private sealed class ClampingHost : GpdlUnhostedEnvironment
    {
        public override void SetQuestStage(string quest, int stage) =>
            base.SetQuestStage(quest, Math.Clamp(stage, 0, 10));
    }
}
