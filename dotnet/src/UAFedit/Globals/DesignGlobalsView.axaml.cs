using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UAFedit.Globals;

/// <summary>
/// The design's own settings — the <c>Design</c> menu, as a pane.
/// </summary>
/// <remarks>
/// <b>No view model of its own.</b> The shell builds a <see cref="DesignGlobalsViewModel"/> from
/// the open design and sets it as the <c>DataContext</c>; nothing here reads a file or holds design
/// state, which is what keeps the whole editor testable without an application running.
/// </remarks>
public partial class DesignGlobalsView : UserControl
{
    public DesignGlobalsView() => AvaloniaXamlLoader.Load(this);
}
