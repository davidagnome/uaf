using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UAFedit.Databases;

/// <summary>
/// The item database editor's view. No code of its own: everything the screen does lives in
/// <see cref="ItemDatabaseViewModel"/>, which is what lets the whole editor be tested headlessly.
/// </summary>
public partial class ItemDatabaseView : UserControl
{
    public ItemDatabaseView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
