namespace UAF.Media.Tests;

/// <summary>
/// Drives the whole audio backend headlessly: real decoders, real mixer, no device.
/// </summary>
public class AudioBackendTests
{
    private static TestPaths.TempFile Effect(short amplitude = 8000) =>
        TestPaths.Temp(".wav", TestAudio.Wave([amplitude, amplitude, amplitude, amplitude],
                                              44100, 1));

    [Fact]
    public void LoadingAndPlayingAnEffectProducesAudio()
    {
        using var file = Effect();
        var device = new NullAudioDevice();
        using var audio = new AudioBackend(device);

        long key = audio.AddSound(file.Path);
        Assert.NotEqual(SurfaceStore.NoSurface, key);
        Assert.True(audio.IsValidHandle(key));
        Assert.True(audio.Play(key));

        var rendered = device.Render(frames: 4).ToArray();

        Assert.Contains(rendered, sample => Math.Abs(sample) > 0.1f);
    }

    /// <summary>
    /// The same filename comes back as the same key. This is deliberate in the original: every item,
    /// monster and spell using the default hit/miss/cast sounds shares one buffer
    /// (<c>SoundMgr::AddSample</c>).
    /// </summary>
    [Fact]
    public void TheSameFileIsLoadedOnce()
    {
        using var file = Effect();
        using var audio = new AudioBackend(new NullAudioDevice());

        long first = audio.AddSound(file.Path);
        long second = audio.AddSound(file.Path);

        Assert.Equal(first, second);
    }

    [Fact]
    public void AMissingFileYieldsNoSurfaceAndAnExplanation()
    {
        using var audio = new AudioBackend(new NullAudioDevice());

        long key = audio.AddSound("/nonexistent/hit.wav");

        Assert.Equal(SurfaceStore.NoSurface, key);
        Assert.False(audio.IsValidHandle(key));
        Assert.Contains("not found", audio.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayingAnUnknownKeyIsARefusalNotACrash()
    {
        using var audio = new AudioBackend(new NullAudioDevice());

        Assert.False(audio.Play(999));
        Assert.False(audio.Play(SurfaceStore.NoSurface));
    }

    [Fact]
    public void StopSampleSilencesJustThatEffect()
    {
        using var loud = Effect(20000);
        var device = new NullAudioDevice();
        using var audio = new AudioBackend(device);

        long key = audio.AddSound(loud.Path);
        audio.Play(key, loop: true);
        Assert.Equal(1, audio.Mixer.ActiveVoiceCount);

        audio.StopSample(key);

        Assert.Equal(0, audio.Mixer.ActiveVoiceCount);
        Assert.All(device.Render(4).ToArray(), sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void RemoveSoundClearsTheCallersKey()
    {
        using var file = Effect();
        using var audio = new AudioBackend(new NullAudioDevice());

        long key = audio.AddSound(file.Path);
        Assert.True(audio.RemoveSound(ref key));
        Assert.Equal(SurfaceStore.NoSurface, key);
    }

    [Fact]
    public void DisablingEffectsStopsNewOnesStarting()
    {
        // EnableSound(ST_SAMPLE, FALSE): the engine's sound option, which must not affect music.
        using var file = Effect();
        using var audio = new AudioBackend(new NullAudioDevice()) { AreEffectsEnabled = false };

        long key = audio.AddSound(file.Path);

        Assert.False(audio.Play(key));
        Assert.Equal(0, audio.Mixer.ActiveVoiceCount);
    }

    [Fact]
    public void TheForegroundAndBackgroundQueuesAreIndependent()
    {
        using var a = Effect();
        using var b = Effect();
        using var audio = new AudioBackend(new NullAudioDevice());

        audio.QueueSound(a.Path);
        audio.QueueBackgroundSound(b.Path, isLevelMusic: true);

        audio.PlayQueue();
        audio.PlayBackgroundQueue();

        Assert.True(audio.IsQueueActive);
        Assert.True(audio.IsBackgroundQueueActive);
        Assert.True(audio.IsBackgroundQueueLevelMusic);

        audio.StopQueue();

        Assert.False(audio.IsQueueActive);
        Assert.True(audio.IsBackgroundQueueActive);
    }

    [Fact]
    public void StopAllSilencesEffectsAndBothQueues()
    {
        using var file = Effect();
        using var audio = new AudioBackend(new NullAudioDevice());

        long key = audio.AddSound(file.Path);
        audio.Play(key, loop: true);
        audio.QueueSound(file.Path);
        audio.PlayQueue(loop: true);
        audio.QueueBackgroundSound(file.Path, isLevelMusic: false);
        audio.PlayBackgroundQueue();

        audio.StopAll();

        Assert.Equal(0, audio.Mixer.ActiveVoiceCount);
        Assert.False(audio.IsQueueActive);
        Assert.False(audio.IsBackgroundQueueActive);
    }

    [Fact]
    public void MasterVolumeScalesTheOutput()
    {
        using var file = Effect(short.MaxValue);
        var device = new NullAudioDevice();
        using var audio = new AudioBackend(device) { MasterVolume = 0.5f };

        long key = audio.AddSound(file.Path);
        audio.Play(key);

        float peak = 0f;
        foreach (float sample in device.Render(4))
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        Assert.InRange(peak, 0.4f, 0.55f);
    }

    [Fact]
    public void MasterVolumeIsClamped()
    {
        using var audio = new AudioBackend(new NullAudioDevice())
        {
            MasterVolume = 5f,
        };

        Assert.Equal(1f, audio.MasterVolume);

        audio.MasterVolume = -1f;
        Assert.Equal(0f, audio.MasterVolume);
    }

    [Fact]
    public void MuteAndMusicEnableAreSeparateSwitches()
    {
        using var audio = new AudioBackend(new NullAudioDevice());

        audio.IsMuted = true;
        audio.IsMusicEnabled = false;

        Assert.True(audio.IsMuted);
        Assert.False(audio.IsMusicEnabled);

        audio.IsMuted = false;

        Assert.False(audio.IsMuted);
        Assert.False(audio.IsMusicEnabled);
    }

    [Fact]
    public void DisposeIsIdempotentAndStopsTheDevice()
    {
        var device = new NullAudioDevice();
        var audio = new AudioBackend(device);

        audio.Dispose();
        audio.Dispose();

        Assert.False(device.IsRunning);
    }

    [Fact]
    public void DecodedEffectsAreSharedBetweenKeys()
    {
        // The cache is in the loader, so two backends over one loader decode the file once.
        using var file = Effect();
        var loader = new AudioSourceLoader();
        using var audio = new AudioBackend(new NullAudioDevice(), loader);

        long key = audio.AddSound(file.Path);
        audio.Play(key);
        audio.Play(key);

        Assert.Equal(1, loader.CachedFileCount);
    }
}
