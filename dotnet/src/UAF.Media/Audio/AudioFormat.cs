namespace UAF.Media;

/// <summary>A PCM format: sample rate and channel count. Samples are always 32-bit float.</summary>
/// <remarks>
/// Float throughout, because everything upstream of the device is managed code that mixes: 16-bit
/// integer mixing needs saturation logic at every summation, and float does not. Conversion to
/// whatever the device wants happens once, at the device.
/// </remarks>
public readonly record struct AudioFormat(int SampleRate, int Channels)
{
    /// <summary>
    /// The rate everything is mixed at. 44.1 kHz because that is what the design assets are —
    /// <c>SoundPlaybackRate</c> in the C++ is a configuration value, but the shipped
    /// <c>.wav</c> files are 11/22/44 kHz, all of which resample to 44100 by a whole or half step.
    /// </summary>
    public static readonly AudioFormat Mix = new(44100, 2);

    public int BytesPerFrame => Channels * sizeof(float);

    public TimeSpan DurationOfFrames(long frames) =>
        TimeSpan.FromSeconds((double)frames / SampleRate);
}

/// <summary>
/// How the original classified a sound file, and by what rule.
/// </summary>
/// <remarks>
/// The original's tests are <c>strstr</c> on the lowercased name, not extension comparisons
/// (<c>Shared/SoundMgr.cpp:2907</c>), so <c>battle.mid.bak</c> is a MIDI file and any file inside a
/// folder called <c>music.mp3</c> is an MP3. Reproduced, including the order of the tests, because
/// designs in the wild have names that hit it and changing the rule would change which sounds play.
/// </remarks>
public enum AudioFileKind
{
    Unknown,

    /// <summary>A sampled effect — <c>ST_SAMPLE</c>. Played immediately, possibly many at once.</summary>
    Wave,

    /// <summary>MIDI music, rendered by a synthesiser.</summary>
    Midi,

    /// <summary>Compressed music: <c>.mp3</c>, <c>.mp2</c>, <c>.mp1</c>.</summary>
    Mpeg,
}

public static class AudioFileKinds
{
    /// <summary>
    /// Classifies a sound file the way <c>SoundMgr</c> does. Wave is tested first, then MIDI, then
    /// MPEG, matching the order the C++ predicates are called in.
    /// </summary>
    public static AudioFileKind Detect(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string lower = path.ToLowerInvariant();

        if (lower.Contains(".wav", StringComparison.Ordinal))
        {
            return AudioFileKind.Wave;
        }

        if (lower.Contains(".mid", StringComparison.Ordinal))
        {
            return AudioFileKind.Midi;
        }

        if (lower.Contains(".mp3", StringComparison.Ordinal) ||
            lower.Contains(".mp2", StringComparison.Ordinal) ||
            lower.Contains(".mp1", StringComparison.Ordinal))
        {
            return AudioFileKind.Mpeg;
        }

        return AudioFileKind.Unknown;
    }
}
