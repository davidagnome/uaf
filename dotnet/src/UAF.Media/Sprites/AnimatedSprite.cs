namespace UAF.Media;

/// <summary>
/// How a multi-frame art record is meant to be played, from <c>PIC_DATA</c>'s anonymous
/// <c>AS_*</c> enum (<c>Shared/PicData.h:80</c>).
/// </summary>
/// <remarks>
/// Only <see cref="Sequenced"/> animates on a timer. The other three mean the frames are an
/// indexed set rather than a sequence — one per facing, one per target — so
/// <see cref="AnimatedSprite.AnimateNextFrame"/> refuses to advance them, matching
/// <c>PIC_DATA::AnimateNextFrame</c> and <c>SetPicTimeDelay</c>, which both bail out when the style
/// is not <c>AS_None</c>.
/// </remarks>
public enum AnimationStyle
{
    /// <summary><c>AS_None</c> — all frames are played in order.</summary>
    Sequenced = 0,

    /// <summary><c>AS_Directional</c> — one frame per direction: N, E, S, W, NW, NE, SW, SE.</summary>
    Directional = 1,

    /// <summary><c>AS_Radius</c> — a single frame covers the whole target radius.</summary>
    Radius = 2,

    /// <summary><c>AS_EachTarget</c> — a single frame covers each targeted character.</summary>
    EachTarget = 3,
}

/// <summary>Looping behaviour from <c>PIC_DATA</c>'s <c>AF_*</c> flags (<c>Shared/PicData.h:89</c>).</summary>
[Flags]
public enum AnimationFlags : uint
{
    None = 0,

    /// <summary><c>AF_KeypressBeforeLoop</c> — hold on the last frame until input arrives.</summary>
    KeypressBeforeLoop = 1,

    /// <summary><c>AF_MaxLoopCounter</c> — stop after <see cref="AnimatedSprite.MaxLoops"/> passes.</summary>
    MaxLoopCounter = 2,

    /// <summary><c>AF_Loop</c> — without this, the animation stops on the last frame.</summary>
    Loop = 4,

    /// <summary><c>AF_LoopForever</c>.</summary>
    LoopForever = 8,
}

/// <summary>
/// The frame-advance state machine for an animated art record — <c>PIC_DATA</c>'s animation half
/// (<c>Shared/PicData.cpp:453</c>) plus the frame wrapping in <c>Graphics::SetNextFrame</c>
/// (<c>Shared/Graphics.cpp:889</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is a pure state machine over an injected timestamp, with no timer and no thread. The
/// original drove it two different ways — a Windows timer in the editor, the engine's
/// <c>virtualGameTime</c> in the game — and neither is testable. Taking the timestamp as an
/// argument means an animation's whole life can be replayed deterministically in a unit test,
/// which is what the recorded-trace strategy in the plan (section 8, item 4) needs.
/// </para>
/// <para>
/// Timestamps are milliseconds on whatever clock the caller uses; only differences matter.
/// </para>
/// </remarks>
public sealed class AnimatedSprite
{
    /// <summary>
    /// Animations with a delay below this never advance. Straight from
    /// <c>PIC_DATA::AnimateNextFrame</c>: <c>if (timeDelay &lt; 30) return FALSE;</c>. It is how
    /// the data expresses "not animated" — a design with <c>timeDelay</c> 0 has a still picture,
    /// not a 1000fps one.
    /// </summary>
    public const int MinimumTimeDelayMs = 30;

    private long lastAdvanceMs;

    public AnimatedSprite(int frameCount, int timeDelayMs = 0, int restartFrame = 0,
                          AnimationFlags flags = AnimationFlags.None, uint maxLoops = 0,
                          AnimationStyle style = AnimationStyle.Sequenced)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);

        FrameCount = frameCount;
        TimeDelayMs = timeDelayMs;
        RestartFrame = restartFrame;
        Flags = flags;
        MaxLoops = maxLoops;
        Style = style;
    }

    /// <summary><c>PIC_DATA::NumFrames</c>.</summary>
    public int FrameCount { get; }

    /// <summary><c>PIC_DATA::timeDelay</c>, milliseconds between frames.</summary>
    public int TimeDelayMs { get; }

    /// <summary>
    /// <c>PIC_DATA::RestartFrame</c>, <b>1-based</b>: looping jumps to frame
    /// <c>RestartFrame - 1</c>, and 0 means jump to frame 0.
    /// </summary>
    /// <remarks>
    /// The off-by-one is real and is in <c>Graphics::SetNextFrame</c>, which smuggles this value
    /// through CDX's unrelated sprite "Type" field and then subtracts one from it
    /// (<c>Shared/Graphics.cpp:914</c> — the comment there calls it hijacked). Treating the stored
    /// value as 0-based restarts one frame too early on every loop.
    /// </remarks>
    public int RestartFrame { get; }

    /// <summary><c>PIC_DATA::flags</c>.</summary>
    public AnimationFlags Flags { get; }

    /// <summary><c>PIC_DATA::MaxLoops</c>, honoured only with <see cref="AnimationFlags.MaxLoopCounter"/>.</summary>
    public uint MaxLoops { get; }

    /// <summary><c>PIC_DATA::style</c>.</summary>
    public AnimationStyle Style { get; }

    /// <summary>The frame to draw, numbered from 0 — <c>PIC_DATA::GetFrame</c>.</summary>
    public int Frame { get; private set; }

    /// <summary><c>PIC_DATA::LoopCounter</c>: completed passes over the frame list.</summary>
    public uint LoopCounter { get; private set; }

    /// <summary>True once the animation has stopped and will not advance again.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// True when the last frame is showing and the animation is waiting for input, because
    /// <see cref="AnimationFlags.KeypressBeforeLoop"/> is set. The engine's cue to hold the
    /// picture up.
    /// </summary>
    public bool IsAwaitingKeypress { get; private set; }

    /// <summary><c>Graphics::IsLastFrame</c> — frames are numbered 0..FrameCount-1.</summary>
    public bool IsLastFrame => Frame >= FrameCount - 1;

    /// <summary>Rewinds to frame 0 and restarts the clock — <c>PIC_DATA::SetFirstFrame</c>.</summary>
    public void SetFirstFrame(long timestampMs = 0)
    {
        Frame = 0;
        LoopCounter = 0;
        IsFinished = false;
        IsAwaitingKeypress = false;
        lastAdvanceMs = timestampMs;
    }

    /// <summary>
    /// Forces the next frame regardless of the clock, wrapping to <see cref="RestartFrame"/> past
    /// the end — <c>Graphics::SetNextFrame</c>. Used by the engine when something other than the
    /// timer drives the animation, such as combat posing.
    /// </summary>
    public void SetNextFrame()
    {
        int next = Frame + 1;
        if (next >= FrameCount)
        {
            next = RestartFrame > 0 ? RestartFrame - 1 : 0;
        }

        Frame = next;
    }

    /// <summary>Jumps to a frame, ignoring out-of-range requests as <c>Graphics::SetFrame</c> does.</summary>
    public bool SetFrame(int frame)
    {
        if (frame < 0 || frame >= FrameCount)
        {
            return false;
        }

        Frame = frame;
        return true;
    }

    /// <summary>Releases a hold created by <see cref="AnimationFlags.KeypressBeforeLoop"/>.</summary>
    /// <remarks>
    /// <c>PIC_DATA</c> has no equivalent: on the last frame with that flag it returns TRUE from
    /// every call and never advances, leaving the engine to move the frame itself. Making the
    /// release explicit keeps the hold observable, so a test can prove an animation waits rather
    /// than inferring it from a return value that never changes.
    /// </remarks>
    public void AcknowledgeKeypress()
    {
        if (!IsAwaitingKeypress)
        {
            return;
        }

        IsAwaitingKeypress = false;

        // Same order as the timer path: the pass is counted before the frame moves, so an
        // animation on its last permitted loop stops instead of wrapping once more.
        if (!CountLoop())
        {
            SetNextFrame();
        }
    }

    /// <summary>
    /// Advances the frame if <see cref="TimeDelayMs"/> has elapsed since the last advance — the
    /// port of <c>PIC_DATA::AnimateNextFrame(LONGLONG)</c>.
    /// </summary>
    /// <returns>
    /// True when the frame changed or the animation is now holding for a keypress; false when it
    /// is too early, the animation is not sequenced, or it has finished. The original's return
    /// value has the same "something happened, redraw" meaning.
    /// </returns>
    public bool AnimateNextFrame(long timestampMs)
    {
        if (TimeDelayMs < MinimumTimeDelayMs || IsFinished || Style != AnimationStyle.Sequenced)
        {
            return false;
        }

        if (IsAwaitingKeypress)
        {
            return true;
        }

        if (timestampMs - lastAdvanceMs < TimeDelayMs)
        {
            return false;
        }

        lastAdvanceMs = timestampMs;

        // The order of these three checks is the original's, and it matters. A last frame with no
        // AF_Loop stops; otherwise a keypress hold takes precedence over the loop counter, so an
        // animation with both flags waits for input before it counts the pass.
        if (IsLastFrame && (Flags & AnimationFlags.Loop) == 0)
        {
            IsFinished = true;
            return false;
        }

        if ((Flags & AnimationFlags.KeypressBeforeLoop) != 0 && IsLastFrame)
        {
            IsAwaitingKeypress = true;
            return true;
        }

        if (IsLastFrame && CountLoop())
        {
            return false;
        }

        SetNextFrame();
        return true;
    }

    /// <summary>
    /// Records a completed pass and reports whether that ended the animation. Split out because
    /// the keypress path reaches it later than the timer path does.
    /// </summary>
    private bool CountLoop()
    {
        if ((Flags & AnimationFlags.MaxLoopCounter) == 0)
        {
            return false;
        }

        LoopCounter++;
        if (LoopCounter < MaxLoops)
        {
            return false;
        }

        IsFinished = true;
        return true;
    }
}
