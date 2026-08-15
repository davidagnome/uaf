using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UAFcore;
using UAFedit.Map;

namespace UAFedit.Levels;

/// <summary>
/// The Level menu's pane: the level list, the map, and the level's three tables.
/// </summary>
/// <remarks>
/// <para>
/// The entry point to this namespace. Give it a <see cref="LoadedDesign"/> — through
/// <see cref="Open"/> or by assigning a <see cref="LevelsViewModel"/> to
/// <see cref="StyledElement.DataContext"/> — and it does the rest.
/// </para>
/// <para>
/// The only thing the code-behind does is scrolling. <see cref="LevelMapView"/> scrolls itself
/// because a torus has no extent for a <c>ScrollViewer</c> to work in, so "show me this square"
/// needs the viewport's size and therefore the control. Everything else is bound.
/// </para>
/// </remarks>
public partial class LevelsView : UserControl
{
    private LevelPanelViewModel? watched;

    public LevelsView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Rewire();
    }

    /// <summary>Builds the view model for a design and shows it.</summary>
    /// <remarks>
    /// The design is not owned: <see cref="LoadedDesign"/> is disposable and whoever opened it
    /// closes it. Handing this a disposed design gives an empty level list, not a crash, because
    /// only the font rasteriser is disposable and nothing here asks for a glyph.
    /// </remarks>
    public LevelsViewModel Open(LoadedDesign design)
    {
        var model = new LevelsViewModel(design);
        DataContext = model;
        return model;
    }

    /// <summary>
    /// Follows the open level, so the scroll request reaches whichever panel is current.
    /// </summary>
    /// <remarks>
    /// The panel is replaced whenever the list selection changes, so the handler has to move with
    /// it. Unsubscribing from the old one matters: a panel that is still subscribed keeps scrolling
    /// a map that is no longer showing it, and holds the level's cells alive.
    /// </remarks>
    private void Rewire()
    {
        if (watched is not null)
        {
            watched.ScrollRequested -= OnScrollRequested;
            watched = null;
        }

        if (DataContext is not LevelsViewModel model)
        {
            return;
        }

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LevelsViewModel.Panel)
                              or nameof(LevelsViewModel.SelectedLevel))
            {
                Follow(model.Panel);
            }
        };

        Follow(model.Panel);
    }

    private void Follow(LevelPanelViewModel? panel)
    {
        if (ReferenceEquals(watched, panel))
        {
            return;
        }

        if (watched is not null)
        {
            watched.ScrollRequested -= OnScrollRequested;
        }

        watched = panel;

        if (watched is not null)
        {
            watched.ScrollRequested += OnScrollRequested;
        }
    }

    private void OnScrollRequested(object? sender, MapPoint point) =>
        this.FindControl<LevelMapView>("MapView")?.ScrollToShow(point.X, point.Y);
}
