using System.Collections.Concurrent;
using System.Reflection;

namespace UAF.Media;

/// <summary>
/// The font bundled with the port, for designs whose requested face cannot be resolved.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a bundled font is unavoidable.</b> The most common face across the reference designs is
/// <c>SYSTEM</c> — both because two designs name it and because it is what the engine substitutes
/// when a design leaves the face empty (<c>GlobalData.cpp:3901</c>). That is the Windows system
/// <i>raster</i> font: a bitmap face with no TrueType equivalent to load, on any platform,
/// including modern Windows. So the most-used face in the corpus has no faithful reproduction
/// available and needs a deliberate substitute.
/// </para>
/// <para>
/// <b>PT Serif</b> (SIL Open Font License 1.1, © ParaType Ltd — see <c>Assets/Fonts/OFL.txt</c>) is
/// that substitute. It is a text face rather than a display one, which is what this engine actually
/// needs: most text it draws is dense UI — stat lines, item names, journal entries — at the 13 and
/// 16 pixel heights designs ask for, where legibility matters more than period flavour. It covers
/// the entire Windows-1252 repertoire bar <c>0x7F</c>, which is the DEL control character.
/// </para>
/// <para>
/// All four styles are embedded rather than synthesised. A design's <c>LOGFONT</c> carries weight
/// and italic, and SDL_ttf's <c>TTF_STYLE_BOLD</c> emboldens an outline algorithmically while
/// <c>TTF_STYLE_ITALIC</c> shears it — both are visibly worse than the drawn faces, and at 13
/// pixels the synthetic italic in particular smears. That costs about 840 KB in the assembly.
/// </para>
/// <para>
/// It is a substitute, not a metric match. Nothing could be one: the original's advances came from
/// a bitmap face, so text will not wrap identically to the C++ build whatever is chosen here.
/// </para>
/// </remarks>
public static class EmbeddedFonts
{
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    /// <summary>
    /// The bundled fallback face in the requested style, as TrueType bytes.
    /// </summary>
    /// <remarks>
    /// Callers pass the design's own <c>LOGFONT</c> flags straight through — see
    /// <c>UAF.Serialization</c>'s <c>LogFont.IsBold</c> and <c>Italic</c>.
    /// </remarks>
    public static byte[] PtSerif(bool bold = false, bool italic = false) => Load(
        (bold, italic) switch
        {
            (true, true) => "PTSerif-BoldItalic",
            (true, false) => "PTSerif-Bold",
            (false, true) => "PTSerif-Italic",
            _ => "PTSerif-Regular",
        });

    /// <summary>The regular style, for callers that do not care.</summary>
    public static byte[] Default => PtSerif();

    private static byte[] Load(string stem) => Cache.GetOrAdd(stem, static key =>
    {
        string resource = $"UAF.Media.Assets.Fonts.{key}.ttf";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"embedded resource '{resource}' is missing");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    });
}
