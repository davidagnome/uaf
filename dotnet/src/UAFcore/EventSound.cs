using UAF.Serialization;

namespace UAFcore;

/// <summary>
/// One call a sound event makes on the foreground sound queue
/// (<c>SoundMgr::StopQueue</c>, <c>QueueSound</c>, <c>PlayQueue</c>, <c>Shared/SoundMgr.cpp:2330</c>,
/// <c>:2255</c>, <c>:2293</c>).
/// </summary>
public enum SoundQueueOp
{
    /// <summary>Stop and discard whatever the queue was playing.</summary>
    Stop,

    /// <summary>Append one name to the queue. Nothing starts playing.</summary>
    Queue,

    /// <summary>Start the queue, once, from the head.</summary>
    Play,
}

/// <summary>One entry in the call sequence a sound event issues.</summary>
/// <param name="Op">Which of the three calls this is.</param>
/// <param name="Sound">
/// The name, for <see cref="SoundQueueOp.Queue"/> only, and passed through exactly as the design
/// stored it — see <see cref="EventSound.Play"/>. Empty for the other two.
/// </param>
public readonly record struct SoundQueueCall(SoundQueueOp Op, string Sound = "");

/// <summary>What a sound event did.</summary>
/// <param name="ReachedQueue">
/// Whether the guard let it touch the queue at all. False means no call was made, not even the
/// stop.
/// </param>
/// <param name="Queued">
/// How many names were queued. Zero with <paramref name="ReachedQueue"/> true is the silence case —
/// see <see cref="EventSound.Play"/>.
/// </param>
public readonly record struct SoundOutcome(bool ReachedQueue, int Queued);

/// <summary>
/// Runs a <c>SOUND_EVENT</c> (<c>SOUND_EVENT::OnIdle</c>, <c>UAFWin/RunEvent.cpp:11558</c>) — the
/// design plays a list of sounds.
/// </summary>
/// <remarks>
/// <para>
/// It presents nothing. <c>OnInitialEvent</c> (<c>RunEvent.cpp:11553</c>) is one line clearing the
/// menu, <c>OnKeypress</c> and <c>OnMouseClickLeft</c> are empty bodies commented "ignore all
/// input" (<c>Shared/GameEvent.h:2997</c>, <c>:2998</c>), and <c>OnIdle</c> does the work and
/// chains. The same shape as <see cref="Utilities"/>, which is why this is a static and not part of
/// <see cref="EventRunner"/>.
/// </para>
/// <para>
/// <b>Playback is injected, because the engine has no audio.</b> The media layer does have the
/// effect path — <c>UAF.Media.IAudioBackend</c> carries the same three calls this emits, over a
/// <c>MusicQueue</c> that is explicitly the port of <c>SoundQueue</c> and
/// <c>BackgroundSoundQueue</c>. What is missing is the wiring: nothing outside
/// <c>UAF.Media.Tests</c> constructs a backend, and <see cref="LoadedDesign"/> has no sound-file
/// resolution to sit next to <see cref="LoadedDesign.Art"/>. So this emits the call sequence and
/// the host decides what a call means; an adapter onto <c>IAudioBackend</c> is a three-case switch
/// when someone wires one up.
/// </para>
/// <para>
/// <b>What the corpus holds: eleven events, and every one of them carries exactly one sound.</b>
/// Four in Ambassador's_Letter and six in Case.dsn (the same event copied across six levels), all
/// ten naming a <c>.mid</c>, plus one <c>.mp3</c> in SomethingWild.dsn. Every named file is present
/// in its design. So the queue this ports has never, in shipped data, held more than one entry —
/// the ordering below is transcription, not observation, and the case worth checking against a real
/// design is the one no design contains.
/// </para>
/// <para>
/// <b>Those names are music, and this is not the music queue.</b> The foreground <c>SoundQueue</c>
/// and the background <c>BackgroundSoundQueue</c> are separate objects and nothing here touches the
/// background one, so in the shipped designs a sound event lays a track over the level's music
/// rather than replacing it. Wiring this to <c>QueueBackgroundSound</c> because the filenames look
/// like music would change what the designs do.
/// </para>
/// <para>
/// <b>This event is the only user of the foreground queue in the whole engine.</b>
/// <c>RunEvent.cpp:11563</c>, <c>:11568</c> and <c>:11570</c> are the only calls to
/// <c>StopQueue</c>/<c>QueueSound</c>/<c>PlayQueue</c> outside <c>SoundMgr</c> itself and the
/// editor's sound-test dialog. So the queue being stopped can only ever hold a previous sound
/// event's list.
/// </para>
/// </remarks>
public static class EventSound
{
    /// <summary>
    /// <c>PlayEventSounds</c> as the engine starts (<c>Shared/Globals.cpp:199</c>).
    /// </summary>
    /// <remarks>
    /// The only thing that ever clears it is the <c>SoundMgr</c> constructor deciding not to load
    /// BASS at all (<c>Shared/SoundMgr.cpp:1289-1293</c>). No menu toggles it and no config file
    /// names it, so in a run with working audio it is true from start to finish.
    /// </remarks>
    public const bool PlayEventSoundsDefault = true;

    /// <summary>
    /// Issues the event's queue calls, in the reference's order
    /// (<c>SOUND_EVENT::OnIdle</c>, <c>UAFWin/RunEvent.cpp:11558</c>).
    /// </summary>
    /// <param name="sound">The event.</param>
    /// <param name="audio">
    /// Where the calls go, or null for <c>pSndMgr == NULL</c> — see the remarks. Called once per
    /// entry in the sequence, in order, and never re-entered.
    /// </param>
    /// <param name="playEventSounds">
    /// <c>PlayEventSounds</c> (<c>Shared/Externs.h:1680</c>). Defaults to
    /// <see cref="PlayEventSoundsDefault"/>, which is what the engine boots with.
    /// </param>
    /// <remarks>
    /// <para>
    /// The whole body is <c>StopQueue</c>, then one <c>QueueSound</c> per name in list order, then
    /// <c>PlayQueue</c> — three lines, and the order is the entire behaviour. Nothing is audible
    /// until the last call, so the list is always assembled whole before any of it starts.
    /// </para>
    /// <para>
    /// <b>The stop comes first so that an event with no sounds is a silence command.</b> With an
    /// empty list the loop does not run and <c>PlayQueue</c> finds no queue to start
    /// (<c>SoundMgr.cpp:2302</c>), so all that is left is the stop — the event's observable effect
    /// is to cut off whatever was playing. That is the one case where the leading stop is not
    /// redundant: with names present, <c>SoundMgr::QueueSound</c> would have destroyed a playing
    /// queue on its own (<c>SoundMgr.cpp:2265</c>) and the result would be the same either way.
    /// <see cref="SoundOutcome.Queued"/> of zero on a run that
    /// <see cref="SoundOutcome.ReachedQueue"/> is how a caller sees it.
    /// </para>
    /// <para>
    /// <b>Every name accumulates into one queue, including repeats.</b> <c>QueueSound</c> throws the
    /// queue away and starts over only when it is <i>playing</i>, and <c>m_IsPlaying</c> is not set
    /// until the queue thread actually runs (<c>SoundQueue::Thread</c>, <c>SoundMgr.cpp:937</c>) —
    /// which cannot happen before the <c>PlayQueue</c> at the end. So the destroy-and-restart branch
    /// is unreachable from this event, and <c>SoundQueue::Add</c> is an unconditional
    /// <c>AddTail</c> (<c>SoundMgr.cpp:808</c>) that de-duplicates nothing.
    /// </para>
    /// <para>
    /// <b>A blank name is queued, not skipped.</b> There is no <c>!= ""</c> guard here, unlike
    /// <c>PLAYSTEPSOUND_EVENT</c> which has one (<c>RunEvent.cpp:6096</c>). The blank travels all
    /// the way to <c>SoundMgr::AddSound</c>, which rejects it with −1 (<c>SoundMgr.cpp:1739</c>).
    /// <see cref="SoundEvent"/> decodes the archive's <c>*</c> blank into an empty string, so blanks
    /// arrive here as empty strings and are passed on unchanged — dropping them would change the
    /// queue's length, which the failure rule below reads.
    /// </para>
    /// <para>
    /// <b>Names are passed through exactly as stored, and resolving them is the host's problem.</b>
    /// The reference hands the raw string to <c>QueueSound</c> and only turns it into a file much
    /// later, inside <c>AddSound</c>, via <c>SearchForFile(tmp, rte.SoundDir())</c>
    /// (<c>SoundMgr.cpp:1749</c>). The corpus stores bare filenames — <c>MID_Binjo.mid</c> — that
    /// live in the design's <c>Resources</c> directory, and this port has nothing that performs that
    /// search. Guessing at one here would be inventing a lookup order.
    /// </para>
    /// <para>
    /// <b>Reference bug: the "keep going after a failure" rule reads the whole queue, not what is
    /// left of it.</b> When a sound will not load or will not play, <c>SoundQueue::Play</c> reports
    /// success anyway — but only <c>if (list.GetCount() &gt; 1)</c> (<c>SoundMgr.cpp:912</c> and
    /// <c>:922</c>), and that count is the size of the entire list, which does not shrink as the
    /// queue advances (<c>Clear()</c> runs only as the thread exits, <c>SoundMgr.cpp:978</c>). The
    /// intent was plainly "there are more sounds after this one". The effect is that a queue of one
    /// dies on a missing file while a queue of several never dies at all, wherever the failure
    /// falls. With every corpus event holding exactly one sound, the shipped behaviour is the
    /// intended one and the bug is unobservable in real data. Worth knowing because
    /// <c>UAF.Media.MusicQueue</c> documents a deliberate divergence from "a failed entry ends the
    /// whole queue", which is the reference's behaviour only for a queue of one.
    /// </para>
    /// <para>
    /// <b>The queue does not loop.</b> <c>SoundMgr::PlayQueue</c> passes FALSE explicitly, commented
    /// "don't loop these sounds" (<c>SoundMgr.cpp:2303</c>), so the list plays once and stops. This
    /// is the difference from the background music queue, which loops.
    /// </para>
    /// <para>
    /// <b>Two guards that look like one, and are not.</b> The reference tests
    /// <c>(pSndMgr != NULL) &amp;&amp; (PlayEventSounds)</c> (<c>RunEvent.cpp:11561</c>). The
    /// pointer is a real global that the game fills in at startup (<c>UAFWin/Dgngame.cpp:825</c>)
    /// and the editor mostly does not, which is why it is checked at all; the flag is separately
    /// cleared by the <c>SoundMgr</c> constructor when audio is switched off
    /// (<c>SoundMgr.cpp:1293</c>) while leaving the pointer perfectly valid. So a null
    /// <paramref name="audio"/> stands for the missing manager and
    /// <paramref name="playEventSounds"/> for the flag, and either one suppresses the event
    /// entirely — including the stop.
    /// </para>
    /// <para>
    /// <b>A third gate exists further down and is not modelled.</b> <c>QueueSound</c> and
    /// <c>PlayQueue</c> return early on <c>SoundEnabled == 0</c> (<c>SoundMgr.cpp:2262</c>,
    /// <c>:2300</c>) where <c>StopQueue</c> does not (<c>:2330</c>), so muting through
    /// <c>EnableSound</c> turns every sound event into the silence command described above. That
    /// belongs to whatever plays the calls, not to the event, so a host that mutes should honour the
    /// stop and drop the rest rather than ignoring the sequence.
    /// </para>
    /// <para>
    /// <b>It runs once, then chains.</b> <c>OnIdle</c> returns false, which the task loop reads as
    /// "does not need input, keep going" (<c>while (!taskList.OnIdle())</c>,
    /// <c>UAFWin/CProcinp.cpp:1175</c>) — not as a request to run again, because
    /// <c>ChainHappened</c> (<c>RunEvent.cpp:855</c>) has already replaced this task or popped it
    /// (<c>:888</c>). The chain is unconditional and cannot fail, so a caller follows it with
    /// <see cref="EventChain.Next"/> and <c>happened: true</c>. Nothing about the sounds feeds into
    /// it: a missing file still chains.
    /// </para>
    /// <para>
    /// One branch of the reference has nothing to port. <c>QueueSound</c> checks
    /// <c>GetCount() == 0</c> immediately after adding (<c>SoundMgr.cpp:2279</c>) and destroys the
    /// queue if so; <c>Add</c> cannot leave it empty, so the branch is dead. Named here so nobody
    /// goes looking for the rejection rule it implies.
    /// </para>
    /// </remarks>
    public static SoundOutcome Play(SoundEvent sound, Action<SoundQueueCall>? audio,
                                    bool playEventSounds = PlayEventSoundsDefault)
    {
        ArgumentNullException.ThrowIfNull(sound);

        // RunEvent.cpp:11561 -- the manager and the flag, both.
        if (audio is null || !playEventSounds)
        {
            return new SoundOutcome(false, 0);
        }

        audio(new SoundQueueCall(SoundQueueOp.Stop));            // :11563

        int queued = 0;
        foreach (string name in sound.Sounds)                    // :11567-11568, head to tail
        {
            audio(new SoundQueueCall(SoundQueueOp.Queue, name));
            queued++;
        }

        audio(new SoundQueueCall(SoundQueueOp.Play));            // :11570

        return new SoundOutcome(true, queued);
    }
}
