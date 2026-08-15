using CommunityToolkit.Mvvm.ComponentModel;

namespace UAFedit.Databases;

/// <summary>
/// One checkbox over one bit of a packed <c>DWORD</c> — a monster's form, penalty, immunity or
/// misc-options flags.
/// </summary>
/// <remarks>
/// <para>
/// Reads and writes through the owning form rather than holding a copy, which is what keeps the
/// <b>bits nobody has a checkbox for</b> intact. That matters here: <c>FormUndead=32</c> is
/// commented out of <c>MonsterFormType</c> (<c>Monster.h:60</c>) and <c>IDC_MF_UNDEAD</c> survives
/// in <c>resource.h:886</c> with no control on the dialog, so a design that still sets bit 5 has a
/// value no editor can see. A form that rebuilt the word from its checkboxes would clear it.
/// </para>
/// <para>
/// <see cref="Description"/> carries the rules text from <c>Monster.h</c>'s block comments. The
/// labels alone are close to meaningless — "Large" means "use the large column of the damage
/// table", not "is physically big", and "Dwarf AC" is a bonus granted to the dwarf, not to this
/// monster.
/// </para>
/// </remarks>
public sealed class FlagBit : ObservableObject
{
    private readonly Func<uint> read;
    private readonly Action<uint> write;
    private readonly uint mask;

    public FlagBit(string label, uint mask, string description,
                   Func<uint> read, Action<uint> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);

        Label = label;
        Description = description;
        this.mask = mask;
        this.read = read;
        this.write = write;
    }

    public string Label { get; }

    /// <summary>What the flag actually does, for the tooltip.</summary>
    public string Description { get; }

    public bool IsSet
    {
        get => (read() & mask) != 0;
        set
        {
            if (value == IsSet)
            {
                return;
            }

            OnPropertyChanging();
            write(value ? read() | mask : read() & ~mask);
            OnPropertyChanged();
        }
    }

    /// <summary>Re-reads the underlying word — for when the whole record was reloaded.</summary>
    public void Refresh() => OnPropertyChanged(nameof(IsSet));
}
