namespace UAF.Media;

/// <summary>An art file format, as <c>CDXIMAGE_*</c> named them (<c>cdx/cdximage.h</c>).</summary>
public enum ImageFormat
{
    Unknown,
    Bmp,
    Pcx,
    Png,
    Psd,
    Jpg,
    Tga,
}

/// <summary>
/// Identifies an art file by its contents and decodes it, as
/// <c>CDXImage::GetImage</c> (<c>cdx/cdximage.cpp:246</c>) did.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dispatch is by signature, not by file extension.</b> The C++ asks each format's
/// <c>Validate</c> in turn — BMP, PCX, PNG, PSD, JPG, TGA (<c>cdximage.cpp:262-312</c>) — and only
/// then switches on the result. That ordering is preserved here because it is observable: a
/// mislabelled file loads by content in the original, so a port that trusted the extension would
/// reject art the engine accepts. Design records store filenames like <c>AreaViewArt.png</c>, but
/// nothing validates that the bytes match.
/// </para>
/// <para>
/// PNG is always decoded by <see cref="PngDecoder"/>, which reproduces the engine's own behaviour
/// and is verified against libpng on the entire reference corpus. Everything else is delegated to
/// an optional <see cref="IImageDecoder"/> — in practice SDL3_image, from <c>UAF.Media.Sdl</c>.
/// When none is installed the format is still named in the exception rather than reported as
/// corrupt, because "this is a JPEG and this build has no JPEG decoder" and "these bytes are
/// nonsense" need different fixes.
/// </para>
/// </remarks>
public sealed class ImageLoader(IImageDecoder? extra = null)
{
    /// <summary>
    /// The loader used when nothing has been composed: PNG only, no native decoder.
    /// </summary>
    /// <remarks>
    /// Instance-based rather than a settable static, so that a test or a tool can construct a
    /// loader with a different decoder without mutating global state that another test is using.
    /// </remarks>
    public static ImageLoader Default { get; } = new();

    /// <summary>The decoder consulted for formats <see cref="PngDecoder"/> does not handle.</summary>
    public IImageDecoder Extra { get; } = extra ?? NullImageDecoder.Instance;

    /// <summary>
    /// Bytes needed to identify every format the original sniffed. TGA is the constraint: it has no
    /// magic number at the start and is identified from an 18-byte footer, so a short file cannot be
    /// classified at all.
    /// </summary>
    private const int SignatureBytes = 18;

    /// <summary>Identifies a format from the leading bytes of a file.</summary>
    public static ImageFormat Identify(ReadOnlySpan<byte> file)
    {
        // Order matches the C++ Validate chain. PNG's 8-byte signature is the only one of these
        // that is genuinely unambiguous.
        if (IsBmp(file))
        {
            return ImageFormat.Bmp;
        }

        if (IsPcx(file))
        {
            return ImageFormat.Pcx;
        }

        if (PngDecoder.IsPng(file))
        {
            return ImageFormat.Png;
        }

        if (IsPsd(file))
        {
            return ImageFormat.Psd;
        }

        if (IsJpg(file))
        {
            return ImageFormat.Jpg;
        }

        return ImageFormat.Unknown;
    }

    /// <summary>Decodes an art file already in memory.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a recognised image.</exception>
    /// <exception cref="NotSupportedException">The format is recognised but has no decoder.</exception>
    public DecodedImage Decode(ReadOnlySpan<byte> file)
    {
        var format = Identify(file);

        if (format == ImageFormat.Png)
        {
            return PngDecoder.Decode(file);
        }

        if (format == ImageFormat.Unknown)
        {
            throw new InvalidDataException("unrecognised image format: no signature matched");
        }

        if (Extra.IsAvailable && Extra.CanDecode(format))
        {
            return Extra.Decode(file, format);
        }

        // Naming the format and the reason separately: "we have no decoder for TGA" and "SDL3_image
        // is missing from this build" need different fixes, and neither is a corrupt file.
        throw new NotSupportedException(
            $"{format} files need a native image decoder; {Extra.UnavailableReason ?? "this one does not handle " + format}");
    }

    /// <summary>Decodes an art file from disk.</summary>
    /// <remarks>
    /// Path-based because that is what the engine has: a filename out of a design record, which is
    /// also why <c>CDXImagePNG::GetImage</c> takes a <c>CHAR*</c> and <c>fopen</c>s it. Callers that
    /// already hold bytes should use the span overload.
    /// </remarks>
    public DecodedImage Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return Decode(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException)
        {
            // The filename is the only thing that makes these diagnosable across 1300 art files.
            throw new InvalidDataException($"{Path.GetFileName(path)}: {e.Message}", e);
        }
    }

    /// <summary>Decodes an art file from disk straight into a surface of the given kind.</summary>
    public Surface LoadSurface(string path, SurfaceKind kind = SurfaceKind.Common) =>
        Load(path).ToSurface(kind);

    /// <summary>
    /// <c>CDXImageBMP::Validate</c>: the two-byte "BM" magic.
    /// </summary>
    private static bool IsBmp(ReadOnlySpan<byte> file) =>
        file.Length >= 2 && file[0] == 'B' && file[1] == 'M';

    /// <summary>
    /// <c>CDXImagePCX::Validate</c>: a 0x0A manufacturer byte, then a version libpcx recognises.
    /// </summary>
    /// <remarks>
    /// Version 1 does not exist, which is the only thing keeping this two-byte test from matching
    /// arbitrary data starting with a newline.
    /// </remarks>
    private static bool IsPcx(ReadOnlySpan<byte> file) =>
        file.Length >= 2 && file[0] == 0x0A && file[1] is 0 or 2 or 3 or 4 or 5;

    /// <summary><c>CDXImagePSD::Validate</c>: the "8BPS" magic.</summary>
    private static bool IsPsd(ReadOnlySpan<byte> file) =>
        file.Length >= 4 && file[0] == '8' && file[1] == 'B' && file[2] == 'P' && file[3] == 'S';

    /// <summary>JPEG's start-of-image marker, 0xFFD8 followed by any marker byte.</summary>
    private static bool IsJpg(ReadOnlySpan<byte> file) =>
        file.Length >= 3 && file[0] == 0xFF && file[1] == 0xD8 && file[2] == 0xFF;

    /// <summary>
    /// The number of leading bytes <see cref="Identify"/> may inspect, for callers that want to
    /// classify without reading a whole file.
    /// </summary>
    public static int SignaturePrefixLength => SignatureBytes;
}
