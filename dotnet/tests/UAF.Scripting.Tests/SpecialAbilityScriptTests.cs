using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// Running the GPDL scripts an object's special abilities carry.
/// </summary>
/// <remarks>
/// The mechanism <c>$RUN_CHAR_SCRIPTS</c> and its four siblings are built on
/// (<c>SPECIAL_ABILITIES::RunScripts</c>, <c>Specab.cpp:1876</c>).
/// </remarks>
public class SpecialAbilityScriptTests
{
    /// <summary>A lookup over a handful of abilities, by name and script name.</summary>
    private static Func<string, string, string?> Lookup(
        params (string Ability, string Script, string Body)[] entries) =>
        (ability, script) => entries
            .Where(e => e.Ability == ability && e.Script == script)
            .Select(e => e.Body)
            .FirstOrDefault();

    /// <summary>A body compiles only inside the wrapper.</summary>
    /// <remarks>
    /// <b>The source in the database is a bare body</b> — statements with no function around
    /// them — so the wrapper is not decoration; without it nothing in the file would compile.
    /// </remarks>
    [Fact]
    public void A_bare_body_needs_the_wrapper()
    {
        const string body = """$RETURN "yes";""";

        // On its own it is not a program.
        var bare = new GpdlCompiler();
        Assert.NotEqual(0, bare.Compile(body));

        // Wrapped, it is.
        Assert.NotNull(SpecialAbilityScripts.Compile(body, out string error));
        Assert.Equal(string.Empty, error);

        Assert.Equal("$PUBLIC $FUNC SA(){" + body + "\n} SA ;",
                     SpecialAbilityScripts.Wrap(body));
    }

    /// <summary>
    /// The epilogue's newline is what saves a script ending in a comment.
    /// </summary>
    /// <remarks>
    /// Without it the closing brace would sit on the commented line and be swallowed — so a
    /// perfectly good script would fail to compile because of how it was joined.
    /// </remarks>
    [Fact]
    public void A_script_ending_in_a_comment_still_closes()
    {
        Assert.NotNull(SpecialAbilityScripts.Compile("""$RETURN "x"; // done""", out _));
        Assert.StartsWith("\n}", SpecialAbilityScripts.Epilogue, StringComparison.Ordinal);
    }

    /// <summary>Every ability carrying the named script runs, in the order given.</summary>
    [Fact]
    public void Each_ability_carrying_the_script_runs()
    {
        var host = new GpdlUnhostedEnvironment();

        string result = SpecialAbilityScripts.Run(
            ["first", "second"],
            Lookup(("first", "onHit", """$SET_GLOBAL_ASL("a", "1"); $RETURN "one";"""),
                   ("second", "onHit", """$SET_GLOBAL_ASL("b", "2"); $RETURN "two";""")),
            "onHit",
            host);

        // Both ran -- each left its mark on the shared host.
        Assert.Equal("1", host.GetAsl(GpdlAslScope.Global, "a"));
        Assert.Equal("2", host.GetAsl(GpdlAslScope.Global, "b"));

        // And the answer is the LAST one's, not the two joined.
        Assert.Equal("two", result);
    }

    /// <summary>An ability without the script is skipped rather than failing the run.</summary>
    [Fact]
    public void An_ability_without_the_script_is_skipped()
    {
        var host = new GpdlUnhostedEnvironment();

        string result = SpecialAbilityScripts.Run(
            ["silent", "loud"],
            Lookup(("loud", "onHit", """$RETURN "heard";""")),
            "onHit",
            host);

        Assert.Equal("heard", result);
    }

    /// <summary>Nothing carrying the script yields empty.</summary>
    [Fact]
    public void No_script_anywhere_yields_empty() =>
        Assert.Equal(string.Empty,
                     SpecialAbilityScripts.Run(["a", "b"], Lookup(), "onHit",
                                               new GpdlUnhostedEnvironment()));

    /// <summary>
    /// A script that will not compile is dropped, and the others still run.
    /// </summary>
    /// <remarks>
    /// <b>One broken script does not stop the rest.</b> The reference logs it, marks the entry so
    /// it is never retried, and carries on — a design with a typo in one ability keeps working.
    /// </remarks>
    [Fact]
    public void A_broken_script_does_not_stop_the_others()
    {
        var host = new GpdlUnhostedEnvironment();
        var errors = new List<(string Ability, string Message)>();

        string result = SpecialAbilityScripts.Run(
            ["broken", "fine"],
            Lookup(("broken", "onHit", "this is not GPDL at all ("),
                   ("fine", "onHit", """$RETURN "ok";""")),
            "onHit",
            host,
            onError: (ability, message) => errors.Add((ability, message)));

        Assert.Equal("ok", result);

        var (name, message) = Assert.Single(errors);
        Assert.Equal("broken", name);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    /// <summary>
    /// The examiner can stop the run, and the stopping script's result is the answer.
    /// </summary>
    /// <remarks>
    /// This is the difference between "run them all" and "ask each until one says yes" — the
    /// reference's <c>CBR_STOP</c>.
    /// </remarks>
    [Fact]
    public void The_examiner_can_stop_the_run()
    {
        var host = new GpdlUnhostedEnvironment();

        string result = SpecialAbilityScripts.Run(
            ["first", "second"],
            Lookup(("first", "onHit", """$SET_GLOBAL_ASL("a", "1"); $RETURN "stop here";"""),
                   ("second", "onHit", """$SET_GLOBAL_ASL("b", "2"); $RETURN "never";""")),
            "onHit",
            host,
            examine: _ => SpecialAbilityScriptVerdict.Stop);

        Assert.Equal("stop here", result);

        // The second never ran.
        Assert.Equal("1", host.GetAsl(GpdlAslScope.Global, "a"));
        Assert.Equal(string.Empty, host.GetAsl(GpdlAslScope.Global, "b"));
    }

    /// <summary>
    /// Each script gets its own machine, so one cannot disturb another's stack.
    /// </summary>
    /// <remarks>
    /// The reference pushes and pops a whole interpreter around every execution. A script that
    /// leaves its own stack in a mess must not affect the next one — which a single shared machine
    /// would not guarantee.
    /// </remarks>
    [Fact]
    public void One_scripts_stack_does_not_reach_the_next()
    {
        var host = new GpdlUnhostedEnvironment();

        string result = SpecialAbilityScripts.Run(
            ["messy", "clean"],
            Lookup(("messy", "onHit", """ "1" == "1"; "leftover"; $RETURN "first";"""),
                   ("clean", "onHit", """$RETURN "second";""")),
            "onHit",
            host);

        Assert.Equal("second", result);
    }
}
