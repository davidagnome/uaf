using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// Covers the attribute sub-opcodes — the first family of game-state calls the VM can serve.
/// </summary>
/// <remarks>
/// Driven through real GPDL source rather than by poking the interpreter, so the argument order the
/// compiler emits is under test alongside the sub-opcodes themselves.
/// </remarks>
public class GpdlAttributeTests
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

    private static GpdlUnhostedEnvironment Host() => new();

    // ---- reading and writing -------------------------------------------------------------------

    [Fact]
    public void Setting_a_global_attribute_yields_the_value_that_was_set()
    {
        var host = Host();

        Assert.Equal("Win",
            Run("""$RETURN $SET_GLOBAL_ASL("Combat Result", "Win");""", host));
        Assert.Equal("Win", host.Attributes[GpdlAslScope.Global]["Combat Result"]);
    }

    [Fact]
    public void The_key_is_the_first_argument_and_the_value_the_second()
    {
        // GPDL pushes arguments left to right, so the value ends up on top and is popped first.
        // Reading the pops in source order stores the key under the value.
        var host = Host();
        Run("""$SET_GLOBAL_ASL("key", "value");""", host);

        Assert.Equal("value", host.Attributes[GpdlAslScope.Global]["key"]);
        Assert.False(host.Attributes[GpdlAslScope.Global].ContainsKey("value"));
    }

    [Fact]
    public void Reading_an_attribute_that_was_set_gives_it_back()
    {
        var host = Host();

        Assert.Equal("two", Run("""
            $SET_GLOBAL_ASL("chapter", "two");
            $RETURN $GET_GLOBAL_ASL("chapter");
            """, host));
    }

    [Fact]
    public void Reading_an_absent_attribute_gives_the_empty_string()
    {
        // Lookup returns a shared empty string rather than signalling, so a script cannot tell an
        // unset attribute from one set to nothing by reading it.
        Assert.Equal("", Run("""$RETURN $GET_GLOBAL_ASL("never set");""", Host()));
    }

    [Fact]
    public void The_party_store_is_separate_from_the_global_one()
    {
        var host = Host();

        Assert.Equal("party", Run("""
            $SET_GLOBAL_ASL("k", "global");
            $SET_PARTY_ASL("k", "party");
            $RETURN $GET_PARTY_ASL("k");
            """, host));

        Assert.Equal("global", host.Attributes[GpdlAslScope.Global]["k"]);
    }

    // ---- testing and removing ------------------------------------------------------------------

    [Fact]
    public void Existence_is_reported_as_the_vms_own_true_and_false()
    {
        var host = Host();

        Assert.Equal("", Run("""$RETURN $IF_PARTY_ASL("absent");""", host));
        Assert.Equal("1", Run("""
            $SET_PARTY_ASL("present", "x");
            $RETURN $IF_PARTY_ASL("present");
            """, host));
    }

    [Fact]
    public void An_attribute_set_to_nothing_still_exists()
    {
        // The other half of the empty-string trap: reading cannot tell them apart, but asking
        // whether the key exists can.
        var host = Host();

        Assert.Equal("1", Run("""
            $SET_PARTY_ASL("blank", "");
            $RETURN $IF_PARTY_ASL("blank");
            """, host));

        Assert.Equal("", Run("""$RETURN $GET_PARTY_ASL("blank");""", host));
    }

    [Fact]
    public void Deleting_removes_the_attribute()
    {
        var host = Host();

        Run("""
            $SET_PARTY_ASL("gone", "x");
            $DELETE_PARTY_ASL("gone");
            """, host);

        Assert.False(host.Attributes[GpdlAslScope.Party].ContainsKey("gone"));
    }

    [Fact]
    public void Deleting_always_reports_false_even_when_it_removed_something()
    {
        // The push exists to balance the stack -- the reference's own comment is "Must supply a
        // result" -- so a script testing the result of a delete learns nothing from it.
        var host = Host();

        Assert.Equal("", Run("""
            $SET_PARTY_ASL("gone", "x");
            $RETURN $DELETE_PARTY_ASL("gone");
            """, host));

        Assert.Equal("", Run("""$RETURN $DELETE_PARTY_ASL("never there");""", host));
    }
}
