using UAFcore;
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
