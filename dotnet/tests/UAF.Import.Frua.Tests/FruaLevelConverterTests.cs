using UAF.Import.Frua;
using UAF.Serialization;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// Converting a DOS FRUA level into a UAF <c>.lvl</c>, and writing it.
/// </summary>
/// <remarks>
/// This is the first slice of the conversion layer — the half of the importer that turns what
/// <c>UAF.Import.Frua</c> reads into what <c>UAF.Serialization</c> writes.
/// </remarks>
public class FruaLevelConverterTests
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

    [Fact]
    public void A_converted_level_keeps_its_dimensions_and_cell_count()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var converted = FruaLevelConverter.Convert(level, 5);

        Assert.Equal(19, converted.Width);
        Assert.Equal(19, converted.Height);
        Assert.Equal(19 * 19, converted.Cells.Count);
        Assert.Equal(5, converted.Level);
    }

    /// <summary>
    /// Walls are re-ordered into UAF's north, south, east, west slots.
    /// </summary>
    /// <remarks>
    /// <b>The two formats disagree about slot order</b> — FRUA stores north, east, south, west and
    /// UAF stores north, SOUTH, east, west. Writing FRUA's order straight through would swap every
    /// east wall with every south one. <see cref="AreaMapCell.WallAt"/> permutes on read, so a
    /// correctly converted cell answers it with the FRUA value for the same compass direction.
    /// </remarks>
    [Fact]
    public void The_wall_slots_are_reordered_for_uaf()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var converted = FruaLevelConverter.Convert(level, 5);
        int compared = 0;

        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                var source = level.Cell(x, y);
                var target = converted.Cells[(y * level.Width) + x];

                // WallAt takes 0=north, 1=east, 2=south, 3=west and permutes internally.
                Assert.Equal(source.WallSlot(FruaFacing.North), target.WallAt(0));
                Assert.Equal(source.WallSlot(FruaFacing.East), target.WallAt(1));
                Assert.Equal(source.WallSlot(FruaFacing.South), target.WallAt(2));
                Assert.Equal(source.WallSlot(FruaFacing.West), target.WallAt(3));
                compared++;
            }
        }

        Assert.Equal(19 * 19, compared);
    }

    /// <summary>The zone, backdrop and event marker survive the conversion.</summary>
    [Fact]
    public void A_cells_zone_backdrop_and_event_marker_survive()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var converted = FruaLevelConverter.Convert(level, 5);
        int withEvents = 0;

        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                var source = level.Cell(x, y);
                var target = converted.Cells[(y * level.Width) + x];

                Assert.Equal(source.Zone, target.Zone);
                Assert.Equal(source.BackdropIndex, target.NorthBg);
                Assert.Equal(source.BackdropIndex, target.EastBg);
                Assert.Equal(source.BackdropIndex, target.SouthBg);
                Assert.Equal(source.BackdropIndex, target.WestBg);
                Assert.Equal(source.EventIndex != 0, target.EventExists);

                if (target.EventExists)
                {
                    withEvents++;
                }
            }
        }

        Assert.True(withEvents > 0, "no converted cell carries an event marker");
    }

    /// <summary>An overland level's terrain becomes the <c>bkgrnd</c> flag, not walls or blockage.</summary>
    [Fact]
    public void An_overland_levels_terrain_becomes_the_bkgrnd_flag()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 1) is not { } level)
        {
            return;
        }

        Assert.True(level.IsOverland);
        var converted = FruaLevelConverter.Convert(level, 1);

        int blocked = 0;

        for (int y = 0; y < level.Height; y++)
        {
            for (int x = 0; x < level.Width; x++)
            {
                var target = converted.Cells[(y * level.Width) + x];

                // No walls or blockage anywhere outdoors — the reference never writes either.
                Assert.All(target.Walls, w => Assert.Equal(0, w));
                Assert.All(target.Blockage, b => Assert.Equal(0, b));

                blocked += target.Background == 1 ? 1 : 0;
            }
        }

        Assert.True(blocked > 0, "no background cells on the overland map");
    }

    /// <summary>The zone names and rest events come across.</summary>
    [Fact]
    public void The_zones_carry_their_names_and_rest_events()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 6) is not { } level)
        {
            return;
        }

        var converted = FruaLevelConverter.Convert(level, 6);

        Assert.Equal(8, converted.Zones.Zones.Count);

        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(level.ZoneNames[i], converted.Zones.Zones[i].Name);
            Assert.Equal(level.RestEvents[i].Chance, converted.Zones.Zones[i].Rest.Chance);
            Assert.Equal(level.RestEvents[i].EveryMinutes,
                         converted.Zones.Zones[i].Rest.EveryMinutes);
        }
    }

    /// <summary>
    /// A converted level writes to a real <c>.lvl</c> and reads back the same.
    /// </summary>
    /// <remarks>
    /// <b>This is the end-to-end proof the slice exists for</b>: the FRUA reader, the conversion
    /// and <c>UAF.Serialization</c>'s writer joined up, with the round trip showing the result is
    /// a file the port's own reader accepts.
    /// </remarks>
    [Fact]
    public void A_converted_level_writes_and_reads_back()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var converted = FruaLevelConverter.Convert(level, 5);

        Assert.True(LevelFileWriter.CanWrite(converted, out string reason), reason);

        using var stream = new MemoryStream();
        LevelFileWriter.WriteFile(stream, converted);

        Assert.True(stream.Length > 0, "the writer produced nothing");

        stream.Position = 0;

        // The bodies have to be read back properly now that the level carries them: an event body
        // has no length prefix, so a callback that declined to read one would leave the stream
        // positioned inside it and every later event would come out of the middle of this one.
        var reread = LevelFileReader.Read(
            stream, ArchiveRole.Editor,
            (ar, type, ver) => EventBodyReader.TryRead(ar, type, ver, ArchiveRole.Editor));

        Assert.Equal(converted.Width, reread.Width);
        Assert.Equal(converted.Height, reread.Height);
        Assert.Equal(converted.Cells.Count, reread.Cells.Count);

        for (int i = 0; i < converted.Cells.Count; i++)
        {
            Assert.Equal(converted.Cells[i].Zone, reread.Cells[i].Zone);
            Assert.Equal(converted.Cells[i].Walls, reread.Cells[i].Walls);
            Assert.Equal(converted.Cells[i].Blockage, reread.Cells[i].Blockage);
        }

        // The events are the part with no length prefix, so a round trip that reads them back in
        // the right order and the right count is what proves the writer and reader agree.
        Assert.True(converted.EventCount > 0, "the level converted no events");
        Assert.Equal(converted.EventCount, reread.EventCount);
        Assert.Equal(converted.Entries.Select(e => e.Type), reread.Entries.Select(e => e.Type));
    }

    /// <summary>
    /// A converted level's events keep their text and their trigger through the round trip.
    /// </summary>
    /// <remarks>
    /// The count and types agreeing would still hold if every body were written as zeroes, so this
    /// checks a field that came from the design.
    /// </remarks>
    [Fact]
    public void A_converted_levels_event_text_survives_the_round_trip()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var converted = FruaLevelConverter.Convert(level, 5);

        using var stream = new MemoryStream();
        LevelFileWriter.WriteFile(stream, converted);
        stream.Position = 0;

        var reread = LevelFileReader.Read(
            stream, ArchiveRole.Editor,
            (ar, type, ver) => EventBodyReader.TryRead(ar, type, ver, ArchiveRole.Editor));

        var written = converted.Entries.Select(e => e.Body).OfType<TextEvent>().ToList();
        var read = reread.Entries.Select(e => e.Body).OfType<TextEvent>().ToList();

        Assert.NotEmpty(written);
        Assert.Equal(written.Count, read.Count);

        for (int i = 0; i < written.Count; i++)
        {
            Assert.Equal(written[i].Base.Text, read[i].Base.Text);
            Assert.Equal(written[i].Base.Control.EventTrigger, read[i].Base.Control.EventTrigger);
            Assert.Equal(written[i].Base.Control.OnceOnly, read[i].Base.Control.OnceOnly);
            Assert.Equal(written[i].WaitForReturn, read[i].WaitForReturn);
        }

        Assert.Contains(read, t => !string.IsNullOrEmpty(t.Base.Text));
    }
}
