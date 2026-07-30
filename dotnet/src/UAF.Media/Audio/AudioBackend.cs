namespace UAF.Media;

/// <summary>
/// What the engine calls to make noise — the replacement for <c>SoundMgr</c>
/// (<c>Shared/SoundMgr.h:296</c>) and the global <c>pSndMgr</c>.
/// </summary>
/// <remarks>
/// <para>
/// An interface because the audio backend was the one dependency the plan explicitly kept swappable
/// (section 9, "BASS licensing / audio backend choice"). BASS is proprietary and its licence
/// conflicts with this project's GPL v2, so it is out; SDL3 audio replaces it. Keeping the seam means
/// that decision can be revisited without touching the engine.
/// </para>
/// <para>
/// Sound effects are addressed by <c>long</c> key, as in the original: the engine stores keys in game
/// data (<c>ITEM_DATA</c>'s hit sound, a wall slot's <c>hsound</c>) and asks the manager to resolve
/// them at play time.
/// </para>
/// </remarks>
public interface IAudioBackend : IDisposable
{
    /// <summary>Loads a sound effect and returns its key, or <see cref="SurfaceStore.NoSurface"/>.</summary>
    long AddSound(string path);

    bool RemoveSound(ref long key);

    bool IsValidHandle(long key);

    /// <summary>Plays a loaded effect — <c>SoundMgr::Play</c>.</summary>
    bool Play(long key, bool loop = false);

    void StopSample(long key);

    void StopAll();

    /// <summary>Adds to the foreground music queue — <c>QueueSound</c>.</summary>
    void QueueSound(string path);

    void PlayQueue(bool loop = false);

    void StopQueue();

    bool IsQueueActive { get; }

    /// <summary>Adds to the background music queue — <c>QueueBgndSound</c>.</summary>
    void QueueBackgroundSound(string path, bool isLevelMusic);

    void PlayBackgroundQueue(bool loop = true);

    void StopBackgroundQueue();

    bool IsBackgroundQueueActive { get; }

    /// <summary><c>IsBgndQueueLevelMusic</c>: level music, as opposed to zone music.</summary>
    bool IsBackgroundQueueLevelMusic { get; }

    /// <summary>0..1 — <c>SetMasterVolume</c>/<c>GetMasterVolume</c>.</summary>
    float MasterVolume { get; set; }

    /// <summary><c>SetMusicEnable</c>/<c>GetMusicEnable</c>.</summary>
    bool IsMusicEnabled { get; set; }

    /// <summary><c>MuteVolume</c>/<c>UnMuteVolume</c>.</summary>
    bool IsMuted { get; set; }

    /// <summary>Turns effect playback on or off wholesale — <c>EnableSound(ST_SAMPLE, …)</c>.</summary>
    bool AreEffectsEnabled { get; set; }
}

/// <summary>
/// The stock audio backend: decoders and a software mixer feeding one
/// <see cref="IAudioDevice"/>.
/// </summary>
/// <remarks>
/// <para>
/// One device stream for everything, rather than one per sound. That is what makes the mixing
/// observable (see <see cref="SoftwareMixer"/>), and it also means the port opens exactly one audio
/// device — the original opened a BASS channel per sample and hit the driver's channel limit on
/// designs with many simultaneous effects.
/// </para>
/// <para>
/// <b>Locking.</b> The device pulls on its own thread while the engine thread starts and stops
/// sounds, so every mutation and the mix itself take one lock. The mix holds it for the duration of
/// a buffer, which at the sizes SDL asks for is well under a millisecond of work, and the alternative
/// — lock-free voice lists — is not worth the failure modes for a program that plays a handful of
/// sounds at once.
/// </para>
/// </remarks>
public sealed class AudioBackend : IAudioBackend
{
    private readonly object gate = new();
    private readonly IAudioDevice device;
    private readonly IAudioSourceLoader loader;
    private readonly SoftwareMixer mixer = new();
    private readonly Dictionary<long, string> effects = [];
    private readonly Dictionary<long, List<int>> voices = [];
    private readonly MusicQueue musicQueue;
    private readonly MusicQueue backgroundQueue;
    private int musicVoice = -1;
    private int backgroundVoice = -1;
    private long nextKey = 1;
    private bool disposed;

    public AudioBackend(IAudioDevice device, IAudioSourceLoader? loader = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        this.device = device;
        this.loader = loader ?? new AudioSourceLoader();
        musicQueue = new MusicQueue(this.loader);
        backgroundQueue = new MusicQueue(this.loader);

        device.Start(Fill);
    }

    /// <summary>The mixer, exposed for the per-channel volumes and for tests that assert on voices.</summary>
    public SoftwareMixer Mixer => mixer;

    /// <summary>Why the most recent load failed, or null. The port's answer to a debug-log line.</summary>
    public string? LastError { get; private set; }

    public long AddSound(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        lock (gate)
        {
            // Deduplicated by path, matching SoundMgr::AddSample -- the same key comes back for the
            // shared default hit/miss/cast sounds instead of a second copy of the samples.
            foreach (var (existing, file) in effects)
            {
                if (string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }

            if (!loader.TryLoad(path, loop: false, out _, out string? reason))
            {
                LastError = reason;
                return SurfaceStore.NoSurface;
            }

            long key = nextKey++;
            effects[key] = path;
            return key;
        }
    }

    public bool RemoveSound(ref long key)
    {
        lock (gate)
        {
            StopSampleLocked(key);
            bool removed = effects.Remove(key);
            key = SurfaceStore.NoSurface;
            return removed;
        }
    }

    public bool IsValidHandle(long key)
    {
        lock (gate)
        {
            return effects.ContainsKey(key);
        }
    }

    public bool Play(long key, bool loop = false)
    {
        lock (gate)
        {
            if (!AreEffectsEnabled || !effects.TryGetValue(key, out string? path))
            {
                return false;
            }

            if (!loader.TryLoad(path, loop, out var source, out string? reason))
            {
                LastError = reason;
                return false;
            }

            int handle = mixer.Add(source!, AudioChannel.Effect);
            if (!voices.TryGetValue(key, out var list))
            {
                voices[key] = list = [];
            }
            list.Add(handle);
            return true;
        }
    }

    public void StopSample(long key)
    {
        lock (gate)
        {
            StopSampleLocked(key);
        }
    }

    public void StopAll()
    {
        lock (gate)
        {
            mixer.RemoveAll();
            voices.Clear();
            musicQueue.Stop();
            backgroundQueue.Stop();
            musicVoice = -1;
            backgroundVoice = -1;
        }
    }

    public void QueueSound(string path)
    {
        lock (gate)
        {
            musicQueue.Add(path);
        }
    }

    public void PlayQueue(bool loop = false)
    {
        lock (gate)
        {
            StartQueueLocked(musicQueue, AudioChannel.Music, ref musicVoice, loop);
        }
    }

    public void StopQueue()
    {
        lock (gate)
        {
            musicQueue.Stop();
            if (musicVoice >= 0)
            {
                mixer.Remove(musicVoice);
                musicVoice = -1;
            }
        }
    }

    public bool IsQueueActive
    {
        get { lock (gate) { return musicQueue.IsPlaying; } }
    }

    public void QueueBackgroundSound(string path, bool isLevelMusic)
    {
        lock (gate)
        {
            backgroundQueue.IsLevelMusic = isLevelMusic;
            backgroundQueue.Add(path);
        }
    }

    public void PlayBackgroundQueue(bool loop = true)
    {
        lock (gate)
        {
            StartQueueLocked(backgroundQueue, AudioChannel.Background, ref backgroundVoice, loop);
        }
    }

    public void StopBackgroundQueue()
    {
        lock (gate)
        {
            backgroundQueue.Stop();
            if (backgroundVoice >= 0)
            {
                mixer.Remove(backgroundVoice);
                backgroundVoice = -1;
            }
        }
    }

    public bool IsBackgroundQueueActive
    {
        get { lock (gate) { return backgroundQueue.IsPlaying; } }
    }

    public bool IsBackgroundQueueLevelMusic
    {
        get { lock (gate) { return backgroundQueue.IsPlaying && backgroundQueue.IsLevelMusic; } }
    }

    public float MasterVolume
    {
        get { lock (gate) { return mixer.MasterVolume; } }
        set { lock (gate) { mixer.MasterVolume = Math.Clamp(value, 0f, 1f); } }
    }

    public bool IsMusicEnabled
    {
        get { lock (gate) { return mixer.IsMusicEnabled; } }
        set { lock (gate) { mixer.IsMusicEnabled = value; } }
    }

    public bool IsMuted
    {
        get { lock (gate) { return mixer.IsMuted; } }
        set { lock (gate) { mixer.IsMuted = value; } }
    }

    public bool AreEffectsEnabled { get; set; } = true;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopAll();
        device.Stop();
        device.Dispose();
    }

    /// <summary>The device's callback. Holds the lock for one buffer's worth of mixing.</summary>
    private void Fill(Span<float> destination)
    {
        lock (gate)
        {
            mixer.Mix(destination);
        }
    }

    private void StopSampleLocked(long key)
    {
        if (!voices.TryGetValue(key, out var handles))
        {
            return;
        }

        foreach (int handle in handles)
        {
            mixer.Remove(handle);
        }

        voices.Remove(key);
    }

    private void StartQueueLocked(MusicQueue queue, AudioChannel channel, ref int voice, bool loop)
    {
        if (voice >= 0)
        {
            mixer.Remove(voice);
            voice = -1;
        }

        queue.Play(loop);

        if (queue.IsPlaying)
        {
            voice = mixer.Add(queue, channel);
        }
    }
}
