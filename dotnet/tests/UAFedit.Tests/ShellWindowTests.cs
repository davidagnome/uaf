using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using UAFedit.ViewModels;
using UAFedit.Views;

namespace UAFedit.Tests;

/// <summary>
/// The shell window itself, built on the headless platform.
/// </summary>
/// <remarks>
/// <para>
/// <b>Compiled bindings are checked at build time; nothing else here is.</b>
/// <c>AvaloniaUseCompiledBindingsByDefault</c> makes a wrong binding path a compile error, which
/// covers the paths — but not that the templates instantiate, that the tree's selection reaches
/// the view model, or that the menu's commands are wired. Those need a real window, and the
/// headless platform gives one with no display attached.
/// </para>
/// <para>
/// <b>All of it runs on one dedicated thread.</b> Avalonia's dispatcher belongs to whichever
/// thread set the platform up, and xunit does not promise to run two tests on the same one, so the
/// platform is initialised, used and abandoned inside a single thread rather than shared. It also
/// means this runs once per process, which is all <c>SetupWithoutStarting</c> allows.
/// </para>
/// </remarks>
public class ShellWindowTests
{
    private static readonly BlockingCollection<Action> Work = [];

    /// <summary>
    /// The one thread that owns Avalonia, started on first use and never restarted.
    /// </summary>
    /// <remarks>
    /// <c>SetupWithoutStarting</c> may be called once in a process — a second call throws "Setup
    /// was already called" — so the platform cannot be stood up per test, and the dispatcher it
    /// installs belongs to this thread alone.
    /// </remarks>
    private static readonly Lazy<Thread> Ui = new(() =>
    {
        var thread = new Thread(() =>
        {
            AppBuilder.Configure<App>()
                      .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                      .SetupWithoutStarting();

            foreach (var item in Work.GetConsumingEnumerable())
            {
                item();
            }
        })
        { IsBackground = true, Name = "avalonia-headless" };

        thread.Start();
        return thread;
    });

    /// <summary>Builds a window on the UI thread, runs a body against it, and closes it.</summary>
    private static void OnUiThread(Action<MainWindow> body)
    {
        _ = Ui.Value;

        Exception? failure = null;
        using var done = new ManualResetEventSlim();

        Work.Add(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                window.Show();
                body(window);
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                window?.Close();      // which disposes the view model, and the design with it
                done.Set();
            }
        });

        done.Wait();

        if (failure is not null)
        {
            throw new InvalidOperationException("the shell window failed to build", failure);
        }
    }

    /// <summary>The window loads, binds to the view model, and shows both panes.</summary>
    [Fact]
    public void The_window_builds_and_binds()
    {
        OnUiThread(window =>
        {
            var model = Assert.IsType<MainWindowViewModel>(window.DataContext);

            // The view supplies the two things the view model cannot have for itself.
            Assert.NotNull(model.ChooseFolder);
            Assert.NotNull(model.RequestExit);

            var menu = Assert.Single(window.GetVisualDescendants().OfType<Menu>());
            var file = Assert.Single(menu.Items.OfType<MenuItem>());
            Assert.Equal("_File", file.Header);
            Assert.Equal(5, file.Items.Count);       // New, Open, Save, separator, Exit

            // Save is disabled until something is edited, so it cannot be pressed on a shell with
            // no design in it.
            var save = file.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "_Save"));
            Assert.False(save.IsEnabled);

            // Nothing is open yet, so the tree has no roots to show.
            var tree = Assert.Single(window.GetVisualDescendants().OfType<TreeView>());
            Assert.Equal(0, tree.ItemCount);

            // Title comes from the view model, not the AXAML.
            Assert.Equal(model.Title, window.Title);
        });
    }

    /// <summary>
    /// Opening a design fills the tree, and selecting in the tree fills the right pane.
    /// </summary>
    /// <remarks>
    /// The one test that runs the whole loop the milestone is about: a real design in, records on
    /// screen. Returns early without the corpus, as everything touching <c>reference/</c> must.
    /// </remarks>
    [Fact]
    public void Opening_a_design_fills_both_panes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        string? root = dir is null
            ? null
            : Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");

        if (root is null || !Directory.Exists(root))
        {
            return;
        }

        OnUiThread(window =>
        {
            var model = (MainWindowViewModel)window.DataContext!;
            Assert.True(model.Open(root));

            var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();

            // Layout has to run before the controls have containers for their items.
            window.UpdateLayout();

            Assert.Same(model.SelectedNode, tree.SelectedItem);
            Assert.Equal(model.Rows.Count, list.ItemCount);
            Assert.NotEqual(0, list.ItemCount);

            // Selecting in the tree is what drives the right pane, not the other way round.
            var monsters = model.Roots[0].Children.Single(c => c.Name == "Monsters");
            tree.SelectedItem = monsters;

            Assert.Same(monsters, model.SelectedNode);
            window.UpdateLayout();
            Assert.Equal(monsters.Table.Rows.Count, list.ItemCount);
        });
    }
}
