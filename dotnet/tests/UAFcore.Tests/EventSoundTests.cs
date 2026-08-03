using UAF.Serialization;
using UAFcore;

namespace UAFcore.Tests;

/// <summary>
/// The design's sound list (<c>SOUND_EVENT</c>).
/// </summary>
/// <remarks>
/// Eleven across the corpus and every one carries exactly one sound, so the ordering these tests pin
/// is transcription rather than observation — which is the reason for pinning the whole call
/// sequence rather than just "the sounds came out".
/// </remarks>
public class EventSoundTests
{
    private const string Corpus = "/Volumes/Data/Dev/uaf/reference";

    private static EventControl Control(int chainTrigger = 0) =>
        new(0, 0, 0, chainTrigger, (int)EventTriggerType.Always, string.Empty,
            0, 0, 0, string.Empty, string.Empty, string.Empty, [], string.Empty, 0, 0, 0,
            string.Empty, 0, 0);

    private static readonly PicRecord NoPic = new(0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static SoundEvent Event(params string[] sounds) =>
        new(new GameEventBase(Control(), NoPic, NoPic, (int)EventType.Sounds, 1, 0, 0,
                              0, 0, string.Empty, string.Empty, string.Empty, []),
            sounds);

    /// <summary>Records the calls in the order they arrive, which is the whole behaviour.</summary>
    private static (List<SoundQueueCall> Calls, SoundOutcome Outcome) Run(
        SoundEvent sound, bool playEventSounds = true)
    {
        List<SoundQueueCall> calls = [];
        var outcome = EventSound.Play(sound, calls.Add, playEventSounds);
        return (calls, outcome);
    }

    private static SoundQueueCall Stop => new(SoundQueueOp.Stop);

    private static SoundQueueCall Queue(string name) => new(SoundQueueOp.Queue, name);

    private static SoundQueueCall Play => new(SoundQueueOp.Play);

    // ---- the order --------------------------------------------------------------------------------

    [Fact]
    public void The_queue_is_stopped_then_filled_then_started()
    {
        var (calls, outcome) = Run(Event("a.wav", "b.wav", "c.wav"));

        Assert.Equal([Stop, Queue("a.wav"), Queue("b.wav"), Queue("c.wav"), Play], calls);
        Assert.Equal(new SoundOutcome(true, 3), outcome);
    }

    [Fact]
    public void Nothing_starts_playing_until_the_whole_list_is_queued()
    {
        // The single observable fact behind "stop, queue*, play": the start call is last, so a
        // partly built queue can never be audible.
        var (calls, _) = Run(Event("a.wav", "b.wav"));

        Assert.Equal(SoundQueueOp.Play, calls[^1].Op);
        Assert.DoesNotContain(calls[..^1], c => c.Op == SoundQueueOp.Play);
    }

    [Fact]
    public void Names_are_queued_in_list_order()
    {
        var (calls, _) = Run(Event("first", "second", "third"));

        Assert.Equal(["first", "second", "third"],
                     calls.Where(c => c.Op == SoundQueueOp.Queue).Select(c => c.Sound));
    }

    [Fact]
    public void A_repeated_name_is_queued_twice()
    {
        // SoundQueue::Add is an unconditional AddTail (SoundMgr.cpp:808) -- no de-duplication, and
        // the destroy-and-restart branch in QueueSound cannot fire because nothing is playing yet.
        var (_, outcome) = Run(Event("same.wav", "same.wav"));

        Assert.Equal(2, outcome.Queued);
    }

    // ---- the silence case -------------------------------------------------------------------------

    [Fact]
    public void An_event_with_no_sounds_is_a_silence_command()
    {
        // The stop still runs, and PlayQueue finds no queue to start. This is the one case where
        // the leading stop is not redundant, and the reason it is ported rather than tidied away.
        var (calls, outcome) = Run(Event());

        Assert.Equal([Stop, Play], calls);
        Assert.Equal(new SoundOutcome(true, 0), outcome);
    }

    [Fact]
    public void A_blank_name_is_queued_rather_than_skipped()
    {
        // No `!= ""` guard here, unlike PLAYSTEPSOUND_EVENT (RunEvent.cpp:6096). The blank reaches
        // AddSound, which rejects it with -1 (SoundMgr.cpp:1739) -- but it still occupies a slot,
        // and the queue's length is what the failure rule reads.
        var (calls, outcome) = Run(Event(string.Empty, "b.wav"));

        Assert.Equal([Stop, Queue(string.Empty), Queue("b.wav"), Play], calls);
        Assert.Equal(2, outcome.Queued);
    }

    [Fact]
    public void Names_are_passed_through_exactly_as_stored()
    {
        // The reference hands the raw string to QueueSound and resolves it much later, inside
        // AddSound via SearchForFile (SoundMgr.cpp:1749). Nothing is trimmed, cased or pathed here.
        var (calls, _) = Run(Event(@"  Sub\Dir\MiXeD Case.MID  "));

        Assert.Equal(@"  Sub\Dir\MiXeD Case.MID  ", calls[1].Sound);
    }

    // ---- the two guards ---------------------------------------------------------------------------

    [Fact]
    public void No_manager_means_no_calls_at_all()
    {
        // pSndMgr == NULL (RunEvent.cpp:11561). Not even the stop, so a sound event on a build with
        // no audio is inert rather than silencing.
        var outcome = EventSound.Play(Event("a.wav"), null);

        Assert.Equal(new SoundOutcome(false, 0), outcome);
    }

    [Fact]
    public void The_flag_suppresses_the_event_including_the_stop()
    {
        var (calls, outcome) = Run(Event("a.wav"), playEventSounds: false);

        Assert.Empty(calls);
        Assert.False(outcome.ReachedQueue);
    }

    [Fact]
    public void The_flag_defaults_to_what_the_engine_boots_with()
    {
        // PlayEventSounds = TRUE (Globals.cpp:199); only the SoundMgr constructor ever clears it.
        Assert.True(EventSound.PlayEventSoundsDefault);

        List<SoundQueueCall> calls = [];
        EventSound.Play(Event("a.wav"), calls.Add);

        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public void A_null_event_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => EventSound.Play(null!, _ => { }));
    }

    // ---- what follows -----------------------------------------------------------------------------

    [Fact]
    public void The_chain_is_unconditional_and_a_missing_sound_does_not_change_it()
    {
        // OnIdle calls ChainHappened() on the way out with no argument and no test, so the caller
        // always asks EventChain for the happened path -- whatever the sounds did.
        var chaining = new SoundEvent(
            new GameEventBase(Control(chainTrigger: (int)ChainTrigger.Always), NoPic, NoPic,
                              (int)EventType.Sounds, 1, 0, 0, 187, 0,
                              string.Empty, string.Empty, string.Empty, []),
            ["nosuchfile.wav"]);

        EventSound.Play(chaining, _ => { });

        Assert.Equal(187u, EventChain.Next(chaining.Base, happened: true));
    }

    // ---- the corpus -------------------------------------------------------------------------------

    [Fact]
    public void Every_sound_event_the_designs_contain_carries_exactly_one_sound()
    {
        // Skips silently on a bare checkout, like the other corpus tests. If a design ever turns up
        // with a real list this fails, and the multi-entry ordering above stops being theory.
        var events = CorpusSoundEvents();
        if (events.Count == 0)
        {
            return;
        }

        Assert.Equal(11, events.Count);
        Assert.All(events, e => Assert.Single(e.Sounds));
        Assert.All(events, e => Assert.NotEmpty(e.Sounds[0]));
    }

    [Fact]
    public void The_shipped_events_all_name_music_and_all_go_to_the_foreground_queue()
    {
        // Ten .mid and one .mp3. They are music by extension and by name, and the reference still
        // sends them through QueueSound -- so they layer over the level's background music instead
        // of replacing it. Wiring these to the background queue would change what the designs do.
        var events = CorpusSoundEvents();
        if (events.Count == 0)
        {
            return;
        }

        var extensions = events.Select(e => Path.GetExtension(e.Sounds[0]).ToLowerInvariant())
                               .ToList();

        Assert.Equal(10, extensions.Count(x => x == ".mid"));
        Assert.Equal(1, extensions.Count(x => x == ".mp3"));
    }

    [Fact]
    public void A_shipped_event_produces_one_stop_one_queue_and_one_play()
    {
        var events = CorpusSoundEvents();
        if (events.Count == 0)
        {
            return;
        }

        foreach (var sound in events)
        {
            var (calls, outcome) = Run(sound);

            Assert.Equal([Stop, Queue(sound.Sounds[0]), Play], calls);
            Assert.Equal(new SoundOutcome(true, 1), outcome);
        }
    }

    private static List<SoundEvent> CorpusSoundEvents()
    {
        List<SoundEvent> found = [];

        if (!Directory.Exists(Corpus))
        {
            return found;
        }

        foreach (string root in Directory.GetDirectories(Corpus).OrderBy(x => x,
                                                                        StringComparer.Ordinal))
        {
            if (!Directory.Exists(Path.Combine(root, "Data")))
            {
                continue;
            }

            using var design = LoadedDesign.Open(root);

            for (int i = 0; i < design.LevelFiles.Count; i++)
            {
                found.AddRange(design.Level(i)?.Events.OfType<SoundEvent>() ?? []);
            }
        }

        return found;
    }
}
