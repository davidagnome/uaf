namespace UAF.Media;

/// <summary>
/// A pull source of interleaved float PCM at <see cref="AudioFormat.Mix"/>.
/// </summary>
/// <remarks>
/// Pull, not push, so the whole chain is driven by whoever asks for samples. In the game that is
/// the audio device's callback; in a test it is the test, one buffer at a time. That is the only
/// reason the audio layer is testable at all with no sound card.
/// </remarks>
public interface IPcmSource
{
    /// <summary>
    /// Fills as much of <paramref name="destination"/> as it can and returns the number of
    /// <em>samples</em> written (frames × channels). A short read means the source ran out.
    /// </summary>
    int Read(Span<float> destination);

    /// <summary>True once the source has no more samples and will not produce any.</summary>
    bool IsFinished { get; }
}

/// <summary>
/// Decoded audio held in memory, ready to be played any number of times.
/// </summary>
/// <remarks>
/// <para>
/// Fully decoded rather than streamed. Sound effects have to start with no latency and get played
/// dozens of times (the default hit/miss/cast sounds are shared by every item, monster and spell —
/// <c>SoundMgr::AddSample</c> deduplicates them by filename for exactly that reason), and music in
/// these designs is a few minutes at most. Streaming would buy nothing but a threading problem.
/// </para>
/// <para>
/// Always stored at <see cref="AudioFormat.Mix"/>: resampling happens once, at decode, so the
/// mixer stays a summation loop.
/// </para>
/// </remarks>
public sealed class PcmData
{
    public PcmData(float[] samples, AudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(samples);

        Samples = samples;
        Format = format;
    }

    /// <summary>Interleaved samples, channel-major within a frame.</summary>
    public float[] Samples { get; }

    public AudioFormat Format { get; }

    public long FrameCount => Samples.Length / Format.Channels;

    public TimeSpan Duration => Format.DurationOfFrames(FrameCount);

    /// <summary>Creates an independent playback cursor over this data.</summary>
    public IPcmSource CreateSource(bool loop = false) => new Cursor(this, loop);

    private sealed class Cursor(PcmData data, bool loop) : IPcmSource
    {
        private int position;

        public bool IsFinished => !loop && position >= data.Samples.Length;

        public int Read(Span<float> destination)
        {
            int written = 0;

            while (written < destination.Length)
            {
                int available = data.Samples.Length - position;
                if (available <= 0)
                {
                    if (!loop || data.Samples.Length == 0)
                    {
                        break;
                    }

                    position = 0;
                    continue;
                }

                int take = Math.Min(available, destination.Length - written);
                data.Samples.AsSpan(position, take).CopyTo(destination[written..]);
                position += take;
                written += take;
            }

            return written;
        }
    }
}
