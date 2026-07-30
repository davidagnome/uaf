using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace UAF.Media.Sdl;

/// <summary>
/// Sends mixed audio to the sound card through SDL3.
/// </summary>
/// <remarks>
/// <para>
/// SDL3 audio replaces both of the original's backends. BASS is proprietary and its licence conflicts
/// with this project's GPL v2; DirectSound is Windows-only. SDL3 is zlib-licensed and already a
/// dependency for the window and input, so the port needs one native library rather than one per
/// subsystem (docs/PORTING-PLAN.md section 6).
/// </para>
/// <para>
/// One stream, float32 at the mix format, with SDL doing any conversion the device needs. The mixing
/// itself stays in managed code (<see cref="SoftwareMixer"/>) so that what the device receives can be
/// produced and asserted on without a device at all.
/// </para>
/// <para>
/// <b>The callback runs on SDL's audio thread and must not allocate.</b> It is an
/// <c>[UnmanagedCallersOnly]</c> static, because a managed delegate passed to native code has to be
/// kept alive by hand and a collected one is a crash in the audio thread rather than an exception. The
/// instance is found through a handle in the userdata pointer, and the scratch buffer is allocated once
/// at construction.
/// </para>
/// </remarks>
public sealed unsafe class SdlAudioDevice : IAudioDevice
{
    private readonly SDL_AudioStream* stream;
    private readonly GCHandle self;
    private readonly float[] scratch;
    private FillBuffer? fill;
    private bool disposed;

    /// <summary>
    /// Opens the default playback device.
    /// </summary>
    /// <param name="bufferFrames">
    /// The largest block the callback will produce in one go. 4096 frames is about 93 ms at 44.1 kHz —
    /// generous, because SDL asks for whatever the device wants and a short scratch buffer would mean
    /// several managed calls per callback.
    /// </param>
    public SdlAudioDevice(int bufferFrames = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferFrames);

        scratch = new float[bufferFrames * Format.Channels];
        self = GCHandle.Alloc(this);

        var spec = new SDL_AudioSpec
        {
            format = SDL_AudioFormat.SDL_AUDIO_F32LE,
            channels = Format.Channels,
            freq = Format.SampleRate,
        };

        stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec,
                                           &OnMoreAudio, GCHandle.ToIntPtr(self));

        if (stream is null)
        {
            self.Free();
            throw new InvalidOperationException(
                $"SDL_OpenAudioDeviceStream failed: {SdlPlatform.LastError()}");
        }
    }

    public AudioFormat Format => AudioFormat.Mix;

    public bool IsRunning { get; private set; }

    /// <summary>Frames handed to SDL since the device started.</summary>
    public long FramesDelivered { get; private set; }

    public void Start(FillBuffer fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        ObjectDisposedException.ThrowIf(disposed, this);

        this.fill = fill;
        IsRunning = true;

        // A stream opened with SDL_OpenAudioDeviceStream starts paused, which is what lets the
        // callback be installed before any audio is asked for.
        SDL_ResumeAudioStreamDevice(stream);
    }

    public void Stop()
    {
        if (disposed || !IsRunning)
        {
            return;
        }

        IsRunning = false;
        SDL_PauseAudioStreamDevice(stream);
        fill = null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        disposed = true;

        // Destroying the stream first means SDL will not enter the callback again, so the handle can
        // be freed safely afterwards.
        SDL_DestroyAudioStream(stream);

        if (self.IsAllocated)
        {
            self.Free();
        }
    }

    /// <summary>
    /// SDL's "the device wants more audio" callback. <paramref name="additionalAmount"/> is in bytes.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnMoreAudio(nint userdata, SDL_AudioStream* stream,
                                    int additionalAmount, int totalAmount)
    {
        if (userdata == 0 || additionalAmount <= 0)
        {
            return;
        }

        var handle = GCHandle.FromIntPtr(userdata);
        if (handle.Target is not SdlAudioDevice device || device.fill is null)
        {
            return;
        }

        try
        {
            device.Produce(stream, additionalAmount / sizeof(float));
        }
        catch (Exception)
        {
            // An exception unwinding into native code terminates the process. Silence is the right
            // failure here: audio dropping out is survivable, and the alternative kills the game.
            device.IsRunning = false;
        }
    }

    private void Produce(SDL_AudioStream* target, int samplesWanted)
    {
        var filler = fill;
        if (filler is null)
        {
            return;
        }

        while (samplesWanted > 0)
        {
            int chunk = Math.Min(samplesWanted, scratch.Length);
            var buffer = scratch.AsSpan(0, chunk);
            filler(buffer);

            fixed (float* pointer = buffer)
            {
                SDL_PutAudioStreamData(target, (nint)pointer, chunk * sizeof(float));
            }

            FramesDelivered += chunk / Format.Channels;
            samplesWanted -= chunk;
        }
    }
}
