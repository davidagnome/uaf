using UAFcore;
using UAFedit.Map;

namespace UAFedit.Levels.Tests;

/// <summary>
/// The level's two fixed tables: 192 wall sets and 16 zones.
/// </summary>
public class WallSlotsAndZonesTests
{
    /// <summary>A panel over the first readable level of a corpus design, or null.</summary>
    private static (LoadedDesign Design, LevelPanelViewModel Panel)? Panel(
        string name, int? number = null)
    {
        if (Corpus.Open(name) is not { } design)
        {
            return null;
        }

        var levels = new LevelsViewModel(design, readFiles: false);

        if (number is { } wanted)
        {
            levels.SelectByNumber(wanted);
        }

        if (levels.Panel is { HasMap: true } panel)
        {
            return (design, panel);
        }

        design.Dispose();
        return null;
    }

    /// <summary>The premise: a real level's tables are full-size and not empty.</summary>
    [Fact]
    public void The_corpus_tables_really_loaded()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var walls = open.Panel.WallSlots!;
        var zones = open.Panel.Zones!;

        Assert.Equal(WallSlotsViewModel.MaxSlots, walls.Slots.Count);
        Assert.Equal(ZonesViewModel.MaxZones, zones.Zones.Count);
        Assert.True(walls.UsedSlotCount > 1, "the level draws from only one wall set");
        Assert.True(zones.UsedZoneCount >= 1);
        Assert.Contains(walls.Slots, s => s.HasDoor);
    }

    /// <summary>
    /// Slot 0 is the empty one, and selecting it forces the blockage back to Open.
    /// </summary>
    /// <remarks>
    /// <c>if (m_Slot == 0) m_Obstruction = OpenBlk;</c> (<c>EditWallSlots.cpp:516</c>). Without it
    /// the tool paints a blockage on a side that draws no wall, which the map shows as a floating
    /// mark and the engine honours as an invisible barrier.
    /// </remarks>
    [Fact]
    public void Slot_zero_is_empty_and_forces_the_blockage_open()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var walls = open.Panel.WallSlots!;

        Assert.True(walls.Slots[0].IsEmpty);
        Assert.Equal(0, walls.Slots[0].UsageCount);

        walls.SelectedIndex = 3;
        walls.Blockage = BlockageType.LockedWizard;
        Assert.Equal(BlockageType.LockedWizard, walls.Blockage);

        walls.SelectedIndex = WallSlotsViewModel.NoWall;
        Assert.Equal(BlockageType.Open, walls.Blockage);
    }

    /// <summary>
    /// The usage counts are the level's own walls, counted once each.
    /// </summary>
    /// <remarks>
    /// The reference's <c>CheckLevelForWallSlot</c> cannot be compared against, because it
    /// subscripts the <i>level</i> table with a <i>wall slot</i> number
    /// (<c>Level.cpp:3584</c>) — see <see cref="WallSlotViewModel.UsageCount"/>. So the check here
    /// is against the map itself: every wall the painter would draw is counted exactly once.
    /// </remarks>
    [Fact]
    public void The_usage_counts_add_up_to_the_levels_walls()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var model = open.Panel.Model!;
        var walls = open.Panel.WallSlots!;

        int drawn = 0;
        for (int y = 0; y < model.Height; y++)
        {
            for (int x = 0; x < model.Width; x++)
            {
                var cell = model.At(x, y);
                foreach (var facing in LevelMapPainter.DrawOrder)
                {
                    if (cell.Side(facing).WallIndex > MapPalette.NoWall)
                    {
                        drawn++;
                    }
                }
            }
        }

        Assert.True(drawn > 0);
        Assert.Equal(drawn, walls.Slots.Sum(s => s.UsageCount));
        Assert.Equal(walls.Slots.Count(s => s.UsageCount > 0), walls.UsedSlotCount);
    }

    /// <summary>Filtering to used slots keeps the selection visible even when it is unused.</summary>
    /// <remarks>
    /// A list that dropped the selected row would leave the wall tool set to a slot the user cannot
    /// see, which is worse than an extra row.
    /// </remarks>
    [Fact]
    public void The_used_only_filter_keeps_the_selected_slot()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var walls = open.Panel.WallSlots!;

        walls.ShowUsedOnly = true;
        Assert.All(walls.Visible, s => Assert.True(s.IsUsedByLevel || s.Index == walls.SelectedIndex));
        Assert.True(walls.Visible.Count < walls.Slots.Count);
        Assert.Contains(walls.Visible, s => s.Index == walls.SelectedIndex);

        walls.ShowUsedOnly = false;
        Assert.Equal(walls.Slots.Count, walls.Visible.Count);
    }

    /// <summary>
    /// A filtered list selects by slot, not by row.
    /// </summary>
    /// <remarks>
    /// The whole reason <see cref="WallSlotsViewModel.Selected"/> is settable. With the filter on,
    /// the fourth row shown is not slot 3, so a list bound by index would arm the wall tool with a
    /// slot the user did not click.
    /// </remarks>
    [Fact]
    public void Selecting_a_filtered_row_selects_that_rows_slot()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var walls = open.Panel.WallSlots!;
        walls.ShowUsedOnly = true;

        // A row whose position in the filtered list is not its slot number -- which the filter
        // guarantees exists, since slot 0 is never used and so is never shown.
        var row = walls.Visible.First(s => s.Index != walls.Visible.ToList().IndexOf(s));

        walls.Selected = row;

        Assert.Equal(row.Index, walls.SelectedIndex);
        Assert.Same(row, walls.Selected);
    }

    /// <summary>The visible list is the same object until the filter or the selection moves it.</summary>
    /// <remarks>
    /// An <c>ItemsSource</c> that answered a fresh list on every read would rebuild the list — and
    /// drop its selection — on any unrelated notification.
    /// </remarks>
    [Fact]
    public void The_visible_list_is_stable_between_changes()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var walls = open.Panel.WallSlots!;

        Assert.Same(walls.Visible, walls.Visible);

        walls.ShowUsedOnly = true;
        var filtered = walls.Visible;
        Assert.Same(filtered, walls.Visible);
        Assert.NotSame(filtered, walls.Slots);

        walls.SelectedIndex = 7;
        Assert.NotSame(filtered, walls.Visible);
    }

    /// <summary>
    /// <c>Case</c> really does place walls the stock palette draws black on black.
    /// </summary>
    /// <remarks>
    /// The count is the editor's one useful answer about a high slot, and it is invisible in the
    /// original by construction — see <see cref="MapPalette"/>. The assertion is that it is
    /// non-zero, not what it is: the number depends on which of Case's levels is open.
    /// </remarks>
    [Fact]
    public void Case_places_walls_the_palette_leaves_black()
    {
        if (Panel(Corpus.Case, 1) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var walls = open.Panel.WallSlots!;

        Assert.True(walls.InvisibleSlotCount > 0,
                    "Case was chosen because it uses undeclared wall slots; it no longer does");
        Assert.True(walls.InvisibleSlotCount <= walls.UsedSlotCount);
        Assert.All(walls.Slots.Take(MapPalette.DeclaredSlots), s => Assert.True(s.IsColorConfigured));
        Assert.False(walls.Slots[MapPalette.DeclaredSlots].IsColorConfigured);
    }

    /// <summary>The tab captions are the original's, off-by-one wording and all.</summary>
    /// <remarks>
    /// "Walls 1-16" holds slots 0 to 15 (<c>EditWallSlots.cpp:328</c>). Reproduced rather than
    /// corrected so that a user following the original's documentation finds the slot they expect.
    /// </remarks>
    [Fact]
    public void The_tab_captions_count_from_one_where_the_slots_count_from_zero()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var walls = open.Panel.WallSlots!;

        Assert.Equal("Walls 1-16", walls.Slots[0].TabLabel);
        Assert.Equal("Walls 1-16", walls.Slots[15].TabLabel);
        Assert.Equal("Walls 17-32", walls.Slots[16].TabLabel);
        Assert.Equal(0, walls.Slots[15].Tab);
        Assert.Equal(1, walls.Slots[16].Tab);
        Assert.Equal(WallSlotsViewModel.MaxSlots / WallSlotsViewModel.SlotsPerTab,
                     walls.Tabs.Count);
    }

    /// <summary>Every square of the level is in exactly one zone, and the tallies say so.</summary>
    /// <remarks>
    /// A cell's zone is a byte with no bounds check anywhere in the format, so this is also the
    /// assertion that no corpus level carries a zone past the sixteen.
    /// </remarks>
    [Fact]
    public void Every_square_is_counted_in_exactly_one_zone()
    {
        foreach (string name in new[] { Corpus.SomethingWild, Corpus.Case })
        {
            if (Panel(name) is not { } open)
            {
                return;
            }

            using (open.Design)
            {
                var model = open.Panel.Model!;
                var zones = open.Panel.Zones!;

                Assert.Equal(model.Width * model.Height, zones.Zones.Sum(z => z.CellCount));
                Assert.True(zones.UsedZoneCount >= 1);
            }
        }
    }

    /// <summary>The zone's number is one ahead of its index, as the dialog's spinner is.</summary>
    [Fact]
    public void A_zones_number_is_one_ahead_of_the_byte_in_the_cell()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;
        var zones = open.Panel.Zones!;

        Assert.Equal(0, zones.Zones[0].Index);
        Assert.Equal(1, zones.Zones[0].Number);
        Assert.Equal(15, zones.Zones[^1].Index);
        Assert.Equal(16, zones.Zones[^1].Number);

        // Zone 0 is a real zone and is where a cleared cell lives, so it is normally the biggest.
        Assert.True(zones.Zones[0].CellCount > 0);
    }

    /// <summary>The zone's mapping rule reads back as one of the three the enum has.</summary>
    [Fact]
    public void Every_corpus_zone_has_a_known_mapping_rule()
    {
        if (Panel(Corpus.SomethingWild, 2) is not { } open)
        {
            return;
        }

        using var _ = open.Design;

        Assert.All(open.Panel.Zones!.Zones,
                   z => Assert.NotEqual("Unknown", z.MappingText));
    }
}
