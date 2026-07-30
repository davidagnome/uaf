namespace UAF.Media;

/// <summary>
/// A list of music files played one after another, optionally looping — the replacement for
/// <c>SoundQueue</c> and <c>BackgroundSoundQueue</c> (<c>Shared/SoundMgr.h:217,269</c>).
/// </summary>
/// <remarks>
/// <para>
/// The original ran each queue on its own <c>CThread</c> that blocked on the sound finishing and
/// signalled a Win32 event. Three threads, three events, and no way to observe any of it from a
/// test. This is the same behaviour as a <see cref="IPcmSource"/> instead: the queue advances
/// because the mixer read past the end of the current entry, so a whole playlist can be played
/// through in a test by asking for its samples, with no threads and no waiting.
/// </para>
/// <para>
/// The sequencing is the original's, including the details. Entries play in insertion order; at the
/// end a looping queue restarts from the head, and a non-looping queue empties itself
/// (<c>SoundQueue::Thread</c> calls <c>Clear()</c> on the way out, which is why the engine can test
/// <c>GetCount()</c> to see whether a queue is spent). An entry that will not load is skipped rather
/// than fatal, which is what the original's <c>Play</c> returning FALSE... does <b>not</b> do — see
/// below.
/// </para>
/// <para>
/// <b>One deliberate divergence.</b> In <c>SoundQueue::Thread</c> a failed <c>Play</c> ends the whole
/// queue, so a single missing or unplayable file silences everything after it. Here a failed entry is
/// skipped and the rest play. The original's behaviour is indistinguishable from "the queue
/// finished" at every call site, and a missing MIDI SoundFont would otherwise silence a level's whole
/// playlist; the skipped entries are reported through <see cref="LastError"/> instead.
/// </para>
/// </remarks>
public sealed class MusicQueue(IAudioSourceLoader loader) : IPcmSource
{
    private readonly List<string> entries = [];
    private IPcmSource? current;
    private int position;

    /// <summary>Files queued but not yet consumed by a non-looping pass.</summary>
    public int Count => entries.Count;

    public bool IsPlaying { get; private set; }

    public bool IsLooping { get; private set; }

    /// <summary>
    /// True when this queue holds level music rather than zone music —
    /// <c>BackgroundSoundQueue::m_IsLevel</c>. The engine needs to know because entering a zone
    /// replaces zone music but not level music.
    /// </summary>
    public bool IsLevelMusic { get; set; } = true;

    /// <summary>Why the most recent entry was skipped, or null. Cleared when a queue starts.</summary>
    public string? LastError { get; private set; }

    /// <summary>How many entries have been skipped because they would not load.</summary>
    public int SkippedCount { get; private set; }

    /// <summary>Raised when a non-looping queue reaches the end — <c>SoundFinishedEvent</c>.</summary>
    public event Action? Finished;

    /// <summary><c>SoundMgr::QueueSound</c>.</summary>
    public void Add(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        entries.Add(path);
    }

    public void Clear()
    {
        entries.Clear();
        current = null;
        position = 0;
        IsPlaying = false;
    }

    /// <summary><c>SoundMgr::PlayQueue</c>: starts at the head of the list.</summary>
    public void Play(bool loop)
    {
        IsLooping = loop;
        position = 0;
        current = null;
        LastError = null;
        SkippedCount = 0;
        IsPlaying = entries.Count > 0;
    }

    /// <summary><c>SoundMgr::StopQueue</c>. Leaves the list intact so it can be replayed.</summary>
    public void Stop()
    {
        IsPlaying = false;
        current = null;
    }

    public bool IsFinished => !IsPlaying;

    public int Read(Span<float> destination)
    {
        if (!IsPlaying)
        {
            return 0;
        }

        int written = 0;

        while (written < destination.Length)
        {
            if (current is null && !TryStartNext())
            {
                break;
            }

            int read = current!.Read(destination[written..]);
            written += read;

            if (read == 0 || current.IsFinished)
            {
                current = null;
            }
        }

        return written;
    }

    /// <summary>
    /// Opens the next playable entry, wrapping or finishing at the end of the list.
    /// </summary>
    private bool TryStartNext()
    {
        while (true)
        {
            if (position >= entries.Count)
            {
                if (!IsLooping || entries.Count == 0)
                {
                    IsPlaying = false;
                    // The original clears the list when a non-looping queue drains, which is how
                    // callers tell a spent queue from a stopped one.
                    entries.Clear();
                    Finished?.Invoke();
                    return false;
                }

                position = 0;
            }

            string path = entries[position++];

            // Individual entries never loop: the loop is over the whole list.
            if (loader.TryLoad(path, loop: false, out var source, out string? reason))
            {
                current = source;
                return true;
            }

            LastError = reason;
            SkippedCount++;

            // A list of nothing but unplayable entries must not spin forever.
            if (SkippedCount > entries.Count)
            {
                IsPlaying = false;
                Finished?.Invoke();
                return false;
            }
        }
    }
}
