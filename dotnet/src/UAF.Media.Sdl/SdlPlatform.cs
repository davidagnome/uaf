using SDL;
using static SDL.SDL3;

namespace UAF.Media.Sdl;

/// <summary>Which SDL subsystems to bring up, and whether to force the headless drivers.</summary>
public sealed record SdlPlatformOptions
{
    public bool Video { get; init; } = true;

    public bool Audio { get; init; } = true;

    /// <summary>
    /// Forces SDL's <c>dummy</c> video and audio drivers, so nothing is opened and nothing is shown.
    /// See <see cref="SdlPlatform.ForceDummyDrivers"/> for why this is a hint rather than an
    /// environment variable.
    /// </summary>
    /// <remarks>
    /// The whole media layer has to work on a machine with no display, because CI has none. The
    /// alternative was learned the hard way on the C++ side: the editor cannot run in CI at all,
    /// because <c>OpenDesign</c> requires a live DirectX device (docs/PORTING-PLAN.md section 7,
    /// Phase 0). Being able to ask for headless explicitly, in-process, is what keeps that from
    /// recurring.
    /// </remarks>
    public bool Headless { get; init; }

    /// <summary>Headless with both subsystems — what the test suite uses.</summary>
    public static SdlPlatformOptions HeadlessDefaults => new() { Headless = true };
}

/// <summary>
/// Owns SDL's lifetime: initialises the subsystems once, shuts them down once.
/// </summary>
/// <remarks>
/// <para>
/// A single instance, disposed at the end, because <c>SDL_Init</c>/<c>SDL_Quit</c> are process-global.
/// <see cref="Initialize"/> reports back which drivers SDL actually chose, and that report is the thing
/// worth asserting on: "headless was requested" and "headless happened" are different claims, and the
/// spike exists because only the second one matters.
/// </para>
/// <para>
/// <b>Driver selection goes through SDL's hints, not through the environment.</b> The obvious approach —
/// <c>Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "dummy")</c> — silently does nothing on
/// macOS and Linux: .NET keeps its own copy of the environment on Unix and never calls <c>setenv</c>, so
/// a native library calling <c>getenv</c> cannot see the change. It works on Windows, which is the worst
/// possible outcome — headless mode that appears to work on one platform and fails on the other two.
/// The hint is set with <c>SDL_HINT_OVERRIDE</c> priority because SDL consults a real environment
/// variable ahead of a normal-priority hint, and an externally set driver must not win over an explicit
/// request for headless.
/// </para>
/// </remarks>
public sealed class SdlPlatform : IDisposable
{
    /// <summary>The driver name SDL uses for "initialise, but open nothing".</summary>
    public const string DummyDriver = "dummy";

    private bool disposed;

    private SdlPlatform(string? videoDriver, string? audioDriver)
    {
        VideoDriver = videoDriver;
        AudioDriver = audioDriver;
    }

    /// <summary>The video driver SDL chose, or null when video was not requested.</summary>
    public string? VideoDriver { get; }

    /// <summary>The audio driver SDL chose, or null when audio was not requested.</summary>
    public string? AudioDriver { get; }

    /// <summary>True when both requested subsystems came up on the <c>dummy</c> driver.</summary>
    public bool IsHeadless =>
        (VideoDriver is null or DummyDriver) && (AudioDriver is null or DummyDriver);

    /// <summary>
    /// Brings SDL up. Throws on failure, because a game that cannot open a window has nothing to
    /// degrade to — unlike movies or MIDI, which are optional by design.
    /// </summary>
    public static SdlPlatform Initialize(SdlPlatformOptions? options = null)
    {
        options ??= new SdlPlatformOptions();

        if (options.Headless)
        {
            ForceDummyDrivers();
        }

        var flags = (SDL_InitFlags)0;
        if (options.Video)
        {
            flags |= SDL_InitFlags.SDL_INIT_VIDEO;
        }
        if (options.Audio)
        {
            flags |= SDL_InitFlags.SDL_INIT_AUDIO;
        }

        if (!SDL_Init(flags))
        {
            throw new InvalidOperationException($"SDL_Init failed: {LastError()}");
        }

        return new SdlPlatform(
            options.Video ? SDL_GetCurrentVideoDriver() : null,
            options.Audio ? SDL_GetCurrentAudioDriver() : null);
    }

    /// <summary>SDL's last error message, or a placeholder — <c>SDL_GetError</c> can return null.</summary>
    public static string LastError() => SDL_GetError() ?? "(no SDL error reported)";

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SDL_Quit();
    }

    /// <summary>
    /// Points SDL at its <c>dummy</c> video and audio drivers, taking effect only if SDL has not
    /// initialised that subsystem yet.
    /// </summary>
    /// <remarks>
    /// Public and static so a host or a test suite can force headless before anything else touches SDL.
    /// See this class's remarks for why it sets a hint at override priority rather than an environment
    /// variable — the environment route is a silent no-op on macOS and Linux.
    /// </remarks>
    public static void ForceDummyDrivers()
    {
        SDL_SetHintWithPriority(SDL_HINT_VIDEO_DRIVER, DummyDriver,
                                SDL_HintPriority.SDL_HINT_OVERRIDE);
        SDL_SetHintWithPriority(SDL_HINT_AUDIO_DRIVER, DummyDriver,
                                SDL_HintPriority.SDL_HINT_OVERRIDE);
    }
}
