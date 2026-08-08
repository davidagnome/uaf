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

    private static string ScriptText(string design)
    {
        string path = Path.Combine(Corpus, design, "AI_Script.BLK");
        Assert.True(File.Exists(path), $"the corpus is missing {path}");
        return File.ReadAllText(path);
    }

    /// <summary>A machine with the kernel built and one design's script loaded on top.</summary>
    private static ForthMachine Loaded(string design)
    {
        var forth = new ForthMachine();

        Assert.True(forth.Bootstrap(),
                    "kernel did not build: " + string.Join("; ", forth.Output));
        Assert.True(forth.LoadScript(ScriptText(design)),
                    "script did not load: " + string.Join("; ", forth.Output));

        return forth;
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Every_shipped_script_compiles(string design)
    {
        var forth = Loaded(design);

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
        var forth = Loaded(design.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(expected, forth.Lookup("Dying?") != 0);
    }
}
