namespace XTerm.NET.Input;

/// <summary>
/// Represents keyboard keys that can generate input sequences.
/// </summary>
public enum Key
{
    // Character keys (A-Z, 0-9, etc. are typically handled as char input)
    
    // Control keys
    Enter,
    Tab,
    Backspace,
    Escape,
    Space,
    
    // Navigation keys
    UpArrow,
    DownArrow,
    RightArrow,
    LeftArrow,
    Home,
    End,
    PageUp,
    PageDown,
    Insert,
    Delete,
    
    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20,
    
    // Keypad keys (numeric keypad)
    Keypad0, Keypad1, Keypad2, Keypad3, Keypad4,
    Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,
    KeypadDecimal,
    KeypadDivide,
    KeypadMultiply,
    KeypadSubtract,
    KeypadAdd,
    KeypadEnter,
}

/// <summary>
/// Modifier keys that can be combined with other keys.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}
