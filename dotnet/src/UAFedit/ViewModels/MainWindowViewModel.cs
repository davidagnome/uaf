using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAFcore;

namespace UAFedit.ViewModels;

/// <summary>
/// The shell: a design, its navigation tree, and the record list of whatever is selected.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opening is <see cref="Open"/>, a plain method, and the command is a wrapper round it.</b>
/// That is what lets the whole inspector be tested against a real design with no window, no
/// display and no Avalonia application at all — the same separation that made
/// <see cref="LoadedDesign"/> testable where <c>OpenDesign</c> was not (docs/PORTING-PLAN.md §7
/// Phase 0). The two things the view must supply, a folder picker and a way to close, are
/// delegates the view sets.
/// </para>
/// </remarks>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string BaseTitle = "Dungeon Craft Editor";

    private LoadedDesign? design;

    /// <summary>The tree's roots — one design at a time, so exactly one until it opens another.</summary>
    public ObservableCollection<DesignNodeViewModel> Roots { get; } = [];

    /// <summary>
    /// Asks the user for a design directory, or null if they cancelled.
    /// </summary>
    /// <remarks>
    /// Set by the view, because a folder picker needs a <c>TopLevel</c>. Left null in tests, which
    /// call <see cref="Open"/> directly.
    /// </remarks>
    public Func<Task<string?>>? ChooseFolder { get; set; }

    /// <summary>What <c>File &gt; Exit</c> does. Set by the view; closing a window is its business.</summary>
    public Action? RequestExit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Columns))]
    [NotifyPropertyChangedFor(nameof(Rows))]
    private DesignNodeViewModel? selectedNode;

    [ObservableProperty]
    private string status = "No design open. File > Open Design… to browse one.";

    /// <summary>The columns of the selected category, or none.</summary>
    public IReadOnlyList<RecordColumn> Columns => SelectedNode?.Table.Columns ?? [];

    /// <summary>
    /// The rows of the selected category, built on this read.
    /// </summary>
    /// <remarks>
    /// Reading this is what materialises a category — see <see cref="RecordTable"/>. Selecting a
    /// node is therefore the work, not opening the design.
    /// </remarks>
    public IReadOnlyList<RecordRow> Rows => SelectedNode?.Table.Rows ?? [];

    public string Title => design is null ? BaseTitle : $"{BaseTitle} — {DesignName}";

    private string DesignName { get; set; } = string.Empty;

    /// <summary>
    /// Opens a design directory and rebuilds the tree, answering whether it opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both decoders are left null.</b> <see cref="LoadedDesign.Open"/> takes an optional image
    /// decoder and font rasteriser; the inspector draws no art and needs no text measured, and
    /// passing null costs it nothing — <c>ImageLoader</c> substitutes a null decoder and
    /// <see cref="LoadedDesign.Font"/> answers null, which is the same degradation contract the
    /// engine accepts for a design whose art is missing. The editor stays free of SDL as a result,
    /// which is the point of <c>UAFcore.App</c> being a separate project.
    /// </para>
    /// <para>
    /// A directory that is not a design is an ordinary outcome — a user picking the wrong folder —
    /// so the failure is a status line rather than a throw. The previously open design is kept
    /// until the new one has actually loaded.
    /// </para>
    /// </remarks>
    public bool Open(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        LoadedDesign opened;
        try
        {
            opened = LoadedDesign.Open(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                    or InvalidDataException or EndOfStreamException
                                    or NotSupportedException or InvalidOperationException
                                    or ArgumentException)
        {
            Status = $"Could not open '{root}': {e.Message}";
            return false;
        }

        design?.Dispose();
        design = opened;

        DesignName = string.IsNullOrWhiteSpace(opened.Name)
            ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar,
                                            Path.AltDirectorySeparatorChar))
            : opened.Name;

        var categories = DesignInspector.Categories(opened);

        SelectedNode = null;
        Roots.Clear();
        Roots.Add(DesignNodeViewModel.Root(DesignName, categories));

        // The first category rather than the design's own node: the root has no records, and a
        // right pane that opens empty reads as a failure to load.
        SelectedNode = categories.Count > 0 ? categories[0] : Roots[0];

        Status = $"{DesignName} — version {opened.Globals.Version.Value.ToString("0.####")}, "
               + $"{categories.Count(c => c.IsReadable)} of {categories.Count} categories readable";

        OnPropertyChanged(nameof(Title));
        return true;
    }

    [RelayCommand]
    private async Task OpenDesignAsync()
    {
        if (ChooseFolder is null)
        {
            Status = "No folder picker is available.";
            return;
        }

        if (await ChooseFolder().ConfigureAwait(true) is { } root)
        {
            Open(root);
        }
    }

    [RelayCommand]
    private void Exit() => RequestExit?.Invoke();

    public void Dispose()
    {
        design?.Dispose();
        design = null;
    }
}
