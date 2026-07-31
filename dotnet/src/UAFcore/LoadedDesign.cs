using UAF.Data;
using UAF.Media;
using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// A design opened from disk: its records, its layout config, and the art and fonts drawn from it.
/// </summary>
/// <remarks>
/// <para>
/// The engine's equivalent of <c>OpenDesign</c>, minus the part that made the original untestable.
/// The C++ version cannot run headless because it needs a live DirectX device before it will read
/// a single record (docs/PORTING-PLAN.md §7 Phase 0); here loading is pure file I/O and the
/// presenter is a separate concern entirely.
/// </para>
/// <para>
/// Art is resolved leniently, because designs genuinely ship with slots naming files that are not
/// present — the original warned and carried on, and a missing wall texture must not stop a design
/// loading.
/// </para>
/// </remarks>
public sealed class LoadedDesign : IDisposable
{
    private readonly Dictionary<string, Surface?> artCache = [];
    private readonly ImageLoader loader;
    private readonly IFontRasterizer? rasterizer;
    private readonly Dictionary<int, BitmapFont> fonts = [];

    private LoadedDesign(string root, GlobalStatsPrefix globals, DesignConfig config,
                         ImageLoader loader, IFontRasterizer? rasterizer)
    {
        Root = root;
        Globals = globals;
        Config = config;
        this.loader = loader;
        this.rasterizer = rasterizer;
    }

    public string Root { get; }

    public GlobalStatsPrefix Globals { get; }

    public DesignConfig Config { get; }

    public string Name => Globals.DesignName;

    /// <summary>
    /// Opens a design directory — the one containing <c>Data/</c> and <c>Resources/</c>.
    /// </summary>
    /// <param name="rasterizer">
    /// Optional. Without one the design still loads and still draws; it simply has no text, which
    /// is the same degradation contract the video and image decoders use.
    /// </param>
    public static LoadedDesign Open(string root, IImageDecoder? extraDecoder = null,
                                    IFontRasterizer? rasterizer = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        string data = Path.Combine(root, "Data");
        if (!Directory.Exists(data))
        {
            throw new DirectoryNotFoundException($"no Data/ directory under '{root}'");
        }

        using var stream = File.OpenRead(Path.Combine(data, "game.dat"));
        var cursor = GameDataReader.Open(stream);
        var globals = GlobalStatsReader.ReadThroughCharacters(cursor.Body, cursor.Version);

        // config640.txt is the 640x480 layout. A design ships one per resolution and falls back to
        // config.txt, which carries the shared settings.
        string config = Path.Combine(data, "config640.txt");
        if (!File.Exists(config))
        {
            config = Path.Combine(data, "config.txt");
        }

        return new LoadedDesign(root, globals,
                                File.Exists(config) ? DesignConfig.Load(config)
                                                    : DesignConfig.Parse([]),
                                new ImageLoader(extraDecoder), rasterizer);
    }

    /// <summary>
    /// Loads art by the filename a design record gave, or returns null when it cannot be found or
    /// decoded.
    /// </summary>
    /// <remarks>
    /// Cached by name including the failures, so a design naming a missing file does not retry the
    /// lookup on every frame.
    /// </remarks>
    public Surface? Art(string fileName, SurfaceKind kind = SurfaceKind.Common)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string key = $"{fileName}|{kind}";
        if (artCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Surface? surface = null;
        string path = Path.Combine(Root, "Resources", fileName);
        if (File.Exists(path))
        {
            try
            {
                surface = loader.LoadSurface(path, kind);
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException)
            {
                surface = null;
            }
        }

        artCache[key] = surface;
        return surface;
    }

    /// <summary>
    /// A font at the given pixel height, rasterised once and reused.
    /// </summary>
    /// <remarks>
    /// The design's requested face (<see cref="LogFont.FaceName"/>) is not honoured yet: the
    /// bundled PT Serif is used at the design's requested height and weight. Resolving a named
    /// system face is a policy question the original also had — it warned "Cannot find specified
    /// font named %s" and carried on.
    /// </remarks>
    public BitmapFont? Font(int pixelHeight)
    {
        if (rasterizer is null || !rasterizer.IsAvailable)
        {
            return null;
        }

        if (fonts.TryGetValue(pixelHeight, out var cached))
        {
            return cached;
        }

        var font = new BitmapFont(rasterizer.Rasterize(
            EmbeddedFonts.PtSerif(bold: Globals.Font.IsBold, italic: Globals.Font.Italic),
            new FontRasterOptions(pixelHeight, Antialias: true)));

        fonts[pixelHeight] = font;
        return font;
    }

    /// <summary>The height the design's <c>LOGFONT</c> asks for.</summary>
    public int RequestedFontHeight => Math.Clamp(Globals.Font.PointSizeHint, 8, 48);

    /// <summary>The level files present, in name order.</summary>
    public IReadOnlyList<string> LevelFiles =>
        Directory.Exists(Path.Combine(Root, "Data"))
            ? Directory.GetFiles(Path.Combine(Root, "Data"), "*.lvl")
                       .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary>
    /// Reads a whole level by index, wall sets included, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// The wall and background tables sit after the event list, so this needs a reader for every
    /// event type in the file. <see cref="EventBodyReader"/> aborts on one it does not know, and a
    /// level containing such an event comes back null rather than partial — the alternative is a
    /// wall table read out of the middle of an event body.
    /// </remarks>
    public LevelFile? Level(int index)
    {
        var files = LevelFiles;
        if (index < 0 || index >= files.Count)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(files[index]);
            return LevelFileReader.Read(stream, ArchiveRole.Editor,
                (ar, type, version) => EventBodyReader.TryRead(ar, type, version, ArchiveRole.Editor));
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException
                                    or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a level's map by index into <see cref="LevelFiles"/>, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Only the grid is read, which always succeeds: the cells are fixed-size and sit before the
    /// event list. <see cref="Level"/> reads everything but can fail on an unported event type, so
    /// movement uses this and wall rendering uses that.
    /// </remarks>
    public Map? Map(int index)
    {
        var files = LevelFiles;
        if (index < 0 || index >= files.Count)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(files[index]);
            var (_, width, height, cells) = LevelFileReader.ReadAreaMapOnly(stream);
            return new Map(width, height, cells);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException
                                    or EndOfStreamException)
        {
            return null;
        }
    }

    public void Dispose() => (rasterizer as IDisposable)?.Dispose();
}
