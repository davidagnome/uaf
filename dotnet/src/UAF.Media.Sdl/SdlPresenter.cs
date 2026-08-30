using SDL;
using static SDL.SDL3;

namespace UAF.Media.Sdl;

/// <summary>
/// Shows the software framebuffer in an SDL3 window.
/// </summary>
/// <remarks>
/// <para>
/// The model is the one the spike proved (<c>dotnet/spike/Sdl3Spike</c>): a managed
/// <c>uint[]</c> framebuffer uploaded to an <c>SDL_TEXTUREACCESS_STREAMING</c> texture and presented.
/// SDL's renderer does the scaling to the window, which is the one job it is better at than managed
/// code, and everything else stays in C#.
/// </para>
/// <para>
/// Two settings are deliberate rather than defaults. The texture's blend mode is forced to
/// <c>SDL_BLENDMODE_NONE</c> because the ported blitter's ancestry is DirectDraw, which ignored the
/// alpha byte entirely; if the texture honoured alpha, art whose source had a zero alpha channel would
/// present as a transparent hole. The scale mode is nearest-neighbour because this is pixel art
/// upscaled by whole multiples, and a smoothing filter would blur the thing the port exists to
/// preserve.
/// </para>
/// </remarks>
public sealed unsafe class SdlPresenter : IPresenter
{
    private readonly SDL_Window* window;
    private readonly SDL_Renderer* renderer;
    private readonly SDL_Texture* texture;
    private bool disposed;

    /// <summary>
    /// Creates a window and its presentation texture. <paramref name="width"/> and
    /// <paramref name="height"/> are the framebuffer's size, not necessarily the window's.
    /// </summary>
    public SdlPresenter(int width, int height, string title = "Dungeon Craft",
                        bool fullscreen = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;

        // Resizable so the player can scale the window; the logical presentation below letterboxes
        // the 4:3 framebuffer into whatever the window is, so the pixel art is never stretched.
        var flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        if (fullscreen)
        {
            flags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;
        }

        SDL_Window* createdWindow;
        SDL_Renderer* createdRenderer;

        if (!SDL_CreateWindowAndRenderer(title, width, height, flags,
                                         &createdWindow, &createdRenderer))
        {
            throw new InvalidOperationException(
                $"SDL_CreateWindowAndRenderer failed: {SdlPlatform.LastError()}");
        }

        window = createdWindow;
        renderer = createdRenderer;

        // Letterbox the fixed framebuffer into the resizable window. SDL scales the logical
        // 640x480 up or down and centres it, which is what makes an arbitrary window size draw the
        // game at its own aspect ratio rather than stretching it.
        if (!SDL_SetRenderLogicalPresentation(
                renderer, width, height,
                SDL_RendererLogicalPresentation.SDL_LOGICAL_PRESENTATION_LETTERBOX))
        {
            SDL_DestroyRenderer(renderer);
            SDL_DestroyWindow(window);
            throw new InvalidOperationException(
                $"SDL_SetRenderLogicalPresentation failed: {SdlPlatform.LastError()}");
        }

        texture = SDL_CreateTexture(renderer, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888,
                                    SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, width, height);
        if (texture is null)
        {
            SDL_DestroyRenderer(renderer);
            SDL_DestroyWindow(window);
            throw new InvalidOperationException(
                $"SDL_CreateTexture failed: {SdlPlatform.LastError()}");
        }

        SDL_SetTextureBlendMode(texture, SDL_BlendMode.SDL_BLENDMODE_NONE);
        SDL_SetTextureScaleMode(texture, SDL_ScaleMode.SDL_SCALEMODE_NEAREST);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The SDL window id, so an input source can filter events to this window.</summary>
    public uint WindowId => (uint)SDL_GetWindowID(window);

    public void Present(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(disposed, this);

        if (surface.Width != Width || surface.Height != Height)
        {
            throw new ArgumentException(
                $"surface is {surface.Width}x{surface.Height}, presenter is {Width}x{Height}",
                nameof(surface));
        }

        fixed (uint* pixels = surface.Pixels)
        {
            if (!SDL_UpdateTexture(texture, null, (nint)pixels, Width * sizeof(uint)))
            {
                throw new InvalidOperationException(
                    $"SDL_UpdateTexture failed: {SdlPlatform.LastError()}");
            }
        }

        SDL_RenderClear(renderer);
        SDL_RenderTexture(renderer, texture, null, null);
        SDL_RenderPresent(renderer);
    }

    /// <summary>Switches between fullscreen and windowed — <c>Graphics::InitGraphicsFullScreen</c>.</summary>
    public bool SetFullscreen(bool fullscreen) => SDL_SetWindowFullscreen(window, fullscreen);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SDL_DestroyTexture(texture);
        SDL_DestroyRenderer(renderer);
        SDL_DestroyWindow(window);
    }
}
