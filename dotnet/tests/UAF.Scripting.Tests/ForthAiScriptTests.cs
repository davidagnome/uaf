namespace UAF.Scripting.Tests;

/// <summary>
/// The shipped <c>AI_Script.BLK</c> compiled and run by the ported VM.
/// </summary>
/// <remarks>
/// These assert against the real script rather than a fixture, which is the only way round the
/// trap this port has hit before: a fixture written from the same reading as the code cannot
/// discover a convention, only pin one.
/// </remarks>
public class ForthAiScriptTests
{
    /// <summary>The corpus, found by walking up to the repository root.</summary>
    private static string Corpus
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, "reference");
        }
    }

    /// <summary>The two shipped versions, by the design that carries each.</summary>
    public static TheoryData<string> Designs => new()
    {
        Path.Combine("dc-default", "data-files"),   // 1.01, 15/10/2014
        Path.Combine("SomethingWild.dsn", "Data"),  // 1.01
        Path.Combine("Case.dsn", "Data"),           // 0.999785, 28/08/2014
        Path.Combine("Ambassador's_Letter", "Data"),
    };

    /// <summary>
    /// One design's script, or null when the corpus is not present.
    /// </summary>
    /// <remarks>
    /// <b><c>reference/</c> is gitignored, so none of the corpus reaches CI</b> — only
    /// <c>dc-default/data-files</c>, which <c>dotnet.yml</c> fetches as the tier-3 fixture. These
    /// tests therefore have to degrade the way every other corpus test here does, rather than
    /// assert the files exist: the first revision of this file asserted, and turned the .NET
    /// workflow red on a checkout that was perfectly correct.
    /// </remarks>
    private static string? ScriptText(string design)
    {
        string path = Path.Combine(Corpus, design, "AI_Script.BLK");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// A machine with the kernel built and one design's script loaded, or null with no corpus.
    /// </summary>
    private static ForthMachine? Loaded(string design)
    {
        if (ScriptText(design) is not { } text)
        {
            return null;
        }

        var forth = new ForthMachine();

        Assert.True(forth.Bootstrap(),
                    "kernel did not build: " + string.Join("; ", forth.Output));
        Assert.True(forth.LoadScript(text),
                    "script did not load: " + string.Join("; ", forth.Output));

        return forth;
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_shipped_script_compiles(string design)
    {
        if (Loaded(design) is not { } forth)
        {
            return;
        }

        // THINK is what RunThink looks up; the rest are the words it is built from, and their
        // presence means the whole file compiled rather than just its first lines.
        foreach (string word in new[]
                 {
                     "THINK", "FGDP?", "TooFar?", "TooNear?", "NotAdjacent?", "Friendly?",
                     "SpellCasterFilter", "AdvanceFilter", "ReadyBestShield",
                 })
        {
            Assert.True(forth.Lookup(word) != 0, $"{design} did not define '{word}'");
        }
    }

    /// <summary>
    /// The one line the two shipped versions differ by.
    /// </summary>
    /// <remarks>
    /// 1.01 adds <c>Dying?</c> to <c>FGDP?</c> and to <c>AdvanceFilter</c>; 0.999785 has neither,
    /// so its monsters keep attacking a combatant who is bleeding out. This is what
    /// <c>MonsterAiScript.AttacksTheDying</c> selects between, and it is worth pinning here because
    /// the transcription's default depends on which version is taken as current.
    /// </remarks>
    [Theory]
    [InlineData("dc-default/data-files", true)]
    [InlineData("SomethingWild.dsn/Data", true)]
    [InlineData("Case.dsn/Data", false)]
    [InlineData("Ambassador's_Letter/Data", false)]
    public void Only_the_newer_script_knows_about_dying(string design, bool expected)
    {
        if (Loaded(design.Replace('/', Path.DirectorySeparatorChar)) is not { } forth)
        {
            return;
        }

        Assert.Equal(expected, forth.Lookup("Dying?") != 0);
    }

    /// <summary>
    /// The kernel builds and a script loads on top of it with no corpus at all.
    /// </summary>
    /// <remarks>
    /// Everything above returns early without the corpus, which on CI is everything. This keeps the
    /// load path covered there: a synthetic script cannot discover what the real ones do, but it can
    /// prove <see cref="ForthMachine.LoadScript"/> still compiles a multi-line definition with
    /// comments — which is what the double-escaped <c>\</c> broke, invisibly, for months.
    /// </remarks>
    [Fact]
    public void A_script_loads_on_top_of_the_kernel_without_the_corpus()
    {
        var forth = new ForthMachine();
        Assert.True(forth.Bootstrap());

        // Deliberately arithmetic-free. The shape that matters is a comment line, a helper, and a
        // THINK that calls it -- which is the shipped script's shape -- not the sums.
        Assert.True(forth.LoadScript("\\ a comment line, as every shipped script opens with\r\n"
                                     + ": Answer 42 ; 1 SP+-\r\n"
                                     + ": THINK Answer ; 1 SP+-\r\n"));

        Assert.NotEqual(0, forth.Lookup("Answer"));
        Assert.NotEqual(0, forth.Lookup("THINK"));
        Assert.Equal(42, forth.RunThink(new ForthCombatSummary()));
    }
}
