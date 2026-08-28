using System.Text;
using XTerm.Options;

namespace XTerm.Input;

/// <summary>
/// Kitty keyboard protocol enhancement flags, as set by <c>CSI = flags ; mode u</c>.
/// </summary>
/// <remarks>
/// Each flag makes the reporting richer, and an application asks for exactly the ones it wants.
/// See https://sw.kovidgoyal.net/kitty/keyboard-protocol/.
/// </remarks>
[Flags]
public enum KittyKeyboardFlags
{
    None = 0,

    /// <summary>Disambiguate escape codes: fixes the encodings that are ambiguous in legacy mode.</summary>
    DisambiguateEscapeCodes = 0b00001,

    /// <summary>Report event types: press, repeat and release.</summary>
    ReportEventTypes = 0b00010,

    /// <summary>Report alternate keys: the shifted key and the base-layout key.</summary>
    ReportAlternateKeys = 0b00100,

    /// <summary>Report all keys as escape codes: text-producing keys arrive as CSI u too.</summary>
    ReportAllKeysAsEscapeCodes = 0b01000,

    /// <summary>Report associated text: the text a keypress produced rides along in the sequence.</summary>
    ReportAssociatedText = 0b10000,
}

/// <summary>
/// The kind of keyboard event being encoded.
/// </summary>
public enum KittyKeyboardEventType
{
    Press = 1,
    Repeat = 2,
    Release = 3,
}

/// <summary>
/// Encodes keyboard events under the Kitty keyboard protocol.
/// </summary>
/// <remarks>
/// <para>Ported from xterm.js (src/common/input/KittyKeyboard.ts, MIT), including its later fixes:
/// Enter/Tab/Backspace keep their legacy bytes in disambiguate mode, shift alone never disambiguates
/// a character key, lock keys are suppressed like modifier presses unless every key is being
/// reported, and enabling only "report event types" must not swallow keys.</para>
/// <para><see cref="Evaluate"/> returns the bytes to send, or null when the event GENUINELY
/// produces nothing — a modifier pressed on its own, or a release the active flags say not to
/// report. Null never means "try the legacy generator": when the active flags call for legacy
/// bytes — plain text, a bare arrow key, or a Ctrl/Alt chord under flags that do not ask for
/// escape-code reporting (bare <see cref="KittyKeyboardFlags.ReportAlternateKeys"/> or
/// <see cref="KittyKeyboardFlags.ReportAssociatedText"/>) — this class produces those bytes
/// itself, exactly as kitty falls back to the legacy encoding for anything the flags do not
/// cover. Ctrl+C must send 0x03 under every legal flag value.</para>
/// </remarks>
public static class KittyKeyboard
{
    /// <summary>
    /// Keys that do not produce text, and the codepoints the protocol assigns them.
    /// F1-F12, arrows and navigation keys are absent on purpose: those keep their legacy CSI/SS3
    /// encodings below. Keyed by the browser-style key name the renderer maps its events to.
    /// </summary>
    private static readonly Dictionary<string, int> FunctionalKeyCodes = new()
    {
        ["Escape"] = 27,
        ["Enter"] = 13,
        ["Tab"] = 9,
        ["Backspace"] = 127,
        ["CapsLock"] = 57358,
        ["ScrollLock"] = 57359,
        ["NumLock"] = 57360,
        ["PrintScreen"] = 57361,
        ["Pause"] = 57362,
        ["ContextMenu"] = 57363,
        // F13-F25 (F1-F12 use the legacy encodings)
        ["F13"] = 57376,
        ["F14"] = 57377,
        ["F15"] = 57378,
        ["F16"] = 57379,
        ["F17"] = 57380,
        ["F18"] = 57381,
        ["F19"] = 57382,
        ["F20"] = 57383,
        ["F21"] = 57384,
        ["F22"] = 57385,
        ["F23"] = 57386,
        ["F24"] = 57387,
        ["F25"] = 57388,
        // Media keys
        ["MediaPlayPause"] = 57430,
        ["MediaStop"] = 57432,
        ["MediaTrackNext"] = 57435,
        ["MediaTrackPrevious"] = 57436,
        ["AudioVolumeDown"] = 57438,
        ["AudioVolumeUp"] = 57439,
        ["AudioVolumeMute"] = 57440,
    };

    /// <summary>Keys that keep the legacy <c>CSI number ~</c> encoding.</summary>
    private static readonly Dictionary<string, int> CsiTildeKeys = new()
    {
        ["Insert"] = 2,
        ["Delete"] = 3,
        ["PageUp"] = 5,
        ["PageDown"] = 6,
        ["F5"] = 15,
        ["F6"] = 17,
        ["F7"] = 18,
        ["F8"] = 19,
        ["F9"] = 20,
        ["F10"] = 21,
        ["F11"] = 23,
        ["F12"] = 24,
    };

    /// <summary>Keys that keep the legacy <c>CSI letter</c> encoding: arrows, Home, End.</summary>
    private static readonly Dictionary<string, char> CsiLetterKeys = new()
    {
        ["ArrowUp"] = 'A',
        ["ArrowDown"] = 'B',
        ["ArrowRight"] = 'C',
        ["ArrowLeft"] = 'D',
        ["Home"] = 'H',
        ["End"] = 'F',
    };

    /// <summary>F1-F4 keep the legacy SS3 encoding when unmodified.</summary>
    private static readonly Dictionary<string, char> Ss3FunctionKeys = new()
    {
        ["F1"] = 'P',
        ["F2"] = 'Q',
        ["F3"] = 'R',
        ["F4"] = 'S',
    };

    // Kitty's modifier bits differ from the legacy xterm encoding; the value sent is 1 + bits.
    private const int ModShift = 0b0001;
    private const int ModAlt   = 0b0010;
    private const int ModCtrl  = 0b0100;
    private const int ModSuper = 0b1000;

    /// <summary>
    /// Maps a physical numpad key to its protocol codepoint, or null for a key that is not on
    /// the numpad. Decided by <see cref="KeyEvent.Code"/>, because the key NAME of Numpad5 is
    /// just "5".
    /// </summary>
    private static int? GetNumpadKeyCode(KeyEvent ev)
    {
        if (!ev.Code.StartsWith("Numpad", StringComparison.Ordinal))
            return null;

        var suffix = ev.Code.Substring(6);
        if (suffix.Length == 1 && suffix[0] >= '0' && suffix[0] <= '9')
            return 57399 + (suffix[0] - '0');

        return suffix switch
        {
            "Decimal" => 57409,
            "Divide" => 57410,
            "Multiply" => 57411,
            "Subtract" => 57412,
            "Add" => 57413,
            "Enter" => 57414,
            "Equal" => 57415,
            _ => null
        };
    }

    /// <summary>
    /// Maps a modifier key to its protocol codepoint — left and right are DIFFERENT keys to this
    /// protocol, which is why this reads <see cref="KeyEvent.Code"/> and not the key name.
    /// </summary>
    private static int? GetModifierKeyCode(KeyEvent ev) => ev.Code switch
    {
        "ShiftLeft" => 57441,
        "ShiftRight" => 57447,
        "ControlLeft" => 57442,
        "ControlRight" => 57448,
        "AltLeft" => 57443,
        "AltRight" => 57449,
        "MetaLeft" => 57444,
        "MetaRight" => 57450,
        _ => null
    };

    /// <summary>
    /// Encodes the held modifiers as the protocol sends them: 1 + bits, or 0 when none are held
    /// (so a zero can be omitted from the sequence entirely).
    /// </summary>
    private static int EncodeModifiers(KeyEvent ev)
    {
        var mods = 0;
        if (ev.ShiftKey) mods |= ModShift;
        if (ev.AltKey) mods |= ModAlt;
        if (ev.CtrlKey) mods |= ModCtrl;
        if (ev.MetaKey) mods |= ModSuper;
        return mods > 0 ? mods + 1 : 0;
    }

    /// <summary>
    /// The protocol's key code for the event: the LOWERCASE, BASE-LAYOUT codepoint for character
    /// keys, or the assigned codepoint for functional keys. Null when the event has no code at
    /// all — a dead key with no way back to the physical key, for instance.
    /// </summary>
    /// <remarks>
    /// <para>The base-layout rule is why <see cref="KeyEvent.Code"/> matters: Shift+5 arrives with
    /// the key name "%", but the protocol wants 53 ('5'). The same unwinding recovers the letter
    /// under a macOS Option chord ("ƒ" back to 'f') when the host treats Option as Alt.</para>
    /// <para>The <c>Length == 1</c> gate means an astral-plane character — a Key that is a
    /// surrogate pair, "𐍈" — returns null and sends nothing once any flag is set, where the legacy
    /// path would have sent its UTF-8. Upstream xterm.js has the same gate
    /// (KittyKeyboard.ts <c>key.length === 1</c>), so this is inherited from the port, not
    /// introduced by it; fixing it means fixing it upstream first.</para>
    /// </remarks>
    private static int? GetKeyCode(KeyEvent ev, bool macOptionAsAlt)
    {
        var numpadCode = GetNumpadKeyCode(ev);
        if (numpadCode is not null)
            return numpadCode;

        var modifierCode = GetModifierKeyCode(ev);
        if (modifierCode is not null)
            return modifierCode;

        if (FunctionalKeyCodes.TryGetValue(ev.Key, out var funcCode))
            return funcCode;

        if ((ev.ShiftKey || (macOptionAsAlt && ev.AltKey)) && ev.Code.Length > 0)
        {
            if (ev.Code.StartsWith("Digit", StringComparison.Ordinal) && ev.Code.Length == 6)
            {
                var digit = ev.Code[5];
                if (digit >= '0' && digit <= '9')
                    return digit;
            }
            if (ev.Code.StartsWith("Key", StringComparison.Ordinal) && ev.Code.Length == 4)
                return char.ToLowerInvariant(ev.Code[3]);
        }

        if (ev.Key.Length == 1)
        {
            int code = ev.Key[0];
            if (code >= 'A' && code <= 'Z')
                return code + 32;
            return code;
        }

        return null;
    }

    /// <summary>A modifier key pressed as a key in its own right.</summary>
    private static bool IsModifierKey(KeyEvent ev)
        => ev.Key is "Shift" or "Control" or "Alt" or "Meta";

    /// <summary>
    /// A lock key pressed as a key in its own right.
    /// </summary>
    /// <remarks>
    /// Kitty's reference implementation classifies these as modifier keys for the purpose of
    /// suppressing press events (kitty/keys.c <c>is_modifier_key()</c> includes CapsLock,
    /// ScrollLock and NumLock), and its test suite asserts that a CapsLock press with no protocol
    /// flags produces empty output.
    /// </remarks>
    private static bool IsLockKey(KeyEvent ev)
        => ev.Key is "CapsLock" or "NumLock" or "ScrollLock";

    /// <summary>
    /// The legacy <c>CSI [1;mod] letter</c> form for arrows, Home and End, with the protocol's
    /// event-type sub-parameter folded in when it applies.
    /// </summary>
    private static string BuildCsiLetterSequence(
        char letter, int modifiers, KittyKeyboardEventType eventType, bool reportEventTypes)
    {
        var needsEventType = reportEventTypes && eventType != KittyKeyboardEventType.Press;

        if (modifiers > 0 || needsEventType)
        {
            var seq = new StringBuilder("\u001b[1;");
            seq.Append(modifiers > 0 ? modifiers : 1);
            if (needsEventType)
                seq.Append(':').Append((int)eventType);
            seq.Append(letter);
            return seq.ToString();
        }
        return $"\u001b[{letter}";
    }

    /// <summary>
    /// F1-F4: legacy <c>SS3 letter</c> unmodified, <c>CSI 1;mod letter</c> otherwise.
    /// </summary>
    private static string BuildSs3Sequence(
        char letter, int modifiers, KittyKeyboardEventType eventType, bool reportEventTypes)
    {
        var needsEventType = reportEventTypes && eventType != KittyKeyboardEventType.Press;

        if (modifiers > 0 || needsEventType)
        {
            var seq = new StringBuilder("\u001b[1;");
            seq.Append(modifiers > 0 ? modifiers : 1);
            if (needsEventType)
                seq.Append(':').Append((int)eventType);
            seq.Append(letter);
            return seq.ToString();
        }
        return $"\u001bO{letter}";
    }

    /// <summary>
    /// The legacy <c>CSI number [;mod[:event]] ~</c> form for Insert, Delete, Page keys and F5-F12.
    /// </summary>
    private static string BuildCsiTildeSequence(
        int number, int modifiers, KittyKeyboardEventType eventType, bool reportEventTypes)
    {
        var needsEventType = reportEventTypes && eventType != KittyKeyboardEventType.Press;

        var seq = new StringBuilder("\u001b[");
        seq.Append(number);
        if (modifiers > 0 || needsEventType)
        {
            seq.Append(';').Append(modifiers > 0 ? modifiers : 1);
            if (needsEventType)
                seq.Append(':').Append((int)eventType);
        }
        seq.Append('~');
        return seq.ToString();
    }

    /// <summary>
    /// The protocol's own form: <c>CSI keycode[:shifted] [;mod[:event][;text]] u</c>.
    /// </summary>
    private static string BuildCsiUSequence(
        KeyEvent ev,
        int keyCode,
        int modifiers,
        KittyKeyboardEventType eventType,
        KittyKeyboardFlags flags,
        bool isFunc,
        bool isMod)
    {
        var reportEventTypes = (flags & KittyKeyboardFlags.ReportEventTypes) != 0;
        var reportAlternateKeys = (flags & KittyKeyboardFlags.ReportAlternateKeys) != 0;

        var seq = new StringBuilder("\u001b[");
        seq.Append(keyCode);

        if (reportAlternateKeys && ev.ShiftKey && ev.Key.Length == 1 && !isFunc && !isMod)
            seq.Append(':').Append((int)ev.Key[0]);

        // The text never rides on a release — releasing a key types nothing — and never under
        // Ctrl, where the keypress produces a control code rather than the character.
        var reportText = (flags & KittyKeyboardFlags.ReportAssociatedText) != 0
            && eventType != KittyKeyboardEventType.Release
            && ev.Key.Length == 1
            && !isFunc
            && !isMod
            && !ev.CtrlKey;
        int? textCode = reportText ? ev.Key[0] : null;

        // The event-type sub-parameter and the text field are independent in the spec's form
        // (CSI code:alt ; mods:event ; text u): a repeat carries its :2 whether or not text rides
        // along, or an application that asked for event types cannot tell a held key from a
        // hammered one.
        var needsEventType = reportEventTypes && eventType != KittyKeyboardEventType.Press;

        if (modifiers > 0 || needsEventType || textCode is not null)
        {
            seq.Append(';');
            if (modifiers > 0)
                seq.Append(modifiers);
            else if (needsEventType)
                seq.Append('1');
            if (needsEventType)
                seq.Append(':').Append((int)eventType);
        }

        if (textCode is not null)
            seq.Append(';').Append(textCode.Value);

        seq.Append('u');
        return seq.ToString();
    }

    /// <summary>
    /// Encodes one keyboard event under the active flags.
    /// </summary>
    /// <param name="ev">The keyboard event.</param>
    /// <param name="flags">The active enhancement flags.</param>
    /// <param name="eventType">Press, repeat or release.</param>
    /// <param name="macOptionAsAlt">
    /// True when the host treats macOS Option as Alt, so an Option-composed key name ("ƒ") is
    /// unwound to the letter under it via <see cref="KeyEvent.Code"/>.
    /// </param>
    /// <returns>The bytes to send, or null when this event sends nothing.</returns>
    public static string? Evaluate(
        KeyEvent ev,
        KittyKeyboardFlags flags,
        KittyKeyboardEventType eventType = KittyKeyboardEventType.Press,
        bool macOptionAsAlt = false)
    {
        var modifiers = EncodeModifiers(ev);
        var isMod = IsModifierKey(ev);
        var reportEventTypes = (flags & KittyKeyboardFlags.ReportEventTypes) != 0;

        if (!reportEventTypes && eventType == KittyKeyboardEventType.Release)
            return null;

        // Spec, "Report all keys as escape codes": "Additionally, with this mode, events for
        // pressing modifier keys are reported." — i.e. WITHOUT this mode, modifier-key presses are
        // suppressed. Kitty's is_modifier_key() treats the lock keys as modifiers for this rule.
        if (isMod && (flags & KittyKeyboardFlags.ReportAllKeysAsEscapeCodes) == 0)
            return null;

        if (IsLockKey(ev) && (flags & KittyKeyboardFlags.ReportAllKeysAsEscapeCodes) == 0)
            return null;

        if (CsiLetterKeys.TryGetValue(ev.Key, out var csiLetter))
            return BuildCsiLetterSequence(csiLetter, modifiers, eventType, reportEventTypes);

        if (Ss3FunctionKeys.TryGetValue(ev.Key, out var ss3Letter))
            return BuildSs3Sequence(ss3Letter, modifiers, eventType, reportEventTypes);

        if (CsiTildeKeys.TryGetValue(ev.Key, out var tildeCode))
            return BuildCsiTildeSequence(tildeCode, modifiers, eventType, reportEventTypes);

        var keyCode = GetKeyCode(ev, macOptionAsAlt);
        if (keyCode is null)
            return null;

        // Enter, Tab and Backspace are special throughout: the spec keeps their legacy bytes in
        // disambiguate mode, and denies them release events unless every key is being reported.
        var specialKey = keyCode is 13 or 9 or 127;

        if (specialKey
            && eventType == KittyKeyboardEventType.Release
            && (flags & KittyKeyboardFlags.ReportAllKeysAsEscapeCodes) == 0)
        {
            return null;
        }

        var isFunc = FunctionalKeyCodes.ContainsKey(ev.Key) || GetNumpadKeyCode(ev) is not null;

        var useCsiU =
            (flags & KittyKeyboardFlags.ReportAllKeysAsEscapeCodes) != 0
            || (reportEventTypes && eventType == KittyKeyboardEventType.Release)
            // Enabling "report event types" without "disambiguate" makes little sense, so event
            // types imply disambiguation here. See kitty issue #9999 for the same conclusion.
            || (((flags & KittyKeyboardFlags.DisambiguateEscapeCodes) != 0 || reportEventTypes)
                && (
                    // Per spec, Enter/Tab/Backspace "still generate the same bytes as in legacy
                    // mode", and space counts as a text-generating key: both skip the functional
                    // fast path and only get CSI u when modifiers demand it below.
                    (isFunc && !specialKey)
                    || (modifiers > 0 && ev.Key.Length != 1)
                    // Shift alone never disambiguates a character key: Shift+a is just "A".
                    || modifiers - 1 > ModShift
                ));

        if (useCsiU)
            return BuildCsiUSequence(ev, keyCode.Value, modifiers, eventType, flags, isFunc, isMod);

        if (specialKey)
            return keyCode switch { 13 => "\r", 9 => "\t", _ => "\u007f" };

        // The flags did not ask for an escape code, so the key sends its LEGACY bytes — the
        // same ones KeyboardInputGenerator.GenerateCharSequence produces: Ctrl maps to the
        // control code, Alt prefixes ESC (even combined with Ctrl), Super has no legacy
        // representation and passes through. Returning null here instead would swallow Ctrl+C
        // under bare ReportAlternateKeys or ReportAssociatedText — legal flag values — and the
        // host does not fall back on null, so the chord would send nothing at all.
        if (ev.Key.Length == 1)
        {
            var text = ev.CtrlKey ? ControlCode(ev.Key[0]) : ev.Key;
            return ev.AltKey ? "\u001b" + text : text;
        }

        return null;
    }

    /// <summary>
    /// The legacy control code for Ctrl+key — the same mapping
    /// <see cref="KeyboardInputGenerator.GenerateCharSequence"/> uses, kept byte-identical so a
    /// chord encodes the same whether or not any protocol flag happens to be set.
    /// </summary>
    private static string ControlCode(char c) => c switch
    {
        >= 'a' and <= 'z' => ((char)(c - 'a' + 1)).ToString(),
        >= 'A' and <= 'Z' => ((char)(c - 'A' + 1)).ToString(),
        ' ' or '@' => "\u0000",
        '[' => "\u001b",
        '\\' => "\u001c",
        ']' => "\u001d",
        '^' => "\u001e",
        '_' => "\u001f",
        '?' => "\u007f",
        _ => c.ToString()
    };

    /// <summary>
    /// Whether the active flags call for this protocol at all. Zero flags is legacy encoding.
    /// </summary>
    public static bool ShouldUseProtocol(KittyKeyboardFlags flags) => flags != KittyKeyboardFlags.None;
}
