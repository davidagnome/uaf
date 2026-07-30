namespace UAF.Media;

/// <summary>Turns a sound file path into something playable, or explains why it cannot.</summary>
/// <remarks>
/// An interface so tests can supply synthetic tones instead of files, and so a host can add a format
/// without the mixer or the queues knowing. Failure is reported, never thrown: the original wrote a
/// line to the debug log and carried on when a sound was missing
/// (<c>SoundMgr::AddSound</c>), and a design with one bad filename must still be playable.
/// </remarks>
public interface IAudioSourceLoader
{
    bool TryLoad(string path, bool loop, out IPcmSource? source, out string? reason);
}

/// <summary>
/// The stock loader: WAV and MPEG decoded into memory and cached, MIDI rendered on demand.
/// </summary>
/// <remarks>
/// <para>
/// Decoded sounds are cached by path, which reproduces a deliberate behaviour of the original rather
/// than adding an optimisation: <c>SoundMgr::AddSample</c> searches for an existing buffer with the
/// same filename before loading, specifically so that every item, monster and spell using the
/// default hit/miss/cast sounds shares one copy (<c>Shared/SoundMgr.cpp:1788</c>).
/// </para>
/// <para>
/// MIDI is not cached because it is not decoded — see <see cref="MidiSynth"/>.
/// </para>
/// </remarks>
public sealed class AudioSourceLoader(MidiSynth? midiSynth = null) : IAudioSourceLoader
{
    private readonly Dictionary<string, PcmData> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The synthesiser used for <c>.mid</c>, or null when no SoundFont was configured.</summary>
    public MidiSynth? MidiSynth { get; } = midiSynth;

    /// <summary>How many distinct files have been decoded and kept.</summary>
    public int CachedFileCount => cache.Count;

    public bool TryLoad(string path, bool loop, out IPcmSource? source, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(path);

        source = null;
        reason = null;

        if (!File.Exists(path))
        {
            reason = $"file not found: {path}";
            return false;
        }

        var kind = AudioFileKinds.Detect(path);

        try
        {
            switch (kind)
            {
                case AudioFileKind.Wave:
                case AudioFileKind.Mpeg:
                    if (!cache.TryGetValue(path, out var decoded))
                    {
                        decoded = kind == AudioFileKind.Wave
                            ? WaveDecoder.Decode(path)
                            : MpegDecoder.Decode(path);
                        cache[path] = decoded;
                    }

                    source = decoded.CreateSource(loop);
                    return true;

                case AudioFileKind.Midi:
                    if (MidiSynth is null)
                    {
                        // Not an error the caller should die on: MIDI needs a SoundFont, which is
                        // an optional runtime asset. The queue treats this as it treats a missing
                        // file and moves to the next entry.
                        reason = "MIDI playback needs a SoundFont; none is configured";
                        return false;
                    }

                    source = MidiSynth.CreateSource(path, loop);
                    return true;

                default:
                    reason = $"unrecognised sound file type: {path}";
                    return false;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                        NotSupportedException or UnauthorizedAccessException)
        {
            reason = $"{Path.GetFileName(path)} could not be decoded ({ex.GetType().Name}): {ex.Message}";
            return false;
        }
    }
}
