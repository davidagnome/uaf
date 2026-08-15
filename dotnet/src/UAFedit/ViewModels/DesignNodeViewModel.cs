using CommunityToolkit.Mvvm.ComponentModel;

namespace UAFedit.ViewModels;

/// <summary>
/// One node of the navigation tree: the design itself, or one category within it.
/// </summary>
/// <remarks>
/// <b>A category that could not be read is a third state, not an empty one.</b> Every database
/// property on <see cref="UAFcore.LoadedDesign"/> answers null when the file is missing or is a
/// shape this port refuses, and an empty collection when it read a file that holds no records. The
/// two look identical in a tree that only shows a count, and telling them apart is most of what a
/// design inspector is for — a design whose <c>races.dat</c> is <c>RaceV1</c> is not a design with
/// no races.
/// </remarks>
public sealed partial class DesignNodeViewModel : ObservableObject
{
    private DesignNodeViewModel(string name, string label, bool isReadable, RecordTable table,
                                IReadOnlyList<DesignNodeViewModel> children)
    {
        Name = name;
        Label = label;
        IsReadable = isReadable;
        Table = table;
        Children = children;
    }

    /// <summary>The design's own node, which holds the categories and no records.</summary>
    public static DesignNodeViewModel Root(string name,
                                           IReadOnlyList<DesignNodeViewModel> children) =>
        new(name, name, isReadable: true, RecordTable.Empty, children);

    /// <summary>A category the design has, whether or not it holds any records.</summary>
    public static DesignNodeViewModel Category(string name, int count, RecordTable table) =>
        new(name, $"{name} ({count})", isReadable: true, table, []);

    /// <summary>A category whose file is missing, or is a shape this port declines to read.</summary>
    public static DesignNodeViewModel Unreadable(string name) =>
        new(name, $"{name} (not readable)", isReadable: false, RecordTable.Empty, []);

    public string Name { get; }

    /// <summary>What the tree shows: the name, with its record count or its refusal.</summary>
    public string Label { get; }

    /// <summary>False when the underlying database came back null rather than empty.</summary>
    public bool IsReadable { get; }

    public RecordTable Table { get; }

    public IReadOnlyList<DesignNodeViewModel> Children { get; }

    /// <summary>
    /// Bound two-way so the design's node opens itself when a design is loaded.
    /// </summary>
    [ObservableProperty]
    private bool isExpanded = true;
}
