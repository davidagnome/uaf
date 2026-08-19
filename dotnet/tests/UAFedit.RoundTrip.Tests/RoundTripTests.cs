using System.Text;
using Xunit.Abstractions;

namespace UAFedit.RoundTrip.Tests;

/// <summary>
/// What happens when the port reads a shipped design and writes it straight back.
/// </summary>
/// <remarks>
/// <para>
/// Phase 5's exit criterion is that the editor can open a design, edit it, and save files both
/// UAFcore and the original <c>UAFWinEd.exe</c> load. Everything else in the editor is built on
/// that, so it is worth establishing before any of it is: this reads each design through the
/// existing readers, writes it back through the existing writers, and says exactly what changed.
/// </para>
/// <para>
/// <b>Byte identity is not the gate, and expecting it would be a misreading of the format.</b>
/// Every writer stamps its own <c>WrittenVersion</c> — 5.24 for the record types, 5.26 for
/// <c>GLOBAL_STATS</c> — because the payload always goes out in the modern shape and a header
/// claiming otherwise is unreadable. The reference does the same, stamping <c>ENGINE_VER</c>
/// rather than whatever it loaded (<c>CharacterFileWriter</c>'s remarks set this out at length).
/// So a design shipped at 2.53 or 3.55 is <i>upgraded</i> by being saved, and its files cannot
/// come back byte-for-byte by construction. What can be demanded, and is demanded here:
/// </para>
/// <list type="number">
/// <item>the save loses nothing it read — only the version stamp changes value, and what else
/// moves is the upgrade adding fields the older format did not carry
/// (<see cref="Saving_a_design_loses_nothing_it_read"/>);</item>
/// <item>the written file reads back, and writing it again produces identical bytes, so a second
/// save does not churn (<see cref="Saving_the_same_design_twice_produces_the_same_bytes"/>);</item>
/// <item>the second read matches the first field for field, so nothing decays across saves
/// (<see cref="Nothing_is_lost_between_the_first_save_and_the_second"/>).</item>
/// </list>
/// </remarks>
public class RoundTripTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// The premise: designs are on disk and their files have bytes in them.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that stops the file passing while proving nothing.</b> Two of the three
    /// designs live under the gitignored <c>reference/</c>, so every case over them has to return
    /// early when they are absent — and a suite of nothing but early returns is a green tick over
    /// an empty room. <c>DefaultDesign</c> is committed, so this assertion always has something to
    /// bite on, and the report below always names what actually ran.
    /// </remarks>
    [Fact]
    public void The_corpus_holds_designs_with_files_worth_round_tripping()
    {
        Assert.NotNull(DesignCorpus.RepoRoot());

        var designs = DesignCorpus.Present();
        Assert.NotEmpty(designs);

        // The committed one is not optional: it is what makes this suite non-vacuous on a
        // checkout that has no reference/ folder.
        Assert.Contains(designs, d => d.IsTracked);

        foreach (var design in designs)
        {
            var files = DesignCorpus.Files(design);
            Assert.NotEmpty(files);

            foreach (string file in files)
            {
                Assert.True(new FileInfo(file).Length > 0, $"{design.Name}: {file} is empty");
            }

            // A design without these is not a design, and a discovery bug that found the wrong
            // folder would show up here rather than as a sweep with nothing in it.
            Assert.Contains(files, f => Path.GetFileName(f) == "game.dat");
            Assert.Contains(files, f => f.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase));
        }

        _output.WriteLine($"Designs present: {string.Join(", ", designs.Select(d => d.Name))}");
        if (designs.Count < 3)
        {
            _output.WriteLine(
                "NOT PROOF OF A FULL PASS: the designs under reference/ are absent from this " +
                "checkout, so every case over them returned early.");
        }
    }

    /// <summary>
    /// The sweep: every file in a design, read and written straight back, and what came out.
    /// </summary>
    /// <remarks>
    /// The report is the deliverable — which files come back byte-identical, which do not, where
    /// the first difference is, and which field it corresponds to. The assertion is narrow on
    /// purpose. A difference is a finding to be read; a reader that cannot open a shipped file, a
    /// writer that fails on something it said it could write, or a reader that cannot open its
    /// own writer's output is a defect.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DesignNames))]
    public void Reading_a_design_and_writing_it_back(string designName)
    {
        if (DesignCorpus.Find(designName) is not { } design)
        {
            return;
        }

        var report = new StringBuilder();
        var defects = new List<string>();

        foreach (var outcome in Sweep(design, report))
        {
            if (outcome.Defect is { } defect)
            {
                defects.Add($"{design.Name}/{outcome.Name}: {defect}");
            }
        }

        _output.WriteLine(report.ToString());

        Assert.True(defects.Count == 0,
                    report + Environment.NewLine + "DEFECTS" + Environment.NewLine
                    + string.Join(Environment.NewLine, defects));
    }

    /// <summary>
    /// A save loses nothing: everything the reader found comes back with the same value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the strongest claim byte comparison against the original can be turned into.</b>
    /// The version stamp has to move — the payload goes out in the modern shape and a header
    /// saying otherwise is unreadable — so the question is not whether the file changed but
    /// whether anything was <i>lost</i>. Comparing the decoded models answers that directly, and
    /// answers it exhaustively: <see cref="StructuralDiff"/> collects every differing field rather
    /// than stopping at the first, because stopping at the first would report the version for
    /// every file and check nothing after it.
    /// </para>
    /// <para>
    /// <b>An upgrade adds as well as restamps, and adding is not losing.</b> A design at 2.53 has
    /// no <c>creditsData</c> — that field arrived at 5.25 — and no wall overrides or cell contents
    /// on its level table, so those come back as empty structures where the read found nothing. A
    /// fixed table can fill out the same way: <c>SomethingWild</c>'s <c>Level002</c> holds a tale
    /// list of 20 that goes out at the modern 255, with the first 20 unchanged. Those are
    /// <see cref="DifferenceKind.Materialised"/> and <see cref="DifferenceKind.Grown"/>, counted
    /// and reported. What this test refuses is the other direction — a value that changed, a
    /// structure that became null, a list that came back shorter.
    /// </para>
    /// <para>
    /// A design the writers refuse outright is not exercised here — <c>DefaultDesign</c> at
    /// 0.915025 is refused on all four of its writable files, each with a reason, and the sweep
    /// above is where that is recorded.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DesignNames))]
    public void Saving_a_design_loses_nothing_it_read(string designName)
    {
        if (DesignCorpus.Find(designName) is not { } design)
        {
            return;
        }

        var globalVersion = DesignFiles.GlobalVersion(design);
        var lost = new List<string>();
        int compared = 0;
        int materialised = 0;
        int grown = 0;

        foreach (string path in DesignCorpus.Files(design))
        {
            if (DesignFiles.CodecFor(path, globalVersion) is not { } codec)
            {
                continue;
            }

            object before;
            byte[] written;
            try
            {
                before = DesignFiles.ReadFile(codec, path);
                written = codec.Write(before);
            }
            catch (Exception e) when (e is NotSupportedException or EndOfStreamException
                                    or InvalidDataException)
            {
                continue;                        // refused or unreadable; the sweep reports it
            }

            compared++;

            var differences = StructuralDiff.All(before, DesignFiles.ReadBytes(codec, written),
                                                 $"{design.Name}/{codec.Name}");

            materialised += differences.Count(d => d.Kind == DifferenceKind.Materialised);
            grown += differences.Count(d => d.Kind == DifferenceKind.Grown);
            lost.AddRange(StructuralDiff.Losses(differences, versionStampIsExpected: true)
                                        .Select(d => d.ToString()));
        }

        _output.WriteLine($"{design.Name}: {compared} files compared field by field; the upgrade " +
                          $"materialised {materialised} fields and grew {grown} fixed tables");

        Assert.True(lost.Count == 0,
                    "a save lost or altered something it had read:"
                    + Environment.NewLine + string.Join(Environment.NewLine, lost));
    }

    /// <summary>
    /// Saving twice produces the same bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the property the editor actually needs, and the one byte identity against the
    /// original cannot give.</b> The first save upgrades; every save after it must be a fixpoint.
    /// A writer that emitted something its own reader decoded differently — a string interned
    /// under one index and re-read under another, a count written before the list it counts was
    /// finalised — would produce a design that drifted a little on every save, and nothing about
    /// the first save alone would show it.
    /// </para>
    /// <para>
    /// It is also the only check here that exercises the reader against the port's own output
    /// rather than against the reference's. A field the writer emits in the wrong order is
    /// invisible to a read-only test and invisible to a write-only test, and lands here.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DesignNames))]
    public void Saving_the_same_design_twice_produces_the_same_bytes(string designName)
    {
        if (DesignCorpus.Find(designName) is not { } design)
        {
            return;
        }

        var globalVersion = DesignFiles.GlobalVersion(design);
        int reached = 0;

        foreach (string path in DesignCorpus.Files(design))
        {
            if (DesignFiles.CodecFor(path, globalVersion) is not { } codec)
            {
                continue;
            }

            byte[] first;
            try
            {
                first = codec.Write(DesignFiles.ReadFile(codec, path));
            }
            catch (Exception e) when (e is NotSupportedException or EndOfStreamException
                                    or InvalidDataException)
            {
                continue;
            }

            byte[] second = codec.Write(DesignFiles.ReadBytes(codec, first));
            reached++;

            Assert.True(
                ByteDiff.FirstDifference(first, second) is null,
                $"{design.Name}/{codec.Name}: the second save differs from the first -- "
                + ByteDiff.Describe(first, second));
        }

        _output.WriteLine($"{design.Name}: {reached} files reached the fixpoint");
    }

    /// <summary>
    /// Nothing decays across saves: the second read matches the first, field for field.
    /// </summary>
    /// <remarks>
    /// The byte fixpoint above proves the two saves agree; this proves they agree about something
    /// meaningful rather than being identically wrong in a way the reader collapses. Both are
    /// needed — a writer that dropped a list entirely would satisfy the byte fixpoint on its
    /// second pass and fail here on its first.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DesignNames))]
    public void Nothing_is_lost_between_the_first_save_and_the_second(string designName)
    {
        if (DesignCorpus.Find(designName) is not { } design)
        {
            return;
        }

        var globalVersion = DesignFiles.GlobalVersion(design);

        foreach (string path in DesignCorpus.Files(design))
        {
            if (DesignFiles.CodecFor(path, globalVersion) is not { } codec)
            {
                continue;
            }

            byte[] first;
            try
            {
                first = codec.Write(DesignFiles.ReadFile(codec, path));
            }
            catch (Exception e) when (e is NotSupportedException or EndOfStreamException
                                    or InvalidDataException)
            {
                continue;
            }

            object once = DesignFiles.ReadBytes(codec, first);
            object twice = DesignFiles.ReadBytes(codec, codec.Write(once));

            Assert.Empty(StructuralDiff.All(once, twice, $"{design.Name}/{codec.Name}"));
        }
    }

    /// <summary>One file's trip out and back.</summary>
    /// <param name="Defect">Set only when something failed that should not have.</param>
    private sealed record Outcome(string Name, string? Defect);

    /// <summary>
    /// Reads and rewrites every file in a design, appending a line per file to
    /// <paramref name="report"/>.
    /// </summary>
    private static List<Outcome> Sweep(CorpusDesign design, StringBuilder report)
    {
        report.AppendLine($"=== {design.Name} ({design.Root}) ===");

        var globalVersion = DesignFiles.GlobalVersion(design);
        report.AppendLine($"design version: {globalVersion.Value}");

        var outcomes = new List<Outcome>();

        foreach (string path in DesignCorpus.Files(design))
        {
            string name = Path.GetFileName(path);

            if (DesignFiles.CodecFor(path, globalVersion) is not { } codec)
            {
                continue;
            }

            object model;
            try
            {
                model = DesignFiles.ReadFile(codec, path);
            }
            catch (InvalidDataException e)
            {
                // The readers refuse a record shape nobody ported, in the same way and for the
                // same reason the writers refuse one they cannot reproduce -- with the shape
                // named. DefaultDesign's Bcd1 baseclasses, CL1 classes and RaceV1 races are all
                // this. Recorded, not failed: what would be a defect is a reader that opened one
                // of them and produced something wrong.
                report.AppendLine($"  {name,-24} REFUSED    {e.Message}");
                outcomes.Add(new Outcome(name, null));
                continue;
            }
            catch (Exception e)
            {
                report.AppendLine($"  {name,-24} UNREADABLE {e.GetType().Name}: {e.Message}");
                report.AppendLine($"  {"",-24}            {Innermost(e)}");
                outcomes.Add(new Outcome(
                    name, $"the reader threw {e.GetType().Name}: {e.Message} (at {Innermost(e)})"));
                continue;
            }

            byte[] written;
            try
            {
                written = codec.Write(model);
            }
            catch (NotSupportedException e)
            {
                // A refusal is a legitimate answer: the writers decline shapes they cannot
                // reproduce rather than guessing. It is recorded, not failed.
                report.AppendLine($"  {name,-24} REFUSED    {e.Message}");
                outcomes.Add(new Outcome(name, null));
                continue;
            }

            byte[] original = File.ReadAllBytes(path);
            if (ByteDiff.FirstDifference(original, written) is null)
            {
                report.AppendLine($"  {name,-24} IDENTICAL  {original.Length} bytes");
                outcomes.Add(new Outcome(name, null));
                continue;
            }

            report.AppendLine($"  {name,-24} CHANGED    {ByteDiff.Describe(original, written)}");

            // Past the prologue a database is LZW, so the file offset above is where the two
            // dictionaries first diverged rather than where the content did. Decompressing both
            // gives an offset into the record stream, which is somewhere a field can be found.
            if (ByteDiff.Decompressed(original) is { } beforePayload
                && ByteDiff.Decompressed(written) is { } afterPayload)
            {
                report.AppendLine(
                    $"  {"",-24} payload:   {ByteDiff.Describe(beforePayload, afterPayload)}");
            }

            object reread;
            try
            {
                reread = DesignFiles.ReadBytes(codec, written);
            }
            catch (Exception e)
            {
                report.AppendLine($"  {"",-24} UNREADABLE OUTPUT {e.GetType().Name}: {e.Message}");
                outcomes.Add(new Outcome(
                    name,
                    $"the written file could not be read back: {e.GetType().Name}: {e.Message}"));
                continue;
            }

            var differences = StructuralDiff.All(model, reread, name);
            report.AppendLine(
                $"  {"",-24} fields:    "
                + (differences.Count == 0
                    ? "none -- only the version stamp moved"
                    : string.Join($"{Environment.NewLine}  {"",-24}            ",
                                  Summarise(differences))));

            outcomes.Add(new Outcome(name, null));
        }

        return outcomes;
    }

    /// <summary>
    /// Field differences for the report, with the repetitive ones rolled up.
    /// </summary>
    /// <remarks>
    /// A level table materialises the same two fields on every one of its entries, and printing
    /// all twenty of them buries the one line that is not like the others. Losses are never rolled
    /// up.
    /// </remarks>
    private static IEnumerable<string> Summarise(IReadOnlyList<Difference> differences)
    {
        foreach (var loss in StructuralDiff.Losses(differences, versionStampIsExpected: false))
        {
            yield return loss.ToString();
        }

        var additions = differences
            .Where(d => d.Kind is DifferenceKind.Materialised or DifferenceKind.Grown)
            .GroupBy(d => (Leaf(d.Path), d.Kind, d.Before, d.After));

        foreach (var group in additions)
        {
            var (leaf, kind, before, after) = group.Key;
            yield return group.Count() == 1
                ? group.First().ToString()
                : $"{leaf} x{group.Count()}: {before} -> {after} [{kind}]";
        }
    }

    /// <summary>The path with its list indices stripped, so repeats collapse onto one another.</summary>
    private static string Leaf(string path) =>
        System.Text.RegularExpressions.Regex.Replace(path, @"\[\d+\]", "[*]");

    /// <summary>
    /// The frames of a reader failure that name a serialization method, innermost first.
    /// </summary>
    /// <remarks>
    /// A message like "expected 4 bytes at offset 4343, got 0" says the read overran the file and
    /// nothing about which field it overran on. The call chain does, and it is the difference
    /// between a finding and a shrug.
    /// </remarks>
    private static string Innermost(Exception e) =>
        string.Join(" <- ",
                    (e.StackTrace ?? string.Empty)
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Contains("UAF.Serialization.", StringComparison.Ordinal))
                    .Take(4)
                    .Select(line => line.Replace("at UAF.Serialization.", string.Empty,
                                                 StringComparison.Ordinal)));

    public static TheoryData<string> DesignNames => DesignCorpus.Names;
}
