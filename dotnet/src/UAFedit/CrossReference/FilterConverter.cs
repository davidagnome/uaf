using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace UAFedit.CrossReference;

/// <summary>
/// Binds one radio button to one value of <see cref="CrossReferenceFilter"/>.
/// </summary>
/// <remarks>
/// <b>Three radio buttons over one enum is the shape XAML has no built-in answer for.</b> The
/// alternative — three booleans on the view model kept mutually exclusive by hand — is three
/// properties and a rule nothing enforces. Converting back only for the checked button is what
/// stops the group writing twice on every click, once for the button being cleared.
/// </remarks>
public sealed class FilterConverter : IValueConverter
{
    public static FilterConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter,
                          CultureInfo culture) =>
        value is CrossReferenceFilter current
        && parameter is string name
        && Enum.TryParse(name, out CrossReferenceFilter wanted)
        && current == wanted;

    public object ConvertBack(object? value, Type targetType, object? parameter,
                              CultureInfo culture)
    {
        // Only the button that just became checked has anything to say; the one being cleared
        // would otherwise write its own value back a moment later.
        if (value is true && parameter is string name
            && Enum.TryParse(name, out CrossReferenceFilter wanted))
        {
            return wanted;
        }

        return BindingOperations.DoNothing;
    }
}
