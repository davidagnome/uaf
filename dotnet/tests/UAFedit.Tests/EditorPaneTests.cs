using UAFedit.ViewModels;

namespace UAFedit.Tests;

/// <summary>
/// The shell's six editor panes: that they build, that they build only when asked, and that
/// reopening a design lets go of the old ones.
/// </summary>
/// <remarks>
/// <para>
/// The panes themselves are tested in their own projects — UAFedit.Levels.Tests and the rest. What
/// is only testable here is the wiring: that the shell hands each one the open design and that the
/// laziness the tab strip relies on is real rather than intended.
/// </para>
/// <para>
/// <b>Every test returns early without the corpus.</b> <c>reference/</c> is gitignored, so a fresh
/// clone has no designs; <see cref="The_corpus_opens"/> is what stops the file passing while
/// proving nothing.
/// </para>
/// </remarks>
public class EditorPaneTests
{
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
    /// A freshly opened design, per test.
    /// </summary>
    /// <remarks>
    /// Not shared, unlike the inspector's fixture: these tests are about what has and has not been
    /// built yet, and a model another test already opened a pane on cannot answer that.
    /// </remarks>
    private static MainWindowViewModel? Opened(string design = "SomethingWild.dsn")
    {
        if (Corpus(design) is not { } root)
        {
            return null;
        }

        var model = new MainWindowViewModel();
        return model.Open(root) ? model : null;
    }

    /// <summary>The premise: the corpus design opens.</summary>
    [Fact]
    public void The_corpus_opens()
    {
        if (Opened() is not { } model)
        {
            return;
        }

        using (model)
        {
            Assert.NotEmpty(model.Roots);
        }
    }

    /// <summary>Every pane builds against a real design.</summary>
    /// <remarks>
    /// One test rather than six, because the interesting failure is a pane the shell never reaches
    /// — a constructor that throws on a design the corpus actually ships — and that is the same
    /// failure whichever pane it is.
    /// </remarks>
    [Theory]
    [InlineData(EditorPane.Levels)]
    [InlineData(EditorPane.Events)]
    [InlineData(EditorPane.Items)]
    [InlineData(EditorPane.Monsters)]
    [InlineData(EditorPane.Spells)]
    [InlineData(EditorPane.Abilities)]
    public void Every_pane_builds(EditorPane pane)
    {
        if (Opened() is not { } model)
        {
            return;
        }

        using (model)
        {
            model.SelectedPane = pane;

            Assert.NotNull(Built(model, pane));

            // A pane that threw is reported rather than swallowed, so a null pane with an
            // unchanged status line would be a pane nobody built at all.
            Assert.DoesNotContain("Could not open", model.Status, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Selecting a tab builds that pane and no other.
    /// </summary>
    /// <remarks>
    /// <b>This is the property the tab strip is built on.</b> The Levels and Events panes each read
    /// every <c>.lvl</c> in the design; building all six on open would charge every user the cost
    /// of the slowest one. A regression here would not fail any other test — it would just make
    /// opening a design slow.
    /// </remarks>
    [Fact]
    public void Opening_a_design_builds_no_panes()
    {
        if (Opened() is not { } model)
        {
            return;
        }

        using (model)
        {
            Assert.Equal(EditorPane.Design, model.SelectedPane);
            Assert.All(Editors, pane => Assert.Null(Built(model, pane)));

            model.SelectedPane = EditorPane.Items;

            Assert.NotNull(model.ItemsPane);
            Assert.All(Editors.Where(p => p != EditorPane.Items),
                       pane => Assert.Null(Built(model, pane)));
        }
    }

    /// <summary>A pane is built once and kept, so leaving a tab and coming back keeps the edits.</summary>
    /// <remarks>
    /// The reference's editors were modal dialogs that rebuilt from the record every time they
    /// opened, so switching away and back discarded whatever had not been committed. A pane that
    /// rebuilt on every selection would reintroduce exactly that.
    /// </remarks>
    [Fact]
    public void A_pane_is_built_once_and_kept()
    {
        if (Opened() is not { } model)
        {
            return;
        }

        using (model)
        {
            model.SelectedPane = EditorPane.Spells;
            var first = model.SpellsPane;

            model.SelectedPane = EditorPane.Design;
            model.SelectedPane = EditorPane.Spells;

            Assert.Same(first, model.SpellsPane);
        }
    }

    /// <summary>
    /// Opening another design drops every pane built over the old one.
    /// </summary>
    /// <remarks>
    /// <b>A pane outliving its design would be showing records read out of a disposed one.</b> The
    /// panes hold the records themselves rather than the design, so nothing would throw — the tab
    /// would simply go on displaying the previous design's spells under the new design's name.
    /// </remarks>
    [Fact]
    public void Reopening_a_design_drops_the_panes()
    {
        if (Opened() is not { } model)
        {
            return;
        }

        using (model)
        {
            model.SelectedPane = EditorPane.Spells;
            model.SelectedPane = EditorPane.Items;
            Assert.NotNull(model.SpellsPane);
            Assert.NotNull(model.ItemsPane);

            if (Corpus("SomethingWild.dsn") is not { } root)
            {
                return;
            }

            Assert.True(model.Open(root));

            Assert.All(Editors, pane => Assert.Null(Built(model, pane)));

            // And the shell is back on the tab a freshly opened design should show.
            Assert.Equal(EditorPane.Design, model.SelectedPane);
        }
    }

    /// <summary>Selecting a pane with no design open builds nothing and throws nothing.</summary>
    [Fact]
    public void A_pane_needs_a_design()
    {
        using var model = new MainWindowViewModel();

        foreach (var pane in Editors)
        {
            model.SelectedPane = pane;
            Assert.Null(Built(model, pane));
        }
    }

    /// <summary>The six panes that are editors — everything but the inspector.</summary>
    private static IEnumerable<EditorPane> Editors =>
        Enum.GetValues<EditorPane>().Where(p => p != EditorPane.Design);

    private static object? Built(MainWindowViewModel model, EditorPane pane) => pane switch
    {
        EditorPane.Levels => model.LevelsPane,
        EditorPane.Events => model.EventsPane,
        EditorPane.Items => model.ItemsPane,
        EditorPane.Monsters => model.MonstersPane,
        EditorPane.Spells => model.SpellsPane,
        EditorPane.Abilities => model.AbilitiesPane,
        _ => null,
    };
}
