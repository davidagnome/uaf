using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UAFedit.Databases;

/// <summary>
/// The monster database editor's view. As with the item view, all behaviour is in
/// <see cref="MonsterDatabaseViewModel"/>.
/// </summary>
public partial class MonsterDatabaseView : UserControl
{
    public MonsterDatabaseView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
