using UAF.Serialization;
using UAFcore;
using UAFedit.CrossReference;

namespace UAFedit.Tests;

/// <summary>
/// The cross-reference sweep: what a design names, what it ships, and how those differ.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sweep is reflective, so what needs testing is its reach.</b> A hand-written walker that
/// missed a record type would be obvious in the code; this one would simply report a smaller
/// number and look fine. The cases below therefore pin the corners — a monster's icon, a spell's
/// art list, a level's zone art, the global sound queue — because each is a different shape of
/// path through the graph, and between them they prove the walk descends into records, into lists,
/// and into records inside lists.
/// </para>
/// <para>
/// Returns early without the corpus, as everything touching <c>reference/</c> must.
/// </para>
/// </remarks>
public class CrossReferenceTests
{
    private static string? Corpus(string design)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null ? null : Path.Combine(dir.FullName, "reference", design);
        return root is not null && Directory.Exists(root) ? root : null;
    }

    private static (LoadedDesign Design, CrossReferenceReport Report)? Swept(
        string design = "SomethingWild.dsn")
    {
        if (Corpus(design) is not { } root)
        {
            return null;
        }

        var opened = LoadedDesign.Open(root, role: ArchiveRole.Editor);
        return (opened, CrossReferenceBuilder.Build(opened));
    }

    /// <summary>
    /// The premise: a real design sweeps, and finds resources of both kinds in real numbers.
    /// </summary>
    /// <remarks>
    /// The counts are floors rather than exact figures — an exact one would fail the first time
    /// the walk legitimately reached further. What they rule out is a sweep that quietly found
    /// almost nothing, which is the failure everything else here would be blind to.
    /// </remarks>
    [Fact]
    public void A_real_design_yields_resources_of_both_kinds()
    {
        if (Swept() is not { } swept)
        {
            return;
        }

        using (swept.Design)
        {
            Assert.True(swept.Report.ResourcesPresent);
            Assert.True(swept.Report.Entries.Count > 400, $"only {swept.Report.Entries.Count}");

            Assert.Contains(swept.Report.Entries, e => e.Kind == ResourceKind.Art);
            Assert.Contains(swept.Report.Entries, e => e.Kind == ResourceKind.Sound);

            // And most of what it found is genuinely referenced, not just a directory listing.
            Assert.True(swept.Report.Entries.Count(e => e.References.Count > 0) > 200);
        }
    }

    /// <summary>
    /// The walk reaches every kind of owner, not just the shallow ones.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that would catch a reflective walk that stopped early.</b> Each of
    /// these is a different depth and shape: the globals are a record, a monster's icon is a
    /// record inside a record, a spell's art is a record inside a list, and a level's zone art is
    /// a string inside a record inside a list inside a record.
    /// </remarks>
    [Fact]
    public void The_walk_reaches_globals_monsters_spells_and_levels()
    {
        if (Swept() is not { } swept)
        {
            return;
        }

        using (swept.Design)
        {
            var owners = swept.Report.Entries
                .SelectMany(e => e.References)
                .Select(r => r.Owner)
                .ToList();

            Assert.Contains(owners, o => o == "Design globals");
            Assert.Contains(owners, o => o.StartsWith("Monster ", StringComparison.Ordinal));
            Assert.Contains(owners, o => o.StartsWith("Spell ", StringComparison.Ordinal));
            Assert.Contains(owners, o => o.StartsWith("Item ", StringComparison.Ordinal));
            Assert.Contains(owners, o => o.StartsWith("Level", StringComparison.Ordinal));
        }
    }

    /// <summary>Every reference names where inside its owner it sits.</summary>
    /// <remarks>
    /// Without the path, a report saying "this monster names a file that is not there" leaves the
    /// user to find which of its fields did it.
    /// </remarks>
    [Fact]
    public void Every_reference_carries_a_path()
    {
        if (Swept() is not { } swept)
        {
            return;
        }

        using (swept.Design)
        {
            Assert.All(swept.Report.Entries.SelectMany(e => e.References), r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Owner));
                Assert.False(string.IsNullOrWhiteSpace(r.Path));
            });
        }
    }

    /// <summary>Nothing is both missing and unreferenced, and both agree with the entry.</summary>
    [Fact]
    public void The_two_questions_do_not_overlap()
    {
        if (Swept() is not { } swept)
        {
            return;
        }

        using (swept.Design)
        {
            Assert.Empty(swept.Report.Missing.Intersect(swept.Report.Unreferenced));

            Assert.All(swept.Report.Missing, e =>
            {
                Assert.False(e.Exists);
                Assert.NotEmpty(e.References);
            });

            Assert.All(swept.Report.Unreferenced, e =>
            {
                Assert.True(e.Exists);
                Assert.Empty(e.References);
            });
        }
    }

    /// <summary>
    /// A design with no <c>Resources</c> folder reports nothing missing.
    /// </summary>
    /// <remarks>
    /// <b>The editor's own template is one</b>, and it names 154 files. Calling all of those
    /// broken would be true of the folder and false about the design — its art comes from the
    /// shared install. A tool that cries wolf on the first design you point it at does not get
    /// used on the second.
    /// </remarks>
    [Fact]
    public void A_design_with_no_resource_folder_claims_nothing_is_missing()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null
            ? null
            : Path.Combine(dir.FullName, "src", "UAFWinEd", "DefaultDesign.dsn");

        if (root is null || !Directory.Exists(root))
        {
            return;
        }

        // The template is committed, so unlike the rest of this file that really runs.
        Assert.False(Directory.Exists(Path.Combine(root, "Resources")));

        using var design = LoadedDesign.Open(root, role: ArchiveRole.Editor);
        var report = CrossReferenceBuilder.Build(design);

        Assert.False(report.ResourcesPresent);
        Assert.NotEmpty(report.Entries);          // it does name files
        Assert.Empty(report.Missing);             // but none of them is called broken
        Assert.Contains("cannot be checked", report.Summary, StringComparison.Ordinal);
    }
}
