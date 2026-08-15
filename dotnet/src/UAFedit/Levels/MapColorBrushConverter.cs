using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using UAFedit.Map;

namespace UAFedit.Levels;

/// <summary>
/// Turns a <see cref="MapColor"/> into a brush, for the swatch columns.
/// </summary>
/// <remarks>
/// <para>
/// A converter rather than a brush property on the view models, so that the wall-slot and zone
/// tables stay free of Avalonia and a headless test can build them. <see cref="LevelMapView"/> keeps
/// its own cache for the same conversion; this one is separate because the two are on opposite sides
/// of the "may not be modified" line and because a table shows a few dozen swatches where the map
/// draws thousands of marks.
/// </para>
/// <para>
/// Brushes are cached by packed colour. The editor's palette is 192 entries, so the cache is bounded
/// by construction and never needs eviction.
/// </para>
/// </remarks>
public sealed class MapColorBrushConverter : IValueConverter
{
    private readonly Dictionary<int, ImmutableSolidColorBrush> cache = [];

    /// <summary>The instance the XAML binds to.</summary>
    public static MapColorBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MapColor color)
        {
            return Brushes.Transparent;
        }

        if (cache.TryGetValue(color.Packed, out var cached))
        {
            return cached;
        }

        var brush = new ImmutableSolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        cache[color.Packed] = brush;
        return brush;
    }

    /// <summary>Not supported: a swatch is never edited by dragging a brush back out of it.</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter,
                               CultureInfo culture) =>
        throw new NotSupportedException();
}
