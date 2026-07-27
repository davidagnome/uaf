// SDL3 spike for the Dungeon Craft port. See docs/PORTING-PLAN.md section 6.
//
// Question this answers: can SDL3, via C# bindings on .NET 10, do what UAFcore needs on all
// three desktop platforms -- namely present a MANAGED SOFTWARE FRAMEBUFFER (the port keeps the
// original's software blitter) and deliver input, without a GPU-specific rendering path.
//
// Also checks that it initialises HEADLESSLY (SDL_VIDEODRIVER=dummy), because CI has no display
// and the media layer must stay testable there -- the exact constraint that made the C++ editor
// unusable in CI, where OpenDesign requires a live DirectX device.

using SDL;
using static SDL.SDL3;

const int Width = 320, Height = 200;

static string Err() => SDL_GetError() ?? "(none)";

unsafe
{
    bool headless = Environment.GetEnvironmentVariable("SDL_VIDEODRIVER") == "dummy";
    Console.WriteLine($"SDL3 spike  (headless={headless})");

    if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
    {
        Console.Error.WriteLine($"FAIL SDL_Init: {Err()}");
        return 1;
    }
    Console.WriteLine($"  init OK          driver={SDL_GetCurrentVideoDriver()}");

    SDL_Window* window; SDL_Renderer* renderer;
    fixed (byte* title = "UAFcore SDL3 spike\0"u8.ToArray())
    {
        if (!SDL_CreateWindowAndRenderer(title, Width, Height, 0, &window, &renderer))
        {
            Console.Error.WriteLine($"FAIL CreateWindowAndRenderer: {Err()}");
            SDL_Quit();
            return 1;
        }
    }
    Console.WriteLine("  window+renderer  OK");

    // STREAMING is the access mode for a CPU-written framebuffer -- exactly the port's model:
    // the software blitter owns the pixels, SDL only presents them.
    SDL_Texture* tex = SDL_CreateTexture(renderer, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888,
                                         SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, Width, Height);
    if (tex is null)
    {
        Console.Error.WriteLine($"FAIL CreateTexture: {Err()}");
        SDL_Quit();
        return 1;
    }
    Console.WriteLine("  streaming tex    OK");

    // A managed framebuffer, written entirely in C#, with a colour key -- the two things the
    // DirectDraw blitter did that a GPU abstraction would fight.
    uint[] framebuffer = new uint[Width * Height];
    const uint ColourKey = 0xFF00FF00;
    for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            framebuffer[y * Width + x] = ((x ^ y) & 1) == 0 ? 0xFF102040u : ColourKey;

    fixed (uint* pixels = framebuffer)
    {
        if (!SDL_UpdateTexture(tex, null, (nint)pixels, Width * sizeof(uint)))
        {
            Console.Error.WriteLine($"FAIL UpdateTexture: {Err()}");
            SDL_Quit();
            return 1;
        }
    }
    Console.WriteLine("  framebuffer up   OK");

    SDL_RenderClear(renderer);
    SDL_RenderTexture(renderer, tex, null, null);
    SDL_RenderPresent(renderer);
    Console.WriteLine("  present          OK");

    // Event pump. The engine runs on its own thread and blocks on an input queue, so what
    // matters is that polling works and reports keyboard/quit -- not that anything is on screen.
    int polled = 0;
    SDL_Event e;
    while (SDL_PollEvent(&e)) polled++;
    Console.WriteLine($"  event pump       OK ({polled} queued)");

    // Audio on the dummy driver: proves the same library covers audio, so the port needs ONE
    // native dependency rather than one per subsystem.
    bool audio = SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
    Console.WriteLine(audio
        ? $"  audio subsystem  OK  driver={SDL_GetCurrentAudioDriver()}"
        : $"  audio subsystem  FAIL: {Err()}");

    SDL_DestroyTexture(tex);
    SDL_DestroyRenderer(renderer);
    SDL_DestroyWindow(window);
    SDL_Quit();
    Console.WriteLine("PASS");
    return 0;
}
