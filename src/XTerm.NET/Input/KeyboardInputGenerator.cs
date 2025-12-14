using System.Text;

namespace XTerm.NET.Input;

/// <summary>
/// Generates escape sequences for keyboard input based on terminal state.
/// </summary>
public class KeyboardInputGenerator
{
    private readonly Terminal _terminal;

    public KeyboardInputGenerator(Terminal terminal)
    {
        _terminal = terminal;
    }

    /// <summary>
    /// Generates the escape sequence for a key press.
    /// </summary>
    public string GenerateKeySequence(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        // Handle control characters for letter keys
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            var ctrlSeq = GetControlCharacter(key);
            if (ctrlSeq != null)
                return ctrlSeq;
        }

        // Handle special keys
        return key switch
        {
            // Control keys
            Key.Enter => "\r",
            Key.Tab => (modifiers & KeyModifiers.Shift) != 0 ? "\x1B[Z" : "\t",
            Key.Backspace => "\x7F", // DEL (127)
            Key.Escape => "\x1B",
            Key.Space => " ",

            // Arrow keys
            Key.UpArrow => GetArrowKey('A', modifiers),
            Key.DownArrow => GetArrowKey('B', modifiers),
            Key.RightArrow => GetArrowKey('C', modifiers),
            Key.LeftArrow => GetArrowKey('D', modifiers),

            // Navigation keys
            Key.Home => GetNavigationKey('H', modifiers),
            Key.End => GetNavigationKey('F', modifiers),
            Key.PageUp => GetModifiedSequence("5~", modifiers),
            Key.PageDown => GetModifiedSequence("6~", modifiers),
            Key.Insert => GetModifiedSequence("2~", modifiers),
            Key.Delete => GetModifiedSequence("3~", modifiers),

            // Function keys
            Key.F1 => GetFunctionKey(1, modifiers),
            Key.F2 => GetFunctionKey(2, modifiers),
            Key.F3 => GetFunctionKey(3, modifiers),
            Key.F4 => GetFunctionKey(4, modifiers),
            Key.F5 => GetFunctionKey(5, modifiers),
            Key.F6 => GetFunctionKey(6, modifiers),
            Key.F7 => GetFunctionKey(7, modifiers),
            Key.F8 => GetFunctionKey(8, modifiers),
            Key.F9 => GetFunctionKey(9, modifiers),
            Key.F10 => GetFunctionKey(10, modifiers),
            Key.F11 => GetFunctionKey(11, modifiers),
            Key.F12 => GetFunctionKey(12, modifiers),
            Key.F13 => GetFunctionKey(13, modifiers),
            Key.F14 => GetFunctionKey(14, modifiers),
            Key.F15 => GetFunctionKey(15, modifiers),
            Key.F16 => GetFunctionKey(16, modifiers),
            Key.F17 => GetFunctionKey(17, modifiers),
            Key.F18 => GetFunctionKey(18, modifiers),
            Key.F19 => GetFunctionKey(19, modifiers),
            Key.F20 => GetFunctionKey(20, modifiers),

            // Keypad keys
            Key.Keypad0 => GetKeypadKey('p', '0'),
            Key.Keypad1 => GetKeypadKey('q', '1'),
            Key.Keypad2 => GetKeypadKey('r', '2'),
            Key.Keypad3 => GetKeypadKey('s', '3'),
            Key.Keypad4 => GetKeypadKey('t', '4'),
            Key.Keypad5 => GetKeypadKey('u', '5'),
            Key.Keypad6 => GetKeypadKey('v', '6'),
            Key.Keypad7 => GetKeypadKey('w', '7'),
            Key.Keypad8 => GetKeypadKey('x', '8'),
            Key.Keypad9 => GetKeypadKey('y', '9'),
            Key.KeypadDecimal => GetKeypadKey('n', '.'),
            Key.KeypadDivide => "/",
            Key.KeypadMultiply => "*",
            Key.KeypadSubtract => "-",
            Key.KeypadAdd => "+",
            Key.KeypadEnter => "\r",

            _ => string.Empty
        };
    }

    /// <summary>
    /// Generates the escape sequence for a character with modifiers.
    /// </summary>
    public string GenerateCharSequence(char c, KeyModifiers modifiers = KeyModifiers.None)
    {
        // Control + character
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            // Ctrl+A through Ctrl+Z generate 0x01 through 0x1A
            if (c >= 'a' && c <= 'z')
                return ((char)(c - 'a' + 1)).ToString();
            if (c >= 'A' && c <= 'Z')
                return ((char)(c - 'A' + 1)).ToString();

            // Special control characters
            return c switch
            {
                ' ' => "\x00", // Ctrl+Space = NUL
                '@' => "\x00", // Ctrl+@ = NUL
                '[' => "\x1B", // Ctrl+[ = ESC
                '\\' => "\x1C", // Ctrl+\ = FS
                ']' => "\x1D", // Ctrl+] = GS
                '^' => "\x1E", // Ctrl+^ = RS
                '_' => "\x1F", // Ctrl+_ = US
                '?' => "\x7F", // Ctrl+? = DEL
                _ => c.ToString()
            };
        }

        // Alt + character
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            // Alt sends ESC prefix - use \u format to avoid interpolation issues
            return $"\u001b{c}";
        }

        // Shift is implicit in the character itself (uppercase vs lowercase)
        return c.ToString();
    }

    private string? GetControlCharacter(Key key)
    {
        // Control key combinations
        return key switch
        {
            Key.Space => "\x00",      // Ctrl+Space = NUL
            Key.LeftArrow => GetArrowKey('D', KeyModifiers.Control),
            Key.RightArrow => GetArrowKey('C', KeyModifiers.Control),
            Key.UpArrow => GetArrowKey('A', KeyModifiers.Control),
            Key.DownArrow => GetArrowKey('B', KeyModifiers.Control),
            _ => null
        };
    }

    private string GetArrowKey(char direction, KeyModifiers modifiers)
    {
        // Arrow keys respect ApplicationCursorKeys mode
        var appMode = _terminal.ApplicationCursorKeys;
        
        if (modifiers == KeyModifiers.None)
        {
            // Normal arrow keys
            return appMode ? $"\x1BO{direction}" : $"\x1B[{direction}";
        }

        // Modified arrow keys use CSI 1 ; modifier ; direction format
        var modCode = GetModifierCode(modifiers);
        return $"\x1B[1;{modCode}{direction}";
    }

    private string GetNavigationKey(char key, KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.None)
        {
            return $"\x1B[{key}";
        }

        // Modified navigation keys
        var modCode = GetModifierCode(modifiers);
        return $"\x1B[1;{modCode}{key}";
    }

    private string GetModifiedSequence(string sequence, KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.None)
        {
            return $"\x1B[{sequence}";
        }

        // Insert modifier code before the final character
        var modCode = GetModifierCode(modifiers);
        var lastChar = sequence[^1];
        var prefix = sequence[..^1];
        return $"\x1B[{prefix};{modCode}{lastChar}";
    }

    private string GetFunctionKey(int number, KeyModifiers modifiers)
    {
        // F1-F4 use SS3 sequences (ESC O)
        if (number >= 1 && number <= 4)
        {
            var code = (char)('P' + number - 1);
            if (modifiers == KeyModifiers.None)
            {
                return $"\x1BO{code}";
            }
            else
            {
                var modCode = GetModifierCode(modifiers);
                return $"\x1B[1;{modCode}{code}";
            }
        }

        // F5-F12 use CSI sequences
        var seqNumber = number switch
        {
            5 => 15,
            6 => 17,
            7 => 18,
            8 => 19,
            9 => 20,
            10 => 21,
            11 => 23,
            12 => 24,
            13 => 25,
            14 => 26,
            15 => 28,
            16 => 29,
            17 => 31,
            18 => 32,
            19 => 33,
            20 => 34,
            _ => 0
        };

        if (seqNumber == 0)
            return string.Empty;

        if (modifiers == KeyModifiers.None)
        {
            return $"\x1B[{seqNumber}~";
        }
        else
        {
            var modCode = GetModifierCode(modifiers);
            return $"\x1B[{seqNumber};{modCode}~";
        }
    }

    private string GetKeypadKey(char appChar, char numChar)
    {
        // Keypad keys respect ApplicationKeypad mode
        if (_terminal.ApplicationKeypad)
        {
            return $"\x1BO{appChar}";
        }
        else
        {
            return numChar.ToString();
        }
    }

    private int GetModifierCode(KeyModifiers modifiers)
    {
        // Modifier encoding: 1 + (Shift * 1) + (Alt * 2) + (Control * 4)
        int code = 1;
        if ((modifiers & KeyModifiers.Shift) != 0) code += 1;
        if ((modifiers & KeyModifiers.Alt) != 0) code += 2;
        if ((modifiers & KeyModifiers.Control) != 0) code += 4;
        return code;
    }
}
