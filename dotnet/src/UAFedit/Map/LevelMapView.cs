using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using UAFcore;
using SurfaceRect = UAF.Media.SurfaceRect;

namespace UAFedit.Map;

/// <summary>
/// The editor's 2-D level map: a scrollable, zoomable grid of cells with their walls, doors and
/// blockages.
/// </summary>
/// <remarks>
/// <para>
/// The Avalonia replacement for <c>CDlgPartialMapPicture</c> hosted in <c>CUAFWinEdView</c>
/// (<c>UAFWinEd/DlgPicture.cpp</c>, <c>UAFWinEd/UAFWinEdView.cpp</c>). Deliberately thin: what to
/// draw and where comes from <see cref="LevelMapPainter"/> and <see cref="LevelMapLayout"/>, both
/// of which are ordinary classes a test can drive without a window. This file owns the parts that
/// genuinely need one — brushes, pointer and key handling, and the redraw.
/// </para>
/// <para>
/// <b>It scrolls itself rather than sitting in a <c>ScrollViewer</c>.</b> The map is a torus and has
/// no extent to scroll within; a scroll viewer would need a content size, and any size it was given
/// would be a lie. So <see cref="ScrollX"/> and <see cref="ScrollY"/> are properties of the view,
/// the wheel and the keyboard drive them, and <see cref="MeasureOverride"/> takes whatever room it
/// is offered.
/// </para>
/// <para>
/// The original's editing modes — placing walls, dragging blockages, the undo stack
/// (<c>UAFWinEdView.cpp:2431</c>) — are not here. This is the view; a tool layer above it can turn
/// <see cref="SelectedCell"/> and <see cref="SelectedSide"/> into edits.
/// </para>
/// </remarks>
public class LevelMapView : Control
{
    private readonly Dictionary<int, ImmutableSolidColorBrush> brushes = [];

    private bool panning;
    private Point panOrigin;
    private double panScrollX;
    private double panScrollY;

    static LevelMapView()
    {
        AffectsRender<LevelMapView>(
            ModelProperty, ModeProperty, PaletteProperty, GeometryProperty,
            ZoomProperty, ScrollXProperty, ScrollYProperty, TileProperty,
            SelectedCellProperty, SelectedSideProperty);
    }

    public LevelMapView()
    {
        // The cells are 16 pixels of hand-placed 2-pixel dashes. Antialiasing them turns the grid
        // dots into grey smudges at any zoom that is not a whole number.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>The level being shown. Null draws nothing.</summary>
    public static readonly StyledProperty<LevelMapModel?> ModelProperty =
        AvaloniaProperty.Register<LevelMapView, LevelMapModel?>(nameof(Model));

    /// <inheritdoc cref="ModelProperty"/>
    public LevelMapModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>What the centre of each cell shows.</summary>
    public static readonly StyledProperty<MapDisplayMode> ModeProperty =
        AvaloniaProperty.Register<LevelMapView, MapDisplayMode>(nameof(Mode));

    /// <inheritdoc cref="ModeProperty"/>
    public MapDisplayMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>The design's editor colours. Defaults to the built-in sixteen.</summary>
    public static readonly StyledProperty<MapPalette> PaletteProperty =
        AvaloniaProperty.Register<LevelMapView, MapPalette>(nameof(Palette), MapPalette.Default);

    /// <inheritdoc cref="PaletteProperty"/>
    public MapPalette Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>The design's cell layout. Defaults to the built-in 16-pixel square.</summary>
    public static readonly StyledProperty<MapCellGeometry> GeometryProperty =
        AvaloniaProperty.Register<LevelMapView, MapCellGeometry>(
            nameof(Geometry), MapCellGeometry.Default);

    /// <inheritdoc cref="GeometryProperty"/>
    public MapCellGeometry Geometry
    {
        get => GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    /// <summary>Screen pixels per geometry pixel, clamped to the layout's range.</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<LevelMapView, double>(
            nameof(Zoom), 2.0, defaultBindingMode: BindingMode.TwoWay);

    /// <inheritdoc cref="ZoomProperty"/>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, LevelMapLayout.MinZoom,
                                                 LevelMapLayout.MaxZoom));
    }

    /// <summary>Leftmost visible column, in cells.</summary>
    public static readonly StyledProperty<double> ScrollXProperty =
        AvaloniaProperty.Register<LevelMapView, double>(
            nameof(ScrollX), defaultBindingMode: BindingMode.TwoWay);

    /// <inheritdoc cref="ScrollXProperty"/>
    public double ScrollX
    {
        get => GetValue(ScrollXProperty);
        set => SetValue(ScrollXProperty, value);
    }

    /// <summary>Topmost visible row, in cells.</summary>
    public static readonly StyledProperty<double> ScrollYProperty =
        AvaloniaProperty.Register<LevelMapView, double>(
            nameof(ScrollY), defaultBindingMode: BindingMode.TwoWay);

    /// <inheritdoc cref="ScrollYProperty"/>
    public double ScrollY
    {
        get => GetValue(ScrollYProperty);
        set => SetValue(ScrollYProperty, value);
    }

    /// <summary>Whether the level repeats past its edges (<see cref="LevelMapLayout.Tile"/>).</summary>
    public static readonly StyledProperty<bool> TileProperty =
        AvaloniaProperty.Register<LevelMapView, bool>(nameof(Tile), true);

    /// <inheritdoc cref="TileProperty"/>
    public bool Tile
    {
        get => GetValue(TileProperty);
        set => SetValue(TileProperty, value);
    }

    /// <summary>The selected square, or null. Two-way by default — this is the view's output.</summary>
    public static readonly StyledProperty<MapPoint?> SelectedCellProperty =
        AvaloniaProperty.Register<LevelMapView, MapPoint?>(
            nameof(SelectedCell), defaultBindingMode: BindingMode.TwoWay);

    /// <inheritdoc cref="SelectedCellProperty"/>
    public MapPoint? SelectedCell
    {
        get => GetValue(SelectedCellProperty);
        set => SetValue(SelectedCellProperty, value);
    }

    /// <summary>
    /// Which side of the selected square a click landed on.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SelectedCell"/> because the editor's wall tools need the side and
    /// its event tools do not, and because a click that does not move the selection can still
    /// change the side.
    /// </remarks>
    public static readonly StyledProperty<Facing> SelectedSideProperty =
        AvaloniaProperty.Register<LevelMapView, Facing>(
            nameof(SelectedSide), defaultBindingMode: BindingMode.TwoWay);

    /// <inheritdoc cref="SelectedSideProperty"/>
    public Facing SelectedSide
    {
        get => GetValue(SelectedSideProperty);
        set => SetValue(SelectedSideProperty, value);
    }

    /// <summary>
    /// Whether a click picks the side nearest the point rather than keeping the current one.
    /// </summary>
    /// <remarks>
    /// <c>m_KwikKlik</c> (<c>UAFWinEd.cpp:62</c>), on by default there and here. With it off the
    /// original returns <c>currFacing</c> from <c>MousePointToWall</c> and the click position picks
    /// only the square.
    /// </remarks>
    public static readonly StyledProperty<bool> KwikKlikProperty =
        AvaloniaProperty.Register<LevelMapView, bool>(nameof(KwikKlik), true);

    /// <inheritdoc cref="KwikKlikProperty"/>
    public bool KwikKlik
    {
        get => GetValue(KwikKlikProperty);
        set => SetValue(KwikKlikProperty, value);
    }

    /// <summary>The layout the view is currently drawing through.</summary>
    /// <remarks>
    /// Rebuilt on every access rather than cached, because every input to it is a styled property
    /// and a cache would need invalidating from eight places to save building a record.
    /// </remarks>
    public LevelMapLayout Layout =>
        Model is { } model
            ? new LevelMapLayout(model.Width, model.Height, Geometry)
            {
                Zoom = Zoom,
                ScrollX = ScrollX,
                ScrollY = ScrollY,
                Tile = Tile,
            }
            : new LevelMapLayout(0, 0, Geometry) { Zoom = Zoom, Tile = Tile };

    /// <summary>The painter the view is currently drawing through.</summary>
    public LevelMapPainter Painter => new(Geometry, Palette) { Mode = Mode };

    /// <summary>Scrolls so a square is visible, moving as little as possible.</summary>
    public void ScrollToShow(int x, int y)
    {
        var moved = Layout.ScrollToShow(x, y, Bounds.Width, Bounds.Height);
        ScrollX = moved.ScrollX;
        ScrollY = moved.ScrollY;
    }

    /// <summary>
    /// Takes whatever room it is offered.
    /// </summary>
    /// <remarks>
    /// A map has no natural size — it is a window onto a torus. When the parent offers infinity
    /// (a <c>StackPanel</c>, say) the level's own extent is the only sensible answer, so that is
    /// what is given.
    /// </remarks>
    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = Layout;

        double width = double.IsInfinity(availableSize.Width)
            ? layout.ContentWidth
            : availableSize.Width;

        double height = double.IsInfinity(availableSize.Height)
            ? layout.ContentHeight
            : availableSize.Height;

        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);

        // The area outside the level, when tiling is off, is the same black an empty cell is --
        // the original clears its whole surface to it (DlgPicture.cpp:914).
        context.FillRectangle(Brush(Palette.Backdrop(LevelMapPainter.EmptyCellColor)), bounds);

        if (Model is not { } model || model.Width <= 0 || model.Height <= 0)
        {
            return;
        }

        var layout = Layout;
        var painter = Painter;
        double zoom = layout.Zoom;
        var wallSets = model.WallSets;

        foreach (var visible in layout.Visible(bounds.Width, bounds.Height))
        {
            var cell = model.At(visible.X, visible.Y);

            foreach (var mark in painter.Marks(cell, wallSets))
            {
                var rect = Place(mark.Rect, visible.Left, visible.Top, zoom);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    context.FillRectangle(Brush(mark.Color), rect);
                }
            }
        }

        DrawSelection(context, layout, painter);
    }

    /// <summary>
    /// Outlines the selected square and marks the side the tools would edit.
    /// </summary>
    /// <remarks>
    /// The original blits a directional arrow from the editor's map art. This draws the same
    /// rectangle (<see cref="LevelMapPainter.SelectionMark"/>) as a triangle, and adds an outline
    /// the original has no equivalent of — it never needed one, because the arrow was the only
    /// thing on the map that was not a coloured box.
    /// </remarks>
    private void DrawSelection(DrawingContext context, LevelMapLayout layout,
                               LevelMapPainter painter)
    {
        if (SelectedCell is not { } selected)
        {
            return;
        }

        var brush = Brush(Palette.Backdrop(LevelMapPainter.MarkerColor));
        double zoom = layout.Zoom;

        // Every copy of the square the torus puts on screen, not just the first: with tiling on a
        // narrow level shows the selected cell several times and highlighting one of them would
        // look like a bug.
        foreach (var visible in layout.Visible(Bounds.Width, Bounds.Height))
        {
            if (visible.X != selected.X || visible.Y != selected.Y)
            {
                continue;
            }

            var cell = new Rect(visible.Left, visible.Top,
                                layout.CellWidth, layout.CellHeight);

            context.DrawRectangle(null, new Pen(brush, Math.Max(1, zoom / 2)), cell.Deflate(0.5));

            var arrow = Place(painter.SelectionMark().Rect, visible.Left, visible.Top, zoom);
            context.DrawGeometry(brush, null, ArrowGeometry(arrow, SelectedSide));
        }
    }

    /// <summary>A triangle filling <paramref name="rect"/> and pointing at <paramref name="side"/>.</summary>
    private static StreamGeometry ArrowGeometry(Rect rect, Facing side)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var (tip, left, right) = ((int)side & 3) switch
            {
                0 => (new Point(rect.Center.X, rect.Top),
                      new Point(rect.Left, rect.Bottom),
                      new Point(rect.Right, rect.Bottom)),
                1 => (new Point(rect.Right, rect.Center.Y),
                      new Point(rect.Left, rect.Top),
                      new Point(rect.Left, rect.Bottom)),
                2 => (new Point(rect.Center.X, rect.Bottom),
                      new Point(rect.Right, rect.Top),
                      new Point(rect.Left, rect.Top)),
                _ => (new Point(rect.Left, rect.Center.Y),
                      new Point(rect.Right, rect.Bottom),
                      new Point(rect.Right, rect.Top)),
            };

            context.BeginFigure(tip, isFilled: true);
            context.LineTo(left);
            context.LineTo(right);
            context.EndFigure(isClosed: true);
        }

        return geometry;
    }

    /// <summary>Places a geometry-space rectangle on screen at a cell's origin.</summary>
    private static Rect Place(SurfaceRect rect, double left, double top, double zoom) =>
        new(left + (rect.Left * zoom), top + (rect.Top * zoom),
            rect.Width * zoom, rect.Height * zoom);

    private ImmutableSolidColorBrush Brush(MapColor color)
    {
        if (brushes.TryGetValue(color.Packed, out var cached))
        {
            return cached;
        }

        var brush = new ImmutableSolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brushes[color.Packed] = brush;
        return brush;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Model is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var position = point.Position;

        if (point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            panning = true;
            panOrigin = position;
            panScrollX = ScrollX;
            panScrollY = ScrollY;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        Select(Layout.HitTest(position.X, position.Y));
        e.Handled = true;
    }

    /// <summary>Applies a hit, honouring <see cref="KwikKlik"/>.</summary>
    private void Select(MapHit hit)
    {
        SelectedCell = new MapPoint(hit.X, hit.Y);

        if (KwikKlik)
        {
            SelectedSide = hit.Side;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!panning)
        {
            return;
        }

        var layout = Layout;
        var position = e.GetPosition(this);

        var (x, y) = layout.ClampScroll(
            panScrollX - ((position.X - panOrigin.X) / layout.CellWidth),
            panScrollY - ((position.Y - panOrigin.Y) / layout.CellHeight),
            Bounds.Width, Bounds.Height);

        ScrollX = x;
        ScrollY = y;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (panning)
        {
            panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Wheel scrolls, control-wheel zooms about the pointer.
    /// </summary>
    /// <remarks>
    /// Zooming about the pointer rather than the centre means the square under the cursor stays
    /// still, which is the only way a 200×200 level is navigable. The scroll position is solved for
    /// afterwards rather than adjusted, because <see cref="Zoom"/> is clamped and an adjustment
    /// computed from the requested zoom would drift at the limits.
    /// </remarks>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var layout = Layout;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            var position = e.GetPosition(this);
            double anchorX = layout.ScrollX + (position.X / layout.CellWidth);
            double anchorY = layout.ScrollY + (position.Y / layout.CellHeight);

            Zoom *= Math.Pow(1.2, e.Delta.Y);

            var zoomed = Layout;
            var (x, y) = zoomed.ClampScroll(anchorX - (position.X / zoomed.CellWidth),
                                            anchorY - (position.Y / zoomed.CellHeight),
                                            Bounds.Width, Bounds.Height);
            ScrollX = x;
            ScrollY = y;
        }
        else
        {
            var (x, y) = layout.ClampScroll(
                layout.ScrollX - (e.Delta.X * LevelMapLayout.LineSize),
                layout.ScrollY - (e.Delta.Y * LevelMapLayout.LineSize),
                Bounds.Width, Bounds.Height);
            ScrollX = x;
            ScrollY = y;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Arrows move the selection, page keys scroll, +/- zoom.
    /// </summary>
    /// <remarks>
    /// The arrows move the <i>selection</i> and drag the view after it, which is what the original
    /// does with the party marker (<c>CUAFWinEdView::MoveTo</c>, <c>UAFWinEdView.cpp:2194</c>) —
    /// there is no way to scroll the map without moving the cursor there, and the scrollbars are
    /// the only alternative.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (Model is not { } model)
        {
            return;
        }

        var layout = Layout;

        switch (e.Key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down:
                {
                    var (dx, dy) = e.Key switch
                    {
                        Key.Left => (-1, 0),
                        Key.Right => (1, 0),
                        Key.Up => (0, -1),
                        _ => (0, 1),
                    };

                    var from = SelectedCell ?? new MapPoint(0, 0);
                    var (x, y) = model.Wrap(from.X + dx, from.Y + dy);
                    SelectedCell = new MapPoint(x, y);
                    ScrollToShow(x, y);
                    e.Handled = true;
                    break;
                }

            case Key.PageUp or Key.PageDown:
                {
                    double delta = e.Key == Key.PageUp ? -LevelMapLayout.PageSize
                                                       : LevelMapLayout.PageSize;
                    var (x, y) = layout.ClampScroll(layout.ScrollX, layout.ScrollY + delta,
                                                    Bounds.Width, Bounds.Height);
                    ScrollX = x;
                    ScrollY = y;
                    e.Handled = true;
                    break;
                }

            case Key.OemPlus or Key.Add:
                Zoom *= 1.25;
                e.Handled = true;
                break;

            case Key.OemMinus or Key.Subtract:
                Zoom /= 1.25;
                e.Handled = true;
                break;
        }
    }
}
