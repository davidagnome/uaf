using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UAFedit.Spells;

/// <summary>
/// The spell database editor — the <c>Database &gt; Edit Spells…</c> pane.
/// </summary>
/// <remarks>
/// <b>No view model of its own.</b> The shell builds a
/// <see cref="SpellDatabaseViewModel"/> from the open design and sets it as the
/// <c>DataContext</c>; nothing here reads a file or holds design state, which is what keeps the
/// whole editor testable without an application running.
/// </remarks>
public partial class SpellDatabaseView : UserControl
{
    public SpellDatabaseView() => AvaloniaXamlLoader.Load(this);
}
