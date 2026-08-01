using SDL;
using UAF.Media.Sdl;
using static SDL.SDL3;

namespace UAF.Media.Tests;

/// <summary>
/// Brings SDL up once for the whole SDL collection.
/// </summary>
/// <remarks>
/// <c>SDL_Init</c>/<c>SDL_Quit</c> are process-global and the event queue is shared, so these tests
/// cannot run in parallel with each other. A collection fixture gives one initialisation and serialises
/// the tests that use it.
/// </remarks>
public sealed class SdlFixture : IDisposable
{
    public SdlFixture() => Platform = SdlPlatform.Initialize(SdlPlatformOptions.HeadlessDefaults);

    public SdlPlatform Platform { get; }

    public void Dispose() => Platform.Dispose();
}

/// <summary>
/// <b>Every test class that touches SDL, SDL_ttf or SDL_image belongs in this collection</b> —
/// not only the ones that take <see cref="SdlFixture"/> as a parameter.
/// </summary>
/// <remarks>
/// The fixture's <c>Dispose</c> calls <c>SDL_Quit</c>, which tears down every subsystem for the whole
/// process. A class left outside the collection runs in parallel with that teardown, and SDL3_ttf work
/// in flight when it lands takes the test host down with no failing assertion — it aborts the run
/// instead. That is exactly what happened on the Linux runner once an unrelated change altered the
/// interleaving: `FontRasterizerTests`, `SdlImageDecoderTests` and `FrameCompositionTests` all
/// constructed SDL objects from outside this collection.
/// </remarks>
[CollectionDefinition("sdl")]
public sealed class SdlCollection : ICollectionFixture<SdlFixture>;

/// <summary>
/// Runs the real SDL3 backend with no display and no sound card.
/// </summary>
/// <remarks>
/// This is the test class the layer's hard constraint hangs on. The C++ editor cannot run in CI at all,
/// because <c>OpenDesign</c> needs a live DirectX device (docs/PORTING-PLAN.md section 7, Phase 0), so
/// the equivalent C# code has to be provably runnable without one — not merely designed to be.
/// </remarks>
[Collection("sdl")]
public class SdlHeadlessTests(SdlFixture fixture)
{
    /// <summary>
    /// Asserts that headless actually happened, not merely that it was requested. The distinction is why
    /// the spike exists: a test that only checked the environment variable would pass on a machine where
    /// SDL had silently fallen back to a real driver.
    /// </summary>
    [Fact]
    public void BothSubsystemsCameUpOnTheDummyDriver()
    {
        Assert.Equal("dummy", fixture.Platform.VideoDriver);
        Assert.Equal("dummy", fixture.Platform.AudioDriver);
        Assert.True(fixture.Platform.IsHeadless);
    }

    [Fact]
    public void AWindowAndItsStreamingTextureCanBeCreatedAndPresented()
    {
        using var presenter = new SdlPresenter(320, 200, "headless test");
        var buffer = new Surface(320, 200, SurfaceKind.Buffer);

        // The spike's checkerboard: a managed framebuffer with a colour key, written entirely in C#.
        const uint key = 0xFF00FF00;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                buffer[x, y] = ((x ^ y) & 1) == 0 ? 0xFF102040u : key;
            }
        }

        presenter.Present(buffer);
        presenter.Present(buffer);

        Assert.Equal(320, presenter.Width);
        Assert.Equal(200, presenter.Height);
        Assert.NotEqual(0u, presenter.WindowId);
    }

    [Fact]
    public void PresentingAMismatchedSurfaceIsRejected()
    {
        using var presenter = new SdlPresenter(64, 64);

        Assert.Throws<ArgumentException>(() => presenter.Present(new Surface(32, 32)));
    }

    [Fact]
    public void TheRealBlitterCanDrawIntoASurfaceThatIsThenPresented()
    {
        // End to end through the parts a frame actually touches: store, blitter, sprite, presenter.
        using var presenter = new SdlPresenter(64, 32);
        var store = new SurfaceStore();

        var art = new Surface(8, 4, SurfaceKind.Sprite);
        art.Fill(0x00FF00FF);
        art[0, 0] = 0x00FF00FF;
        art[4, 2] = 0x00223344;
        art.SetColorKeyFromTopLeft();
        long artKey = store.Add(art);

        var buffer = new Surface(64, 32, SurfaceKind.Buffer);
        buffer.Fill(0x00080808);

        var sheet = new SpriteSheet(store.Get(artKey)!, frameWidth: 4, frameHeight: 4,
                                   frameCount: 2);
        var sprite = new AnimatedSprite(frameCount: 2, timeDelayMs: 50,
                                        flags: AnimationFlags.Loop);
        sprite.SetFirstFrame(0);
        sprite.AnimateNextFrame(50);

        Assert.True(Blitter.Blit(buffer, 10, 10, sheet.Surface, sheet.FrameRect(sprite.Frame)));
        presenter.Present(buffer);

        // Frame 1 is source columns 4..7; its (4,2) pixel lands at destination (10, 12).
        Assert.Equal(0xFF223344u, buffer[10, 12]);
        Assert.Equal(0xFF080808u, buffer[11, 12]);
    }

    [Fact]
    public unsafe void PushedKeyboardEventsBecomeInputEvents()
    {
        var input = new SdlInputSource(320, 200);
        DrainSdlQueue();

        PushKey(SDL_Scancode.SDL_SCANCODE_KP_7, down: true, SDL_Keymod.SDL_KMOD_LSHIFT);
        PushKey(SDL_Scancode.SDL_SCANCODE_KP_7, down: false, SDL_Keymod.SDL_KMOD_NONE);

        input.Pump();

        Assert.True(input.TryPoll(out var down));
        Assert.Equal(InputEventKind.KeyDown, down.Kind);
        Assert.Equal(VirtualKey.NumPad7, down.Key);
        Assert.Equal(KeyModifiers.Shift, down.Modifiers);

        Assert.True(input.TryPoll(out var up));
        Assert.Equal(InputEventKind.KeyUp, up.Kind);
        Assert.Equal(VirtualKey.NumPad7, up.Key);
    }

    /// <summary>
    /// The digit rows are the trap: SDL numbers 1..9 then 0, Win32 numbers 0..9. A loop written the
    /// obvious way puts the zero key one place out on both the main row and the keypad.
    /// </summary>
    [Theory]
    [InlineData(SDL_Scancode.SDL_SCANCODE_0, VirtualKey.D0)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_1, VirtualKey.D1)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_9, VirtualKey.D9)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_KP_0, VirtualKey.NumPad0)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_KP_1, VirtualKey.NumPad1)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_KP_5, VirtualKey.NumPad5)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_KP_9, VirtualKey.NumPad9)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_A, VirtualKey.A)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_Z, VirtualKey.Z)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_F1, VirtualKey.F1)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_F12, VirtualKey.F12)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_ESCAPE, VirtualKey.Escape)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_RETURN, VirtualKey.Return)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_HOME, VirtualKey.Home)]
    [InlineData(SDL_Scancode.SDL_SCANCODE_PAGEUP, VirtualKey.PageUp)]
    public void ScancodesMapToTheKeysTheEngineExpects(SDL_Scancode scancode, VirtualKey expected)
    {
        var input = new SdlInputSource(320, 200);
        DrainSdlQueue();

        PushKey(scancode, down: true, SDL_Keymod.SDL_KMOD_NONE);
        input.Pump();

        Assert.True(input.TryPoll(out var translated));
        Assert.Equal(expected, translated.Key);
    }

    [Fact]
    public void UnmappedKeysAreIgnoredRatherThanGuessed()
    {
        var input = new SdlInputSource(320, 200);
        DrainSdlQueue();

        // A key the engine has no case for. Reporting it as a plausible wrong key would be worse.
        PushKey(SDL_Scancode.SDL_SCANCODE_AC_SEARCH, down: true, SDL_Keymod.SDL_KMOD_NONE);
        input.Pump();

        Assert.False(input.TryPoll(out _));
        Assert.Equal(1, input.IgnoredCount);
    }

    [Fact]
    public unsafe void MouseEventsCarryPositionAndButton()
    {
        var input = new SdlInputSource(320, 200);
        DrainSdlQueue();

        var press = new SDL_Event
        {
            button = new SDL_MouseButtonEvent
            {
                type = SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN,
                button = (byte)SDL_BUTTON_RIGHT,
                x = 100,
                y = 50,
                down = true,
                clicks = 1,
            },
        };
        SDL_PushEvent(&press);

        var move = new SDL_Event
        {
            motion = new SDL_MouseMotionEvent
            {
                type = SDL_EventType.SDL_EVENT_MOUSE_MOTION,
                x = 12,
                y = 34,
                state = SDL_MouseButtonFlags.SDL_BUTTON_LMASK,
            },
        };
        SDL_PushEvent(&move);

        input.Pump();

        Assert.True(input.TryPoll(out var pressed));
        Assert.Equal(InputEventKind.MouseDown, pressed.Kind);
        Assert.Equal(MouseButtons.Right, pressed.Button);
        Assert.Equal(100, pressed.X);
        Assert.Equal(50, pressed.Y);

        Assert.True(input.TryPoll(out var moved));
        Assert.Equal(InputEventKind.MouseMove, moved.Kind);
        Assert.Equal(MouseButtons.Left, moved.ButtonsHeld);
        Assert.Equal(12, moved.X);
        Assert.Equal(34, moved.Y);
    }

    [Fact]
    public unsafe void QuitBecomesAQuitEvent()
    {
        var input = new SdlInputSource(320, 200);
        DrainSdlQueue();

        var quit = new SDL_Event { quit = new SDL_QuitEvent { type = SDL_EventType.SDL_EVENT_QUIT } };
        SDL_PushEvent(&quit);

        input.Pump();

        Assert.True(input.TryPoll(out var translated));
        Assert.Equal(InputEventKind.Quit, translated.Kind);
    }

    /// <summary>
    /// The audio device opens on the dummy driver, which is what a CI runner has. Whether it produces
    /// audio is covered by <see cref="MixerTests"/> over <see cref="NullAudioDevice"/>, because the
    /// dummy driver still runs on a real-time clock and asserting on its output would mean sleeping.
    /// </summary>
    [Fact]
    public void TheAudioDeviceOpensAndStartsHeadlessly()
    {
        using var device = new SdlAudioDevice(bufferFrames: 512);
        var mixer = new SoftwareMixer();

        Assert.Equal(AudioFormat.Mix, device.Format);

        device.Start(mixer.Mix);
        Assert.True(device.IsRunning);

        device.Stop();
        Assert.False(device.IsRunning);
    }

    /// <summary>
    /// Proves SDL really enters the managed audio callback.
    /// </summary>
    /// <remarks>
    /// The riskiest interop in the layer, and the only test that covers it. The callback is an
    /// <c>[UnmanagedCallersOnly]</c> static entered from SDL's audio thread; if the signature, the
    /// calling convention or the userdata handle were wrong, the failure would be a process crash in a
    /// thread nothing here owns, not an exception. The wait is bounded because the dummy driver
    /// consumes at a real-time pace — it is the one place the suite cannot be purely deterministic.
    /// </remarks>
    [Fact]
    public void TheDummyDriverPullsThroughTheManagedCallback()
    {
        using var device = new SdlAudioDevice(bufferFrames: 512);
        var mixer = new SoftwareMixer();
        mixer.Add(new PcmData(new float[AudioFormat.Mix.SampleRate * 2], AudioFormat.Mix)
                      .CreateSource(loop: true),
                  AudioChannel.Music);

        device.Start(mixer.Mix);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (device.FramesDelivered == 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(25);
        }

        Assert.True(device.FramesDelivered > 0,
                    "SDL never pulled audio through the managed callback; check the " +
                    "UnmanagedCallersOnly signature and that the device was resumed");
    }

    [Fact]
    public void TheAudioDeviceSurvivesRepeatedDisposal()
    {
        var device = new SdlAudioDevice(bufferFrames: 256);
        device.Start(_ => { });

        device.Dispose();
        device.Dispose();

        Assert.False(device.IsRunning);
    }

    [Fact]
    public void AFullBackendCanRunOverTheSdlDevice()
    {
        // The composition a real host uses. It must at least construct and tear down cleanly with no
        // sound card, because that is what CI is.
        using var file = TestPaths.Temp(".wav",
            TestAudio.Wave(TestAudio.Tone(44100, 512, 440), 44100, 1));

        using var audio = new AudioBackend(new SdlAudioDevice(bufferFrames: 256));

        long key = audio.AddSound(file.Path);
        Assert.NotEqual(SurfaceStore.NoSurface, key);
        Assert.True(audio.Play(key));
    }

    /// <summary>
    /// Empties whatever the dummy drivers or a previous test left queued, so a test only sees the events
    /// it pushed. The SDL event queue is process-wide.
    /// </summary>
    private static unsafe void DrainSdlQueue()
    {
        SDL_Event ignored;
        while (SDL_PollEvent(&ignored))
        {
        }
    }

    private static unsafe void PushKey(SDL_Scancode scancode, bool down, SDL_Keymod modifiers)
    {
        var sdlEvent = new SDL_Event
        {
            key = new SDL_KeyboardEvent
            {
                type = down ? SDL_EventType.SDL_EVENT_KEY_DOWN : SDL_EventType.SDL_EVENT_KEY_UP,
                scancode = scancode,
                mod = modifiers,
                down = down,
            },
        };

        SDL_PushEvent(&sdlEvent);
    }
}
