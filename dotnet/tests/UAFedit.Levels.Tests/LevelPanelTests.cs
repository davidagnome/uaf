using UAFcore;
using UAFedit.Map;

namespace UAFedit.Levels.Tests;

/// <summary>
/// A level open in the panel: the map, and the tables that hang off it.
/// </summary>
/// <remarks>
/// The panel is where the numbering has to be right twice over — the grid is fetched by position
/// and the stats by number-minus-one — so most of what is asserted here is that the two halves
/// belong to the same level.
/// </remarks>
public class LevelPanelTests
{
    /// <summary>
    /// The premise: a panel over a real level has a map with walls on it.
    /// </summary>
    [Fact]
    public void The_corpus_panel_really_opened()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);
        var panel = levels.Panel;

        Assert.NotNull(panel);
        Assert.True(panel!.HasMap);
        Assert.NotNull(panel.WallSlots);
        Assert.NotNull(panel.Zones);
        Assert.Equal(EntryPointsViewModel.Count, panel.EntryPoints.Points.Count);

        var model = panel.Model!;
        Assert.True(model.Width > 0 && model.Height > 0);
        Assert.NotEmpty(model.WallSets);
        Assert.True(panel.WallSlots!.UsedSlotCount > 0, "the level places no walls at all");
    }

    /// <summary>
    /// <b>Case's tenth file opens as level 255 with level 255's stats.</b>
    /// </summary>
    /// <remarks>
    /// The grid comes from position 9 and the name, extent and entry points from <c>stats[254]</c>.
    /// Getting either half from the other's number gives a panel that draws one level and describes
    /// another, and on this design it gives a panel that describes nothing at all.
    /// </remarks>
    [Fact]
    public void Cases_last_file_opens_as_level_255()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);

        levels.SelectByNumber(255);
        var panel = levels.Panel!;

        Assert.Equal(255, panel.Entry.Number);
        Assert.Equal(254, panel.Entry.StatsIndex);
        Assert.Equal(9, panel.Entry.Position);
        Assert.Equal("Level255.lvl", panel.Entry.FileName);

        // Name and extent from the stats...
        Assert.Equal("Reeftest", panel.Entry.Name);
        Assert.Contains("Level 255", panel.Title);
        Assert.Contains("Reeftest", panel.Title);
        Assert.Contains("stats[254]", panel.Subtitle);

        // ...and the same extent from the grid, which is the half that came in by position.
        Assert.Equal(10, panel.Model!.Width);
        Assert.Equal(10, panel.Model.Height);
    }

    /// <summary>
    /// The panel pairs a mis-stamped file with the stats its <i>name</i> points at.
    /// </summary>
    /// <remarks>
    /// <c>Case</c>'s <c>Level004.lvl</c> records <c>m_level</c> 11. Trusting that would open it as
    /// <c>Dartcave_Amis</c>; the file name says level 4, so it is
    /// <c>Dartcave_Amis_SafetyCopy20160512</c>. The two are near-identical copies, which is exactly
    /// why the mistake would go unnoticed on this design.
    /// </remarks>
    [Fact]
    public void A_mis_stamped_file_is_paired_by_its_name()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);

        levels.SelectByNumber(4);
        var panel = levels.Panel!;

        Assert.Equal("Dartcave_Amis_SafetyCopy20160512", panel.Entry.Name);
        Assert.NotEqual("Dartcave_Amis", panel.Entry.Name);

        // The disagreement is surfaced rather than swallowed -- and the panel notices it from the
        // file it opened, even though this catalog was built without reading any.
        Assert.Null(panel.Entry.StoredNumber);
        Assert.Equal(12, panel.StoredNumber);
        Assert.False(panel.AgreesWithFileName);
        Assert.Contains("records itself as level 12", panel.Subtitle);

        // Every other Case level agrees with its own name, so the flag is not simply always set.
        levels.SelectByNumber(255);
        Assert.True(levels.Panel!.AgreesWithFileName);
    }

    /// <summary>The party's start square is drawn on one level and no other.</summary>
    /// <remarks>
    /// <c>globalData.startLevel</c> is a stats index, so it is compared with
    /// <see cref="LevelCatalogEntry.StatsIndex"/> and not with the position or the number.
    /// </remarks>
    [Fact]
    public void The_start_square_appears_only_on_the_start_level()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);
        int startIndex = design.Globals.StartLevel;

        int withStart = 0;

        foreach (var entry in levels.Levels)
        {
            var panel = new LevelPanelViewModel(design, entry);

            if (entry.StatsIndex == startIndex)
            {
                Assert.Equal(((int)design.Globals.StartX, (int)design.Globals.StartY),
                             panel.Model!.StartLocation);
                withStart++;
            }
            else
            {
                Assert.Null(panel.Model!.StartLocation);
            }
        }

        Assert.Equal(1, withStart);
    }

    /// <summary>Selecting a square describes it, and the description follows the side.</summary>
    [Fact]
    public void The_selection_description_reads_the_square_the_map_selected()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);
        levels.SelectByNumber(2);                       // 'Sigil', 44x34 and heavily walled
        var panel = levels.Panel!;
        var model = panel.Model!;

        Assert.Equal(string.Empty, panel.SelectionDescription);

        // Find a side that actually carries a wall, so the description has something to say.
        (MapPoint Cell, Facing Side)? walled = null;
        for (int y = 0; y < model.Height && walled is null; y++)
        {
            for (int x = 0; x < model.Width && walled is null; x++)
            {
                foreach (var facing in LevelMapPainter.DrawOrder)
                {
                    if (model.At(x, y).Side(facing).WallIndex > MapPalette.NoWall)
                    {
                        walled = (new MapPoint(x, y), facing);
                        break;
                    }
                }
            }
        }

        Assert.NotNull(walled);

        panel.SelectedCell = walled!.Value.Cell;
        panel.SelectedSide = walled.Value.Side;

        string text = panel.SelectionDescription;
        Assert.Contains($"({walled.Value.Cell.X}, {walled.Value.Cell.Y})", text);
        Assert.Contains(walled.Value.Side.ToString(), text);
        Assert.Contains("wall ", text);
        Assert.Contains("zone ", text);
    }

    /// <summary>Asking for the entry point moves the selection and asks the view to scroll.</summary>
    /// <remarks>
    /// The scroll itself needs a viewport and so belongs to the control; what the view model owes is
    /// the request, and this is the only place its wiring is checked without a window.
    /// </remarks>
    [Fact]
    public void Showing_an_entry_point_selects_it_and_requests_a_scroll()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);
        levels.SelectByNumber(2);                       // 'Sigil': entry point 0 is (24, 17)
        var panel = levels.Panel!;

        var requested = new List<MapPoint>();
        panel.ScrollRequested += (_, point) => requested.Add(point);

        panel.ShowSelectedEntryPoint();

        var expected = panel.EntryPoints.Points[0];
        Assert.Equal(new MapPoint(expected.X, expected.Y), panel.SelectedCell);
        Assert.Equal(MapDisplayMode.EntryPoints, panel.Mode);
        Assert.Equal([new MapPoint(expected.X, expected.Y)], requested);
    }

    /// <summary>Overrides off gives back the same model rather than a copy.</summary>
    /// <remarks>
    /// The map view rebinds on every change of <see cref="LevelPanelViewModel.EffectiveModel"/>, so
    /// handing back an equal-but-different model would redraw the whole map on any unrelated
    /// notification.
    /// </remarks>
    [Fact]
    public void The_effective_model_is_the_model_until_overrides_are_turned_on()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);
        var panel = levels.Panel!;

        Assert.Same(panel.Model, panel.EffectiveModel);

        panel.ShowOverrides = true;
        Assert.NotSame(panel.Model, panel.EffectiveModel);
        Assert.True(panel.EffectiveModel!.ShowOverrides);

        // And it is the same object on every read: the map rebinds on identity, so a getter that
        // built a fresh model each time would redraw the level on any unrelated notification.
        Assert.Same(panel.EffectiveModel, panel.EffectiveModel);

        // Same grid either way -- the override tables are what change, and this design has none.
        Assert.Equal(panel.Model!.Width, panel.EffectiveModel.Width);
        Assert.False(panel.HasOverrides);
    }

    /// <summary>Moving through the list builds a new panel, and reading it twice does not.</summary>
    [Fact]
    public void The_panel_follows_the_selection_and_is_cached()
    {
        if (Corpus.Open(Corpus.Case) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);

        var first = levels.Panel;
        Assert.Same(first, levels.Panel);

        levels.SelectByNumber(18);
        var second = levels.Panel;

        Assert.NotSame(first, second);
        Assert.Equal(18, second!.Entry.Number);
        Assert.Same(second, levels.Panel);
    }

    /// <summary>The list opens on the design's own start level.</summary>
    [Fact]
    public void The_list_opens_on_the_start_level()
    {
        if (Corpus.Open(Corpus.SomethingWild) is not { } design)
        {
            return;
        }

        using var _ = design;
        var levels = new LevelsViewModel(design, readFiles: false);

        Assert.NotNull(levels.SelectedLevel);
        Assert.Equal(design.Globals.StartLevel, levels.SelectedLevel!.StatsIndex);
        Assert.Contains($"Level {levels.SelectedLevel.Number}", levels.Status);
        Assert.Contains($"position {levels.SelectedLevel.Position}", levels.Status);
    }
}
