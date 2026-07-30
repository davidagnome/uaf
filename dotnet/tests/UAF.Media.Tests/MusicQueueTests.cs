namespace UAF.Media.Tests;

/// <summary>
/// Plays the music queues through, which the original could not be made to do in a test: it ran each
/// queue on its own thread and signalled a Win32 event.
/// </summary>
public class MusicQueueTests
{
    /// <summary>
    /// A loader that hands out a one-frame tone per name and refuses anything starting with "missing".
    /// </summary>
    private sealed class FakeLoader : IAudioSourceLoader
    {
        private readonly Dictionary<string, float> tones = [];

        public List<string> Requested { get; } = [];

        public FakeLoader With(string name, float value)
        {
            tones[name] = value;
            return this;
        }

        public bool TryLoad(string path, bool loop, out IPcmSource? source, out string? reason)
        {
            Requested.Add(path);

            if (!tones.TryGetValue(path, out float value))
            {
                source = null;
                reason = $"file not found: {path}";
                return false;
            }

            // Two frames each, so a queue entry is consumed in a predictable number of samples.
            source = new PcmData([value, value, value, value], AudioFormat.Mix).CreateSource(loop);
            reason = null;
            return true;
        }
    }

    [Fact]
    public void EntriesPlayInInsertionOrder()
    {
        var loader = new FakeLoader().With("a", 0.1f).With("b", 0.2f);
        var queue = new MusicQueue(loader);
        queue.Add("a");
        queue.Add("b");
        queue.Play(loop: false);

        var buffer = new float[8];
        int written = queue.Read(buffer);

        Assert.Equal(8, written);
        Assert.Equal([0.1f, 0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.2f, 0.2f], buffer);
    }

    [Fact]
    public void ALoopingQueueRestartsAtTheHead()
    {
        var loader = new FakeLoader().With("a", 0.1f).With("b", 0.2f);
        var queue = new MusicQueue(loader);
        queue.Add("a");
        queue.Add("b");
        queue.Play(loop: true);

        var buffer = new float[12];
        Assert.Equal(12, queue.Read(buffer));

        // a, b, then a again -- the loop is over the whole list, not the individual entry.
        Assert.Equal([0.1f, 0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.2f, 0.2f, 0.1f, 0.1f, 0.1f, 0.1f],
                     buffer);
        Assert.True(queue.IsPlaying);
    }

    /// <summary>
    /// A drained non-looping queue empties its list, which is how the engine tells a spent queue from a
    /// stopped one — <c>SoundQueue::Thread</c> calls <c>Clear()</c> on the way out.
    /// </summary>
    [Fact]
    public void ADrainedQueueClearsItselfAndReportsFinished()
    {
        var loader = new FakeLoader().With("a", 0.1f);
        var queue = new MusicQueue(loader);
        queue.Add("a");

        bool finished = false;
        queue.Finished += () => finished = true;

        queue.Play(loop: false);
        queue.Read(new float[64]);

        Assert.True(finished);
        Assert.False(queue.IsPlaying);
        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// A deliberate divergence: <c>SoundQueue::Thread</c> ends the whole queue when a file will not
    /// play, so one missing sound silences everything after it. Here the entry is skipped and reported.
    /// </summary>
    [Fact]
    public void AnUnplayableEntryIsSkippedRatherThanEndingTheQueue()
    {
        var loader = new FakeLoader().With("good", 0.3f);
        var queue = new MusicQueue(loader);
        queue.Add("missing.mid");
        queue.Add("good");
        queue.Play(loop: false);

        var buffer = new float[4];
        Assert.Equal(4, queue.Read(buffer));

        Assert.All(buffer, sample => Assert.Equal(0.3f, sample));
        Assert.Equal(1, queue.SkippedCount);
        Assert.Contains("missing.mid", queue.LastError!, StringComparison.Ordinal);
    }

    [Fact]
    public void AQueueOfNothingButUnplayableEntriesTerminates()
    {
        var queue = new MusicQueue(new FakeLoader());
        queue.Add("missing-one");
        queue.Add("missing-two");
        queue.Play(loop: true);

        Assert.Equal(0, queue.Read(new float[16]));
        Assert.False(queue.IsPlaying);
    }

    [Fact]
    public void StopLeavesTheListSoItCanBeReplayed()
    {
        var loader = new FakeLoader().With("a", 0.5f);
        var queue = new MusicQueue(loader);
        queue.Add("a");
        queue.Play(loop: false);
        queue.Stop();

        Assert.False(queue.IsPlaying);
        Assert.Equal(1, queue.Count);
        Assert.Equal(0, queue.Read(new float[4]));

        queue.Play(loop: false);
        Assert.Equal(4, queue.Read(new float[4]));
    }

    [Fact]
    public void PlayingAnEmptyQueueDoesNothing()
    {
        var queue = new MusicQueue(new FakeLoader());

        queue.Play(loop: true);

        Assert.False(queue.IsPlaying);
        Assert.Equal(0, queue.Read(new float[4]));
    }

    /// <summary>
    /// Level music and zone music are the same class with a flag, because entering a zone replaces
    /// zone music but must leave level music alone — <c>BackgroundSoundQueue::m_IsLevel</c>.
    /// </summary>
    [Fact]
    public void LevelMusicFlagIsCarried()
    {
        var queue = new MusicQueue(new FakeLoader()) { IsLevelMusic = false };

        Assert.False(queue.IsLevelMusic);
    }

    [Fact]
    public void MidiEntriesAreSkippedWhenNoSoundFontIsConfigured()
    {
        // The real loader, no SoundFont: a design's MIDI music must not stop the game or the queue.
        using var midi = TestPaths.Temp(".mid", TestAudio.MidiOneNote());
        using var wave = TestPaths.Temp(".wav",
            TestAudio.Wave([8000, 8000, 8000, 8000], 44100, 1));

        var queue = new MusicQueue(new AudioSourceLoader(midiSynth: null));
        queue.Add(midi.Path);
        queue.Add(wave.Path);
        queue.Play(loop: false);

        int written = queue.Read(new float[16]);

        Assert.True(written > 0, "the WAV after the skipped MIDI should still have played");
        Assert.Equal(1, queue.SkippedCount);
        Assert.Contains("SoundFont", queue.LastError!, StringComparison.OrdinalIgnoreCase);
    }
}
