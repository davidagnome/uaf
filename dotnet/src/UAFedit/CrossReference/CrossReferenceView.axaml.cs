using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UAFedit.CrossReference;

/// <summary>
/// The cross-reference pane — the reference's <c>Tools &gt; Cross Reference</c>.
/// </summary>
/// <remarks>
/// <b>No view model of its own.</b> The shell builds a <see cref="CrossReferenceViewModel"/> from
/// the open design and sets it as the <c>DataContext</c>; nothing here reads a file or holds design
/// state, which is what keeps the whole editor testable without an application running.
/// </remarks>
public partial class CrossReferenceView : UserControl
{
    public CrossReferenceView() => AvaloniaXamlLoader.Load(this);
}
