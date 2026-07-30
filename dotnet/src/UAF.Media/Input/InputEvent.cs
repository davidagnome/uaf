namespace UAF.Media;

public enum InputEventKind
{
    KeyDown,
    KeyUp,

    /// <summary>
    /// A typed character, delivered separately from <see cref="KeyDown"/>.
    /// </summary>
    /// <remarks>
    /// The original derived the character from the virtual key plus a shift flag, hard-coding a US
    /// layout (<c>CInput::LookupKeyCode</c> maps shift+2 to '@'). SDL reports text input as its own
    /// event after the platform has applied the real keyboard layout, so the port carries both: the
    /// key for game bindings, the character for text entry. A non-US keyboard then types its own
    /// punctuation correctly, which the original could not do.
    /// </remarks>
    TextInput,

    MouseMove,
    MouseDown,
    MouseUp,
    MouseWheel,

    /// <summary>The window was closed or the platform asked the process to exit.</summary>
    Quit,
}

/// <summary>
/// One input event. Shaped to feed the engine's <c>INPUT_DATA</c>
/// (<c>UAFWin/Getinput.h:83</c>) without losing anything on the way.
/// </summary>
/// <remarks>
/// <para>
/// This carries the raw event, not the engine's semantic <c>key_code</c> classification (KC_NW,
/// KC_CENTER and friends). That classification is a game rule — numpad 7 means north-west only
/// because the game says so — and it lives in <c>CInput</c>, which Phase 4 ports. Keeping it out
/// of the media layer is what lets the editor consume the same events without inheriting the
/// game's key bindings.
/// </para>
/// <para>
/// A struct, and deliberately not a class hierarchy: the engine polls input in a tight loop
/// (<c>CProcessInput</c>) and allocating per keystroke would be a regression against a program
/// that allocated nothing.
/// </para>
/// </remarks>
public readonly record struct InputEvent
{
    public required InputEventKind Kind { get; init; }

    /// <summary>Milliseconds on the source's clock. Only differences are meaningful.</summary>
    public long TimestampMs { get; init; }

    /// <summary>Set for <see cref="InputEventKind.KeyDown"/> and <see cref="InputEventKind.KeyUp"/>.</summary>
    public VirtualKey Key { get; init; }

    public KeyModifiers Modifiers { get; init; }

    /// <summary>True when the platform generated this key event from auto-repeat.</summary>
    public bool IsRepeat { get; init; }

    /// <summary>Set for <see cref="InputEventKind.TextInput"/>.</summary>
    public char Character { get; init; }

    /// <summary>Cursor position in framebuffer pixels, for the mouse events.</summary>
    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>The button that changed, for <see cref="InputEventKind.MouseDown"/>/<see cref="InputEventKind.MouseUp"/>.</summary>
    public MouseButtons Button { get; init; }

    /// <summary>Buttons currently held, for <see cref="InputEventKind.MouseMove"/>.</summary>
    public MouseButtons ButtonsHeld { get; init; }

    /// <summary>Wheel detents, positive away from the user.</summary>
    public int WheelDelta { get; init; }

    public static InputEvent KeyDown(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None,
                                     long timestampMs = 0, bool isRepeat = false) =>
        new() { Kind = InputEventKind.KeyDown, Key = key, Modifiers = modifiers,
                TimestampMs = timestampMs, IsRepeat = isRepeat };

    public static InputEvent KeyUp(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None,
                                   long timestampMs = 0) =>
        new() { Kind = InputEventKind.KeyUp, Key = key, Modifiers = modifiers,
                TimestampMs = timestampMs };

    public static InputEvent Text(char character, long timestampMs = 0) =>
        new() { Kind = InputEventKind.TextInput, Character = character, TimestampMs = timestampMs };

    public static InputEvent MouseMove(int x, int y, MouseButtons held = MouseButtons.None,
                                       long timestampMs = 0) =>
        new() { Kind = InputEventKind.MouseMove, X = x, Y = y, ButtonsHeld = held,
                TimestampMs = timestampMs };

    public static InputEvent MouseDown(int x, int y, MouseButtons button, long timestampMs = 0) =>
        new() { Kind = InputEventKind.MouseDown, X = x, Y = y, Button = button,
                ButtonsHeld = button, TimestampMs = timestampMs };

    public static InputEvent MouseUp(int x, int y, MouseButtons button, long timestampMs = 0) =>
        new() { Kind = InputEventKind.MouseUp, X = x, Y = y, Button = button,
                TimestampMs = timestampMs };

    public static InputEvent Quit(long timestampMs = 0) =>
        new() { Kind = InputEventKind.Quit, TimestampMs = timestampMs };
}
