using SDL;
using static SDL.SDL3;
using static SDL.SDL3_ttf;

namespace UAF.Media.Sdl;

/// <summary>
/// Rasterises a TrueType face into a <see cref="FontAtlas"/> with SDL3_ttf, replacing
/// <c>CDXBitmapFont::PaintCharactersInSurface</c>'s GDI <c>TextOut</c> loop
/// (<c>UAFWin/CDXBitmapFont.cpp:246</c>).
/// </summary>
/// <remarks>
/// <para>
/// The structure is the original's: measure all 256 characters, pack them into a sheet, then draw
/// each one into its cell. What differs is that GDI drew coloured text and this draws white on a
/// keyed background, because the port tints at blit time instead of keeping eleven atlases — see
/// <see cref="FontAtlas"/>.
/// </para>
/// <para>
/// <b>The original clipped each glyph to its own cell</b> (<c>CDXBitmapFont.cpp:277-282</c>) with
/// the comment that letters like 'j' otherwise reach into their neighbours' bitmaps. That is not
/// needed here: SDL3_ttf renders each glyph to its own surface, so a descender cannot bleed. The
/// consequence is a real difference, and the better one — the original silently truncated
/// overhanging glyphs, this does not.
/// </para>
/// </remarks>
public sealed unsafe class SdlFontRasterizer : IFontRasterizer, IDisposable
{
    private const uint Key = 0xFF000000;

    private readonly string? unavailable;
    private bool initialised;
    private bool disposed;

    public SdlFontRasterizer()
    {
        try
        {
            // Touch core SDL first. SDL3_ttf's dylib links libSDL3 through @rpath, and a .NET host
            // dlopen()s it without an LC_RPATH that resolves it -- so loading SDL3_ttf into a
            // process that has not already loaded libSDL3 fails with "no LC_RPATH's found".
            // Whether that happens depended entirely on which decoder a caller constructed first,
            // which is exactly the sort of dependency that works in tests and fails in the
            // application. This call makes the order explicit and harmless.
            _ = SDL_GetVersion();

            if (!TTF_Init())
            {
                unavailable = $"TTF_Init failed: {SDL_GetError()}";
                return;
            }

            initialised = true;
        }
        catch (DllNotFoundException e)
        {
            unavailable = $"SDL3_ttf native library is not present: {e.Message}";
        }
        catch (EntryPointNotFoundException e)
        {
            unavailable = $"SDL3_ttf is present but too old: {e.Message}";
        }
    }

    public bool IsAvailable => unavailable is null;

    public string? UnavailableReason => unavailable;

    public FontAtlas Rasterize(ReadOnlySpan<byte> fontFile, FontRasterOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!IsAvailable)
        {
            throw new NotSupportedException(unavailable);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PixelHeight);

        fixed (byte* bytes = fontFile)
        {
            SDL_IOStream* io = SDL_IOFromConstMem((nint)bytes, (nuint)fontFile.Length);
            if (io is null)
            {
                throw new InvalidDataException($"SDL_IOFromConstMem failed: {SDL_GetError()}");
            }

            TTF_Font* font = TTF_OpenFontIO(io, true, options.PixelHeight);
            if (font is null)
            {
                throw new InvalidDataException($"TTF_OpenFontIO failed: {SDL_GetError()}");
            }

            try
            {
                return Build(font, options);
            }
            finally
            {
                TTF_CloseFont(font);
            }
        }
    }

    private static FontAtlas Build(TTF_Font* font, FontRasterOptions options)
    {
        // Mono hinting when not antialiasing, so the outline snaps to the pixel grid instead of
        // being thresholded from a grey coverage map. Without it a 13px blackletter face loses
        // whole strokes rather than merely hardening its edges.
        TTF_SetFontHinting(font, options.Antialias
            ? TTF_HintingFlags.TTF_HINTING_NORMAL
            : TTF_HintingFlags.TTF_HINTING_MONO);

        var style = TTF_FontStyleFlags.TTF_STYLE_NORMAL;
        if (options.Bold)
        {
            style |= TTF_FontStyleFlags.TTF_STYLE_BOLD;
        }
        if (options.Italic)
        {
            style |= TTF_FontStyleFlags.TTF_STYLE_ITALIC;
        }
        TTF_SetFontStyle(font, style);

        var rendered = new SurfaceHolder[FontAtlas.CharacterCount];
        var extents = new (int Width, int Height)[FontAtlas.CharacterCount];

        try
        {
            for (int i = 0; i < FontAtlas.CharacterCount; i++)
            {
                rendered[i] = RenderOne(font, (byte)i, options.Antialias, out int advance);
                extents[i] = (advance, rendered[i].Height);
            }

            var glyphs = FontAtlas.Layout(extents, FontAtlas.DefaultSheetWidth, out int sheetHeight);
            var sheet = new Surface(FontAtlas.DefaultSheetWidth, Math.Max(1, sheetHeight),
                                    SurfaceKind.Font);
            sheet.Fill(Key);
            sheet.ColorKey = Key;

            for (int i = 0; i < FontAtlas.CharacterCount; i++)
            {
                rendered[i].CopyInto(sheet, glyphs[i].Source.Left, glyphs[i].Source.Top);
            }

            return new FontAtlas(sheet, glyphs);
        }
        finally
        {
            foreach (var holder in rendered)
            {
                holder.Dispose();
            }
        }
    }

    /// <summary>
    /// Renders one codepage byte, returning its pixels and the advance the atlas should step by.
    /// </summary>
    /// <remarks>
    /// The advance comes from <c>TTF_GetStringSize</c> rather than from the rendered surface's
    /// width, because those are different numbers and the original used the former:
    /// <c>GetTextExtentPoint32</c> reports the advance, which for a space is non-zero while its
    /// rendered bitmap is empty. Using the surface width would collapse every space in the game.
    /// </remarks>
    private static SurfaceHolder RenderOne(TTF_Font* font, byte value, bool antialias,
                                           out int advance)
    {
        // Windows-1252 to Unicode, then UTF-8 for SDL. The 0x80-0x9F block is where the two
        // codepages genuinely differ, and it is exactly the range a naive Latin-1 cast gets wrong.
        string text = CodepageText(value);

        int width = 0, height = 0;
        if (TTF_GetStringSize(font, text, 0, &width, &height))
        {
            advance = width;
        }
        else
        {
            advance = 0;
        }

        var white = new SDL_Color { r = 255, g = 255, b = 255, a = 255 };
        SDL_Surface* glyph = antialias
            ? TTF_RenderText_Blended(font, text, 0, white)
            : TTF_RenderText_Solid(font, text, 0, white);

        if (glyph is null)
        {
            // Not an error: control characters and codepoints the face lacks render as nothing,
            // and the original's TextOut drew nothing for them too.
            return SurfaceHolder.Empty(Math.Max(0, advance), TTF_GetFontHeight(font));
        }

        return SurfaceHolder.Adopt(glyph);
    }

    /// <summary>
    /// The Unicode text for one codepage byte. Windows-1252, not Latin-1: the two agree everywhere
    /// except 0x80-0x9F, which is where the typographic punctuation lives.
    /// </summary>
    private static string CodepageText(byte value) => Cp1252.GetString([value]);

    private static readonly System.Text.Encoding Cp1252 = CreateCp1252();

    private static System.Text.Encoding CreateCp1252()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (initialised)
        {
            TTF_Quit();
            initialised = false;
        }
    }

    /// <summary>Owns a rendered SDL surface and copies it into the atlas sheet.</summary>
    private readonly struct SurfaceHolder(SDL_Surface* surface, int width, int height) : IDisposable
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public static SurfaceHolder Adopt(SDL_Surface* glyph)
        {
            SDL_Surface* converted =
                SDL_ConvertSurface(glyph, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888);
            SDL_DestroySurface(glyph);

            if (converted is null)
            {
                throw new InvalidDataException($"SDL_ConvertSurface failed: {SDL_GetError()}");
            }

            return new SurfaceHolder(converted, converted->w, converted->h);
        }

        public static SurfaceHolder Empty(int width, int height) =>
            new(null, width, Math.Max(0, height));

        /// <summary>
        /// Copies the glyph into the sheet, treating a fully transparent source pixel as
        /// background so the sheet's colour key keeps working.
        /// </summary>
        public void CopyInto(Surface sheet, int x, int y)
        {
            if (surface is null)
            {
                return;
            }

            byte* rows = (byte*)surface->pixels;
            for (int row = 0; row < Height; row++)
            {
                int targetY = y + row;
                if (targetY < 0 || targetY >= sheet.Height)
                {
                    continue;
                }

                var source = new ReadOnlySpan<uint>(rows + (row * surface->pitch), surface->w);
                for (int column = 0; column < surface->w; column++)
                {
                    int targetX = x + column;
                    if (targetX < 0 || targetX >= sheet.Width)
                    {
                        continue;
                    }

                    uint pixel = source[column];
                    byte coverage = (byte)(pixel >> 24);

                    // Coverage is stored as a white-to-black ramp, not thresholded. The sheet's
                    // alpha byte cannot carry it -- Surface treats alpha as meaningless and forces
                    // it opaque -- so the grey level is the coverage, which works because glyphs
                    // are rendered white on black. BitmapFont reads it back as a blend weight.
                    //
                    // Thresholding here instead (anything non-zero becomes ink) does not produce
                    // aliased text, it produces *dilated* text: every partially covered edge pixel
                    // is promoted to full ink and the face gains roughly a pixel of weight all
                    // round. Solid rendering already gives 0 or 255, so this costs that path
                    // nothing.
                    sheet[targetX, targetY] =
                        0xFF000000u | ((uint)coverage << 16) | ((uint)coverage << 8) | coverage;
                }
            }
        }

        public void Dispose()
        {
            if (surface is not null)
            {
                SDL_DestroySurface(surface);
            }
        }
    }
}
