namespace XTerm.Input;

/// <summary>
/// Generates Win32 input mode escape sequences for ConPTY.
/// Win32 Input Mode (CSI ? 9001 h) sends keyboard events in a format that
/// preserves virtual key codes, scan codes, and key up/down state.
/// </summary>
public static class Win32InputGenerator
{
    /// <summary>
    /// Control key state flags matching Windows Console API.
    /// </summary>
    [Flags]
    public enum ControlKeyState : uint
    {
        None = 0,
        RightAltPressed = 0x0001,
        LeftAltPressed = 0x0002,
        RightCtrlPressed = 0x0004,
        LeftCtrlPressed = 0x0008,
        ShiftPressed = 0x0010,
        NumLockOn = 0x0020,
        ScrollLockOn = 0x0040,
        CapsLockOn = 0x0080,
        EnhancedKey = 0x0100
    }

    /// <summary>
    /// Generates a Win32 input mode key event sequence.
    /// Format: CSI Vk ; Sc ; Uc ; Kd ; Cs ; Rc _
    /// </summary>
    /// <param name="virtualKeyCode">Windows virtual key code (VK_*)</param>
    /// <param name="scanCode">Keyboard scan code</param>
    /// <param name="unicodeChar">Unicode character (0 for non-character keys)</param>
    /// <param name="keyDown">True for key down, false for key up</param>
    /// <param name="controlKeyState">Control key state flags</param>
    /// <param name="repeatCount">Repeat count (usually 1)</param>
    /// <returns>The escape sequence to send to ConPTY</returns>
    public static string GenerateKeyEvent(
        ushort virtualKeyCode,
        ushort scanCode,
        char unicodeChar,
        bool keyDown,
        ControlKeyState controlKeyState,
        ushort repeatCount = 1)
    {
        // Format: CSI Vk ; Sc ; Uc ; Kd ; Cs ; Rc _
        return $"\u001b[{virtualKeyCode};{scanCode};{(ushort)unicodeChar};{(keyDown ? 1 : 0)};{(uint)controlKeyState};{repeatCount}_";
    }

    /// <summary>
    /// Generates a pair of key down and key up events.
    /// </summary>
    public static string GenerateKeyPress(
        ushort virtualKeyCode,
        ushort scanCode,
        char unicodeChar,
        ControlKeyState controlKeyState)
    {
        var keyDown = GenerateKeyEvent(virtualKeyCode, scanCode, unicodeChar, true, controlKeyState);
        var keyUp = GenerateKeyEvent(virtualKeyCode, scanCode, unicodeChar, false, controlKeyState);
        return keyDown + keyUp;
    }

    /// <summary>
    /// Converts XTerm.NET KeyModifiers to Win32 ControlKeyState.
    /// </summary>
    public static ControlKeyState ConvertModifiers(KeyModifiers modifiers)
    {
        var state = ControlKeyState.None;
        
        if ((modifiers & KeyModifiers.Shift) != 0)
            state |= ControlKeyState.ShiftPressed;
        if ((modifiers & KeyModifiers.Control) != 0)
            state |= ControlKeyState.LeftCtrlPressed;
        if ((modifiers & KeyModifiers.Alt) != 0)
            state |= ControlKeyState.LeftAltPressed;
            
        return state;
    }

    /// <summary>
    /// Gets the Windows virtual key code for a character.
    /// </summary>
    public static ushort GetVirtualKeyCode(char c)
    {
        // Letters A-Z
        if (c >= 'a' && c <= 'z')
            return (ushort)(0x41 + (c - 'a')); // VK_A through VK_Z
        if (c >= 'A' && c <= 'Z')
            return (ushort)(0x41 + (c - 'A')); // VK_A through VK_Z
            
        // Numbers 0-9
        if (c >= '0' && c <= '9')
            return (ushort)(0x30 + (c - '0')); // VK_0 through VK_9
            
        // Common special characters
        return c switch
        {
            ' ' => 0x20,  // VK_SPACE
            '\r' => 0x0D, // VK_RETURN
            '\n' => 0x0D, // VK_RETURN
            '\t' => 0x09, // VK_TAB
            '\b' => 0x08, // VK_BACK
            '\x1b' => 0x1B, // VK_ESCAPE
            '-' => 0xBD,  // VK_OEM_MINUS
            '=' => 0xBB,  // VK_OEM_PLUS
            '[' => 0xDB,  // VK_OEM_4
            ']' => 0xDD,  // VK_OEM_6
            '\\' => 0xDC, // VK_OEM_5
            ';' => 0xBA,  // VK_OEM_1
            '\'' => 0xDE, // VK_OEM_7
            ',' => 0xBC,  // VK_OEM_COMMA
            '.' => 0xBE,  // VK_OEM_PERIOD
            '/' => 0xBF,  // VK_OEM_2
            '`' => 0xC0,  // VK_OEM_3
            _ => 0        // Unknown
        };
    }

    /// <summary>
    /// Gets a default scan code for a virtual key code.
    /// This is a simplified mapping - real scan codes depend on keyboard layout.
    /// </summary>
    public static ushort GetScanCode(ushort virtualKeyCode)
    {
        // These are US keyboard scan codes - they may differ on other layouts
        return virtualKeyCode switch
        {
            0x41 => 0x1E, // A
            0x42 => 0x30, // B
            0x43 => 0x2E, // C
            0x44 => 0x20, // D
            0x45 => 0x12, // E
            0x46 => 0x21, // F
            0x47 => 0x22, // G
            0x48 => 0x23, // H
            0x49 => 0x17, // I
            0x4A => 0x24, // J
            0x4B => 0x25, // K
            0x4C => 0x26, // L
            0x4D => 0x32, // M
            0x4E => 0x31, // N
            0x4F => 0x18, // O
            0x50 => 0x19, // P
            0x51 => 0x10, // Q
            0x52 => 0x13, // R
            0x53 => 0x1F, // S
            0x54 => 0x14, // T
            0x55 => 0x16, // U
            0x56 => 0x2F, // V
            0x57 => 0x11, // W
            0x58 => 0x2D, // X
            0x59 => 0x15, // Y
            0x5A => 0x2C, // Z
            0x30 => 0x0B, // 0
            0x31 => 0x02, // 1
            0x32 => 0x03, // 2
            0x33 => 0x04, // 3
            0x34 => 0x05, // 4
            0x35 => 0x06, // 5
            0x36 => 0x07, // 6
            0x37 => 0x08, // 7
            0x38 => 0x09, // 8
            0x39 => 0x0A, // 9
            0x20 => 0x39, // Space
            0x0D => 0x1C, // Enter
            0x09 => 0x0F, // Tab
            0x08 => 0x0E, // Backspace
            0x1B => 0x01, // Escape
            _ => 0x00
        };
    }
}
