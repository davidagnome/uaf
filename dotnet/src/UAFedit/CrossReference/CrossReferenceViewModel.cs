using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UAFcore;

namespace UAFedit.CrossReference;

/// <summary>Which slice of the report the list is showing.</summary>
public enum CrossReferenceFilter
{
    /// <summary>Named by something and not on disk — a design that will fail in play.</summary>
    Missing,

    /// <summary>On disk and named by nothing — dead weight, not a defect.</summary>
    Unreferenced,

    /// <summary>Everything the sweep found, either way.</summary>
    Everything,
}

/// <summary>One row of the report.</summary>
public sealed record CrossReferenceRow(CrossReferenceEntry Entry)
{
    public string Kind => Entry.Kind.ToString();

    public string Name => Entry.Name;

    public string Exists => Entry.Exists ? "Yes" : "No";

    public int Count => Entry.References.Count;

    /// <summary>Who names it — all of them, since the count alone rarely answers the question.</summary>
    public string Referrers => Entry.References.Count == 0
        ? "(nothing)"
        : string.Join(Environment.NewLine, Entry.References.Select(r => r.ToString()));
}

/// <summary>
/// The cross-reference pane — the reference's <c>CrossReferenceDlg</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sweep is on demand, not on open.</b> It reads every level in the design to find what
/// the events name, which is the same cost the event editor pays — and unlike that pane, nobody
/// wants this one incidentally. The tab opens empty with a button.
/// </para>
/// <para>
/// The original's filters were six checkboxes combining reference count with existence
/// (<c>CrossReferenceDlg.cpp:56</c>). The two that answer a question are kept.
/// </para>
/// </remarks>
public sealed partial class CrossReferenceViewModel : ObservableObject
{
    private readonly LoadedDesign design;

    public CrossReferenceViewModel(LoadedDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        this.design = design;
    }

    /// <summary>The rows currently shown.</summary>
    public ObservableCollection<CrossReferenceRow> Rows { get; } = [];

    [ObservableProperty]
    private string status = "Nothing swept yet. Run it to see what this design names.";

    [ObservableProperty]
    private CrossReferenceFilter filter = CrossReferenceFilter.Missing;

    /// <summary>Whether a sweep has been run, so the filter buttons mean something.</summary>
    [ObservableProperty]
    private bool hasReport;

    private CrossReferenceReport? report;

    partial void OnFilterChanged(CrossReferenceFilter value)
    {
        _ = value;
        Refresh();
    }

    /// <summary>Walks the design and builds the report.</summary>
    /// <remarks>
    /// A design whose levels cannot all be read still produces a report — the sweep skips those
    /// levels rather than failing, so what it says is "these are the references I could see".
    /// </remarks>
    [RelayCommand]
    public void Run()
    {
        report = CrossReferenceBuilder.Build(design);
        HasReport = true;
        Refresh();
    }

    private void Refresh()
    {
        Rows.Clear();

        if (report is not { } built)
        {
            return;
        }

        var slice = Filter switch
        {
            CrossReferenceFilter.Missing => built.Missing,
            CrossReferenceFilter.Unreferenced => built.Unreferenced,
            _ => [.. built.Entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)],
        };

        foreach (var entry in slice)
        {
            Rows.Add(new CrossReferenceRow(entry));
        }

        Status = $"{built.Summary} — showing {Rows.Count}.";
    }
}
