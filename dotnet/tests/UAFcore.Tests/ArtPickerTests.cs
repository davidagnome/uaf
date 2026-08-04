using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Covers the character generator's two art screens.
/// </summary>
public class ArtPickerTests : IDisposable
{
    private readonly string scratch =
        Path.Combine(Path.GetTempPath(), $"uaf-art-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private void Drop(params string[] names)
    {
        Directory.CreateDirectory(scratch);
        foreach (string name in names)
        {
            File.WriteAllText(Path.Combine(scratch, name), "");
        }
    }

    // ---- what is on offer ----------------------------------------------------------------------

    [Fact]
    public void The_series_is_scanned_by_name_not_enumerated()
    {
        // The engine asks for numbers 1 to 50 by name, so a portrait called anything else is
        // invisible however well formed it is.
        Drop("prt_SPic1.png", "prt_SPic2.png", "portrait_of_aramil.png");

        Assert.Equal(["prt_SPic1.png", "prt_SPic2.png"],
                     ArtPicker.Available(scratch, ArtPicker.SmallPicturePattern));
    }

    [Fact]
    public void A_gap_in_the_numbering_is_skipped_not_a_stop()
    {
        Drop("prt_SPic1.png", "prt_SPic4.png", "prt_SPic50.png");

        Assert.Equal(["prt_SPic1.png", "prt_SPic4.png", "prt_SPic50.png"],
                     ArtPicker.Available(scratch, ArtPicker.SmallPicturePattern));
    }

    [Fact]
    public void The_scan_stops_at_fifty()
    {
        Drop("prt_SPic50.png", "prt_SPic51.png");

        Assert.Equal(["prt_SPic50.png"],
                     ArtPicker.Available(scratch, ArtPicker.SmallPicturePattern));
    }

    [Fact]
    public void Only_png_counts_at_play_time()
    {
        // FindImageWithValidExt reads as though it tries every image format, and under UAFEngine
        // it checks the exact file and returns FALSE. A design shipping .pcx portraits shows a
        // player nothing while showing the designer everything.
        Drop("prt_SPic1.pcx", "prt_SPic2.bmp", "prt_SPic3.png");

        Assert.Equal(["prt_SPic3.png"],
                     ArtPicker.Available(scratch, ArtPicker.SmallPicturePattern));
    }

    [Fact]
    public void The_two_screens_read_different_series()
    {
        Drop("prt_SPic1.png", "cn_Icon1.png", "cn_Icon2.png");

        Assert.Single(ArtPicker.Available(scratch, ArtPicker.SmallPicturePattern));
        Assert.Equal(2, ArtPicker.Available(scratch, ArtPicker.IconPattern).Count);
    }

    [Fact]
    public void A_design_with_no_art_folder_offers_nothing()
    {
        Assert.Empty(ArtPicker.Available(scratch, ArtPicker.IconPattern));
        Assert.Empty(ArtPicker.Available(null, ArtPicker.IconPattern));
    }

    // ---- stepping ------------------------------------------------------------------------------

    [Fact]
    public void Both_directions_wrap()
    {
        // The opposite of the roster and the inventory, which stop at the ends; this is a
        // carousel because there is only ever one picture on screen.
        Assert.Equal(0, ArtPicker.Step(selected: 2, count: 3, delta: 1));
        Assert.Equal(2, ArtPicker.Step(selected: 0, count: 3, delta: -1));
        Assert.Equal(1, ArtPicker.Step(selected: 0, count: 3, delta: 1));
    }

    [Fact]
    public void Stepping_an_empty_list_stays_at_zero()
    {
        Assert.Equal(0, ArtPicker.Step(selected: 0, count: 0, delta: 1));
        Assert.Equal(0, ArtPicker.Step(selected: 0, count: 0, delta: -1));
    }

    [Fact]
    public void One_picture_darkens_both_paging_entries()
    {
        // The test is numSmallPics <= 1, so a design with exactly one portrait offers only
        // SELECT -- and one with none offers only SELECT too, over an empty screen.
        Assert.False(ArtPicker.CanStep(0));
        Assert.False(ArtPicker.CanStep(1));
        Assert.True(ArtPicker.CanStep(2));
    }

    [Fact]
    public void The_menu_is_next_prev_select()
    {
        Assert.Equal(["NEXT", "PREV", "SELECT"], ArtPicker.Menu.Select(m => m.Label));
    }

    // ---- the wizard's two art steps ------------------------------------------------------------

    [Fact]
    public void The_icon_comes_before_the_portrait()
    {
        var making = new CharacterCreation();
        making.Choose("Elf");
        making.Choose(nameof(Gender.Male));
        making.Choose("Fighter");
        making.Choose("4");
        making.SkipStats();
        making.Name("Aramil");

        Assert.Equal(CreationStep.Icon, making.Step);

        making.Pick("cn_Icon3.png");
        Assert.Equal(CreationStep.SmallPicture, making.Step);
        Assert.Equal("cn_Icon3.png", making.Icon);

        making.Pick("prt_SPic7.png");
        Assert.Equal(CreationStep.Spells, making.Step);
        Assert.Equal("prt_SPic7.png", making.SmallPicture);
    }

    [Fact]
    public void A_design_with_no_art_still_advances()
    {
        // SELECT is always available, even with nothing to select.
        var making = new CharacterCreation();
        making.Choose("Elf");
        making.Choose(nameof(Gender.Male));
        making.Choose("Fighter");
        making.Choose("4");
        making.SkipStats();
        making.Name("Aramil");

        making.Pick(null);
        making.Pick(null);

        Assert.Equal(CreationStep.Spells, making.Step);
        Assert.Null(making.Icon);
        Assert.Null(making.SmallPicture);
    }
}
