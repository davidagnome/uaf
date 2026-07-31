using UAF.Media.Sdl;
using UAF.Data;
using UAF.Serialization;

namespace UAF.Media.Tests;

/// <summary>
/// Draws a real design's screen: design records read by <c>UAF.Serialization</c>, art decoded by
/// <see cref="ImageLoader"/>, composited by <see cref="Blitter"/>, labelled with
/// <see cref="BitmapFont"/>.
/// </summary>
/// <remarks>
/// <para>
/// The first test that crosses both halves of the port. Every layer below it has been verified in
/// isolation — the serialization readers against a C++ oracle, the PNG decoder against libpng, the
/// blitter and the font layer against authored fixtures — but nothing had yet checked that they
/// compose. Integration is where the mismatches live: a colour key that is set but never consulted,
/// art whose dimensions disagree with the layout the design assumes, a surface kind that turns
/// transparency off.
/// </para>
/// <para>
/// The art is gitignored, so this returns early without <c>reference/</c>. It also writes the
/// composed frame to the scratchpad when <c>UAF_FRAME_OUT</c> is set, which is how the layout gets
/// eyeballed — several defects in this port were found by looking at output rather than by
/// asserting on it.
/// </para>
/// </remarks>
public class FrameCompositionTests
{
    private const int ScreenWidth = 640;
    private const int ScreenHeight = 480;

    private static string? DesignRoot()
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

    private static GlobalStatsPrefix ReadDesign(string root)
    {
        using var stream = File.OpenRead(Path.Combine(root, "Data", "game.dat"));
        var cursor = GameDataReader.Open(stream);
        return GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version);
    }

    private static Surface Art(string root, string name, SurfaceKind kind) =>
        ImageLoader.Default.LoadSurface(Path.Combine(root, "Resources", name), kind);

    [Fact]
    public void A_real_design_composes_into_a_screen()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        var design = ReadDesign(root);

        // Everything drawn below is named by the design's own records, not guessed. The three
        // border pieces come out of GLOBAL_STATS' art slots in the order the engine stores them.
        Assert.Equal("Something Wild", design.DesignName);
        Assert.Equal("Garamond", design.Font.FaceName);
        Assert.Equal(13, design.Font.PointSizeHint);
        Assert.True(design.Font.IsBold);

        // The layout comes from the design's own config640.txt, not from guesswork. An earlier
        // revision of this test invented the positions and blitted whole art files; the result put
        // the bar images' unused padding on screen, because several of these keys are *source*
        // rectangles into a sheet that holds three strips stacked.
        var config = DesignConfig.Load(Path.Combine(root, "Data", "config640.txt"));

        var screen = new Surface(ScreenWidth, ScreenHeight);
        screen.Fill(0xFF000000);

        var horizontal = Art(root, "border_Horizontal.png", SurfaceKind.Common);
        var vertical = Art(root, "border_Vertical.png", SurfaceKind.Common);

        Assert.Equal(ScreenWidth, horizontal.Width);
        Assert.Equal(ScreenHeight, vertical.Height);

        // Three horizontal strips stacked in one 640x42 image, each placed separately.
        BlitConfigured(screen, config, horizontal, "HORZ_BAR_LONG", "HORZ_BAR_TOP");
        BlitConfigured(screen, config, horizontal, "HORZ_BAR_LONG_2", "HORZ_BAR_MIDDLE");
        BlitConfigured(screen, config, horizontal, "HORZ_BAR_LONG_3", "HORZ_BAR_BOTTOM");

        // Two vertical strips in one 48x480 image. The long one is 14 wide, so the remaining
        // 34 columns are padding the engine never draws.
        BlitConfigured(screen, config, vertical, "VERT_BAR_LONG", "VERT_BAR_LEFT");
        BlitConfigured(screen, config, vertical, "VERT_BAR_SHORT", "VERT_BAR_MIDDLE");
        BlitConfigured(screen, config, vertical, "VERT_BAR_LONG", "VERT_BAR_RIGHT");

        // The viewport frame and the 3D view inside it, both at configured positions.
        var viewport = Art(root, "border_Viewport.png", SurfaceKind.Common);
        var backdrop = Art(root, "backdrop_IndoorGreyStone.png", SurfaceKind.Background);

        Assert.True(config.TryGetPoint("VIEWPORT_FRAME", out int frameX, out int frameY));
        Assert.True(config.TryGetRect("VIEWPORT_RECT", out int viewX, out int viewY, out _, out _));

        Blitter.BlitOpaque(screen, frameX, frameY, viewport);
        Blitter.BlitOpaque(screen, viewX, viewY, backdrop);

        // A keyed sprite over the backdrop. This composition step only works if the decoder
        // adopted the top-left pixel as the colour key and the surface kind allows it.
        var sprite = Art(root, "icon_GiantRat.png", SurfaceKind.Icon);
        Assert.True(sprite.IsKeyed, "icon art must be keyed or it draws an opaque box");

        var cell = SurfaceRect.FromBounds(0, 0, sprite.Width / 2, sprite.Height);
        Blitter.BlitTransparent(screen, viewX + 60, viewY + 120, sprite, cell);

        // TEXTBOX is where the engine puts event text; PARTYNAMES is the roster column.
        Assert.True(config.TryGetPoint("TEXTBOX", out int textX, out int textY));
        config.TryGetInts("PARTYNAMES", out int[] party, 4);
        DrawTextPanel(screen, design, textX, textY, party[2], party[3]);

        // Structural assertions rather than a golden hash, because the hash would depend on
        // gitignored art and could never run anywhere else.
        Assert.NotEqual(0xFF000000u, screen[ScreenWidth / 2, 4]);      // top bar drew
        Assert.NotEqual(0xFF000000u, screen[4, ScreenHeight / 2]);     // left bar drew
        Assert.NotEqual(0xFF000000u, screen[viewX + 100, viewY + 100]);   // backdrop drew

        // The frame must not be uniform: a single-colour result is what a silently failed blit
        // chain produces, and it would satisfy every assertion above if the fill colour changed.
        Assert.True(screen.Pixels.Distinct().Count() > 500,
            "the composed frame has too few distinct colours to be real art");

        Dump(screen);
    }

    /// <summary>
    /// Blits a configured source rectangle to a configured destination point.
    /// </summary>
    /// <remarks>
    /// The pairing of a <c>*_LONG</c> source with a <c>*_BAR_*</c> destination is the whole trick:
    /// the art file is a sheet of strips, and the config says which strip goes where.
    /// </remarks>
    private static void BlitConfigured(Surface screen, DesignConfig config, Surface art,
                                       string sourceKey, string destinationKey)
    {
        if (!config.TryGetRect(sourceKey, out int left, out int top, out int right, out int bottom)
            || !config.TryGetPoint(destinationKey, out int x, out int y))
        {
            return;
        }

        var source = new SurfaceRect(left, top, right, bottom);
        if (!source.TryClipTo(art.Bounds, out var clipped))
        {
            return;
        }

        Blitter.BlitOpaque(screen, x, y, art, clipped);
    }

    private static void DrawTextPanel(Surface screen, GlobalStatsPrefix design, int x, int y,
                                      int rosterX, int rosterY)
    {
        using var rasterizer = new SdlFontRasterizer();
        if (!rasterizer.IsAvailable)
        {
            return;
        }

        // The design asks for Garamond at 13, bold. Garamond is not installed on a CI runner or on
        // most Linux machines, and the original warned and carried on in exactly that situation
        // (GlobalData.cpp:5846) -- so the substitution here is the behaviour, not a shortcut.
        var options = new FontRasterOptions(design.Font.PointSizeHint, Antialias: true);
        var body = new BitmapFont(rasterizer.Rasterize(
            EmbeddedFonts.PtSerif(bold: design.Font.IsBold), options));
        var heading = new BitmapFont(rasterizer.Rasterize(
            EmbeddedFonts.PtSerif(bold: true), options with { PixelHeight = 18 }));

        // The roster, where the engine draws party names during play.
        int cursor = rosterY;
        heading.Draw(screen, rosterX, cursor, design.DesignName, tint: 0xFFE8C86A);
        cursor += heading.Atlas.MaxCharHeight + 2;

        foreach (var character in design.Characters.Take(6))
        {
            body.Draw(screen, rosterX, cursor, character.Name, tint: 0xFFF0E6D2);
            cursor += body.Atlas.MaxCharHeight;
        }

        // Event text, at TEXTBOX.
        body.Draw(screen, x, y, $"{design.DesignName}, version {design.Version.Value:0.00}.",
                  tint: 0xFFF0E6D2);
        body.Draw(screen, x, y + body.Atlas.MaxCharHeight,
                  $"{design.Characters.Count} characters and {design.SpecialItems.Count} special items.",
                  tint: 0xFF60C060);
        body.Draw(screen, x, y + (body.Atlas.MaxCharHeight * 2),
                  "Laid out from the design's own config640.txt.", tint: 0xFF9A9AB0);
    }

    /// <summary>Writes the composed frame as raw BGRA when <c>UAF_FRAME_OUT</c> names a path.</summary>
    private static void Dump(Surface screen)
    {
        string? path = Environment.GetEnvironmentVariable("UAF_FRAME_OUT");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var raw = new byte[screen.Pixels.Length * 4];
        for (int i = 0; i < screen.Pixels.Length; i++)
        {
            uint pixel = screen.Pixels[i];
            raw[i * 4] = (byte)(pixel >> 16);
            raw[(i * 4) + 1] = (byte)(pixel >> 8);
            raw[(i * 4) + 2] = (byte)pixel;
            raw[(i * 4) + 3] = 0xFF;
        }

        File.WriteAllBytes(path, raw);
        File.WriteAllText(path + ".dim", $"{screen.Width}x{screen.Height}");
    }

    [Fact]
    public void Every_art_slot_the_design_names_actually_loads()
    {
        string? root = DesignRoot();
        if (root is null)
        {
            return;
        }

        var design = ReadDesign(root);
        var missing = new List<string>();
        var failed = new List<string>();

        foreach (var slot in design.Art.Select(a => a.Name).Distinct())
        {
            if (slot.Length == 0)
            {
                continue;
            }

            string path = Path.Combine(root, "Resources", slot);
            if (!File.Exists(path))
            {
                missing.Add(slot);
                continue;
            }

            try
            {
                var surface = ImageLoader.Default.LoadSurface(path, SurfaceKind.Common);
                Assert.True(surface.Width > 0 && surface.Height > 0);
            }
            catch (Exception e)
            {
                failed.Add($"{slot}: {e.Message}");
            }
        }

        // Missing files are the design's problem, not the port's -- the original warned and
        // carried on too -- so they are reported separately from decode failures, which are ours.
        Assert.True(failed.Count == 0, $"art the design names failed to decode:\n  " +
                                       string.Join("\n  ", failed));
        Assert.True(missing.Count <= 1,
            $"{missing.Count} art slots name files that are not on disk: {string.Join(", ", missing)}");
    }
}
