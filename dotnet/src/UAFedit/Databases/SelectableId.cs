using CommunityToolkit.Mvvm.ComponentModel;

namespace UAFedit.Databases;

/// <summary>One tickable row of an id list — a baseclass an item may be used by.</summary>
/// <remarks>
/// <b><see cref="IsKnown"/> is the point of the type.</b> An item can name a baseclass the design
/// no longer defines, and the MFC "Baseclass List" dialog built its list from the class database
/// alone, so such an id was invisible there and vanished the moment the dialog was OK'd. Here the
/// dangling id is listed and ticked, flagged as unknown, and survives an edit that never touches
/// it.
/// </remarks>
public sealed partial class SelectableId : ObservableObject
{
    public SelectableId(string id, bool isSelected, bool isKnown)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        this.isSelected = isSelected;
        IsKnown = isKnown;
    }

    public string Id { get; }

    /// <summary>False when the design defines no such record — a dangling reference.</summary>
    public bool IsKnown { get; }

    public string Label => IsKnown ? Id : $"{Id} (not in this design)";

    [ObservableProperty]
    private bool isSelected;
}
