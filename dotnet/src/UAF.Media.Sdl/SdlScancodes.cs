using SDL;

namespace UAF.Media.Sdl;

/// <summary>
/// Maps SDL scancodes onto <see cref="VirtualKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scancodes, not keycodes. A scancode is the physical key; a keycode is what the layout says that
/// key produces. The engine's key handling is positional — numpad 7 is north-west, WASD-style letter
/// keys are commands — so it wants the physical key, and using keycodes would move the movement keys
/// around on an AZERTY keyboard. Typed text takes the other route entirely, through
/// <see cref="InputEventKind.TextInput"/>, which is where the layout should apply.
/// </para>
/// <para>
/// Deliberately partial: only the keys <c>CInput::LookupKeyCode</c> handles are mapped
/// (<c>UAFWin/Getinput.cpp:346</c>). Anything else becomes <see cref="VirtualKey.Unknown"/>, which the
/// engine ignores, rather than a plausible-looking wrong key.
/// </para>
/// </remarks>
internal static class SdlScancodes
{
    private static readonly Dictionary<SDL_Scancode, VirtualKey> Map = Build();

    public static VirtualKey ToVirtualKey(SDL_Scancode scancode) =>
        Map.GetValueOrDefault(scancode, VirtualKey.Unknown);

    private static Dictionary<SDL_Scancode, VirtualKey> Build()
    {
        var map = new Dictionary<SDL_Scancode, VirtualKey>
        {
            [SDL_Scancode.SDL_SCANCODE_BACKSPACE] = VirtualKey.Backspace,
            [SDL_Scancode.SDL_SCANCODE_TAB] = VirtualKey.Tab,
            [SDL_Scancode.SDL_SCANCODE_RETURN] = VirtualKey.Return,
            [SDL_Scancode.SDL_SCANCODE_KP_ENTER] = VirtualKey.Return,
            [SDL_Scancode.SDL_SCANCODE_LSHIFT] = VirtualKey.Shift,
            [SDL_Scancode.SDL_SCANCODE_RSHIFT] = VirtualKey.Shift,
            [SDL_Scancode.SDL_SCANCODE_LCTRL] = VirtualKey.Control,
            [SDL_Scancode.SDL_SCANCODE_RCTRL] = VirtualKey.Control,
            [SDL_Scancode.SDL_SCANCODE_LALT] = VirtualKey.Menu,
            [SDL_Scancode.SDL_SCANCODE_RALT] = VirtualKey.Menu,
            [SDL_Scancode.SDL_SCANCODE_ESCAPE] = VirtualKey.Escape,
            [SDL_Scancode.SDL_SCANCODE_SPACE] = VirtualKey.Space,
            [SDL_Scancode.SDL_SCANCODE_PAGEUP] = VirtualKey.PageUp,
            [SDL_Scancode.SDL_SCANCODE_PAGEDOWN] = VirtualKey.PageDown,
            [SDL_Scancode.SDL_SCANCODE_END] = VirtualKey.End,
            [SDL_Scancode.SDL_SCANCODE_HOME] = VirtualKey.Home,
            [SDL_Scancode.SDL_SCANCODE_LEFT] = VirtualKey.Left,
            [SDL_Scancode.SDL_SCANCODE_UP] = VirtualKey.Up,
            [SDL_Scancode.SDL_SCANCODE_RIGHT] = VirtualKey.Right,
            [SDL_Scancode.SDL_SCANCODE_DOWN] = VirtualKey.Down,
            [SDL_Scancode.SDL_SCANCODE_INSERT] = VirtualKey.Insert,
            [SDL_Scancode.SDL_SCANCODE_DELETE] = VirtualKey.Delete,

            [SDL_Scancode.SDL_SCANCODE_KP_MULTIPLY] = VirtualKey.Multiply,
            [SDL_Scancode.SDL_SCANCODE_KP_PLUS] = VirtualKey.Add,
            [SDL_Scancode.SDL_SCANCODE_KP_MINUS] = VirtualKey.Subtract,
            [SDL_Scancode.SDL_SCANCODE_KP_PERIOD] = VirtualKey.Decimal,
            [SDL_Scancode.SDL_SCANCODE_KP_DIVIDE] = VirtualKey.Divide,

            [SDL_Scancode.SDL_SCANCODE_SEMICOLON] = VirtualKey.Semicolon,
            [SDL_Scancode.SDL_SCANCODE_EQUALS] = VirtualKey.Plus,
            [SDL_Scancode.SDL_SCANCODE_COMMA] = VirtualKey.Comma,
            [SDL_Scancode.SDL_SCANCODE_MINUS] = VirtualKey.Minus,
            [SDL_Scancode.SDL_SCANCODE_PERIOD] = VirtualKey.Period,
            [SDL_Scancode.SDL_SCANCODE_SLASH] = VirtualKey.Slash,
            [SDL_Scancode.SDL_SCANCODE_GRAVE] = VirtualKey.Grave,
            [SDL_Scancode.SDL_SCANCODE_LEFTBRACKET] = VirtualKey.LeftBracket,
            [SDL_Scancode.SDL_SCANCODE_BACKSLASH] = VirtualKey.Backslash,
            [SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET] = VirtualKey.RightBracket,
            [SDL_Scancode.SDL_SCANCODE_APOSTROPHE] = VirtualKey.Quote,
        };

        // A..Z and the twelve function keys are contiguous in both enumerations, so a loop is both
        // shorter and less error-prone than 38 more table entries.
        for (int i = 0; i < 26; i++)
        {
            map[SDL_Scancode.SDL_SCANCODE_A + i] = VirtualKey.A + i;
        }

        for (int i = 0; i < 12; i++)
        {
            map[SDL_Scancode.SDL_SCANCODE_F1 + i] = VirtualKey.F1 + i;
        }

        // The digit row is NOT contiguous in the same order: SDL numbers 1..9 then 0, while VK
        // numbers 0..9. Writing this as a loop is exactly how the zero key ends up wrong.
        for (int digit = 1; digit <= 9; digit++)
        {
            map[SDL_Scancode.SDL_SCANCODE_1 + (digit - 1)] = VirtualKey.D0 + digit;
        }
        map[SDL_Scancode.SDL_SCANCODE_0] = VirtualKey.D0;

        // The keypad has the same twist: SDL_SCANCODE_KP_1..KP_9 then KP_0.
        for (int digit = 1; digit <= 9; digit++)
        {
            map[SDL_Scancode.SDL_SCANCODE_KP_1 + (digit - 1)] = VirtualKey.NumPad0 + digit;
        }
        map[SDL_Scancode.SDL_SCANCODE_KP_0] = VirtualKey.NumPad0;

        return map;
    }
}
