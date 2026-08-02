using UAF.Media;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers measuring and slicing combat icon sheets
/// (<c>determineIconSize</c>, <c>Combatant.cpp:8579</c>).
/// </summary>
public class CombatIconTests
{
    private static Surface Sheet(int width, int height)
    {
        var s = new Surface(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                s[x, y] = 0xFF808080;
            }
        }
        return s;
    }

    [Theory]
    // Two poses of one square: the commonest shape by far.
    [InlineData(96, 48, 2, 1, 1)]
    // Two poses of two squares -- SomethingWild's Tiger.
    [InlineData(192, 48, 2, 2, 1)]
    // Tall rather than wide -- a Hill Giant.
    [InlineData(96, 96, 2, 1, 2)]
    // A Red Dragon: eight squares across and four down, well past the 4x4 a comment claims.
    [InlineData(768, 192, 2, 8, 4)]
    public void A_footprint_is_measured_off_the_sheet(int width, int height, int frames,
                                                     int expectedWide, int expectedHigh)
    {
        var icon = CombatIcons.SizeOf(Sheet(width, height), frames);

        Assert.Equal(expectedWide, icon.Width);
        Assert.Equal(expectedHigh, icon.Height);
    }

    [Fact]
    public void More_frames_divide_the_width_further()
    {
        // The sheet holds NumFrames poses side by side, so four frames of a one-square monster is
        // the same width as two frames of a two-square one.
        Assert.Equal(2, CombatIcons.SizeOf(Sheet(192, 48), frames: 2).Width);
        Assert.Equal(1, CombatIcons.SizeOf(Sheet(192, 48), frames: 4).Width);
    }

    [Fact]
    public void A_sheet_too_small_to_measure_still_gives_one_square()
    {
        // Both axes floor at 1: a monster with no footprint could not be placed at all.
        var icon = CombatIcons.SizeOf(Sheet(10, 10), frames: 2);

        Assert.Equal(1, icon.Width);
        Assert.Equal(1, icon.Height);
    }

    [Fact]
    public void A_frame_count_of_zero_does_not_divide_by_zero()
    {
        // The reference would; a malformed record should not take the design down.
        Assert.Equal(1, CombatIcons.SizeOf(Sheet(96, 48), frames: 0).Width);
    }

    [Fact]
    public void The_two_poses_sit_side_by_side()
    {
        var sheet = Sheet(192, 48);
        var icon = CombatIcons.SizeOf(sheet, frames: 2);      // 2x1

        var ready = CombatIcons.PoseRect(sheet, icon);
        var attacking = CombatIcons.PoseRect(sheet, icon, attacking: true);

        Assert.Equal(new SurfaceRect(0, 0, 96, 48), ready);
        Assert.Equal(new SurfaceRect(96, 0, 192, 48), attacking);
    }

    [Fact]
    public void A_pose_is_the_footprints_own_size_in_pixels()
    {
        var sheet = Sheet(768, 192);
        var icon = CombatIcons.SizeOf(sheet, frames: 2);       // 8x4

        var rect = CombatIcons.PoseRect(sheet, icon);
        Assert.Equal(8 * CombatMap.TileWidth, rect.Width);
        Assert.Equal(4 * CombatMap.TileHeight, rect.Height);
    }

    [Fact]
    public void An_icon_index_past_the_end_of_the_sheet_rewinds_to_the_first()
    {
        var sheet = Sheet(192, 48);
        var icon = CombatIcons.SizeOf(sheet, frames: 2);       // 2x1, one pair on the sheet

        // Index 2 would start at 192, off the end.
        Assert.Equal(new SurfaceRect(0, 0, 96, 48), CombatIcons.PoseRect(sheet, icon, iconIndex: 2));
        Assert.Equal(new SurfaceRect(0, 0, 96, 48), CombatIcons.PoseRect(sheet, icon, iconIndex: 9));
    }

    [Fact]
    public void An_icon_index_within_the_sheet_selects_its_own_pair()
    {
        // Four pairs of a one-square monster: 8 tile-widths.
        var sheet = Sheet(8 * 48, 48);
        var icon = new CombatantIcon(1, 1);

        Assert.Equal(new SurfaceRect(0, 0, 48, 48), CombatIcons.PoseRect(sheet, icon, iconIndex: 1));
        Assert.Equal(new SurfaceRect(96, 0, 144, 48),
                     CombatIcons.PoseRect(sheet, icon, iconIndex: 2));
    }

    [Fact]
    public void Loading_returns_null_when_the_design_ships_no_art()
    {
        Assert.Null(CombatIcons.Load("", 2, _ => null));
        Assert.Null(CombatIcons.Load("missing.png", 2, _ => null));
    }

    [Fact]
    public void Loading_measures_what_it_found()
    {
        var sheet = Sheet(192, 48);
        var loaded = CombatIcons.Load("icon_Tiger.png", 2, _ => sheet);

        Assert.NotNull(loaded);
        Assert.Same(sheet, loaded.Value.Sheet);
        Assert.Equal(new CombatantIcon(2, 1), loaded.Value.Icon);
    }

    [Fact]
    public void Real_monster_icons_measure_to_sensible_footprints()
    {
        string? root = ReferenceDesign();
        if (root is null)
        {
            return;
        }

        using var design = LoadedDesign.Open(root);
        if (design.Monsters is null)
        {
            return;
        }

        // No decoder in this fixture, so art will not load -- what is checked is that every
        // monster names an icon and a frame count the measurement can use.
        int named = design.Monsters.Count(m => m.Icon is { FileName.Length: > 0 });
        Assert.True(named > 0, "no monster in the database names an icon");
        Assert.All(design.Monsters.Where(m => m.Icon is { FileName.Length: > 0 }),
                   m => Assert.True(m.Icon!.NumFrames >= 2,
                                    $"{m.Name} declares {m.Icon.NumFrames} frames; " +
                                    "the reference requires at least one pose pair"));
    }

    private static string? ReferenceDesign()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        string design = Path.Combine(dir.FullName, "reference", "SomethingWild.dsn");
        return Directory.Exists(design) ? design : null;
    }
}
