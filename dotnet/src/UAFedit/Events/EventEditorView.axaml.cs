using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UAFedit.Events;

/// <summary>
/// The event editor's control.
/// </summary>
/// <remarks>
/// Code-behind exists only to load the XAML. Everything the pane does lives in
/// <see cref="EventEditorViewModel"/>, which is what lets the whole editor be driven headlessly in
/// tests — selection, editing and chain navigation included.
/// </remarks>
public partial class EventEditorView : UserControl
{
    public EventEditorView() => AvaloniaXamlLoader.Load(this);
}
