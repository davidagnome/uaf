namespace UAF.Media;

/// <summary>
/// Where input comes from. Polled, never pushed.
/// </summary>
/// <remarks>
/// <para>
/// Polling matches both ends. The engine already polls (<c>CInput::GetKeyboard</c> /
/// <c>GetMouse</c> called from the task loop), and SDL's event queue is a poll
/// (<c>SDL_PollEvent</c>), so a callback-based interface would only add a queue in the middle. It
/// also means a recorded trace is a drop-in substitute for a keyboard, which is what the plan's
/// input-trace strategy (section 8, item 4) rests on.
/// </para>
/// <para>
/// Implementations must be safe to poll from the engine thread, which is not the thread that
/// created the window on every platform. <c>UAF.Media.Sdl</c> documents how it satisfies that.
/// </para>
/// </remarks>
public interface IInputSource
{
    /// <summary>
    /// Takes the next event, or returns false when the queue is empty. Never blocks.
    /// </summary>
    bool TryPoll(out InputEvent inputEvent);

    /// <summary>
    /// Lets the source do whatever it must to notice new input. Separate from
    /// <see cref="TryPoll"/> because SDL requires the platform's event queue to be pumped on the
    /// thread that created the window, while polling can happen anywhere.
    /// </summary>
    void Pump();
}

/// <summary>
/// An input source fed from a list — the substitute for a keyboard in tests and in recorded-trace
/// replay.
/// </summary>
/// <remarks>
/// Not thread-safe by design. A trace replay is single-threaded and deterministic; adding a lock
/// would hide the case where a test accidentally shares one across threads.
/// </remarks>
public sealed class RecordedInputSource : IInputSource
{
    private readonly Queue<InputEvent> pending;

    public RecordedInputSource(params InputEvent[] events)
        => pending = new Queue<InputEvent>(events ?? []);

    public RecordedInputSource(IEnumerable<InputEvent> events)
        => pending = new Queue<InputEvent>(events);

    public int PendingCount => pending.Count;

    public void Add(InputEvent inputEvent) => pending.Enqueue(inputEvent);

    public bool TryPoll(out InputEvent inputEvent) => pending.TryDequeue(out inputEvent);

    /// <summary>No-op: a recorded trace has nothing to pump.</summary>
    public void Pump()
    {
    }
}
