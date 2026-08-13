using UAF.Import.Frua;
using UAF.Serialization;

namespace UAF.Import.Frua.Tests;

/// <summary>
/// The art an imported design points at — placeholders, not FRUA's own pictures.
/// </summary>
public class FruaArtConverterTests
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

    /// <summary>
    /// Every wall slot names the same two images.
    /// </summary>
    /// <remarks>
    /// <b>Not an oversight in this port.</b> The reference's per-index <c>Format</c> calls for
    /// walls and doors are commented out, so all sixteen slots get one wall and one door.
    /// </remarks>
    [Fact]
    public void Every_wall_slot_names_the_same_images()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var walls = FruaArtConverter.WallSets(level);

        Assert.NotEmpty(walls);
        Assert.All(walls, w => Assert.Equal(FruaArtConverter.WallFile, w.WallFile));
        Assert.All(walls, w => Assert.Equal(FruaArtConverter.DoorFile, w.DoorFile));
        Assert.All(walls, w => Assert.Equal(1, w.Used));

        // The door draws over the wall, not under it.
        Assert.All(walls, w => Assert.Equal(0, w.DoorFirst));
    }

    /// <summary>Backdrops are the one table that varies by slot.</summary>
    [Fact]
    public void Backdrops_vary_by_slot()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var backgrounds = FruaArtConverter.Backgrounds(level);

        Assert.NotEmpty(backgrounds);

        for (int i = 0; i < backgrounds.Count(); i++)
        {
            Assert.Equal(FruaArtConverter.BackdropFile(level.BackdropSlots[i]),
                         backgrounds[i].BackgroundFile);
        }
    }

    /// <summary>A backdrop the editor does not ship falls back to the first.</summary>
    [Fact]
    public void A_missing_backdrop_falls_back()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var backgrounds = FruaArtConverter.Backgrounds(level, _ => false);

        Assert.NotEmpty(backgrounds);
        Assert.All(backgrounds,
                   b => Assert.Equal(FruaArtConverter.DefaultBackdropFile, b.BackgroundFile));
    }

    /// <summary>
    /// The flag decides whether an event has art, not how big it is.
    /// </summary>
    /// <remarks>
    /// <c>AssignPic</c> returns an empty record whenever the bit is clear, whatever the slot
    /// number says — so a slot alone names nothing.
    /// </remarks>
    [Theory]
    [InlineData(1, true, true)]
    [InlineData(240, true, true)]
    [InlineData(1, false, false)]       // A real slot, but the flag is clear
    [InlineData(0, true, false)]        // The flag is set, but zero is not a slot
    [InlineData(241, true, false)]      // Past the last slot
    public void The_flag_decides_whether_there_is_a_picture(byte slot, bool has, bool expected)
    {
        var picture = FruaArtConverter.Picture(slot, has);

        Assert.Equal(expected, picture is not null);

        if (picture is not null)
        {
            Assert.Equal(FruaArtConverter.PictureFile, picture.FileName);
        }
    }

    /// <summary>
    /// The picture type is a bit flag, not a position in the list.
    /// </summary>
    /// <remarks>
    /// <c>SurfaceType</c> is a power-of-two set, so <c>SmallPicDib</c> is 1024. Writing its
    /// position in the enum would name <c>CombatDib</c> instead — a plausible-looking small number
    /// that means something else entirely.
    /// </remarks>
    [Fact]
    public void The_picture_type_is_a_bit_flag()
    {
        Assert.Equal(1024, FruaArtConverter.SmallPicDib);

        var picture = FruaArtConverter.Picture(1, hasPicture: true);

        Assert.NotNull(picture);
        Assert.Equal(FruaArtConverter.SmallPicDib, picture.PicType);
    }

    /// <summary>A converted level carries its art through the round trip.</summary>
    [Fact]
    public void A_converted_levels_art_survives_the_round_trip()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var converted = FruaLevelConverter.Convert(level, 5);

        Assert.NotEmpty(converted.WallSets);
        Assert.NotEmpty(converted.BackgroundSets);

        using var stream = new MemoryStream();
        LevelFileWriter.WriteFile(stream, converted);
        stream.Position = 0;

        var reread = LevelFileReader.Read(
            stream, ArchiveRole.Editor,
            (ar, type, ver) => EventBodyReader.TryRead(ar, type, ver, ArchiveRole.Editor));

        Assert.Equal(converted.WallSets.Select(w => w.WallFile),
                     reread.WallSets.Select(w => w.WallFile));
        Assert.Equal(converted.BackgroundSets.Select(b => b.BackgroundFile),
                     reread.BackgroundSets.Select(b => b.BackgroundFile));
    }

    /// <summary>
    /// Sounds are dropped, which is the reference's behaviour rather than a gap here.
    /// </summary>
    /// <remarks>
    /// <c>AssignSound</c> opens with an unconditional <c>return</c> and its body is commented out,
    /// so no imported event names a sound. Inventing paths here would produce a design that asks
    /// for files nothing ships.
    /// </remarks>
    [Fact]
    public void No_imported_event_names_a_sound()
    {
        if (Heirs() is not { } design || FruaLevel.ReadFile(design, 5) is not { } level)
        {
            return;
        }

        var converted = FruaLevelConverter.Convert(level, 5);
        int checkedEvents = 0;

        foreach (var entry in converted.Entries)
        {
            switch (entry.Body)
            {
                case TextEvent t:
                    Assert.Equal(string.Empty, t.Sound);
                    checkedEvents++;
                    break;
                case GainExperienceEvent g:
                    Assert.Equal(string.Empty, g.Sound);
                    checkedEvents++;
                    break;
                case SoundEvent s:
                    Assert.All(s.Sounds, name => Assert.Equal(string.Empty, name));
                    checkedEvents++;
                    break;
            }
        }

        Assert.True(checkedEvents > 0, "no event with a sound field to check");
    }
}
