namespace UAF.Media;

/// <summary>
/// Something that consumes mixed audio. The only part of the audio path a platform implements.
/// </summary>
/// <remarks>
/// Deliberately tiny. The decoders, the mixer and the queues above it are managed and testable; all
/// a backend has to do is ask for frames and hand them to the operating system. SDL3's
/// implementation lives in <c>UAF.Media.Sdl</c>.
/// </remarks>
public interface IAudioDevice : IDisposable
{
    AudioFormat Format { get; }

    /// <summary>Begins pulling from <paramref name="fill"/>, which is called with interleaved frames.</summary>
    void Start(FillBuffer fill);

    void Stop();

    bool IsRunning { get; }
}

/// <summary>Writes one buffer of interleaved mixed audio.</summary>
public delegate void FillBuffer(Span<float> destination);

/// <summary>
/// An audio device that produces no sound and is advanced by hand.
/// </summary>
/// <remarks>
/// <para>
/// The headless counterpart to <see cref="HeadlessPresenter"/>, and the reason the audio layer can
/// be asserted on in CI. A real device pulls on its own thread at its own pace, which makes any test
/// of "what did the mixer produce" a race; this one produces exactly the frames a test asks for,
/// when it asks.
/// </para>
/// <para>
/// SDL's <c>dummy</c> audio driver is not a substitute: it does run, but on a real-time clock, so a
/// test that wants three buffers still has to sleep for them.
/// </para>
/// </remarks>
public sealed class NullAudioDevice : IAudioDevice
{
    private FillBuffer? fill;
    private float[] buffer = [];

    public AudioFormat Format => AudioFormat.Mix;

    public bool IsRunning { get; private set; }

    /// <summary>Total frames pulled since the device started.</summary>
    public long FramesRendered { get; private set; }

    /// <summary>The last buffer produced, for assertions about what would have been heard.</summary>
    public ReadOnlySpan<float> LastBuffer => buffer;

    public void Start(FillBuffer fill)
    {
        ArgumentNullException.ThrowIfNull(fill);

        this.fill = fill;
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
        fill = null;
    }

    /// <summary>Pulls exactly <paramref name="frames"/> frames and returns them.</summary>
    public ReadOnlySpan<float> Render(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);

        int samples = frames * Format.Channels;
        if (buffer.Length != samples)
        {
            buffer = new float[samples];
        }

        Array.Clear(buffer);

        if (IsRunning)
        {
            fill?.Invoke(buffer);
            FramesRendered += frames;
        }

        return buffer;
    }

    /// <summary>Pulls a duration's worth of audio, for tests written in terms of time.</summary>
    public ReadOnlySpan<float> Render(TimeSpan duration) =>
        Render(Math.Max(1, (int)(duration.TotalSeconds * Format.SampleRate)));

    public void Dispose() => Stop();
}
