using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using UAFedit.ViewModels;

namespace UAFedit.Views;

/// <summary>
/// The shell window.
/// </summary>
/// <remarks>
/// It owns the view model and supplies the two things a view model cannot have: the folder picker,
/// which needs a <c>TopLevel</c>, and closing the window. Everything else the window shows is
/// bound.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel model = new();

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        model.ChooseFolder = ChooseFolderAsync;
        model.RequestExit = Close;
        DataContext = model;
    }

    /// <remarks>
    /// <b>A design is a directory, not a file.</b> A <c>.dsn</c> is the folder holding <c>Data/</c>
    /// and <c>Resources/</c> — <c>SomethingWild.dsn</c> is a directory — so this is a folder picker
    /// and a file picker would find nothing to open.
    /// </remarks>
    private async Task<string?> ChooseFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Design",
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    /// <summary>Releases the open design, whose font rasteriser is disposable.</summary>
    protected override void OnClosed(EventArgs e)
    {
        model.Dispose();
        base.OnClosed(e);
    }
}
