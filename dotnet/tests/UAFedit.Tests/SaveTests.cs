using UAFcore;
using UAFedit.Events;
using UAFedit.ViewModels;

namespace UAFedit.Tests;

/// <summary>
/// File &gt; Save: what reaches disk, what does not, and what a refusal leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every test copies a design to a temporary folder first.</b> The corpus under
/// <c>reference/</c> is the only ground truth this port has, and a test that wrote into it would
/// destroy that the first time it ran — silently, since the round trip would go on passing against
/// the mangled copy.
/// </para>
/// <para>
/// Returns early without the corpus, as everything touching <c>reference/</c> must;
/// <see cref="A_writable_design_can_be_copied"/> is the premise that stops the file passing while
/// proving nothing.
/// </para>
/// </remarks>
public sealed class SaveTests : IDisposable
{
    private readonly string scratch =
        Path.Combine(Path.GetTempPath(), $"uafedit-save-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>A corpus design's directory, or null when the corpus is not present.</summary>
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

    /// <summary>
    /// A throwaway copy of a design, or null when the corpus is absent.
    /// </summary>
    /// <remarks>
    /// Only <c>Data/</c> is copied. It holds every file a save touches, and a design's art can run
    /// to hundreds of megabytes that no test here reads.
    /// </remarks>
    private string? Copy(string design = "SomethingWild.dsn")
    {
        if (Corpus(design) is not { } source)
        {
            return null;
        }

        string root = Path.Combine(scratch, design);
        Directory.CreateDirectory(Path.Combine(root, "Data"));

        foreach (string file in Directory.EnumerateFiles(Path.Combine(source, "Data")))
        {
            File.Copy(file, Path.Combine(root, "Data", Path.GetFileName(file)));
        }

        return root;
    }

    private MainWindowViewModel? Opened(string root)
    {
        var model = new MainWindowViewModel();
        return model.Open(root) ? model : null;
    }

    /// <summary>
    /// The premise: the copied design opens, and its items are a shape the writers accept.
    /// </summary>
    /// <remarks>
    /// <b>The second half is what stops the rest of this file proving nothing.</b> A design below
    /// 0.998101 is refused rather than written, and every test here treats a refusal as "nothing to
    /// prove" and returns — which is right for a legacy design and catastrophic as a silent default.
    /// If <c>SomethingWild</c> ever stops being writable, this fails rather than the file quietly
    /// going hollow.
    /// </remarks>
    [Fact]
    public void A_writable_design_can_be_copied()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        using var model = Opened(root);
        Assert.NotNull(model);
        Assert.NotEmpty(model!.Roots);

        // The event tests below need more than one level and something on the first one to edit,
        // and they return early without it. Asserted here so that never happens quietly.
        model.SelectedPane = EditorPane.Events;
        Assert.True(model.EventsPane!.Levels.Count > 1);
        Assert.NotEmpty(model.EventsPane.Events);

        model.SelectedPane = EditorPane.Items;
        model.ItemsPane!.All[0].IdName += " (edited)";

        Assert.Equal("Saved items.dat.", model.Save());
    }

    /// <summary>A design nobody has edited saves nothing at all.</summary>
    /// <remarks>
    /// <b>The most important property here.</b> Every writer refuses records below 0.998101 rather
    /// than guessing, and saves at 5.24 the ones it accepts — so writing untouched databases would
    /// either refuse a legacy design that the user only opened, or quietly upgrade files they never
    /// looked at. Opening and saving must be a no-op.
    /// </remarks>
    [Fact]
    public void Opening_and_saving_without_editing_writes_nothing()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        var before = Stamps(root);

        using var model = Opened(root);
        Assert.NotNull(model);
        Assert.False(model!.IsDirty);

        Assert.Equal("Nothing has changed.", model.Save());
        Assert.Equal(before, Stamps(root));
    }

    /// <summary>An edited item reaches the file, and reopening the design finds it.</summary>
    /// <remarks>
    /// <b>The whole loop the exit criterion is about</b>, minus the C++ editor: open a design, make
    /// an edit, save, and read it back with a reader that never saw the edit in memory.
    /// </remarks>
    [Fact]
    public void An_edited_item_survives_a_save_and_a_reopen()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        string renamed;

        using (var model = Opened(root))
        {
            Assert.NotNull(model);
            model!.SelectedPane = EditorPane.Items;

            var items = model.ItemsPane;
            Assert.NotNull(items);
            Assert.True(items!.IsReadable);

            var first = items.All[0];
            renamed = first.IdName + " (edited)";
            first.IdName = renamed;

            Assert.True(items.IsDirty);
            Assert.True(model.IsDirty);

            // A refusal is reported rather than thrown, so a design the writers decline shows up
            // here as a message instead of a crash -- and then this test has nothing to prove.
            string outcome = model.Save();
            if (!outcome.Contains("items.dat", StringComparison.Ordinal))
            {
                Assert.StartsWith("Nothing was saved", outcome, StringComparison.Ordinal);
                return;
            }

            // Saved, so nothing is outstanding any more.
            Assert.False(items.IsDirty);
            Assert.False(model.IsDirty);
        }

        // A completely fresh read of the folder, through the ordinary open path.
        using var reopened = new MainWindowViewModel();
        Assert.True(reopened.Open(root));
        reopened.SelectedPane = EditorPane.Items;

        Assert.Contains(reopened.ItemsPane!.All, i => i.IdName == renamed);
    }

    /// <summary>Saving writes the edited database and leaves the others alone.</summary>
    /// <remarks>
    /// A save that rewrote every database would be indistinguishable from this one by the design's
    /// contents, and quite different by its version stamps.
    /// </remarks>
    [Fact]
    public void Only_the_edited_database_is_written()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        using var model = Opened(root);
        Assert.NotNull(model);

        model!.SelectedPane = EditorPane.Items;
        model.SelectedPane = EditorPane.Monsters;

        var before = Stamps(root);

        model.ItemsPane!.All[0].IdName += " (edited)";

        if (!model.Save().Contains("items.dat", StringComparison.Ordinal))
        {
            return;                              // refused; covered by the test above
        }

        var after = Stamps(root);

        Assert.NotEqual(before["items.dat"], after["items.dat"]);
        Assert.Equal(before["monsters.dat"], after["monsters.dat"]);
        Assert.Equal(before["spells.dat"], after["spells.dat"]);
        Assert.Equal(before["game.dat"], after["game.dat"]);
    }

    /// <summary>
    /// A save leaves no staging file behind, whatever happened.
    /// </summary>
    /// <remarks>
    /// <see cref="DesignSaver"/> writes beside the real file and moves it into place. A staging
    /// file left in <c>Data/</c> would be picked up by the next directory listing as though it
    /// were part of the design.
    /// </remarks>
    [Fact]
    public void A_save_leaves_no_staging_file()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        using var model = Opened(root);
        Assert.NotNull(model);

        model!.SelectedPane = EditorPane.Items;
        model.ItemsPane!.All[0].IdName += " (edited)";
        model.Save();

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "Data"), "*.saving"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "Data"), ".*"));
    }

    /// <summary>An edited event reaches its level file, and reopening the design finds it.</summary>
    [Fact]
    public void An_edited_event_survives_a_save_and_a_reopen()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        string renamed;

        using (var model = Opened(root))
        {
            Assert.NotNull(model);
            model!.SelectedPane = EditorPane.Events;

            var events = model.EventsPane;
            Assert.NotNull(events);

            if (events!.Events.Count == 0)
            {
                return;                          // a design whose first level has no events
            }

            var row = events.Events[0];
            events.SelectedEvent = row;

            renamed = row.Body.Base.Text + " (edited)";
            events.Apply(EventRecords.WithBase(row.Body, row.Body.Base with { Text = renamed }));

            Assert.True(events.HasUnsavedLevels);
            Assert.True(model.IsDirty);

            // Asserted rather than skipped past: the premise test has already established that
            // this design is a shape the writers accept, so a refusal here is a defect.
            Assert.Contains(".lvl", model.Save(), StringComparison.OrdinalIgnoreCase);

            Assert.False(events.HasUnsavedLevels);
            Assert.False(model.IsDirty);
        }

        using var reopened = new MainWindowViewModel();
        Assert.True(reopened.Open(root));
        reopened.SelectedPane = EditorPane.Events;

        Assert.Contains(reopened.EventsPane!.Events, e => e.Body.Base.Text == renamed);
    }

    /// <summary>
    /// An edit made on one level is not lost by looking at another.
    /// </summary>
    /// <remarks>
    /// <b>The event editor holds one level at a time and re-reads on every switch.</b> Before the
    /// stash existed, editing level 1 and then selecting level 2 discarded level 1 silently — and
    /// a save afterwards wrote level 2 and dropped the other, which is the worst version of the
    /// bug because it looks like it worked.
    /// </remarks>
    [Fact]
    public void An_edit_survives_switching_levels()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        using var model = Opened(root);
        Assert.NotNull(model);
        model!.SelectedPane = EditorPane.Events;

        var events = model.EventsPane;
        Assert.NotNull(events);

        if (events!.Levels.Count < 2 || events.Events.Count == 0)
        {
            return;                              // needs two levels and something to edit
        }

        var first = events.Levels[0];
        var row = events.Events[0];
        events.SelectedEvent = row;

        string renamed = row.Body.Base.Text + " (edited)";
        events.Apply(EventRecords.WithBase(row.Body, row.Body.Base with { Text = renamed }));

        // Away, and back.
        events.SelectedLevel = events.Levels[1];
        Assert.True(events.HasUnsavedLevels);
        Assert.True(model.IsDirty);

        events.SelectedLevel = first;

        Assert.Contains(events.Events, e => e.Body.Base.Text == renamed);
        Assert.Contains(events.EditedLevels, l => l.Key == first.Index);
    }

    /// <summary>An edited script reaches specialAbilities.txt, and reopening finds it.</summary>
    /// <remarks>
    /// <b>The text file, not the binary one.</b> A design ships both <c>specialAbilities.txt</c>
    /// and <c>specialAbilities.dat</c> and they are unrelated formats; the scripts the editor edits
    /// live in the text one.
    /// </remarks>
    [Fact]
    public void An_edited_script_survives_a_save_and_a_reopen()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        string edited;
        string abilityName;

        using (var model = Opened(root))
        {
            Assert.NotNull(model);
            model!.SelectedPane = EditorPane.Abilities;

            var abilities = model.AbilitiesPane;
            Assert.NotNull(abilities);
            Assert.NotEmpty(abilities!.Abilities);

            var ability = abilities.Abilities[0];
            abilities.SelectedAbility = ability;
            abilityName = ability.Name;

            edited = ability.Name + " (edited)";
            ability.Name = edited;

            Assert.True(abilities.IsDirty);
            Assert.True(model.IsDirty);

            Assert.Contains("specialAbilities.txt", model.Save(), StringComparison.Ordinal);
            Assert.False(model.IsDirty);
        }

        using var reopened = new MainWindowViewModel();
        Assert.True(reopened.Open(root));
        reopened.SelectedPane = EditorPane.Abilities;

        Assert.Contains(reopened.AbilitiesPane!.Abilities, a => a.Name == edited);
        Assert.DoesNotContain(reopened.AbilitiesPane.Abilities, a => a.Name == abilityName);
    }

    /// <summary>An edited setting reaches game.dat, and reopening the design finds it.</summary>
    /// <remarks>
    /// <b>The design name is the field to check</b>, because it is the one the shell puts in the
    /// window title — so a reopen proves the value came off disk rather than out of the pane.
    /// </remarks>
    [Fact]
    public void An_edited_setting_survives_a_save_and_a_reopen()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        string renamed;

        using (var model = Opened(root))
        {
            Assert.NotNull(model);
            model!.SelectedPane = EditorPane.Settings;

            var settings = model.SettingsPane;
            Assert.NotNull(settings);
            Assert.False(settings!.IsDirty);

            renamed = settings.DesignName + " (edited)";
            settings.DesignName = renamed;
            settings.StartPlatinum += 250;

            Assert.True(settings.IsDirty);
            Assert.True(model.IsDirty);

            Assert.Contains("game.dat", model.Save(), StringComparison.Ordinal);
            Assert.False(model.IsDirty);
        }

        using var reopened = new MainWindowViewModel();
        Assert.True(reopened.Open(root));
        reopened.SelectedPane = EditorPane.Settings;

        Assert.Equal(renamed, reopened.SettingsPane!.DesignName);
        Assert.Contains(renamed, reopened.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// Saving game.dat keeps the global events the pane does not edit.
    /// </summary>
    /// <remarks>
    /// <b>The failure this guards against is silent.</b> The prefix does not carry the global
    /// events — the reader hands each body to a callback as it passes — so a save built from the
    /// prefix alone writes a design whose global events have all vanished, and a count of zero is
    /// a perfectly valid file that nothing would complain about.
    /// </remarks>
    [Fact]
    public void Saving_the_settings_keeps_the_global_events()
    {
        if (Copy() is not { } root)
        {
            return;
        }

        int before;

        using (var model = Opened(root))
        {
            Assert.NotNull(model);
            model!.SelectedPane = EditorPane.Settings;

            before = model.SettingsPane!.GlobalEventCount;
            Assert.True(before > 0);            // else this proves nothing

            model.SettingsPane.StartExp += 1;
            Assert.Contains("game.dat", model.Save(), StringComparison.Ordinal);
        }

        using var reopened = new MainWindowViewModel();
        Assert.True(reopened.Open(root));
        reopened.SelectedPane = EditorPane.Settings;

        Assert.Equal(before, reopened.SettingsPane!.GlobalEventCount);
    }

    /// <summary>Saving with no design open says so rather than throwing.</summary>
    [Fact]
    public void Saving_with_nothing_open_is_reported()
    {
        using var model = new MainWindowViewModel();

        Assert.False(model.IsDirty);
        Assert.Equal("No design is open.", model.Save());
    }

    /// <summary>Every file in <c>Data/</c>, by length and last-write time.</summary>
    private static Dictionary<string, (long Length, DateTime Written)> Stamps(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "Data"))
                 .ToDictionary(
                     f => Path.GetFileName(f),
                     f => (new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)),
                     StringComparer.OrdinalIgnoreCase);
}
