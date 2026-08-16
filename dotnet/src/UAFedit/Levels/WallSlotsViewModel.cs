using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UAF.Serialization;
using UAFcore;
using UAFedit.Map;

namespace UAFedit.Levels;

/// <summary>
/// One wall-set slot of the level's table (<c>WallSetSlotMemType</c>, <c>PicSlot.cpp:503</c>).
/// </summary>
/// <remarks>
/// <para>
/// The fields <c>CEditWallSlots</c> edits, one slot at a time, across its twelve tabs of sixteen.
/// </para>
/// <para>
/// <b>The wall table belongs to the level, not to the design.</b> Every <c>.lvl</c> carries its own
/// 192 slots, so slot 5 on level 1 and slot 5 on level 2 are unrelated art. This is easy to lose:
/// the dialog reads a file-scope <c>WallSets[]</c>, which is the <i>current</i> level's table
/// swapped in by <c>LEVEL::GetSlots</c>.
/// </para>
/// </remarks>
public sealed partial class WallSlotViewModel : ObservableObject
{
    internal WallSlotViewModel(int index, WallSetSlot slot, MapColor color, bool colorConfigured,
                               int usageCount)
    {
        Index = index;
        Slot = slot;
        Color = color;
        IsColorConfigured = colorConfigured;
        UsageCount = usageCount;
    }

    /// <summary>
    /// The slot's index, which is the number a cell's <c>wall[]</c> byte stores.
    /// </summary>
    /// <remarks>
    /// <b>Zero-based, and the dialog's tab labels are not.</b> The tabs read "Walls 1-16",
    /// "Walls 17-32" (<c>EditWallSlots.cpp:328</c>) while the slots they hold are 0–15 and 16–31 —
    /// <c>m_Slot = i + (baseSlot*16)</c> (<c>EditWallSlots.cpp:481</c>). So the label is one ahead
    /// of everything else in the editor, including the map's own wall indices. This exposes the
    /// index, and <see cref="TabLabel"/> reproduces the original's off-by-one wording where it is
    /// wanted.
    /// </remarks>
    public int Index { get; }

    /// <summary>The slot as read.</summary>
    public WallSetSlot Slot { get; }

    /// <summary>The colour the 2-D map draws this slot's walls in.</summary>
    public MapColor Color { get; }

    /// <summary>
    /// Whether the design's config actually declared a colour for this slot.
    /// </summary>
    /// <remarks>
    /// False means the map draws this slot's walls black on black — see <see cref="MapPalette"/>.
    /// It is the single most useful thing this table can say about a high slot, and it is invisible
    /// in the original editor by construction.
    /// </remarks>
    public bool IsColorConfigured { get; }

    /// <summary>How many cell sides of this level reference the slot.</summary>
    /// <remarks>
    /// <para>
    /// The question <c>CheckLevelForWallSlot</c> (<c>Level.cpp:3582</c>) is asked before deleting a
    /// slot. <b>The reference's implementation is wrong and this one is not.</b> It opens with
    /// <c>if (!globalData.levelInfo.stats[slot].used) return FALSE;</c> — subscripting the
    /// <i>level</i> table with a <i>wall slot</i> number — and then bounds its scan of the current
    /// level's grid by <c>stats[slot]</c>'s width and height. So asking about wall slot 5 consults
    /// level 6's used flag and scans level 6's rectangle over level 1's cells: for a slot at or
    /// above the design's level count it always answers "not used", and below it, it searches the
    /// wrong shape.
    /// </para>
    /// <para>
    /// Reproducing that would mean an editor that offers to delete a slot the level is covered in.
    /// The count is over the level's own extent, and every side of every cell.
    /// </para>
    /// </remarks>
    public int UsageCount { get; }

    /// <summary>Whether the level places this slot anywhere.</summary>
    public bool IsUsedByLevel => UsageCount > 0;

    /// <summary>
    /// <c>used</c> as the slot itself records it.
    /// </summary>
    /// <remarks>
    /// Not the same question as <see cref="IsUsedByLevel"/>: this is a flag on the slot record and
    /// says the author filled the slot in, not that any wall was drawn with it.
    /// </remarks>
    public bool IsMarkedUsed => Slot.Used != 0;

    /// <summary>Whether the slot names any art at all.</summary>
    /// <remarks>
    /// <b>Slot 0 is empty by construction</b> — <c>LoadSlot</c> opens with "leave slot 0 blank
    /// (=black, color 0)" and calls <c>Clear()</c> on it (<c>EditWallSlots.cpp:369</c>), and every
    /// tool that places a wall treats 0 as "no wall". So a table with slot 0 filled in is a design
    /// that will never draw it.
    /// </remarks>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Slot.WallFile)
        && string.IsNullOrWhiteSpace(Slot.DoorFile)
        && string.IsNullOrWhiteSpace(Slot.OverlayFile);

    /// <summary>Whether the map leaves a gap in this slot's walls for a door.</summary>
    /// <remarks>See <see cref="LevelMapCell.HasDoorGap"/> — door art is the entire representation.</remarks>
    public bool HasDoor => !string.IsNullOrWhiteSpace(Slot.DoorFile);

    public string WallFile => Slot.WallFile;

    public string DoorFile => Slot.DoorFile;

    public string OverlayFile => Slot.OverlayFile;

    public string SoundFile => Slot.SoundFile;

    public string AreaViewFile => Slot.AreaViewFile;

    /// <summary>Whether the door is drawn in front of the wall rather than behind it.</summary>
    public bool DoorFirst => Slot.DoorFirst != 0;

    public bool DrawAreaView => Slot.DrawAreaView != 0;

    /// <summary>The spell, or hook script name, that opens this slot's spell-locked doors.</summary>
    /// <remarks>
    /// It is a <c>SPELL_ID</c> — a name, not an index — and the editor's help text
    /// (<c>EditWallSlots.cpp:568</c>) explains that a name matching no spell is taken as the name of
    /// a Special Ability hook script instead. So an id that resolves to nothing is not necessarily
    /// broken.
    /// </remarks>
    public string UnlockSpellId => Slot.UnlockSpellId;

    public bool BlendOverlay => Slot.BlendOverlay != 0;

    public int BlendAmount => Slot.BlendAmount;

    /// <summary>The tab this slot appears on in the original dialog.</summary>
    public int Tab => Index / WallSlotsViewModel.SlotsPerTab;

    /// <inheritdoc cref="Index"/>
    public string TabLabel
    {
        get
        {
            int start = (Tab * WallSlotsViewModel.SlotsPerTab) + 1;
            return $"Walls {start}-{start + WallSlotsViewModel.SlotsPerTab - 1}";
        }
    }
}

/// <summary>
/// A level's wall-set table (<c>ID_VIEW_WALLSETS</c>, <c>MainFrm.cpp:627</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>CEditWallSlots</c> is a picture grid: sixteen thumbnails a tab, a colour swatch and a
/// blockage swatch under each, and the fields of whichever one is selected along the side. Its two
/// outputs are <c>currWallSlot</c> and <c>currBlockage</c> — the wall-painting tool's state — which
/// <c>OnViewWallsets</c> copies out on OK and nothing else in the dialog affects.
/// </para>
/// <para>
/// This is the table as a list, with no art: the editor has no image decoder wired in (see
/// <c>MainWindowViewModel.Open</c>), so a thumbnail grid would be 192 empty boxes. What it can say
/// instead is what the original could not — which slots the level actually places, and which ones
/// the palette draws invisibly.
/// </para>
/// <para>
/// <b>The table is 192 long whatever the design uses.</b> <c>MAX_WALLSETS</c>
/// (<c>Externs.h:863</c>) is written out in full by every level of every shipped design; there is no
/// short form.
/// </para>
/// </remarks>
public sealed partial class WallSlotsViewModel : ObservableObject
{
    /// <summary><c>MAX_WALLSETS</c> (<c>Externs.h:863</c>).</summary>
    public const int MaxSlots = 192;

    /// <summary>Slots per tab in the original dialog (<c>EditWallSlots.cpp:325</c>).</summary>
    public const int SlotsPerTab = 16;

    /// <summary>The index that means "no wall" and is never drawn.</summary>
    public const int NoWall = MapPalette.NoWall;

    /// <summary>Builds the table over a level, counting usage against its grid.</summary>
    /// <param name="model">
    /// The level's map model, which supplies both the wall table and the cells the usage counts are
    /// taken over. Overrides are read through it as well, so a model with
    /// <see cref="LevelMapModel.ShowOverrides"/> on counts the walls a script would show.
    /// </param>
    /// <param name="palette">The design's editor colours.</param>
    /// <param name="currentSlot"><c>currWallSlot</c> — the slot the wall tool would paint.</param>
    /// <param name="currentBlockage"><c>currBlockage</c> — the blockage it would paint with.</param>
    public WallSlotsViewModel(LevelMapModel model, MapPalette? palette = null,
                              int currentSlot = 0,
                              BlockageType currentBlockage = BlockageType.Open)
    {
        ArgumentNullException.ThrowIfNull(model);

        var colors = palette ?? MapPalette.Default;
        var sets = model.WallSets;
        var usage = CountUsage(model, sets.Count);

        Slots =
        [
            .. sets.Select((slot, index) => new WallSlotViewModel(
                index, slot, colors.Wall(index), colors.IsConfigured(index), usage[index])),
        ];

        Tabs =
        [
            .. Slots.Select(s => s.TabLabel).Distinct(),
        ];

        selectedIndex = Math.Clamp(currentSlot, 0, Math.Max(Slots.Count - 1, 0));
        blockage = currentBlockage;
        Palette = colors;
    }

    /// <summary>
    /// The sixteen blockages, for a picker to bind to.
    /// </summary>
    /// <remarks>
    /// The dialog offers four radio buttons — Open, Open&#160;Secret, Blocked, False&#160;Door — plus
    /// twelve more that the generated resource keeps in the same group
    /// (<c>DDX_Radio(pDX, IDC_OPEN_BLK, m_Obstruction)</c>, <c>EditWallSlots.cpp:120</c>), and the
    /// ordinal is written straight into the cell. So the list is the whole enum, in value order.
    /// </remarks>
    public static IReadOnlyList<BlockageType> Blockages { get; } = Enum.GetValues<BlockageType>();

    /// <summary>Every slot of the level's table.</summary>
    public ObservableCollection<WallSlotViewModel> Slots { get; }

    /// <summary>The original's tab captions, in order.</summary>
    public IReadOnlyList<string> Tabs { get; }

    /// <summary>The palette the swatches come from.</summary>
    public MapPalette Palette { get; }

    /// <summary><c>currWallSlot</c>: what the wall tool paints.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected))]
    private int selectedIndex;

    /// <summary>
    /// <c>currBlockage</c>: what the blockage tool paints.
    /// </summary>
    /// <remarks>
    /// <b>Forced to <see cref="BlockageType.Open"/> when slot 0 is selected</b> —
    /// <c>if (m_Slot == 0) m_Obstruction = OpenBlk;</c> (<c>EditWallSlots.cpp:516</c>). There is no
    /// wall to obstruct, and letting the two disagree is how a level ends up with a blockage on a
    /// side that draws nothing.
    /// </remarks>
    [ObservableProperty]
    private BlockageType blockage;

    /// <summary>Whether only slots the level places are listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    private bool showUsedOnly;

    private IReadOnlyList<WallSlotViewModel>? visible;

    /// <summary>
    /// The slot the tool would paint with.
    /// </summary>
    /// <remarks>
    /// <b>This, not <see cref="SelectedIndex"/>, is what a list binds its selection to.</b> With
    /// <see cref="ShowUsedOnly"/> on, a row's position in <see cref="Visible"/> is not its slot
    /// number — a list bound by index would set the wall tool to slot 3 for the fourth row shown,
    /// whatever slot that row actually is.
    /// </remarks>
    public WallSlotViewModel? Selected
    {
        get => SelectedIndex >= 0 && SelectedIndex < Slots.Count ? Slots[SelectedIndex] : null;
        set => SelectedIndex = value?.Index ?? -1;
    }

    /// <summary>
    /// The rows the list shows, honouring <see cref="ShowUsedOnly"/>.
    /// </summary>
    /// <remarks>
    /// Cached, because an <c>ItemsSource</c> that answered a fresh list on every read would rebuild
    /// the list and drop its selection on any unrelated notification. The cache is dropped whenever
    /// the filter or the selection changes, which are the only two things it depends on.
    /// </remarks>
    public IReadOnlyList<WallSlotViewModel> Visible =>
        visible ??= ShowUsedOnly
            ? [.. Slots.Where(s => s.IsUsedByLevel || s.Index == SelectedIndex)]
            : Slots;

    /// <summary>How many slots the level actually places walls from.</summary>
    public int UsedSlotCount => Slots.Count(s => s.IsUsedByLevel);

    /// <summary>
    /// How many of those the palette has no colour for, and so draws black on black.
    /// </summary>
    /// <remarks>
    /// Non-zero on <c>Case</c>, whose first level places walls from 44 slots above 15.
    /// </remarks>
    public int InvisibleSlotCount => Slots.Count(s => s.IsUsedByLevel && !s.IsColorConfigured);

    partial void OnSelectedIndexChanged(int value)
    {
        if (value == NoWall)
        {
            Blockage = BlockageType.Open;
        }

        // Only when the filter is on: with it off the list is every slot and cannot change, and
        // rebuilding it on every selection would drop the list's own selection each time.
        if (ShowUsedOnly)
        {
            visible = null;
            OnPropertyChanged(nameof(Visible));
        }
    }

    partial void OnShowUsedOnlyChanged(bool value) => visible = null;

    /// <summary>
    /// How many cell sides reference each slot, over the level's whole grid.
    /// </summary>
    /// <remarks>
    /// A slot index beyond the table is counted nowhere rather than throwing: the format allows a
    /// cell to name a slot the level does not carry, and the engine clamps such a wall to nothing
    /// (<c>Level.cpp:1822</c>) while the editor's map draws it in whatever palette slot it lands on.
    /// </remarks>
    private static int[] CountUsage(LevelMapModel model, int slots)
    {
        var counts = new int[Math.Max(slots, 1)];

        for (int y = 0; y < model.Height; y++)
        {
            for (int x = 0; x < model.Width; x++)
            {
                var cell = model.At(x, y);
                foreach (var facing in LevelMapPainter.DrawOrder)
                {
                    int index = cell.Side(facing).WallIndex;
                    if (index > NoWall && index < counts.Length)
                    {
                        counts[index]++;
                    }
                }
            }
        }

        return counts;
    }
}
