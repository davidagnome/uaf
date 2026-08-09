using UAF.Import.Frua;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Reading a DOS FRUA level header (<c>ImportGeoDatFile</c>, <c>UAFWinEd/UAImport.cpp:4827</c>).
/// </summary>
public class FruaLevelTests
{
    private static string? Heirs()
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

        string design = Path.Combine(dir.FullName, "reference", "Unlimited Adventures -ENG",
                                     "DESIGNS", "UA", "HEIRS.DSN");
        return Directory.Exists(design) ? design : null;
    }

    private static byte[] Synthetic()
    {
        var b = new byte[FruaLevel.Length];
        b[26] = 19;                        // height
        b[27] = 20;                        // width
        b[28] = 5; b[29] = 8; b[30] = 1;   // wall slots
        b[31] = 1;                         // mapping allowed
        b[32] = 8; b[33] = 1; b[34] = 36; b[35] = 3;
        b[36] = 1; b[37] = 2;

        // entry point 0: y=7, x=13, facing south, then the discarded byte
        b[38] = 7; b[39] = 13; b[40] = 4; b[41] = 0xEE;

        // rest event 0: every 30, event 9, chance 25, resting FORBIDDEN (high bit set)
        b[70] = 30; b[71] = 0xEE; b[72] = 9; b[73] = 0x80 | 25;

        // step event 0: every 4 steps, event 12, zones 0 and 1 excluded
        b[102] = 4; b[103] = 0xEE; b[104] = 12; b[105] = 0b0000_0011;

        FruaGameData.TextEncoding.GetBytes("TEST LEVEL").CopyTo(b, 142);
        return b;
    }

    [Fact]
    public void The_header_reads_field_by_field()
    {
        var level = FruaLevel.Read(Synthetic());

        Assert.Equal(20, level.Width);
        Assert.Equal(19, level.Height);
        Assert.Equal("TEST LEVEL", level.Name);
        Assert.True(level.AllowMapping);
        Assert.False(level.IsOverland);
        Assert.Equal([5, 8, 1], level.WallSlots);
        Assert.Equal([8, 1, 36, 3], level.BackdropSlots);
        Assert.Equal(1, level.DungeonCombatArt);
        Assert.Equal(2, level.WildernessCombatArt);
    }

    /// <summary>
    /// Entry points are four bytes apart, and x/y are stored in that order reversed.
    /// </summary>
    [Fact]
    public void An_entry_point_stores_y_before_x()
    {
        var level = FruaLevel.Read(Synthetic());

        Assert.Equal(new FruaEntryPoint(13, 7, FruaFacing.South), level.EntryPoints[0]);
        Assert.Equal(8, level.EntryPoints.Count);
    }

    /// <summary>The high bit of a rest event's chance byte <i>forbids</i> resting.</summary>
    [Fact]
    public void A_set_high_bit_forbids_resting()
    {
        var rest = FruaLevel.Read(Synthetic()).RestEvents[0];

        Assert.Equal(30, rest.EveryMinutes);
        Assert.Equal(9, rest.EventIndex);
        Assert.Equal(25, rest.Chance);
        Assert.False(rest.AllowResting);
    }

    /// <summary>A step event's zone byte lists the zones to <i>exclude</i>.</summary>
    [Fact]
    public void A_step_events_zone_byte_is_an_exclusion_mask()
    {
        var step = FruaLevel.Read(Synthetic()).StepEvents[0];

        Assert.Equal(4, step.StepCount);
        Assert.Equal(12, step.EventIndex);
        Assert.Equal(0b1111_1100, step.ZoneMask);   // 0 and 1 excluded
    }

    /// <summary>Zero means every zone, not "no zones".</summary>
    [Fact]
    public void A_zero_zone_byte_means_every_zone()
    {
        var b = Synthetic();
        b[105] = 0;

        Assert.Equal(0xFF, FruaLevel.Read(b).StepEvents[0].ZoneMask);
    }

    [Fact]
    public void Three_wall_slots_of_255_mean_an_overland_level()
    {
        var b = Synthetic();
        b[28] = b[29] = b[30] = 255;

        Assert.True(FruaLevel.Read(b).IsOverland);
    }

    /// <summary>An odd facing has no case in the reference's switch.</summary>
    [Fact]
    public void An_unmapped_facing_is_not_guessed_at()
    {
        var b = Synthetic();
        b[40] = 3;

        Assert.Equal(FruaFacing.Unknown, FruaLevel.Read(b).EntryPoints[0].Facing);
    }

    [Fact]
    public void A_short_file_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => FruaLevel.Read(new byte[500]));
    }

    // ---- the real DOS levels ---------------------------------------------------------------

    /// <summary>
    /// Every level of <c>HEIRS.DSN</c>, read as the reference reads it.
    /// </summary>
    /// <remarks>
    /// <b>The level name is the assertion that matters.</b> It sits at offset 142, after four
    /// variable-stride tables, so a single wrong stride anywhere before it turns every name to
    /// noise. Real names across 26 levels is the cheapest proof the whole header is right — and it
    /// is proof a synthetic fixture could not give, since a fixture built from my reading would
    /// agree with my reading.
    /// </remarks>
    [Fact]
    public void Every_level_of_heirs_reads_with_a_real_name()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        var levels = FruaLevel.ReadAll(design);

        Assert.Equal(26, levels.Count);
        Assert.Equal("DRAGONJAW MTS.", levels[1].Name);
        Assert.Equal("SKULL CRAG TOWN", levels[6].Name);
        Assert.Equal("DRAGON CAVE", levels[12].Name);

        foreach (var (number, level) in levels)
        {
            Assert.False(string.IsNullOrWhiteSpace(level.Name), $"level {number} has no name");
            Assert.InRange(level.Width, 1, 255);
            Assert.InRange(level.Height, 1, 255);
            Assert.Equal(8, level.EntryPoints.Count);
        }
    }

    /// <summary>
    /// The first four levels are overland and the dungeons are not.
    /// </summary>
    [Fact]
    public void The_overland_levels_are_the_ones_with_no_walls()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        var levels = FruaLevel.ReadAll(design);

        foreach (int n in new[] { 1, 2, 3, 4 })
        {
            Assert.True(levels[n].IsOverland, $"level {n} should be overland");
            Assert.False(levels[n].AllowMapping, $"level {n} should not allow mapping");
            Assert.Equal(38, levels[n].Width);
            Assert.Equal(15, levels[n].Height);
        }

        Assert.False(levels[5].IsOverland);
        Assert.True(levels[5].AllowMapping);
        Assert.Equal([5, 8, 1], levels[5].WallSlots);
    }

    /// <summary>Level numbering has gaps, and they are not errors.</summary>
    [Fact]
    public void A_missing_level_is_absent_rather_than_failing()
    {
        if (Heirs() is not { } design)
        {
            return;
        }

        Assert.Null(FruaLevel.ReadFile(design, 14));    // HEIRS skips 14, 16, 20-24, 26-32
        Assert.NotNull(FruaLevel.ReadFile(design, 15));
        Assert.Null(FruaLevel.ReadFile(design, 40 + 1));
    }
}
