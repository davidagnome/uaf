namespace UAF.Media;

/// <summary>
/// Which of the original's three volume controls a voice answers to.
/// </summary>
/// <remarks>
/// <c>SoundMgr</c> keeps <c>m_SampleVol</c>, <c>m_MusicVol</c> and <c>m_StreamVol</c> separately and
/// restores them on exit, and the engine's options screen sets them independently. Music also has
/// its own on/off switch (<c>SetMusicEnable</c>) that does not affect effects.
/// </remarks>
public enum AudioChannel
{
    /// <summary>A sound effect — <c>ST_SAMPLE</c>.</summary>
    Effect,

    /// <summary>Foreground music: the queue the engine waits on.</summary>
    Music,

    /// <summary>Background music: level and zone loops.</summary>
    Background,
}

/// <summary>
/// Sums the playing voices into one buffer, applying per-channel and master volume.
/// </summary>
/// <remarks>
/// <para>
/// Software mixing rather than one device stream per sound. The original let BASS or DirectSound do
/// this; SDL3 could too, with a stream per voice, but then every mixing decision would happen
/// inside a native library on a thread the tests cannot step. Mixing here means the exact samples
/// the device will receive can be produced and asserted on with no device at all.
/// </para>
/// <para>
/// Not thread-safe on its own. The device pulls from it on the audio callback thread while the game
/// thread starts and stops sounds, so <see cref="AudioBackend"/> owns the lock; a lock in here as
/// well would only make the per-buffer cost of holding it less obvious.
/// </para>
/// </remarks>
public sealed class SoftwareMixer
{
    private readonly List<Voice> voices = [];
    private float[] scratch = [];
    private int nextHandle = 1;

    /// <summary>Voices currently producing samples.</summary>
    public int ActiveVoiceCount => voices.Count;

    /// <summary>Master volume, 0..1 — <c>SoundMgr::SetMasterVolume</c>.</summary>
    public float MasterVolume { get; set; } = 1f;

    /// <summary>Suppresses all output without disturbing the volumes — <c>MuteVolume</c>.</summary>
    public bool IsMuted { get; set; }

    public float EffectVolume { get; set; } = 1f;

    public float MusicVolume { get; set; } = 1f;

    /// <summary>
    /// Music's separate on/off switch (<c>SetMusicEnable</c>). Distinct from
    /// <see cref="MusicVolume"/> being zero because the engine restores the previous volume when it
    /// is turned back on.
    /// </summary>
    public bool IsMusicEnabled { get; set; } = true;

    /// <summary>Starts a voice and returns a handle for stopping it.</summary>
    public int Add(IPcmSource source, AudioChannel channel, float gain = 1f)
    {
        ArgumentNullException.ThrowIfNull(source);

        int handle = nextHandle++;
        voices.Add(new Voice(handle, source, channel, gain));
        return handle;
    }

    public bool Remove(int handle) => voices.RemoveAll(voice => voice.Handle == handle) > 0;

    public int RemoveChannel(AudioChannel channel) =>
        voices.RemoveAll(voice => voice.Channel == channel);

    public void RemoveAll() => voices.Clear();

    public bool IsPlaying(int handle) => voices.Exists(voice => voice.Handle == handle);

    public bool IsChannelPlaying(AudioChannel channel) =>
        voices.Exists(voice => voice.Channel == channel);

    /// <summary>
    /// Fills <paramref name="destination"/> with the sum of the active voices, dropping any that
    /// have run out. Overwrites the buffer rather than adding to it.
    /// </summary>
    public void Mix(Span<float> destination)
    {
        destination.Clear();

        if (voices.Count == 0)
        {
            return;
        }

        if (scratch.Length < destination.Length)
        {
            scratch = new float[destination.Length];
        }

        var buffer = scratch.AsSpan(0, destination.Length);

        for (int index = voices.Count - 1; index >= 0; index--)
        {
            var voice = voices[index];
            buffer.Clear();
            int read = voice.Source.Read(buffer);

            float gain = voice.Gain * ChannelGain(voice.Channel);
            if (gain != 0f)
            {
                for (int sample = 0; sample < read; sample++)
                {
                    destination[sample] += buffer[sample] * gain;
                }
            }

            // A short read that also reports finished is the end of the voice. A short read that
            // does not is a stall the source will recover from, so the voice stays.
            if (read < buffer.Length && voice.Source.IsFinished)
            {
                voices.RemoveAt(index);
            }
        }

        // Clamped, not normalised. The original's mixers clipped too, and scaling the whole buffer
        // to fit one loud frame would make the volume of a long sound depend on its peak.
        for (int sample = 0; sample < destination.Length; sample++)
        {
            destination[sample] = Math.Clamp(destination[sample], -1f, 1f);
        }
    }

    private float ChannelGain(AudioChannel channel)
    {
        if (IsMuted)
        {
            return 0f;
        }

        float channelVolume = channel switch
        {
            AudioChannel.Effect => EffectVolume,
            _ => IsMusicEnabled ? MusicVolume : 0f,
        };

        return MasterVolume * channelVolume;
    }

    private sealed record Voice(int Handle, IPcmSource Source, AudioChannel Channel, float Gain);
}
