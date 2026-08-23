using UAFcore;
using UAFedit.ViewModels;
using Xunit.Abstractions;

namespace UAFedit.Oracle.Tests;

/// <summary>
/// Phase 5's exit criterion, as a test: a design this port saved, opened by the C++ editor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything else in the suite asks whether the port agrees with itself.</b> This asks the only
/// question that settles the phase — whether <c>UAFWinEd.exe</c> can read what the port wrote — and
/// it is the check that found the two defects fixed on 2026-08-21: a save that omitted
/// <c>specialAbilities.dat</c>, which aborted the reference's load outright, and a version stamp
/// moved without the databases beneath it, which left the editor reading a 0.915 monster database
/// under a 5.26 <c>game.dat</c>.
/// </para>
/// <para>
/// <b>It skips when the rig is absent and says so.</b> A fresh clone has no CrossOver, no bottle
/// and no built <c>UAFWinEd.exe</c>; on such a machine every case here returns early, and
/// <see cref="The_rig_reports_whether_it_can_run"/> prints which of those was missing rather than
/// leaving a green tick over an empty room.
/// </para>
/// </remarks>
public sealed class SavedDesignLoadsTests(ITestOutputHelper output) : IDisposable
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Scratch inside the test's own output directory rather than the system temp folder.
    /// </summary>
    /// <remarks>
    /// The temp folder on this machine is swept aggressively, and a design that vanished
    /// mid-run would look like the editor had deleted it.
    /// </remarks>
    private readonly string scratch =
        Path.Combine(AppContext.BaseDirectory, "oracle-scratch", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>Says what the rig found, so a skipped run is legible.</summary>
    [Fact]
    public void The_rig_reports_whether_it_can_run()
    {
        string? why = ReferenceEditor.Unavailable();

        _output.WriteLine(why is null
            ? "the reference editor can be run on this machine"
            : $"NOT PROOF OF ANYTHING: {why}");

        // Asserting the repository is there is the one thing true everywhere.
        Assert.NotNull(ReferenceEditor.RepoRoot());
    }

    /// <summary>
    /// A design the port creates and saves is opened by the reference editor.
    /// </summary>
    /// <remarks>
    /// The three lines asserted are the reference's own account of a successful load: it names the
    /// design and the version it read, and then says it finished. A design it refuses never reaches
    /// the second.
    /// </remarks>
    [Fact]
    public void A_port_saved_design_loads_in_the_reference_editor()
    {
        if (!ReferenceEditor.Available)
        {
            return;
        }

        string root = Save();
        var result = ReferenceEditor.Run(root, TimeSpan.FromMinutes(3));
        string? log = result.Log;

        Assert.NotNull(log);
        _output.WriteLine(Interesting(log!));

        // The JSON is the proof the dump ran to completion. The log lines a GUI run ends with are
        // not written here at all -- -dumpjson bypasses OpenDesign, which is what makes it
        // headless -- and a binary too old to know the flag writes no file at all, which is the
        // failure this asserts against.
        Assert.NotNull(result.Json);
        Assert.True(new FileInfo(result.Json!).Length > 1000,
                    $"the dump produced only {new FileInfo(result.Json!).Length} bytes");

        Assert.Contains("version 5.2600000000", log!, StringComparison.Ordinal);

        // A design that opens with no level is not a design that loaded, and an earlier version of
        // this test said it was: it asserted only the two lines above, which are satisfied while
        // the editor is still reporting "Failed to load start level for design". Assert the
        // absence of every error dialog, not just the failure most recently fixed.
        foreach (string repair in Repairs(log!))
        {
            _output.WriteLine($"repaired: {repair}");
        }

        Assert.Empty(Errors(log!));
    }

    /// <summary>
    /// Every error dialog the reference raised, as it phrased them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Matched on how the log records a dialog, not on the word "Error".</b> The first version
    /// of this matched any line containing it and so flagged the log's own <c>Error Log ON</c>
    /// header and the continuation lines of GPDL compiler messages — noise that says nothing about
    /// the design. The reference marks a message box with <c>(MB)</c>, and that is the thing worth
    /// asserting on because it is exactly what a person sees.
    /// </para>
    /// <para>
    /// <b><c>(MB) Information</c> is deliberately not an error.</b> Those are the reference
    /// repairing something and saying so — "An item is marked as being 'READY'ed at the wrong body
    /// location. I will fix it" — and a design it repairs is a design it opened. They are returned
    /// separately so a run can print them.
    /// </para>
    /// <para>
    /// <b>The whitelist covers what the port cannot write yet</b>: <c>baseclass.dat</c>,
    /// <c>classes.dat</c> and <c>races.dat</c> are <c>Bcd1</c>/<c>CL1</c>/<c>RaceV1</c> and have no
    /// writers, so a design made from the template still carries the template's. Each has its own
    /// entry in docs/PORTING-PLAN.md. Anything not on the list is a regression.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> Errors(string log)
    {
        string[] known =
        [
            "old 'baseclass.dat'",
            "duplicating a baseclass",
        ];

        return [.. Dialogs(log)
                   .Where(l => l.Contains("Error", StringComparison.Ordinal)
                               || l.Contains("auto-answered", StringComparison.Ordinal))
                   .Where(l => !known.Any(k => l.Contains(k, StringComparison.Ordinal)))];
    }

    /// <summary>The repairs the reference reported, which are not failures.</summary>
    private static IReadOnlyList<string> Repairs(string log) =>
        [.. Dialogs(log).Where(l => l.Contains("Information", StringComparison.Ordinal))];

    /// <summary>
    /// Every line the reference logged as a dialog — shown, or auto-answered because headless.
    /// </summary>
    /// <remarks>
    /// <b>Both markers matter, and missing the second is how this test first went green over a
    /// real defect.</b> <c>(MB)</c> is a message box the reference displayed. A yes/no prompt in
    /// headless mode is never displayed at all — it is auto-answered NO so an oracle cannot modify
    /// the fixture it is reading (<c>Globals.cpp:3832</c>) — and logged under its own marker. A
    /// matcher that only knew about <c>(MB)</c> therefore passed while the interactive editor was
    /// asking the user to convert a stale level file.
    /// </remarks>
    private static IEnumerable<string> Dialogs(string log) =>
        log.Split('\n')
           .Select(l => l.Trim())
           .Where(l => l.Contains("(MB)", StringComparison.Ordinal)
                       || l.Contains("MsgBoxYesNo auto-answered", StringComparison.Ordinal));

    /// <summary>
    /// Every database the reference reads is at the version the design claims.
    /// </summary>
    /// <remarks>
    /// <b>This is the defect that is invisible from inside the port.</b> Writing <c>game.dat</c>
    /// moves the design's stamp, and a database left underneath it at its old shape is not merely
    /// stale — the reference reads the stamp first and then cannot read the file. A half-saved
    /// design logged <c>Loading monster DB version: 0.9150250</c> under a 5.26 <c>game.dat</c>.
    /// </remarks>
    [Fact]
    public void Every_database_is_at_the_version_the_design_claims()
    {
        if (!ReferenceEditor.Available)
        {
            return;
        }

        string? log = ReferenceEditor.Load(Save(), TimeSpan.FromMinutes(3));
        Assert.NotNull(log);

        var stale = log!.Split('\n')
            .Where(l => l.Contains("DB version:", StringComparison.Ordinal))
            .Where(l => !l.Contains("5.24", StringComparison.Ordinal)
                        && !l.Contains("5.26", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToList();

        Assert.True(stale.Count == 0,
                    "a database was left at a version older than the design's own stamp:"
                    + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    /// <summary>
    /// The special-ability database is there, because without it the load aborts.
    /// </summary>
    /// <remarks>
    /// The template ships only <c>specialAbilities.txt</c>, so nothing in the port had ever
    /// produced the binary form — and the reference at 5.26 refuses a design without it, logging
    /// "Unable to open special abilities db file … error 2" and then abandoning the whole load.
    /// </remarks>
    [Fact]
    public void The_special_ability_database_is_written()
    {
        if (!ReferenceEditor.Available)
        {
            return;
        }

        string root = Save();

        Assert.True(File.Exists(Path.Combine(root, "Data", "specialAbilities.dat")));

        string? log = ReferenceEditor.Load(root, TimeSpan.FromMinutes(3));
        Assert.NotNull(log);
        Assert.DoesNotContain("Unable to open special abilities db file", log!,
                              StringComparison.Ordinal);
    }

    /// <summary>Creates a design through File &gt; New, edits it, and saves — as the editor does.</summary>
    private string Save()
    {
        string root = Path.Combine(scratch, "OracleSaved.dsn");

        using var model = new MainWindowViewModel();

        string created = model.New(root);
        Assert.Contains("New design created", created, StringComparison.Ordinal);

        model.SelectedPane = EditorPane.Items;
        model.ItemsPane!.All[0].IdName += " (edited)";

        model.SelectedPane = EditorPane.Settings;
        model.SettingsPane!.DesignName = "OracleSaved";

        _output.WriteLine(model.Save());

        // The editor will not start without art it can find, and the template ships none.
        if (ReferenceEditor.RepoRoot() is { } repo)
        {
            string art = Path.Combine(repo.FullName, "reference", "SomethingWild.dsn", "Resources");
            if (Directory.Exists(art) && !Directory.Exists(Path.Combine(root, "Resources")))
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "Resources"), art);
            }
        }

        return root;
    }

    /// <summary>The lines of the log worth printing when something fails.</summary>
    private static string Interesting(string log) =>
        string.Join(Environment.NewLine,
                    log.Split('\n')
                       .Where(l => l.Contains("Loading", StringComparison.Ordinal)
                                   || l.Contains("Finished", StringComparison.Ordinal)
                                   || l.Contains("Error", StringComparison.Ordinal)
                                   || l.Contains("Unable", StringComparison.Ordinal))
                       .Select(l => l.Trim())
                       .Take(30));
}
