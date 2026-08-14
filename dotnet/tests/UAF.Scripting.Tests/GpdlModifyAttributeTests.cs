using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$MODIFY_CHAR_ATTRIBUTE</c> and <c>$REMOVE_CHAR_MODIFICATION</c>.
/// </summary>
/// <remarks>
/// <b>Both declare a character parameter the engine path never pops</b> — the third instance of
/// the defect <c>$COINCOUNT</c> showed, and the reason this port pops every declared argument and
/// then discards the character.
/// </remarks>
public class GpdlModifyAttributeTests
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

    /// <summary>Every argument reaches the host in the right slot.</summary>
    /// <remarks>
    /// Seven are declared and six are used, so a miscount shows up as the attribute arriving as
    /// the amount — which is what pinning each field catches.
    /// </remarks>
    [Fact]
    public void The_arguments_arrive_in_their_own_slots()
    {
        var host = new GpdlUnhostedEnvironment();
        string result = Run(
            """$RETURN $MODIFY_CHAR_ATTRIBUTE("hero", "STR", 2, "MINUTES", 30, "Bulls", "potion");""",
            host);

        var (attribute, amount, minutes, text, source) = Assert.Single(host.Modifications);

        Assert.Equal("STR", attribute);
        Assert.Equal(2, amount);
        Assert.Equal(30, minutes);
        Assert.Equal("Bulls", text);
        Assert.Equal("potion", source);

        // The call yields nothing -- m_pushEmptyString.
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Minutes are the only unit; anything else adds nothing.
    /// </summary>
    /// <remarks>
    /// <b>And the script cannot tell.</b> The reference warns to the debug log and returns false,
    /// but the sub-opcode pushes the same empty string either way — so a design asking for
    /// "ROUNDS" gets silence and no effect.
    /// </remarks>
    [Theory]
    [InlineData("MINUTES", 1)]
    [InlineData("minutes", 1)]
    [InlineData("ROUNDS", 0)]
    [InlineData("DAYS", 0)]
    [InlineData("", 0)]
    public void Only_minutes_are_accepted(string units, int expected)
    {
        var host = new GpdlUnhostedEnvironment();
        string result = Run(
            $"""$RETURN $MODIFY_CHAR_ATTRIBUTE("hero", "STR", 2, "{units}", 30, "t", "s");""",
            host);

        Assert.Equal(expected, host.Modifications.Count);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>Removing finds a change by its source and takes exactly one.</summary>
    [Fact]
    public void Removing_takes_one_match()
    {
        var host = new GpdlUnhostedEnvironment();
        host.ModifyCharacterAttribute("STR", 1, 10, "a", "potion");
        host.ModifyCharacterAttribute("DEX", 1, 10, "b", "potion");

        Assert.Equal("1", Run("""$RETURN $REMOVE_CHAR_MODIFICATION("hero", "potion");""", host));
        Assert.Single(host.Modifications);

        // The second one still goes on a second call.
        Assert.Equal("1", Run("""$RETURN $REMOVE_CHAR_MODIFICATION("hero", "potion");""", host));
        Assert.Empty(host.Modifications);

        // And nothing left to remove.
        Assert.Equal(string.Empty,
                     Run("""$RETURN $REMOVE_CHAR_MODIFICATION("hero", "potion");""", host));
    }

    /// <summary>
    /// The mask matches whole words, and <c>*</c> stands for exactly one of them.
    /// </summary>
    /// <remarks>
    /// <b>This is not a glob.</b> <c>MatchMask</c> walks both strings word by word — so
    /// <c>"fire*"</c> does not match <c>"firestorm"</c>, because that is a single word and the
    /// mask word is not <c>*</c>. A mask that runs out matches whatever is left.
    /// </remarks>
    [Theory]
    [InlineData("potion", "potion", true)]
    [InlineData("potion", "potion of strength", true)]     // the mask runs out and matches
    [InlineData("potion of", "potion of strength", true)]
    [InlineData("*", "potion", true)]                      // one word, any word
    [InlineData("* of", "potion of strength", true)]
    [InlineData("potion *", "potion of strength", true)]
    [InlineData("potion", "elixir", false)]
    [InlineData("potion of", "potion", false)]             // the data runs out first
    [InlineData("fire*", "firestorm", false)]              // NOT a prefix glob
    [InlineData("storm", "firestorm", false)]              // NOT a substring search
    public void The_mask_matches_words(string mask, string data, bool expected) =>
        Assert.Equal(expected, GpdlMask.Matches(mask, data));

    /// <summary>An empty mask matches anything, including nothing.</summary>
    [Fact]
    public void An_empty_mask_matches_anything()
    {
        Assert.True(GpdlMask.Matches("", "potion"));
        Assert.True(GpdlMask.Matches("", ""));
        Assert.True(GpdlMask.Matches("   ", "potion"));
    }

    /// <summary>A mask reaching past the end of the data does not read past it.</summary>
    /// <remarks>
    /// The reference's skip loops test the pointer rather than the character, so a <c>*</c> word
    /// with nothing after it walks off the end. There is nothing there to reproduce.
    /// </remarks>
    [Fact]
    public void A_wildcard_at_the_end_does_not_run_off()
    {
        Assert.True(GpdlMask.Matches("*", ""));
        Assert.True(GpdlMask.Matches("* * *", "one"));
    }
}
