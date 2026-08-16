using System.Globalization;
using Avalonia.Data.Converters;

namespace UAFedit.Events;

/// <summary>
/// The two value conversions the event pane needs.
/// </summary>
/// <remarks>
/// Kept to two on purpose. Anything a view model can answer directly it does — <c>IsBroken</c>,
/// <c>CanFollow</c>, <c>Summary</c> are properties, not converters — because a property is
/// testable without a display and a converter is not. What is left is presentation with no
/// meaning: how faded a greyed row is, and how tall a multi-line box is.
/// </remarks>
public static class EventConverters
{
    /// <summary>Full opacity when a field is relevant, faded when the trigger ignores it.</summary>
    public static readonly IValueConverter Dim =
        new FuncValueConverter<bool, double>(relevant => relevant ? 1.0 : 0.4);

    /// <summary>A paragraph box gets room for several lines; a single-line one does not.</summary>
    public static readonly IValueConverter BoxHeight =
        new FuncValueConverter<bool, double>(multiline => multiline ? 72 : double.NaN);

    /// <summary>Unused placeholder guard — keeps the culture parameter honest if one is added.</summary>
    internal static CultureInfo Culture => CultureInfo.InvariantCulture;
}
