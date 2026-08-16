using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using UAFedit.Map;

namespace UAFedit.Levels.Tests;

/// <summary>
/// The pane itself, built on the headless platform.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in this project is view models, which need no platform. This file exists for one
/// question the view models cannot answer: does <see cref="LevelMapView"/> — which this namespace
/// consumes and must not modify — actually compose into the panel and receive a level?
/// </para>
/// <para>
/// <b>All of it runs on one dedicated thread.</b> Avalonia's dispatcher belongs to whichever thread
/// set the platform up and <c>SetupWithoutStarting</c> may be called once per process, so the
/// platform is stood up on a thread of its own and every body is posted to it. The pattern is
/// <c>UAFedit.Tests/ShellWindowTests</c>'s, for the same reasons.
/// </para>
/// </remarks>
public class LevelsViewTests
{
    private static readonly BlockingCollection<Action> Work = [];

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
        { IsBackground = true, Name = "avalonia-headless-levels" };

        thread.Start();
        return thread;
    });

    /// <summary>Builds the pane inside a window, runs a body against it, and closes it.</summary>
    /// <remarks>
    /// A window rather than a bare control: a control outside a visual tree is never measured or
    /// arranged, so its templates never instantiate and the map would report a zero viewport.
    /// </remarks>
    private static void OnUiThread(Action<LevelsView> body)
    {
        _ = Ui.Value;

        Exception? failure = null;
        using var done = new ManualResetEventSlim();

        Work.Add(() =>
        {
            Window? window = null;
            try
            {
                var view = new LevelsView();
                window = new Window { Content = view, Width = 1200, Height = 800 };
                window.Show();
                body(view);
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                window?.Close();
                done.Set();
            }
        });

        done.Wait();

        if (failure is not null)
        {
            throw new InvalidOperationException("the level pane failed to build", failure);
        }
    }

    /// <summary>The pane builds, and the map control is in it.</summary>
    [Fact]
    public void The_pane_builds_with_a_map_in_it()
    {
        OnUiThread(view =>
        {
            var map = Assert.Single(view.GetVisualDescendants().OfType<LevelMapView>());

            // Nothing open yet, so the map has no level and draws its empty backdrop.
            Assert.Null(map.Model);
            Assert.Equal(4, view.GetVisualDescendants().OfType<TabItem>().Count());
        });
    }

    /// <summary>
    /// Opening a design hands the map a level, its palette and its geometry.
    /// </summary>
    /// <remarks>
    /// The real question this project has to answer about the map control: it is a plain
    /// <c>Control</c> with styled properties, and everything it needs arrives by binding through
    /// <see cref="LevelPanelViewModel"/>. If any of those bindings were wrong the AXAML would not
    /// compile; if the <c>DataContext</c> rebinding to <c>Panel</c> were wrong it would compile and
    /// draw nothing, which is what this catches.
    /// </remarks>
    [Fact]
    public void Opening_a_design_hands_the_map_a_level()
    {
        if (Corpus.Root(Corpus.SomethingWild) is not { } root)
        {
            return;
        }

        OnUiThread(view =>
        {
            using var design = UAFcore.LoadedDesign.Open(root);
            var model = view.Open(design);

            var map = Assert.Single(view.GetVisualDescendants().OfType<LevelMapView>());

            Assert.NotNull(map.Model);
            Assert.Same(model.Panel!.EffectiveModel, map.Model);
            Assert.Same(model.Panel.Palette, map.Palette);
            Assert.Same(model.Panel.Geometry, map.Geometry);
            Assert.True(map.Model!.Width > 0 && map.Model.Height > 0);

            // The map takes the room it is offered rather than the level's extent, which is what
            // makes it scrollable rather than scrolled.
            Assert.True(map.Bounds.Width > 0);
            Assert.True(map.Bounds.Height > 0);
        });
    }

    /// <summary>
    /// Changing the level in the list changes the level on the map.
    /// </summary>
    /// <remarks>
    /// And on <c>Case</c> specifically, because that is where the two candidate keys disagree: the
    /// tenth row is level 255 and its 10 × 10 grid has to be what appears, not the 21 × 21 of
    /// whatever sits at <c>stats[9]</c>.
    /// </remarks>
    [Fact]
    public void Selecting_a_level_swaps_the_map()
    {
        if (Corpus.Root(Corpus.Case) is not { } root)
        {
            return;
        }

        OnUiThread(view =>
        {
            using var design = UAFcore.LoadedDesign.Open(root);
            var model = view.Open(design);
            var map = Assert.Single(view.GetVisualDescendants().OfType<LevelMapView>());

            Assert.Equal(21, map.Model!.Width);          // level 1, Dartcave_Intro

            model.SelectByNumber(255);

            Assert.Equal(10, map.Model!.Width);
            Assert.Equal(10, map.Model.Height);
            Assert.Equal("Reeftest", model.SelectedLevel!.Name);
        });
    }

    /// <summary>
    /// A scroll request from the panel reaches the map.
    /// </summary>
    /// <remarks>
    /// The one thing the view's code-behind does. It has to survive the panel being replaced, which
    /// happens on every list selection — so the level is changed first and the request made after.
    /// </remarks>
    [Fact]
    public void A_scroll_request_reaches_the_map()
    {
        if (Corpus.Root(Corpus.SomethingWild) is not { } root)
        {
            return;
        }

        OnUiThread(view =>
        {
            using var design = UAFcore.LoadedDesign.Open(root);
            var model = view.Open(design);

            model.SelectByNumber(2);                     // 'Sigil', 44x34: bigger than the viewport
            var map = Assert.Single(view.GetVisualDescendants().OfType<LevelMapView>());

            Assert.Equal(0, map.ScrollX);
            Assert.Equal(0, map.ScrollY);

            var panel = model.Panel!;
            panel.SelectedCell = new MapPoint(40, 30);
            panel.ShowSelectedCell();

            Assert.True(map.ScrollX > 0 || map.ScrollY > 0,
                        "the map did not scroll towards the far corner of a 44x34 level");
        });
    }
}
