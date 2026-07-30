using MeltySynth;

namespace UAF.Media;

/// <summary>
/// Renders MIDI music with a SoundFont, replacing the original's <c>winmm</c> MIDI device.
/// </summary>
/// <remarks>
/// <para>
/// The original handed <c>.mid</c> files to the Windows MIDI mapper, which played them on whatever
/// synthesiser the machine had — a wavetable card, or Microsoft's GS software synth. Neither exists
/// on macOS or Linux, and there is no cross-platform equivalent, so the port has to synthesise the
/// audio itself. MeltySynth is a pure-C# SoundFont 2 synthesiser (MIT), which also means MIDI
/// renders identically on all three platforms instead of sounding like whatever the user's sound
/// card decided.
/// </para>
/// <para>
/// <b>It needs a SoundFont, and this repository does not ship one.</b> A General MIDI SoundFont is
/// a separate, large asset with its own licence, so it has to be supplied at runtime. Everything
/// here therefore degrades rather than throws: with no SoundFont configured,
/// <see cref="IsAvailable"/> is false and the music queue skips <c>.mid</c> entries the way it
/// skips a missing file. That is the same contract movie playback has, for the same reason — a
/// design must still run when an optional asset is absent.
/// </para>
/// <para>
/// Unlike <see cref="WaveDecoder"/> and <see cref="MpegDecoder"/>, MIDI is rendered as it plays.
/// Fully rendering a three-minute sequence at the mix format would be about 60 MB of floats, and
/// unlike a sound effect it is played once.
/// </para>
/// </remarks>
public sealed class MidiSynth
{
    private readonly SoundFont soundFont;

    private MidiSynth(SoundFont soundFont, string path)
    {
        this.soundFont = soundFont;
        SoundFontPath = path;
    }

    /// <summary>Where the loaded SoundFont came from.</summary>
    public string SoundFontPath { get; }

    /// <summary>
    /// The environment variable a host or a test uses to point the port at a SoundFont, checked by
    /// <see cref="TryCreateFromEnvironment"/>.
    /// </summary>
    public const string SoundFontEnvironmentVariable = "UAF_SOUNDFONT";

    /// <summary>
    /// Loads a SoundFont, reporting failure rather than throwing so a missing or corrupt file
    /// degrades MIDI to silence instead of stopping the game.
    /// </summary>
    public static bool TryCreate(string soundFontPath, out MidiSynth? synth, out string? error)
    {
        synth = null;
        error = null;

        if (string.IsNullOrWhiteSpace(soundFontPath))
        {
            error = "no SoundFont path given";
            return false;
        }

        if (!File.Exists(soundFontPath))
        {
            error = $"SoundFont not found: {soundFontPath}";
            return false;
        }

        try
        {
            synth = new MidiSynth(new SoundFont(soundFontPath), soundFontPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                         NotSupportedException or UnauthorizedAccessException)
        {
            error = $"SoundFont could not be read ({ex.GetType().Name}): {ex.Message}";
            return false;
        }
    }

    /// <summary>Loads the SoundFont named by <see cref="SoundFontEnvironmentVariable"/>, if any.</summary>
    public static bool TryCreateFromEnvironment(out MidiSynth? synth, out string? error)
    {
        string? path = Environment.GetEnvironmentVariable(SoundFontEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            synth = null;
            error = $"{SoundFontEnvironmentVariable} is not set";
            return false;
        }

        return TryCreate(path, out synth, out error);
    }

    /// <summary>Starts rendering a MIDI file.</summary>
    public IPcmSource CreateSource(string midiPath, bool loop = false)
    {
        ArgumentNullException.ThrowIfNull(midiPath);

        using var stream = File.OpenRead(midiPath);
        return CreateSource(stream, loop);
    }

    public IPcmSource CreateSource(Stream midi, bool loop = false)
    {
        ArgumentNullException.ThrowIfNull(midi);

        var synthesizer = new Synthesizer(soundFont,
                                          new SynthesizerSettings(AudioFormat.Mix.SampleRate));
        var sequencer = new MidiFileSequencer(synthesizer);
        sequencer.Play(new MidiFile(midi), loop);

        return new SequencerSource(sequencer, loop);
    }

    /// <summary>
    /// Reads a MIDI file's length without needing a SoundFont — parsing and synthesis are separate
    /// in MeltySynth, which is what lets a build with no SoundFont still validate the file.
    /// </summary>
    public static bool TryReadDuration(Stream midi, out TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(midi);

        try
        {
            duration = new MidiFile(midi).Length;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                         EndOfStreamException or NotSupportedException)
        {
            duration = default;
            return false;
        }
    }

    /// <remarks>
    /// MeltySynth renders into separate channel spans, so this owns the two scratch buffers and the
    /// interleave. It renders in blocks rather than one sample at a time because the synthesiser's
    /// per-call overhead dominates at small sizes.
    /// </remarks>
    private sealed class SequencerSource(MidiFileSequencer sequencer, bool loop) : IPcmSource
    {
        private const int BlockFrames = 1024;

        private readonly float[] left = new float[BlockFrames];
        private readonly float[] right = new float[BlockFrames];
        private bool finished;

        public bool IsFinished => finished;

        public int Read(Span<float> destination)
        {
            if (finished)
            {
                return 0;
            }

            int channels = AudioFormat.Mix.Channels;
            int framesWanted = destination.Length / channels;
            int framesWritten = 0;

            while (framesWritten < framesWanted)
            {
                if (!loop && sequencer.EndOfSequence)
                {
                    finished = true;
                    break;
                }

                int block = Math.Min(BlockFrames, framesWanted - framesWritten);
                sequencer.Render(left.AsSpan(0, block), right.AsSpan(0, block));

                for (int frame = 0; frame < block; frame++)
                {
                    int at = (framesWritten + frame) * channels;
                    destination[at] = left[frame];
                    destination[at + 1] = right[frame];
                }

                framesWritten += block;
            }

            return framesWritten * channels;
        }
    }
}
