using SDL;
using static SDL.SDL3;
using static SDL.SDL3_image;

namespace UAF.Media.Sdl;

/// <summary>
/// Decodes the legacy art formats through SDL3_image — everything the C++ sniffed except PNG.
/// </summary>
/// <remarks>
/// <para>
/// The C++ dispatched to six loaders (<c>cdx/cdximage.cpp:262-312</c>): BMP, PCX, PNG, PSD, JPG and
/// TGA. <c>UAF.Media</c> decodes PNG itself and stays free of native dependencies; this covers the
/// rest, and lives here because SDL3_image is native. A build without this assembly still runs and
/// still loads PNG — see <see cref="NullImageDecoder"/>.
/// </para>
/// <para>
/// <b>PNG is deliberately not claimed</b> by <see cref="CanDecode"/>, even though SDL3_image handles
/// it perfectly well. The managed decoder reproduces the engine's own behaviour — stripped alpha,
/// the <c>gAMA</c>-versus-2.2 gamma convention, 16-bit chopping — and is verified against libpng
/// across the whole reference corpus. Routing PNG here would change those 1312 files' pixels to
/// gain nothing.
/// </para>
/// <para>
/// <b>Alpha is forced opaque</b> on the way out, for the same reason the PNG decoder discards it:
/// transparency in this engine is the colour key, not the alpha channel. SDL3_image will happily
/// return a real alpha channel for a 32-bit TGA or BMP, and honouring it would put transparent holes
/// in art the original drew as solid.
/// </para>
/// <para>
/// No gamma correction is applied. That is not an omission: the C++ applied gamma only in the PNG
/// path, from libpng's <c>gAMA</c> handling. Its JPEG loader went through <c>OleLoadPicture</c>
/// (<c>cdximagejpg.cpp:82</c>) and the BMP, PCX and TGA loaders are straight byte copies, none of
/// which gamma-correct anything.
/// </para>
/// </remarks>
public sealed unsafe class SdlImageDecoder : IImageDecoder
{
    private readonly string? unavailable;

    public SdlImageDecoder()
    {
        // Probing at construction rather than at first use: the caller composing the loader is in a
        // position to log or degrade, whereas the art-loading call site 20 frames down is not.
        // A 1x1 BMP is the cheapest thing SDL3_image will decode, and it needs no SDL_Init.
        try
        {
            fixed (byte* probe = TinyBmp)
            {
                SDL_IOStream* io = SDL_IOFromConstMem((nint)probe, (nuint)TinyBmp.Length);
                if (io is null)
                {
                    unavailable = $"SDL_IOFromConstMem failed: {SDL_GetError()}";
                    return;
                }

                SDL_Surface* surface = IMG_Load_IO(io, true);
                if (surface is null)
                {
                    unavailable = $"SDL3_image could not decode its probe image: {SDL_GetError()}";
                    return;
                }

                SDL_DestroySurface(surface);
            }
        }
        catch (DllNotFoundException e)
        {
            unavailable = $"SDL3_image native library is not present: {e.Message}";
        }
        catch (EntryPointNotFoundException e)
        {
            unavailable = $"SDL3_image is present but too old: {e.Message}";
        }
    }

    public bool IsAvailable => unavailable is null;

    public string? UnavailableReason => unavailable;

    /// <summary>
    /// Every format the C++ sniffed except PNG, which <see cref="PngDecoder"/> owns.
    /// </summary>
    public bool CanDecode(ImageFormat format) => format switch
    {
        ImageFormat.Bmp or ImageFormat.Pcx or ImageFormat.Jpg or ImageFormat.Tga => true,

        // SDL3_image has no PSD loader. The original did (cdximagepsd.cpp), but no reference design
        // ships one, so this is a real and knowingly accepted gap rather than an oversight.
        ImageFormat.Psd => false,

        _ => false,
    };

    public DecodedImage Decode(ReadOnlySpan<byte> file, ImageFormat format)
    {
        if (!IsAvailable)
        {
            throw new NotSupportedException(unavailable);
        }

        if (!CanDecode(format))
        {
            throw new NotSupportedException($"SDL3_image does not decode {format}");
        }

        fixed (byte* bytes = file)
        {
            SDL_IOStream* io = SDL_IOFromConstMem((nint)bytes, (nuint)file.Length);
            if (io is null)
            {
                throw new InvalidDataException($"SDL_IOFromConstMem failed: {SDL_GetError()}");
            }

            // closeio: true, so SDL frees the stream on both the success and failure paths.
            SDL_Surface* decoded = IMG_Load_IO(io, true);
            if (decoded is null)
            {
                throw new InvalidDataException($"SDL3_image could not decode: {SDL_GetError()}");
            }

            try
            {
                return ToArgb(decoded);
            }
            finally
            {
                SDL_DestroySurface(decoded);
            }
        }
    }

    /// <summary>
    /// Converts a decoded SDL surface to the framebuffer's opaque ARGB8888.
    /// </summary>
    private static DecodedImage ToArgb(SDL_Surface* decoded)
    {
        // SDL does the format conversion, which saves reimplementing a palette expansion and the
        // half-dozen packed layouts SDL3_image can hand back.
        SDL_Surface* converted = SDL_ConvertSurface(decoded, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888);
        if (converted is null)
        {
            throw new InvalidDataException($"SDL_ConvertSurface failed: {SDL_GetError()}");
        }

        try
        {
            int width = converted->w;
            int height = converted->h;
            var pixels = new uint[checked(width * height)];

            // Row by row through the pitch rather than one bulk copy: an SDL surface's pitch can
            // exceed width * 4 for alignment, and a flat copy would shear the image.
            byte* rows = (byte*)converted->pixels;
            for (int y = 0; y < height; y++)
            {
                var row = new ReadOnlySpan<uint>(rows + (y * converted->pitch), width);
                for (int x = 0; x < width; x++)
                {
                    pixels[(y * width) + x] = row[x] | 0xFF000000u;
                }
            }

            return new DecodedImage(width, height, pixels);
        }
        finally
        {
            SDL_DestroySurface(converted);
        }
    }

    /// <summary>
    /// A 1x1 24bpp BMP, used only to prove the native library loads and decodes.
    /// </summary>
    private static ReadOnlySpan<byte> TinyBmp =>
    [
        0x42, 0x4D, 0x3A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
        0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
        0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
        0x13, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0x00, 0x00, 0x00,
    ];
}
