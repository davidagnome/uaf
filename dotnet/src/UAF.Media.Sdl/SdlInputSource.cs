using System.Collections.Concurrent;
using SDL;
using static SDL.SDL3;

namespace UAF.Media.Sdl;

/// <summary>
/// Translates SDL's event queue into <see cref="InputEvent"/>s.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pump on the window's thread, poll from anywhere.</b> SDL requires <c>SDL_PumpEvents</c> — and
/// therefore <c>SDL_PollEvent</c>, which pumps implicitly — to run on the thread that created the
/// window; on macOS it drives the Cocoa run loop. The engine runs on its own thread and blocks on
/// input (docs/PORTING-PLAN.md section 4.4), so it cannot be the thread that pumps. Splitting
/// <see cref="Pump"/> from <see cref="TryPoll"/> and putting a concurrent queue between them lets the
/// host pump on its main thread while the engine polls on its own.
/// </para>
/// <para>
/// Mouse coordinates come back as floats in window space. They are truncated to framebuffer pixels
/// using the framebuffer's size rather than the window's, because everything above this layer thinks
/// in framebuffer coordinates — the original had no scaling to undo, since its window <i>was</i> the
/// framebuffer.
/// </para>
/// </remarks>
public sealed unsafe class SdlInputSource : IInputSource
{
    private readonly ConcurrentQueue<InputEvent> queue = new();
    private readonly int framebufferWidth;
    private readonly int framebufferHeight;
    private int windowWidth;
    private int windowHeight;

    /// <summary>
    /// <paramref name="framebufferWidth"/>/<paramref name="framebufferHeight"/> are the surface size
    /// mouse positions are reported in. The window's own size is tracked from resize events.
    /// </summary>
    public SdlInputSource(int framebufferWidth, int framebufferHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framebufferWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framebufferHeight);

        this.framebufferWidth = framebufferWidth;
        this.framebufferHeight = framebufferHeight;
        windowWidth = framebufferWidth;
        windowHeight = framebufferHeight;
    }

    /// <summary>Events translated so far, for diagnostics and tests.</summary>
    public int TranslatedCount { get; private set; }

    /// <summary>
    /// Events SDL delivered that the layer has no mapping for. Not a problem — it is the count of
    /// gamepad, pen and display events the game does not use.
    /// </summary>
    public int IgnoredCount { get; private set; }

    public void Pump()
    {
        SDL_Event sdlEvent;
        while (SDL_PollEvent(&sdlEvent))
        {
            if (Translate(sdlEvent, out var translated))
            {
                queue.Enqueue(translated);
                TranslatedCount++;
            }
            else
            {
                IgnoredCount++;
            }
        }
    }

    public bool TryPoll(out InputEvent inputEvent) => queue.TryDequeue(out inputEvent);

    private bool Translate(SDL_Event sdlEvent, out InputEvent result)
    {
        result = default;

        switch ((SDL_EventType)sdlEvent.type)
        {
            case SDL_EventType.SDL_EVENT_QUIT:
            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                result = InputEvent.Quit((long)sdlEvent.common.timestamp / 1_000_000);
                return true;

            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
            case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                windowWidth = Math.Max(1, sdlEvent.window.data1);
                windowHeight = Math.Max(1, sdlEvent.window.data2);
                return false;

            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            case SDL_EventType.SDL_EVENT_KEY_UP:
            {
                var key = SdlScancodes.ToVirtualKey(sdlEvent.key.scancode);
                if (key == VirtualKey.Unknown)
                {
                    return false;
                }

                bool down = (SDL_EventType)sdlEvent.type == SDL_EventType.SDL_EVENT_KEY_DOWN;
                long timestamp = (long)sdlEvent.key.timestamp / 1_000_000;
                var modifiers = ToModifiers(sdlEvent.key.mod);

                result = down
                    ? InputEvent.KeyDown(key, modifiers, timestamp, sdlEvent.key.repeat)
                    : InputEvent.KeyUp(key, modifiers, timestamp);
                return true;
            }

            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
            {
                string? text = PtrToStringUTF8(sdlEvent.text.text, free: false);
                if (string.IsNullOrEmpty(text))
                {
                    return false;
                }

                // One event per UTF-16 code unit would be wrong for anything outside the BMP, but the
                // engine's text fields are single-byte MBCS anyway (see section 4.3), so a character
                // it cannot store is a character it should not receive.
                result = InputEvent.Text(text[0], (long)sdlEvent.text.timestamp / 1_000_000);
                return true;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                result = InputEvent.MouseMove(
                    ToFramebufferX(sdlEvent.motion.x), ToFramebufferY(sdlEvent.motion.y),
                    ToButtons(sdlEvent.motion.state),
                    (long)sdlEvent.motion.timestamp / 1_000_000);
                return true;

            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
            {
                var button = ToButton(sdlEvent.button.button);
                if (button == MouseButtons.None)
                {
                    return false;
                }

                int x = ToFramebufferX(sdlEvent.button.x);
                int y = ToFramebufferY(sdlEvent.button.y);
                long timestamp = (long)sdlEvent.button.timestamp / 1_000_000;

                result = (SDL_EventType)sdlEvent.type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN
                    ? InputEvent.MouseDown(x, y, button, timestamp)
                    : InputEvent.MouseUp(x, y, button, timestamp);
                return true;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                result = new InputEvent
                {
                    Kind = InputEventKind.MouseWheel,
                    X = ToFramebufferX(sdlEvent.wheel.mouse_x),
                    Y = ToFramebufferY(sdlEvent.wheel.mouse_y),
                    WheelDelta = sdlEvent.wheel.integer_y,
                    TimestampMs = (long)sdlEvent.wheel.timestamp / 1_000_000,
                };
                return true;

            default:
                return false;
        }
    }

    private int ToFramebufferX(float x) =>
        Math.Clamp((int)(x * framebufferWidth / windowWidth), 0, framebufferWidth - 1);

    private int ToFramebufferY(float y) =>
        Math.Clamp((int)(y * framebufferHeight / windowHeight), 0, framebufferHeight - 1);

    private static KeyModifiers ToModifiers(SDL_Keymod mod)
    {
        var modifiers = KeyModifiers.None;

        if ((mod & SDL_Keymod.SDL_KMOD_SHIFT) != 0)
        {
            modifiers |= KeyModifiers.Shift;
        }
        if ((mod & SDL_Keymod.SDL_KMOD_CTRL) != 0)
        {
            modifiers |= KeyModifiers.Control;
        }
        if ((mod & SDL_Keymod.SDL_KMOD_ALT) != 0)
        {
            modifiers |= KeyModifiers.Alt;
        }

        return modifiers;
    }

    private static MouseButtons ToButton(byte button) => button switch
    {
        (byte)SDL_BUTTON_LEFT => MouseButtons.Left,
        (byte)SDL_BUTTON_RIGHT => MouseButtons.Right,
        (byte)SDL_BUTTON_MIDDLE => MouseButtons.Middle,
        _ => MouseButtons.None,
    };

    private static MouseButtons ToButtons(SDL_MouseButtonFlags flags)
    {
        var buttons = MouseButtons.None;

        if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_LMASK) != 0)
        {
            buttons |= MouseButtons.Left;
        }
        if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_RMASK) != 0)
        {
            buttons |= MouseButtons.Right;
        }
        if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_MMASK) != 0)
        {
            buttons |= MouseButtons.Middle;
        }

        return buttons;
    }
}
