using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UAFedit.Spells;

/// <summary>
/// The special-abilities editor — the <c>Database &gt; Edit Special Abilities</c> pane.
/// </summary>
/// <remarks>
/// As with <see cref="SpellDatabaseView"/>, the shell builds a
/// <see cref="SpecialAbilityDatabaseViewModel"/> from the open design and sets it as the
/// <c>DataContext</c>. Nothing here touches a file.
/// </remarks>
public partial class SpecialAbilityDatabaseView : UserControl
{
    public SpecialAbilityDatabaseView() => AvaloniaXamlLoader.Load(this);
}
