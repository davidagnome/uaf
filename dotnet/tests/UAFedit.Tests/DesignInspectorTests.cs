using UAFcore;
using UAFedit.ViewModels;

namespace UAFedit.Tests;

/// <summary>
/// The read-only design inspector, driven headlessly.
/// </summary>
/// <remarks>
/// <para>
/// No window, no display, no Avalonia application: the shell was arranged so that opening a design
/// is a plain method and the two things needing a window — the folder picker and closing — are
/// delegates the view supplies. This is the same separation that let <see cref="LoadedDesign"/> be
/// tested where the reference's <c>OpenDesign</c> could not (docs/PORTING-PLAN.md §7 Phase 0).
/// </para>
/// <para>
/// <b>Every test touching a corpus design returns early when it is absent.</b> <c>reference/</c> is
/// gitignored, so a fresh clone has no designs at all. The premise test is what stops the file
/// passing while proving nothing.
/// </para>
/// </remarks>
public class DesignInspectorTests
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
    /// SomethingWild, opened once and reused.
    /// </summary>
    /// <remarks>
    /// Shared because opening reads <c>game.dat</c> and every database in the design, and xunit
    /// runs the tests of one class one at a time — so the sharing is sequential, and the rows each
    /// test asks for stay lazily built per category.
    /// </remarks>
    private static MainWindowViewModel? shared;

    private static MainWindowViewModel? SomethingWild()
    {
        if (shared is not null)
        {
            return shared;
        }

        if (Corpus("SomethingWild.dsn") is not { } root)
        {
            return null;
        }

        var model = new MainWindowViewModel();
        return model.Open(root) ? shared = model : null;
    }

    private static DesignNodeViewModel? Category(MainWindowViewModel model, string name) =>
        model.Roots[0].Children.FirstOrDefault(c => c.Name == name);

    /// <summary>
    /// The premise: a real design opens and its core categories hold records.
    /// </summary>
    /// <remarks>
    /// Everything below early-returns without the corpus, so this is the test that fails rather
    /// than passing vacuously if the projection ever stops finding anything.
    /// </remarks>
    [Fact]
    public void The_corpus_design_opens_with_records_in_its_core_categories()
    {
        if (SomethingWild() is not { } model)
        {
            return;
        }

        var root = Assert.Single(model.Roots);
        Assert.Equal(9, root.Children.Count);

        foreach (string name in new[] { "Levels", "Items", "Monsters", "Spells" })
        {
            var category = Category(model, name);
            Assert.NotNull(category);
            Assert.True(category.IsReadable, $"{name} was not readable");
            Assert.NotEmpty(category.Table.Rows);
        }

        // The design's own name out of game.dat, not the folder it sits in: the directory is
        // "SomethingWild.dsn" and GLOBAL_STATS calls it "Something Wild".
        Assert.Contains("Something Wild", model.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tree names every category <see cref="LoadedDesign"/> exposes as a browsable list.
    /// </summary>
    [Fact]
    public void The_tree_lists_every_category_the_design_exposes()
    {
        if (SomethingWild() is not { } model)
        {
            return;
        }

        Assert.Equal(
            ["Levels", "Items", "Monsters", "Spells", "Baseclasses", "Classes", "Races",
             "Abilities", "Special Abilities"],
            model.Roots[0].Children.Select(c => c.Name));
    }

    /// <summary>Every row of every category lines up with its own header.</summary>
    [Fact]
    public void Every_row_has_one_cell_per_column()
    {
        if (SomethingWild() is not { } model)
        {
            return;
        }

        foreach (var category in model.Roots[0].Children)
        {
            foreach (var row in category.Table.Rows)
            {
                Assert.Equal(category.Table.Columns.Count, row.Cells.Count);
            }
        }
    }

    /// <summary>
    /// The item rows are the design's own records, in order, named by the id that resolves.
    /// </summary>
    /// <remarks>
    /// <b>The name column is <c>UniqueName</c>.</b> An item's id is its <c>m_uniqueName</c>
    /// (<c>Items.h:701</c>) and <c>IdName</c> is the fuller display name; the two differ in shipped
    /// designs, so a list keyed on the wrong one names items that nothing can look up.
    /// </remarks>
    [Fact]
    public void Item_rows_carry_the_design_s_own_records_in_order()
    {
        if (Corpus("SomethingWild.dsn") is not { } root || SomethingWild() is not { } model)
        {
            return;
        }

        using var design = LoadedDesign.Open(root);
        var items = design.Items;
        Assert.NotNull(items);

        var category = Category(model, "Items");
        Assert.NotNull(category);

        Assert.Equal(items.Items.Count, category.Table.Rows.Count);
        Assert.Equal(items.Items.Select(i => i.Names.UniqueName),
                     category.Table.Rows.Select(r => r.Cells[0].Text));
        Assert.Equal(items.Items.Select(i => i.Names.IdName),
                     category.Table.Rows.Select(r => r.Cells[1].Text));
    }

    /// <summary>The level list covers every <c>.lvl</c> the design ships, with its extent read.</summary>
    [Fact]
    public void Level_rows_cover_every_level_file()
    {
        if (Corpus("SomethingWild.dsn") is not { } root || SomethingWild() is not { } model)
        {
            return;
        }

        using var design = LoadedDesign.Open(root);
        var category = Category(model, "Levels");
        Assert.NotNull(category);

        Assert.Equal(design.LevelFiles.Count, category.Table.Rows.Count);
        Assert.All(category.Table.Rows, row => Assert.Contains(" x ", row.Cells[4].Text,
                                                               StringComparison.Ordinal));
    }

    /// <summary>
    /// A design numbered without gaps agrees with the file order; a sparse one does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The number in a level's file name is its index plus one</b> (<c>Shared/Level.cpp:3643</c>)
    /// — <b>not</b> its position in <see cref="LoadedDesign.LevelFiles"/>. <c>Case.dsn</c> is the
    /// design that shows the difference: eleven files numbered up to <c>Level255.lvl</c>, so its
    /// last file is level 254 and sits at position 10.
    /// </para>
    /// <para>
    /// The inspector shows both columns rather than choosing, because the two really are different
    /// numbers and a design with holes is ordinary.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_sparse_design_numbers_its_levels_from_the_file_name()
    {
        if (Corpus("Case.dsn") is not { } root)
        {
            return;
        }

        using var model = new MainWindowViewModel();
        Assert.True(model.Open(root));

        var category = Category(model, "Levels");
        Assert.NotNull(category);

        var rows = category.Table.Rows;
        Assert.NotEmpty(rows);

        // Position, then the design's own level number.
        Assert.Equal("0", rows[0].Cells[0].Text);
        Assert.Equal("0", rows[0].Cells[1].Text);
        Assert.Contains(rows, r => r.Cells[0].Text != r.Cells[1].Text);
    }

    /// <summary>Selecting a node is what fills the right pane.</summary>
    [Fact]
    public void Selecting_a_node_publishes_its_columns_and_rows()
    {
        if (SomethingWild() is not { } model)
        {
            return;
        }

        var monsters = Category(model, "Monsters");
        Assert.NotNull(monsters);

        model.SelectedNode = monsters;

        Assert.Equal(monsters.Table.Columns, model.Columns);
        Assert.Equal(monsters.Table.Rows.Count, model.Rows.Count);
        Assert.Equal("Name", model.Columns[0].Header);

        // The design's own node has no records of its own, so the pane empties rather than
        // repeating the last category's rows.
        model.SelectedNode = model.Roots[0];
        Assert.Empty(model.Rows);
        Assert.Empty(model.Columns);
    }

    /// <summary>
    /// A category's rows are built when it is asked for them, not when the design opens.
    /// </summary>
    /// <remarks>
    /// This is what keeps opening a design cheap: the level projection reads every <c>.lvl</c> file
    /// whole, so an eager tree would parse the whole design to draw nine labels.
    /// </remarks>
    [Fact]
    public void Rows_are_built_on_demand()
    {
        if (Corpus("SomethingWild.dsn") is not { } root)
        {
            return;
        }

        using var model = new MainWindowViewModel();
        Assert.True(model.Open(root));

        Assert.All(model.Roots[0].Children, c => Assert.False(c.Table.IsMaterialised));

        var spells = Category(model, "Spells");
        Assert.NotNull(spells);
        Assert.NotEmpty(spells.Table.Rows);

        Assert.True(spells.Table.IsMaterialised);
        Assert.False(Category(model, "Levels")!.Table.IsMaterialised);
    }

    /// <summary>
    /// The design loads with no image decoder and no font rasteriser.
    /// </summary>
    /// <remarks>
    /// The editor passes null for both: it draws no art yet, and <see cref="LoadedDesign"/> treats
    /// both as optional — <c>ImageLoader</c> substitutes a null decoder and <c>Font</c> answers
    /// null. Asserted here because it is what keeps SDL out of the editor's dependencies.
    /// </remarks>
    [Fact]
    public void A_design_opens_with_neither_decoder()
    {
        if (Corpus("SomethingWild.dsn") is not { } root)
        {
            return;
        }

        using var design = LoadedDesign.Open(root);

        Assert.NotEmpty(design.Name);
        Assert.NotNull(design.Items);
        Assert.NotNull(design.Monsters);
        Assert.NotNull(design.Spells);
        Assert.Null(design.Font(design.RequestedFontHeight));
    }

    /// <summary>A directory that is not a design is reported, not thrown.</summary>
    [Fact]
    public void Opening_something_that_is_not_a_design_reports_it()
    {
        using var model = new MainWindowViewModel();

        Assert.False(model.Open(Path.GetTempPath()));
        Assert.Empty(model.Roots);
        Assert.Contains("Could not open", model.Status, StringComparison.Ordinal);
    }

    /// <summary>A category whose database could not be read is not a category with no records.</summary>
    [Fact]
    public void An_unreadable_category_says_so()
    {
        var node = DesignNodeViewModel.Unreadable("Races");

        Assert.False(node.IsReadable);
        Assert.Contains("not readable", node.Label, StringComparison.Ordinal);
        Assert.Empty(node.Table.Rows);

        Assert.True(DesignNodeViewModel.Category("Races", 0, RecordTable.Empty).IsReadable);
    }

    /// <summary>Exit runs whatever the view gave it, and nothing when it gave none.</summary>
    [Fact]
    public void Exit_runs_what_the_view_supplied()
    {
        using var model = new MainWindowViewModel();
        model.ExitCommand.Execute(null);           // no handler: must not throw

        bool asked = false;
        model.RequestExit = () => asked = true;
        model.ExitCommand.Execute(null);

        Assert.True(asked);
    }
}
