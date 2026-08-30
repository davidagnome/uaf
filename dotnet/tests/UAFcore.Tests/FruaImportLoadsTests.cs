using UAF.Import.Frua;
using UAF.Media;
using UAF.Media.Sdl;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// Phase 6's exit criterion: a DOS FRUA design that has been imported produces a design that loads
/// and plays in UAFcore, not merely one that converts.
/// </summary>
/// <remarks>
/// The designs and the template are gitignored, so these return early without <c>reference/</c>.
/// </remarks>
public class FruaImportLoadsTests : IDisposable
{
    private readonly string scratch =
        Path.Combine(Path.GetTempPath(), "uaf-frua-load-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static DirectoryInfo? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Shared")))
        {
            dir = dir.Parent;
        }

        return dir;
    }

    private static string? Design(params string[] parts)
    {
        if (Root() is not { } root)
        {
            return null;
        }

        string path = Path.Combine([root.FullName, "reference", .. parts]);
        return Directory.Exists(path) ? path : null;
    }

    private static string? Heirs() =>
        Design("Unlimited Adventures -ENG", "DESIGNS", "UA", "HEIRS.DSN");

    private static string? Sl4Fath() => Design("example_dsn", "SL4-FATH.DSN");

    private static string? Template() => Design("Case.dsn");

    /// <summary>
    /// Imports <paramref name="frua"/> onto a template and confirms the result loads and plays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Loading is <see cref="LoadedDesign.Open"/> with every record read</b> — the imported
    /// levels, the header written from the template, and the rules databases the template supplies.
    /// A design whose <c>game.dat</c> level table did not agree with its level files would throw
    /// here rather than open, so this is the check the conversion alone cannot make.
    /// </para>
    /// <para>
    /// <b>Playing is the state machine walking and drawing</b>, the same path <c>UAFcore.App --dump</c>
    /// takes: step until an event fires or the budget runs out, then render a frame. A design that
    /// loaded but whose levels could not be walked would throw in <see cref="Game.Update"/>.
    /// </para>
    /// </remarks>
    private void Plays(string? frua, string? template)
    {
        if (frua is null || template is null)
        {
            return;
        }

        var source = FruaDesign.Open(frua);

        var converted = FruaDesignConverter.Convert(
            source, Path.Combine(template, "Data", "game.dat"));
        FruaDesignConverter.Write(converted, scratch, template);

        using var rasterizer = new SdlFontRasterizer();
        using var design = LoadedDesign.Open(scratch, new SdlImageDecoder(), rasterizer);
        var game = new Game(design);

        // The FRUA design's own name survives the import and comes back from the written header.
        Assert.Equal(source.Game.DesignName, design.Name);
        Assert.NotEmpty(design.LevelFiles);

        // Walk until an event fires or the budget runs out, then draw.
        for (int i = 0; i < 200 && game.CurrentEvent is null; i++)
        {
            game.Update(InputEvent.KeyDown(i % 3 == 2 ? VirtualKey.Right : VirtualKey.Up));
        }

        var frame = game.Render();
        Assert.True(frame.Width > 0 && frame.Height > 0, "no frame rendered");
    }

    [Fact]
    public void Heirs_imports_loads_and_plays() => Plays(Heirs(), Template());

    [Fact]
    public void Sl4_fath_imports_loads_and_plays() => Plays(Sl4Fath(), Template());
}
