namespace UAF.Media;

/// <summary>
/// Decodes art formats that <see cref="PngDecoder"/> does not, and says up front whether it can.
/// </summary>
/// <remarks>
/// <para>
/// Same shape and same reason as <see cref="IVideoDecoderFactory"/>: the implementation is native
/// and lives in <c>UAF.Media.Sdl</c>, so this assembly must be able to run without it. A build with
/// no SDL present decodes PNG and reports the rest as unsupported, rather than failing to load.
/// </para>
/// <para>
/// The split is by format, not by "try this first". PNG stays with the managed decoder even when a
/// native one is available, because the managed one reproduces the engine's specific behaviour —
/// stripped alpha, the <c>gAMA</c>-versus-2.2 gamma convention, 16-bit chopping — and is verified
/// against libpng on the whole reference corpus. Routing PNG through a general-purpose decoder
/// would quietly change 1312 files' pixels to gain nothing.
/// </para>
/// </remarks>
public interface IImageDecoder
{
    /// <summary>Whether the decoder's native dependencies are actually present.</summary>
    bool IsAvailable { get; }

    /// <summary>A short description of why not, when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    /// <summary>Whether this decoder handles the given format.</summary>
    bool CanDecode(ImageFormat format);

    /// <summary>Decodes a file whose format has already been identified.</summary>
    DecodedImage Decode(ReadOnlySpan<byte> file, ImageFormat format);
}

/// <summary>
/// The decoder used when no native image support is installed: available for nothing.
/// </summary>
/// <remarks>
/// The default for <see cref="ImageLoader.Default"/>, so that a build without SDL behaves correctly
/// by construction rather than by every call site remembering to handle a null.
/// </remarks>
public sealed class NullImageDecoder : IImageDecoder
{
    public static readonly NullImageDecoder Instance = new();

    public bool IsAvailable => false;

    public string? UnavailableReason => "no native image decoder is installed in this build";

    public bool CanDecode(ImageFormat format) => false;

    public DecodedImage Decode(ReadOnlySpan<byte> file, ImageFormat format) =>
        throw new NotSupportedException(UnavailableReason);
}
