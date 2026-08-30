using Xunit.Abstractions;

namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// Which design files the port can write at all.
/// </summary>
/// <remarks>
/// <para>
/// The round trip can only speak for files it has both halves of. Five of a design's databases —
/// <c>ability.dat</c>, <c>baseclass.dat</c>, <c>classes.dat</c>, <c>races.dat</c> and
/// <c>specialAbilities.dat</c> — had a reader and no writer, so a save would necessarily have left
/// them as it found them. <b>That gap is closed</b>, and what is asserted here is the closing: the
/// designs really carry these files, and this harness really has a codec for each.
/// </para>
/// <para>
/// <b>This file used to assert the opposite.</b> It held a case per database that failed the
/// moment a writer appeared — green-to-red on good news, so the person adding the writer was the
/// person who had to come here. That is what happened; the cases are gone and the coverage claim
/// took their place.
/// </para>
/// </remarks>
public class WriterCoverageTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Every one of the five now has a codec, so the round trip really covers it.
    /// </summary>
    /// <remarks>
    /// The check is through <see cref="DesignFiles.CodecFor"/> rather than against a list of type
    /// names, because a writer nothing dispatches to is a writer the editor cannot use — which is
    /// indistinguishable, from a design's point of view, from not having one.
    /// </remarks>
    [Fact]
    public void The_late_databases_all_have_codecs()
    {
        foreach (var design in DesignCorpus.Present())
        {
            var version = DesignFiles.GlobalVersion(design);

            foreach (string file in DesignCorpus.LateDatabases)
            {
                string path = Path.Combine(design.DataDirectory, file);
                if (!File.Exists(path))
                {
                    continue;
                }

                Assert.NotNull(DesignFiles.CodecFor(path, version));
            }
        }

        // And with no design on this machine at all, the codecs are still reachable by name --
        // a claim that needs no corpus, so a bare checkout proves this much.
        foreach (string file in DesignCorpus.LateDatabases)
        {
            Assert.NotNull(DesignFiles.CodecFor(file, new UAF.Common.DesignVersion(1.0)));
        }
    }

    /// <summary>
    /// The five are really in the designs on disk, so the coverage is not theoretical.
    /// </summary>
    /// <remarks>
    /// A codec for a file no design carries would be a curiosity. Every one of these is in every
    /// design in the corpus bar one.
    /// </remarks>
    [Fact]
    public void The_late_databases_are_in_the_designs()
    {
        // specialAbilities.dat is carried only by the gitignored reference designs — the one
        // committed design ships specialAbilities.txt alone — so this claim needs the corpus and
        // must return early without it, the same rule every other corpus test follows. A bare CI
        // checkout would otherwise fail on a design file nothing present carries.
        if (!DesignCorpus.Present().Any(d => !d.IsTracked))
        {
            return;
        }

        foreach (var design in DesignCorpus.Present())
        {
            var missing = DesignCorpus.LateDatabases
                .Where(f => !File.Exists(Path.Combine(design.DataDirectory, f)))
                .ToList();

            _output.WriteLine(
                $"{design.Name}: carries "
                + $"{DesignCorpus.LateDatabases.Count - missing.Count} of "
                + $"{DesignCorpus.LateDatabases.Count} late databases"
                + (missing.Count == 0 ? string.Empty : $" (absent: {string.Join(", ", missing)})"));
        }

        // SomethingWild and Case carry all five; DefaultDesign ships specialAbilities as .txt
        // only, so the claim worth asserting is that at least one design has each of them.
        foreach (string file in DesignCorpus.LateDatabases)
        {
            Assert.Contains(DesignCorpus.Present(),
                            d => File.Exists(Path.Combine(d.DataDirectory, file)));
        }
    }
}
