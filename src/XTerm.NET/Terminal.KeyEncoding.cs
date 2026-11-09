using XTerm.Input;
using XTerm.Options;
using Win32State = XTerm.Input.Win32InputGenerator.ControlKeyState;

namespace XTerm;

/// <summary>
/// One entry point that encodes a key for whichever keyboard protocol is live.
/// </summary>
public partial class Terminal
{
    /// <summary>
    /// Encodes one key event the way the running application is currently reading keys: Win32
    /// input records, then the Kitty keyboard protocol, then the legacy encodings.
    /// </summary>
    /// <remarks>
    /// <para><b>Precedence.</b> <see cref="Win32InputMode"/> (DECSET 9001) first: it is a different
    /// transport rather than a competing encoding, and a process reading INPUT_RECORDs reads them for
    /// every key. Then <see cref="KittyKeyboardActive"/>, through <see cref="GenerateKittyKeyInput"/>.
    /// Otherwise the legacy generators, which honour DECCKM (<see cref="ApplicationCursorKeys"/>)
    /// and DECKPAM (<see cref="ApplicationKeypad"/>).</para>
    /// <para><b>The key model</b> is the browser's, as <see cref="KeyEvent"/> already is for Kitty.
    /// <see cref="KeyEvent.Key"/> is what the key means: a name for a functional key
    /// (<c>"ArrowUp"</c>, <c>"Enter"</c>, <c>"Backspace"</c>, <c>"F5"</c>, <c>"Shift"</c>) or the
    /// text it typed (<c>"a"</c>, <c>"A"</c>, <c>"@"</c>, <c>" "</c>). <see cref="KeyEvent.Code"/> is
    /// the physical key (<c>"KeyA"</c>, <c>"Numpad7"</c>, <c>"AltRight"</c>); the keypad is told apart
    /// from the main keys by it alone. <see cref="KeyEvent.KeyCode"/>, when set, is taken as the
    /// Windows virtual-key code, which is what browsers report there.</para>
    /// <para><b>Modifiers.</b> Shift, Alt and Ctrl map to every protocol. <see cref="KeyEvent.MetaKey"/>
    /// is Super (Cmd, the Windows key): Kitty encodes it, and neither the legacy encodings nor a
    /// Win32 record can, so there it is dropped -- <see cref="KeyModifiers"/> has no Super member for
    /// that reason. Report AltGr as the character it composed with Ctrl and Alt clear; reported as
    /// Ctrl+Alt it is encoded as that chord.</para>
    /// <para><b>Not here:</b> host policy that rewrites keys before encoding them (Alt+Left as
    /// <c>ESC b</c>, Cmd shortcuts, selection editing), lock-key state in Win32 records (the event
    /// does not carry it), and ConPTY's need for a Win32 record for a bare Escape outside Win32 mode,
    /// which is a fact about the transport -- a host on ConPTY decides that.</para>
    /// </remarks>
    /// <param name="ev">The key event.</param>
    /// <param name="eventType">Press, repeat or release. Only Win32 records and Kitty (when its
    /// flags ask) report releases; the legacy encodings return null for one.</param>
    /// <returns>The bytes to send, or null when this event sends nothing: the keyboard is locked
    /// (KAM), a bare modifier outside Win32 and Kitty, a release legacy cannot express, or a key no
    /// protocol here has an encoding for.</returns>
    public string? EncodeKey(KeyEvent ev, KittyKeyboardEventType eventType = KittyKeyboardEventType.Press)
    {
        ArgumentNullException.ThrowIfNull(ev);

        if (KeyboardLocked)
            return null;

        if (Win32InputMode)
        {
            var record = EncodeWin32Record(ev, isKeyDown: eventType != KittyKeyboardEventType.Release);
            if (record is not null)
                return record;
            // No virtual key for it: fall through, as the control does.
        }

        if (KittyKeyboardActive)
            return GenerateKittyKeyInput(ev, eventType);

        if (eventType == KittyKeyboardEventType.Release)
            return null;

        var modifiers = GetLegacyModifiers(ev);

        if (TryGetLegacyNamedKey(ev, out var key))
            return NullIfEmpty(_keyboardInput.GenerateKeySequence(key, modifiers));

        if (ev.Key.Length == 0 || IsFunctionalKeyName(ev.Key))
            return null;

        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0 && ev.Key.Length == 1)
            return NullIfEmpty(_keyboardInput.GenerateCharSequence(ev.Key[0], modifiers));

        // Plain text: Shift is already in the character.
        return ev.Key;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>A name rather than text: more than one character and no character outside ASCII letters
    /// and digits. "Shift", "F13", "Unidentified" -- not "ab" typed by an IME, which has spaces or
    /// non-ASCII, nor a two-unit emoji.</summary>
    private static bool IsFunctionalKeyName(string key)
    {
        if (key.Length < 2 || !char.IsAsciiLetterUpper(key[0]))
            return false;
        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        }
        return true;
    }

    private static bool TryGetLegacyNamedKey(KeyEvent ev, out Key key)
    {
        if (ev.Code.StartsWith("Numpad", StringComparison.Ordinal))
        {
            Key? pad = ev.Code switch
            {
                "Numpad0" => Key.Keypad0, "Numpad1" => Key.Keypad1, "Numpad2" => Key.Keypad2,
                "Numpad3" => Key.Keypad3, "Numpad4" => Key.Keypad4, "Numpad5" => Key.Keypad5,
                "Numpad6" => Key.Keypad6, "Numpad7" => Key.Keypad7, "Numpad8" => Key.Keypad8,
                "Numpad9" => Key.Keypad9,
                "NumpadDecimal" => Key.KeypadDecimal,
                "NumpadDivide" => Key.KeypadDivide,
                "NumpadMultiply" => Key.KeypadMultiply,
                "NumpadSubtract" => Key.KeypadSubtract,
                "NumpadAdd" => Key.KeypadAdd,
                "NumpadEnter" => Key.KeypadEnter,
                _ => null
            };

            // With NumLock off the keypad digits are navigation keys, and Key says which.
            if (pad is { } p && (ev.Key.Length == 1 || p == Key.KeypadEnter))
            {
                key = p;
                return true;
            }
        }

        Key? named = ev.Key switch
        {
            "Enter" => Key.Enter,
            "Tab" => Key.Tab,
            "Backspace" => Key.Backspace,
            "Escape" => Key.Escape,
            "ArrowUp" => Key.UpArrow,
            "ArrowDown" => Key.DownArrow,
            "ArrowLeft" => Key.LeftArrow,
            "ArrowRight" => Key.RightArrow,
            "Home" => Key.Home,
            "End" => Key.End,
            "PageUp" => Key.PageUp,
            "PageDown" => Key.PageDown,
            "Insert" => Key.Insert,
            "Delete" => Key.Delete,
            _ => null
        };

        if (named is { } n)
        {
            key = n;
            return true;
        }

        if (ev.Key.Length is 2 or 3
            && ev.Key[0] == 'F'
            && int.TryParse(ev.Key.AsSpan(1), out var number)
            && number is >= 1 and <= 20)
        {
            key = (Key)((int)Key.F1 + number - 1);
            return true;
        }

        key = default;
        return false;
    }

    /// <summary>
    /// One Win32 INPUT_RECORD for the key, or null when it has no virtual-key code.
    /// </summary>
    private static string? EncodeWin32Record(KeyEvent ev, bool isKeyDown)
    {
        var vk = ev.KeyCode > 0 ? ev.KeyCode : GetVirtualKey(ev);
        if (vk is <= 0 or > ushort.MaxValue)
            return null;

        var unicode = 0;
        if (ev.Key.Length == 1 || (ev.Key.Length == 2 && char.IsSurrogatePair(ev.Key, 0)))
        {
            unicode = char.ConvertToUtf32(ev.Key, 0);
        }
        else
        {
            unicode = ev.Key switch
            {
                "Enter" => 0x0D,
                "Tab" => 0x09,
                "Backspace" => 0x08,
                "Escape" => 0x1B,
                _ => 0
            };
        }

        // Ctrl with a character sends its control code, which console programs expecting ^C read.
        if (ev.CtrlKey && unicode != 0)
        {
            unicode = unicode switch
            {
                >= 'a' and <= 'z' => unicode - 'a' + 1,
                >= 'A' and <= 'Z' => unicode - 'A' + 1,
                0x20 or '@' => 0x00,
                '[' => 0x1B,
                '\\' => 0x1C,
                ']' => 0x1D,
                '^' => 0x1E,
                '_' => 0x1F,
                '?' => 0x7F,
                _ => unicode
            };
        }

        var state = Win32State.None;
        if (ev.ShiftKey) state |= Win32State.ShiftPressed;
        if (ev.CtrlKey) state |= Win32State.LeftCtrlPressed;
        if (ev.AltKey) state |= Win32State.LeftAltPressed;

        // A modifier key names its own side; the flags above cannot.
        if (ev.Code == "AltRight")
            state = (state & ~Win32State.LeftAltPressed) | Win32State.RightAltPressed;
        if (ev.Code == "ControlRight")
            state = (state & ~Win32State.LeftCtrlPressed) | Win32State.RightCtrlPressed;

        if (IsEnhancedKey(ev))
            state |= Win32State.EnhancedKey;

        // Scan code 0: there is no hardware event to take one from.
        return $"\u001b[{vk};0;{unicode};{(isKeyDown ? 1 : 0)};{(int)state};1_";
    }

    private static bool IsEnhancedKey(KeyEvent ev) => ev.Code switch
    {
        "NumpadDivide" or "NumLock" or "ControlRight" or "AltRight" or "PrintScreen" or "Pause" => true,
        _ => !ev.Code.StartsWith("Numpad", StringComparison.Ordinal) && ev.Key is
            "Insert" or "Delete" or "Home" or "End" or "PageUp" or "PageDown"
            or "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight"
    };

    private static int GetVirtualKey(KeyEvent ev)
    {
        var code = ev.Code;

        // The physical key first, so a shifted or composed character still names its key.
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && char.IsAsciiLetterUpper(code[3]))
            return code[3];
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && char.IsAsciiDigit(code[5]))
            return code[5];
        // A keypad digit is VK_NUMPADn only while it types its digit. With NumLock off it is Home,
        // an arrow, Insert..., and Key says which: that key's own VK, below, is what the console sends.
        var typesCharacter = ev.Key.Length == 1;
        if (code.Length == 7 && code.StartsWith("Numpad", StringComparison.Ordinal) && char.IsAsciiDigit(code[6]))
        {
            if (typesCharacter)
                return 0x60 + code[6] - '0';
        }

        var vk = code switch
        {
            "NumpadMultiply" => 0x6A,
            "NumpadAdd" => 0x6B,
            "NumpadSubtract" => 0x6D,
            "NumpadDecimal" when typesCharacter => 0x6E,
            "NumpadDivide" => 0x6F,
            "NumpadEnter" => 0x0D,
            "MetaLeft" or "OSLeft" => 0x5B,
            "MetaRight" or "OSRight" => 0x5C,

            // The OEM keys, by position: the character a shifted one types ("?", ":", "{") has no
            // VK of its own, so the physical key is the only way to name it.
            "Semicolon" => 0xBA,
            "Equal" => 0xBB,
            "Comma" => 0xBC,
            "Minus" => 0xBD,
            "Period" => 0xBE,
            "Slash" => 0xBF,
            "Backquote" => 0xC0,
            "BracketLeft" => 0xDB,
            "Backslash" => 0xDC,
            "BracketRight" => 0xDD,
            "Quote" => 0xDE,
            "IntlBackslash" => 0xE2,
            "Space" => 0x20,
            _ => 0
        };
        if (vk != 0)
            return vk;

        vk = ev.Key switch
        {
            "ArrowLeft" => 0x25, "ArrowUp" => 0x26, "ArrowRight" => 0x27, "ArrowDown" => 0x28,
            "Home" => 0x24, "End" => 0x23, "PageUp" => 0x21, "PageDown" => 0x22,
            "Insert" => 0x2D, "Delete" => 0x2E,
            "Backspace" => 0x08, "Tab" => 0x09, "Enter" => 0x0D, "Escape" => 0x1B,
            "Pause" => 0x13, "CapsLock" => 0x14, "NumLock" => 0x90, "ScrollLock" => 0x91,
            "PrintScreen" => 0x2C,
            "Shift" => 0x10, "Control" => 0x11, "Alt" => 0x12, "Meta" => 0x5B,
            _ => 0
        };
        if (vk != 0)
            return vk;

        if (ev.Key.Length is 2 or 3
            && ev.Key[0] == 'F'
            && int.TryParse(ev.Key.AsSpan(1), out var number)
            && number is >= 1 and <= 24)
            return 0x70 + number - 1;

        if (ev.Key.Length != 1)
            return 0;

        var c = ev.Key[0];
        vk = Win32InputGenerator.GetVirtualKeyCode(c);
        return vk != 0 ? vk : ShiftedUsKey(c);
    }

    /// <summary>
    /// The key a shifted character is on, for a host that gave no physical code: the US layout,
    /// which is the same assumption <see cref="Win32InputGenerator.GetVirtualKeyCode"/> makes.
    /// </summary>
    private static int ShiftedUsKey(char c) => c switch
    {
        ')' => 0x30, '!' => 0x31, '@' => 0x32, '#' => 0x33, '$' => 0x34,
        '%' => 0x35, '^' => 0x36, '&' => 0x37, '*' => 0x38, '(' => 0x39,
        ':' => 0xBA, '+' => 0xBB, '<' => 0xBC, '_' => 0xBD, '>' => 0xBE,
        '?' => 0xBF, '~' => 0xC0, '{' => 0xDB, '|' => 0xDC, '}' => 0xDD, '"' => 0xDE,
        _ => 0
    };
}
