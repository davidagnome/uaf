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


    /// <summary>
    /// A script can read what it is running for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The context frame is what makes <c>$CharacterContext</c> and its siblings work.</b>
    /// <c>RunScripts</c> records the ability and what the script is for before executing and
    /// clears it after, so a script reading a context outside one gets the reference's
    /// "called when no ... context exists" rather than a stale answer.
    /// </para>
    /// <para>
    /// <b><c>$CharacterContext</c> returns an <c>ACTOR</c>, not a string</b> — its table row is the
    /// only one of the five that does — so <c>$RETURN $CharacterContext();</c> does not compile.
    /// It has to be used where an actor is wanted, which is why this reads it through a call that
    /// takes one.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_script_reads_the_context_it_runs_for()
    {
        var host = new ActorWatchingHost();
        var errors = new List<string>();

        SpecialAbilityScripts.Run(
            ["ability"],
            Lookup(("ability", "onHit", "$RETURN $GET_ISMAMMAL($CharacterContext());")),
            "onHit",
            host,
            onError: (_, message) => errors.Add(message),
            contexts: new Dictionary<GpdlContext, string> { [GpdlContext.Character] = "hero" });

        Assert.Empty(errors);

        // The context reached the call as the actor it names.
        Assert.Equal(["hero"], host.Asked);
    }

    /// <summary>A host that records which actor it was asked about.</summary>
    private sealed class ActorWatchingHost : GpdlUnhostedEnvironment
    {
        public List<string> Asked { get; } = [];

        public override string GetCharStat(string actor, GpdlCharStat stat)
        {
            Asked.Add(actor);
            return base.GetCharStat(actor, stat);
        }
    }

    /// <summary>Each of the four string-valued contexts reaches its own call.</summary>
    /// <remarks>
    /// <c>$CharacterContext</c> is absent here because it alone returns an <c>ACTOR</c> — see
    /// <see cref="A_script_reads_the_context_it_runs_for"/>.
    /// </remarks>
    [Theory]
    [InlineData("$ItemContext", GpdlContext.Item, "sword")]
    [InlineData("$SpellContext", GpdlContext.Spell, "bless")]
    [InlineData("$ClassContext", GpdlContext.Class, "Fighter")]
    [InlineData("$RaceContext", GpdlContext.Race, "Elf")]
    public void Each_context_call_reads_its_own(string call, GpdlContext which, string value)
    {
        var host = new GpdlUnhostedEnvironment();
        var errors = new List<string>();

        string result = SpecialAbilityScripts.Run(
            ["ability"],
            Lookup(("ability", "onHit", $"$RETURN {call}();")),
            "onHit",
            host,
            onError: (_, message) => errors.Add(message),
            contexts: new Dictionary<GpdlContext, string> { [which] = value });

        Assert.Empty(errors);
        Assert.Equal(value, result);
    }

    /// <summary>
    /// The frame is torn down, so a context does not leak into the next script.
    /// </summary>
    /// <remarks>
    /// The reference clears the ability after each execution. A context surviving into an
    /// unrelated script would answer confidently and wrongly, which is worse than answering
    /// nothing.
    /// </remarks>
    [Fact]
    public void A_context_does_not_leak_to_the_next_run()
    {
        var host = new GpdlUnhostedEnvironment();
        var lookup = Lookup(("ability", "onHit", "$RETURN $SpellContext();"));

        Assert.Equal("bless", SpecialAbilityScripts.Run(
            ["ability"], lookup, "onHit", host,
            contexts: new Dictionary<GpdlContext, string> { [GpdlContext.Spell] = "bless" }));

        // A second run with no context supplied reads nothing rather than "bless".
        Assert.Equal(string.Empty,
                     SpecialAbilityScripts.Run(["ability"], lookup, "onHit", host));

        // And the host was told the context was missing.
        Assert.Contains(host.Context.Missing,
                        m => m.Contains("$SpellContext", StringComparison.Ordinal));
    }

    /// <summary>The ability that is running names itself.</summary>
    [Fact]
    public void The_running_ability_names_itself()
    {
        var host = new GpdlUnhostedEnvironment();

        SpecialAbilityScripts.Run(
            ["Bless"],
            Lookup(("Bless", "onHit", """$RETURN "ran";""")),
            "onHit",
            host);

        // The frame is gone by now, so what is checked is that the run completed rather than
        // the name surviving it.
        Assert.Empty(host.Context.Missing);
    }
}
