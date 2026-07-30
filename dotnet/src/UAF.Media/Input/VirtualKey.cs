namespace UAF.Media;

/// <summary>
/// Keys, numbered with the Win32 <c>VK_*</c> values the engine already speaks.
/// </summary>
/// <remarks>
/// <para>
/// Keeping the Win32 numbering is a deliberate compatibility choice, not laziness. The engine's
/// input path stores a raw virtual key in <c>KEY_DATA::vkey</c> and switches on it in
/// <c>CInput::LookupKeyCode</c> (<c>UAFWin/Getinput.cpp:346</c>), and designs can bind keys through
/// script. Renumbering would make that translation a table lookup that has to be got exactly right
/// for 60-odd cases; matching the values makes it a cast.
/// </para>
/// <para>
/// Only the keys the engine actually looks at are listed. The set was taken from every <c>VK_</c>
/// reference under <c>src/UAFWin</c> plus the letter, digit and punctuation cases in
/// <c>LookupKeyCode</c>. A platform backend that sees anything else reports
/// <see cref="Unknown"/> rather than inventing a value.
/// </para>
/// </remarks>
public enum VirtualKey
{
    Unknown = 0,

    Backspace = 0x08,
    Tab = 0x09,
    Return = 0x0D,
    Shift = 0x10,
    Control = 0x11,

    /// <summary><c>VK_MENU</c> — the Alt key.</summary>
    Menu = 0x12,

    Escape = 0x1B,
    Space = 0x20,

    /// <summary><c>VK_PRIOR</c> — Page Up.</summary>
    PageUp = 0x21,

    /// <summary><c>VK_NEXT</c> — Page Down.</summary>
    PageDown = 0x22,

    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Insert = 0x2D,
    Delete = 0x2E,

    D0 = 0x30, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    A = 0x41, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    NumPad0 = 0x60, NumPad1, NumPad2, NumPad3, NumPad4,
    NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,

    Multiply = 0x6A,
    Add = 0x6B,
    Subtract = 0x6D,
    Decimal = 0x6E,
    Divide = 0x6F,

    F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    /// <summary><c>VK_OEM_1</c> — <c>;:</c> on a US layout.</summary>
    Semicolon = 0xBA,

    /// <summary><c>VK_OEM_PLUS</c> — <c>=+</c>.</summary>
    Plus = 0xBB,

    Comma = 0xBC,

    /// <summary><c>VK_OEM_MINUS</c> — <c>-_</c>.</summary>
    Minus = 0xBD,

    Period = 0xBE,

    /// <summary><c>VK_OEM_2</c> — <c>/?</c>.</summary>
    Slash = 0xBF,

    /// <summary><c>VK_OEM_3</c> — <c>`~</c>.</summary>
    Grave = 0xC0,

    /// <summary><c>VK_OEM_4</c> — <c>[{</c>.</summary>
    LeftBracket = 0xDB,

    /// <summary><c>VK_OEM_5</c> — <c>\|</c>.</summary>
    Backslash = 0xDC,

    /// <summary><c>VK_OEM_6</c> — <c>]}</c>.</summary>
    RightBracket = 0xDD,

    /// <summary><c>VK_OEM_7</c> — <c>'"</c>.</summary>
    Quote = 0xDE,
}

/// <summary>
/// Modifier state, with the bit values from <c>UAFWin/Getinput.h:65</c> so
/// <c>KEY_DATA::flags</c> transcribes directly.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}

/// <summary>Mouse buttons. The engine only ever distinguishes two (<c>MOUSE_DATA</c>).</summary>
[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
}
