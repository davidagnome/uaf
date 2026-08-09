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
        FruaGameData.TextEncoding.GetBytes("MAP ").CopyTo(b, 314);
        FruaGameData.TextEncoding.GetBytes("ENCR").CopyTo(b, 3778);
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

    /// <summary>A file whose markers have drifted is an error, not 576 cells of noise.</summary>
    [Fact]
    public void A_missing_marker_is_refused()
    {
        var b = Synthetic();
        b[314] = (byte)'X';

        var thrown = Assert.Throws<InvalidDataException>(() => FruaLevel.Read(b));
        Assert.Contains("MAP", thrown.Message, StringComparison.Ordinal);
    }

    // ---- map cells --------------------------------------------------------------------------

    /// <summary>Puts one cell into a synthetic level at (x, y), row-major by width.</summary>
    private static void PutCell(byte[] b, int x, int y, int width, params byte[] cell) =>
        cell.CopyTo(b, 322 + (((y * width) + x) * 6));

    [Fact]
    public void A_cell_splits_each_wall_byte_into_slot_and_blockage()
    {
        var b = Synthetic();

        // North: slot 6, blockage 2 (locked). East: slot 0, blockage 14 (blocked).
        PutCell(b, 3, 2, width: 20, 0x26, 0xE0, 0x00, 0x00, 0, 0);

        var cell = FruaLevel.Read(b).Cell(3, 2);

        Assert.Equal(6, cell.WallSlot(FruaFacing.North));
        Assert.Equal(FruaBlockage.Locked, cell.Blockage(FruaFacing.North));
        Assert.Equal(0, cell.WallSlot(FruaFacing.East));
        Assert.Equal(FruaBlockage.Blocked, cell.Blockage(FruaFacing.East));
    }

    /// <summary>
    /// FRUA's blockage nibble and the engine's enum are in different orders.
    /// </summary>
    /// <remarks>
    /// The two that would be caught last by a naive cast: FRUA 2 is a locked door where the enum
    /// has 4, and FRUA 14 is a blocked wall where the enum has 2. A cast would swap them.
    /// </remarks>
    [Theory]
    [InlineData(0x00, FruaBlockage.Open)]
    [InlineData(0x10, FruaBlockage.OpenSecret)]
    [InlineData(0x20, FruaBlockage.Locked)]
    [InlineData(0x30, FruaBlockage.LockedSecret)]
    [InlineData(0x40, FruaBlockage.LockedWizard)]
    [InlineData(0x60, FruaBlockage.LockedKey1)]
    [InlineData(0xD0, FruaBlockage.LockedKey8)]
    [InlineData(0xE0, FruaBlockage.Blocked)]
    [InlineData(0xF0, FruaBlockage.FalseDoor)]
    public void The_blockage_nibble_is_remapped_not_cast(byte wall, FruaBlockage expected)
    {
        var b = Synthetic();
        PutCell(b, 0, 0, width: 20, wall, 0, 0, 0, 0, 0);

        Assert.Equal(expected, FruaLevel.Read(b).Cell(0, 0).Blockage(FruaFacing.North));
    }

    /// <summary>Overland squares recognise only "blocked" and "secret blocked".</summary>
    [Theory]
    [InlineData(0x20, false)]   // locked means nothing outdoors
    [InlineData(0x60, false)]   // nor does a keyed door
    [InlineData(0xE0, true)]
    [InlineData(0xF0, true)]
    public void Overland_blockage_recognises_only_two_values(byte wall, bool blocked)
    {
        var b = Synthetic();
        PutCell(b, 0, 0, width: 20, wall, 0, 0, 0, 0, 0);

        Assert.Equal(blocked,
                     FruaLevel.Read(b).Cell(0, 0).IsOverlandBlocked(FruaFacing.North));
    }

    /// <summary>The backdrop is the low two bits plus one; the zone sits above them.</summary>
    [Theory]
    [InlineData(0x00, 1, 0)]
    [InlineData(0x01, 2, 0)]
    [InlineData(0x07, 4, 1)]    // 0x07 & 0xFC == 4 -> zone 1
    [InlineData(0x1C, 1, 7)]    // 28 -> zone 7
    [InlineData(0x20, 1, 0)]    // 32 is past the last case and falls to zone 0
    public void The_backdrop_and_zone_share_one_byte(byte packed, int backdrop, int zone)
    {
        var b = Synthetic();
        PutCell(b, 0, 0, width: 20, 0, 0, 0, 0, 0, packed);

        var cell = FruaLevel.Read(b).Cell(0, 0);
        Assert.Equal(backdrop, cell.BackdropIndex);
        Assert.Equal(zone, cell.Zone);
    }

    [Fact]
    public void A_cell_outside_the_level_is_refused()
    {
        var level = FruaLevel.Read(Synthetic());   // 20 wide, 19 high

        Assert.Throws<ArgumentOutOfRangeException>(() => level.Cell(20, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => level.Cell(0, 19));
        Assert.Throws<ArgumentOutOfRangeException>(() => level.Cell(-1, 0));
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

    /// <summary>
    /// A real dungeon's cells decode into sensible ranges throughout.
    /// </summary>
    /// <remarks>
    /// Every event index a cell names must exist among the level's hundred, every zone must be one
    /// of the eight, and every backdrop one of the four. A stride error anywhere in the header
    /// would push the cell block off its offset and break all three at once.
    /// </remarks>
    [Fact]
    public void A_real_dungeons_cells_are_all_in_range()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        Assert.Equal(19, level.Width);
        Assert.Equal(19, level.Height);

        var zones = new HashSet<int>();
        int walls = 0;

        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                var cell = level.Cell(x, y);

                Assert.InRange(cell.EventIndex, 0, 99);
                Assert.InRange(cell.Zone, 0, 7);
                Assert.InRange(cell.BackdropIndex, 1, 4);
                zones.Add(cell.Zone);

                foreach (var f in new[] { FruaFacing.North, FruaFacing.East,
                                          FruaFacing.South, FruaFacing.West })
                {
                    Assert.InRange(cell.WallSlot(f), 0, 15);
                    if (cell.WallSlot(f) != 0)
                    {
                        walls++;
                    }
                }
            }
        }

        // A town level uses several zones and a good many walls; zero of either would mean the
        // cell block was read from the wrong offset and happened to stay in range.
        Assert.True(zones.Count >= 3, $"only {zones.Count} zones across the level");
        Assert.True(walls > 100, $"only {walls} walled sides in a 19x19 dungeon");
    }

    /// <summary>
    /// An overland level has no walls at all, and blocks movement with terrain instead.
    /// </summary>
    /// <remarks>
    /// <b>This is the strongest single check on the cell layout.</b> The four overland levels are
    /// distinguishable from the dungeons in the header (three wall slots of 255) and independently
    /// in the cells (every wall slot 0). The two agreeing, across 570 cells, is very hard to
    /// arrange by accident from a wrong offset.
    /// </remarks>
    [Fact]
    public void An_overland_levels_cells_carry_terrain_rather_than_walls()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 1) is not { } level)
        {
            return;
        }

        Assert.True(level.IsOverland);

        int blocked = 0;
        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                var cell = level.Cell(x, y);

                // No wall slot anywhere -- an overland level draws terrain, not walls.
                foreach (var f in new[] { FruaFacing.North, FruaFacing.East,
                                          FruaFacing.South, FruaFacing.West })
                {
                    Assert.Equal(0, cell.WallSlot(f));
                    if (cell.IsOverlandBlocked(f))
                    {
                        blocked++;
                    }
                }

                Assert.Equal(1, cell.BackdropIndex);
            }
        }

        Assert.True(blocked > 100, $"only {blocked} impassable sides on the overland map");
    }

    /// <summary>Zone names read, and empty slots are numbered.</summary>
    [Fact]
    public void Zone_names_read_from_a_real_level()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 6) is not { } level)
        {
            return;
        }

        Assert.Equal(8, level.ZoneNames.Count);
        Assert.All(level.ZoneNames, n => Assert.False(string.IsNullOrWhiteSpace(n)));
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
