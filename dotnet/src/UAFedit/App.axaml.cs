using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UAFedit.Views;

namespace UAFedit;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The first non-flag argument is a design to open on launch, mirroring the player.
            string? startup = null;
            foreach (string arg in desktop.Args ?? [])
            {
                if (!string.IsNullOrWhiteSpace(arg) && !arg.StartsWith('-'))
                {
                    startup = arg;
                    break;
                }
            }

            desktop.MainWindow = new MainWindow(startup);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
