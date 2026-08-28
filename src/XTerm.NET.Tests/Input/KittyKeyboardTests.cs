using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// The Kitty keyboard encoder, held to the xterm.js test suite (KittyKeyboard.test.ts) it was
/// ported from — including the behaviors that suite only gained through later fixes: legacy bytes
/// for Enter/Tab/Backspace in disambiguate mode, shift alone never disambiguating, lock keys
/// suppressed without "report all keys", and "report event types" alone not swallowing keys.
/// </summary>
public class KittyKeyboardTests
{
    private const KittyKeyboardFlags Disambiguate = KittyKeyboardFlags.DisambiguateEscapeCodes;
    private const KittyKeyboardFlags EventTypes = KittyKeyboardFlags.ReportEventTypes;
    private const KittyKeyboardFlags Alternates = KittyKeyboardFlags.ReportAlternateKeys;
    private const KittyKeyboardFlags AllKeys = KittyKeyboardFlags.ReportAllKeysAsEscapeCodes;
    private const KittyKeyboardFlags Text = KittyKeyboardFlags.ReportAssociatedText;


    private static KeyEvent Ev(
        string key, string code = "",
        bool shift = false, bool alt = false, bool ctrl = false, bool meta = false)
        => new() { Key = key, Code = code, ShiftKey = shift, AltKey = alt, CtrlKey = ctrl, MetaKey = meta };

    [Fact]
    public void Protocol_is_off_at_zero_flags_and_on_at_any()
    {
        Assert.False(KittyKeyboard.ShouldUseProtocol(KittyKeyboardFlags.None));
        Assert.True(KittyKeyboard.ShouldUseProtocol(Disambiguate));
        Assert.True(KittyKeyboard.ShouldUseProtocol(EventTypes));
        Assert.True(KittyKeyboard.ShouldUseProtocol((KittyKeyboardFlags)0b11111));
    }

    // ----- Modifier encoding (value = 1 + bits) --------------------------------------------

    [Fact]
    public void Shift_alone_on_a_character_key_stays_plain_text()
    {
        // Shift+a is "A" — not ambiguous in legacy encoding, so disambiguate leaves it alone.
        Assert.Equal("A", KittyKeyboard.Evaluate(Ev("A", shift: true), Disambiguate));
    }

    [Theory]
    [InlineData(false, true, false, false, "\u001b[97;3u")]  // alt = 1+2
    [InlineData(false, false, true, false, "\u001b[97;5u")]  // ctrl = 1+4
    [InlineData(false, false, false, true, "\u001b[97;9u")]  // super = 1+8
    [InlineData(true, false, true, false, "\u001b[97;6u")]   // ctrl+shift = 1+4+1
    [InlineData(false, true, true, false, "\u001b[97;7u")]   // ctrl+alt = 1+4+2
    [InlineData(true, true, true, false, "\u001b[97;8u")]    // ctrl+alt+shift
    [InlineData(false, false, true, true, "\u001b[97;13u")]  // ctrl+super
    [InlineData(true, true, true, true, "\u001b[97;16u")]    // all four
    public void Modifiers_encode_as_one_plus_bits(bool shift, bool alt, bool ctrl, bool meta, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev("a", shift: shift, alt: alt, ctrl: ctrl, meta: meta), Disambiguate));
    }

    [Fact]
    public void No_modifiers_omits_the_modifier_field()
    {
        Assert.Equal("\u001b[27u", KittyKeyboard.Evaluate(Ev("Escape"), Disambiguate));
    }

    // ----- C0 control keys in disambiguate mode --------------------------------------------

    [Theory]
    [InlineData("Escape", "\u001b[27u")]
    [InlineData("Enter", "\r")]      // per spec these keep their legacy bytes...
    [InlineData("Tab", "\t")]
    [InlineData("Backspace", "\u007f")]
    [InlineData(" ", " ")]           // ...and space counts as a text-generating key
    public void C0_keys_unmodified(string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key), Disambiguate));
    }

    [Fact]
    public void Shift_Tab_is_CSI_9_2_u()
        => Assert.Equal("\u001b[9;2u", KittyKeyboard.Evaluate(Ev("Tab", shift: true), Disambiguate));

    [Fact]
    public void Ctrl_Enter_is_CSI_13_5_u()
        => Assert.Equal("\u001b[13;5u", KittyKeyboard.Evaluate(Ev("Enter", ctrl: true), Disambiguate));

    [Fact]
    public void Alt_Escape_is_CSI_27_3_u()
        => Assert.Equal("\u001b[27;3u", KittyKeyboard.Evaluate(Ev("Escape", alt: true), Disambiguate));

    [Fact]
    public void Ctrl_Backspace_is_CSI_127_5_u()
        => Assert.Equal("\u001b[127;5u", KittyKeyboard.Evaluate(Ev("Backspace", ctrl: true), Disambiguate));

    [Fact]
    public void Ctrl_Space_is_CSI_32_5_u()
        => Assert.Equal("\u001b[32;5u", KittyKeyboard.Evaluate(Ev(" ", ctrl: true), Disambiguate));

    [Fact]
    public void Alt_Space_is_CSI_32_3_u()
        => Assert.Equal("\u001b[32;3u", KittyKeyboard.Evaluate(Ev(" ", alt: true), Disambiguate));

    // ----- Navigation and arrows keep their legacy shapes ----------------------------------

    [Theory]
    [InlineData("Insert", "\u001b[2~")]
    [InlineData("Delete", "\u001b[3~")]
    [InlineData("PageUp", "\u001b[5~")]
    [InlineData("PageDown", "\u001b[6~")]
    [InlineData("Home", "\u001b[H")]
    [InlineData("End", "\u001b[F")]
    [InlineData("ArrowUp", "\u001b[A")]
    [InlineData("ArrowDown", "\u001b[B")]
    [InlineData("ArrowRight", "\u001b[C")]
    [InlineData("ArrowLeft", "\u001b[D")]
    public void Navigation_keys_unmodified(string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key), Disambiguate));
    }

    [Theory]
    [InlineData("PageUp", true, false, "\u001b[5;2~")]
    [InlineData("Home", false, true, "\u001b[1;5H")]
    [InlineData("ArrowUp", true, false, "\u001b[1;2A")]
    [InlineData("ArrowLeft", false, true, "\u001b[1;5D")]
    public void Navigation_keys_modified(string key, bool shift, bool ctrl, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key, shift: shift, ctrl: ctrl), Disambiguate));
    }

    [Fact]
    public void Ctrl_Shift_ArrowRight_is_CSI_1_6_C()
        => Assert.Equal("\u001b[1;6C", KittyKeyboard.Evaluate(Ev("ArrowRight", ctrl: true, shift: true), Disambiguate));

    // ----- Function keys -------------------------------------------------------------------

    [Theory]
    [InlineData("F1", "\u001bOP")]
    [InlineData("F2", "\u001bOQ")]
    [InlineData("F3", "\u001bOR")]
    [InlineData("F4", "\u001bOS")]
    [InlineData("F5", "\u001b[15~")]
    [InlineData("F6", "\u001b[17~")]
    [InlineData("F7", "\u001b[18~")]
    [InlineData("F8", "\u001b[19~")]
    [InlineData("F9", "\u001b[20~")]
    [InlineData("F10", "\u001b[21~")]
    [InlineData("F11", "\u001b[23~")]
    [InlineData("F12", "\u001b[24~")]
    public void Function_keys_keep_legacy_encodings(string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key), Disambiguate));
    }

    [Fact]
    public void Shift_F1_moves_to_the_CSI_form()
        => Assert.Equal("\u001b[1;2P", KittyKeyboard.Evaluate(Ev("F1", shift: true), Disambiguate));

    [Fact]
    public void Ctrl_F5_is_CSI_15_5_tilde()
        => Assert.Equal("\u001b[15;5~", KittyKeyboard.Evaluate(Ev("F5", ctrl: true), Disambiguate));

    [Theory]
    [InlineData("F13", "\u001b[57376u")]
    [InlineData("F14", "\u001b[57377u")]
    [InlineData("F20", "\u001b[57383u")]
    [InlineData("F24", "\u001b[57387u")]
    public void Extended_function_keys_use_their_assigned_codepoints(string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key), Disambiguate));
    }

    // ----- Numpad keys, recognised by PHYSICAL key -----------------------------------------

    [Theory]
    [InlineData("0", "Numpad0", "\u001b[57399u")]
    [InlineData("1", "Numpad1", "\u001b[57400u")]
    [InlineData("9", "Numpad9", "\u001b[57408u")]
    [InlineData(".", "NumpadDecimal", "\u001b[57409u")]
    [InlineData("/", "NumpadDivide", "\u001b[57410u")]
    [InlineData("*", "NumpadMultiply", "\u001b[57411u")]
    [InlineData("-", "NumpadSubtract", "\u001b[57412u")]
    [InlineData("+", "NumpadAdd", "\u001b[57413u")]
    [InlineData("Enter", "NumpadEnter", "\u001b[57414u")]
    [InlineData("=", "NumpadEqual", "\u001b[57415u")]
    public void Numpad_keys_use_their_assigned_codepoints(string key, string code, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key, code), Disambiguate));
    }

    [Fact]
    public void Ctrl_Numpad5_carries_the_modifier()
        => Assert.Equal("\u001b[57404;5u", KittyKeyboard.Evaluate(Ev("5", "Numpad5", ctrl: true), Disambiguate));

    // ----- Modifier keys as keys, left and right distinct ----------------------------------

    [Theory]
    [InlineData("Shift", "ShiftLeft", true, false, false, false, "\u001b[57441;2u")]
    [InlineData("Shift", "ShiftRight", true, false, false, false, "\u001b[57447;2u")]
    [InlineData("Control", "ControlLeft", false, false, true, false, "\u001b[57442;5u")]
    [InlineData("Control", "ControlRight", false, false, true, false, "\u001b[57448;5u")]
    [InlineData("Alt", "AltLeft", false, true, false, false, "\u001b[57443;3u")]
    [InlineData("Alt", "AltRight", false, true, false, false, "\u001b[57449;3u")]
    [InlineData("Meta", "MetaLeft", false, false, false, true, "\u001b[57444;9u")]
    [InlineData("Meta", "MetaRight", false, false, false, true, "\u001b[57450;9u")]
    public void Modifier_keys_report_under_ReportAllKeys(
        string key, string code, bool shift, bool alt, bool ctrl, bool meta, string expected)
    {
        Assert.Equal(expected,
            KittyKeyboard.Evaluate(Ev(key, code, shift: shift, alt: alt, ctrl: ctrl, meta: meta), AllKeys));
    }

    [Theory]
    [InlineData("CapsLock", "\u001b[57358u")]
    [InlineData("NumLock", "\u001b[57360u")]
    [InlineData("ScrollLock", "\u001b[57359u")]
    public void Lock_keys_report_under_ReportAllKeys(string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key, key), AllKeys));
    }

    // ----- Event types: press, repeat, release ---------------------------------------------

    [Fact]
    public void Press_never_carries_an_event_type_suffix()
    {
        Assert.Equal("a", KittyKeyboard.Evaluate(Ev("a"), Disambiguate | EventTypes));
        Assert.Equal("\u001b[27u", KittyKeyboard.Evaluate(Ev("Escape"), Disambiguate | EventTypes));
        Assert.Equal("\u001b[97;5u", KittyKeyboard.Evaluate(Ev("a", ctrl: true), Disambiguate | EventTypes));
    }

    [Fact]
    public void Repeat_suffixes_colon_two()
    {
        Assert.Equal("a", KittyKeyboard.Evaluate(Ev("a"), Disambiguate | EventTypes, KittyKeyboardEventType.Repeat));
        Assert.Equal("\u001b[27;1:2u",
            KittyKeyboard.Evaluate(Ev("Escape"), Disambiguate | EventTypes, KittyKeyboardEventType.Repeat));
        Assert.Equal("\u001b[97;4:2u",
            KittyKeyboard.Evaluate(Ev("a", shift: true, alt: true), Disambiguate | EventTypes, KittyKeyboardEventType.Repeat));
    }

    [Theory]
    [InlineData("Enter", "\r")]
    [InlineData("Tab", "\t")]
    [InlineData("Backspace", "\u007f")]
    public void Special_keys_keep_legacy_bytes_on_repeat(string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key), Disambiguate | EventTypes, KittyKeyboardEventType.Repeat));
    }

    [Fact]
    public void Release_suffixes_colon_three()
    {
        Assert.Equal("\u001b[97;1:3u",
            KittyKeyboard.Evaluate(Ev("a"), Disambiguate | EventTypes, KittyKeyboardEventType.Release));
        Assert.Equal("\u001b[27;1:3u",
            KittyKeyboard.Evaluate(Ev("Escape"), Disambiguate | EventTypes, KittyKeyboardEventType.Release));
        Assert.Equal("\u001b[97;5:3u",
            KittyKeyboard.Evaluate(Ev("a", ctrl: true), Disambiguate | EventTypes, KittyKeyboardEventType.Release));
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("Tab")]
    [InlineData("Backspace")]
    public void Special_keys_have_no_release_without_ReportAllKeys(string key)
    {
        Assert.Null(KittyKeyboard.Evaluate(Ev(key), Disambiguate | EventTypes, KittyKeyboardEventType.Release));
    }

    [Fact]
    public void Functional_key_release_keeps_the_tilde_form()
        => Assert.Equal("\u001b[3;1:3~",
            KittyKeyboard.Evaluate(Ev("Delete"), Disambiguate | EventTypes, KittyKeyboardEventType.Release));

    [Fact]
    public void Modifier_key_release_reports_its_own_bit_cleared()
    {
        // Releasing the left Shift: shiftKey is already false in the event, so the modifier
        // field is the bare event type.
        Assert.Equal("\u001b[57441;1:3u",
            KittyKeyboard.Evaluate(Ev("Shift", "ShiftLeft"),
                Disambiguate | EventTypes | AllKeys, KittyKeyboardEventType.Release));
    }

    // ----- ReportEventTypes without Disambiguate must not swallow keys ---------------------

    [Fact]
    public void EventTypes_alone_still_encodes_press_repeat_and_release()
    {
        Assert.Equal("\u001b[97;5u", KittyKeyboard.Evaluate(Ev("a", ctrl: true), EventTypes));
        Assert.Equal("\u001b[97;5:2u", KittyKeyboard.Evaluate(Ev("a", ctrl: true), EventTypes, KittyKeyboardEventType.Repeat));
        Assert.Equal("\u001b[97;5:3u", KittyKeyboard.Evaluate(Ev("a", ctrl: true), EventTypes, KittyKeyboardEventType.Release));
    }

    // ----- Modifier and lock presses are suppressed without ReportAllKeys ------------------

    [Fact]
    public void Modifier_press_and_release_send_nothing_without_ReportAllKeys()
    {
        Assert.Null(KittyKeyboard.Evaluate(Ev("Shift", "ShiftLeft", shift: true), EventTypes));
        Assert.Null(KittyKeyboard.Evaluate(Ev("Shift", "ShiftLeft"), EventTypes, KittyKeyboardEventType.Release));
    }

    [Theory]
    [InlineData("CapsLock")]
    [InlineData("NumLock")]
    [InlineData("ScrollLock")]
    public void Lock_key_press_sends_nothing_without_ReportAllKeys(string key)
    {
        Assert.Null(KittyKeyboard.Evaluate(Ev(key, key), Disambiguate));
        Assert.Null(KittyKeyboard.Evaluate(Ev(key, key), EventTypes));
        Assert.Null(KittyKeyboard.Evaluate(Ev(key, key), Disambiguate | EventTypes));
    }

    [Fact]
    public void Lock_key_release_sends_nothing_without_ReportAllKeys()
        => Assert.Null(KittyKeyboard.Evaluate(Ev("CapsLock", "CapsLock"), EventTypes, KittyKeyboardEventType.Release));

    // ----- ReportAllKeysAsEscapeCodes ------------------------------------------------------

    [Theory]
    [InlineData("a", "\u001b[97u")]
    [InlineData("5", "\u001b[53u")]
    [InlineData(".", "\u001b[46u")]
    [InlineData(",", "\u001b[44u")]
    [InlineData(";", "\u001b[59u")]
    [InlineData("/", "\u001b[47u")]
    [InlineData("[", "\u001b[91u")]
    [InlineData("]", "\u001b[93u")]
    [InlineData(" ", "\u001b[32u")]
    public void Every_key_becomes_CSI_u_under_ReportAllKeys(string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key), AllKeys));
    }

    [Fact]
    public void Uppercase_uses_the_lowercase_codepoint()
        => Assert.Equal("\u001b[97;2u", KittyKeyboard.Evaluate(Ev("A", shift: true), AllKeys));

    [Theory]
    [InlineData(KittyKeyboardEventType.Press, "Enter", "\u001b[13u")]
    [InlineData(KittyKeyboardEventType.Press, "Tab", "\u001b[9u")]
    [InlineData(KittyKeyboardEventType.Press, "Backspace", "\u001b[127u")]
    [InlineData(KittyKeyboardEventType.Repeat, "Enter", "\u001b[13;1:2u")]
    [InlineData(KittyKeyboardEventType.Repeat, "Tab", "\u001b[9;1:2u")]
    [InlineData(KittyKeyboardEventType.Repeat, "Backspace", "\u001b[127;1:2u")]
    [InlineData(KittyKeyboardEventType.Release, "Enter", "\u001b[13;1:3u")]
    [InlineData(KittyKeyboardEventType.Release, "Tab", "\u001b[9;1:3u")]
    [InlineData(KittyKeyboardEventType.Release, "Backspace", "\u001b[127;1:3u")]
    public void Special_keys_get_CSI_u_and_release_events_under_ReportAllKeys(
        KittyKeyboardEventType eventType, string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key), AllKeys | EventTypes, eventType));
    }

    // ----- ReportAssociatedText ------------------------------------------------------------

    [Fact]
    public void Text_rides_along_after_the_modifier_field()
    {
        Assert.Equal("\u001b[97;;97u", KittyKeyboard.Evaluate(Ev("a"), AllKeys | Text));
        Assert.Equal("\u001b[97;2;65u", KittyKeyboard.Evaluate(Ev("A", shift: true), AllKeys | Text));
        Assert.Equal("\u001b[53;;53u", KittyKeyboard.Evaluate(Ev("5"), AllKeys | Text));
        Assert.Equal("\u001b[53;2;37u", KittyKeyboard.Evaluate(Ev("%", "Digit5", shift: true), AllKeys | Text));
    }

    [Fact]
    public void Text_is_omitted_under_Ctrl_and_for_functional_keys()
    {
        Assert.Equal("\u001b[97;5u", KittyKeyboard.Evaluate(Ev("a", ctrl: true), AllKeys | Text));
        Assert.Equal("\u001b[27u", KittyKeyboard.Evaluate(Ev("Escape"), AllKeys | Text));
    }

    [Fact]
    public void A_repeat_with_text_still_carries_its_event_type()
    {
        // The event-type sub-parameter and the text field are independent in the spec's form
        // (CSI code:alt ; mods:event ; text u). Without the :2 an application that asked for
        // event types cannot tell a held key from a hammered one — the repeat would be
        // byte-identical to the press.
        var flags = AllKeys | Text | EventTypes;
        Assert.Equal("\u001b[97;;97u", KittyKeyboard.Evaluate(Ev("a"), flags));
        Assert.Equal("\u001b[97;1:2;97u",
            KittyKeyboard.Evaluate(Ev("a"), flags, KittyKeyboardEventType.Repeat));
        Assert.Equal("\u001b[97;1:3u",
            KittyKeyboard.Evaluate(Ev("a"), flags, KittyKeyboardEventType.Release));
    }

    [Fact]
    public void Text_is_omitted_on_release()
        => Assert.Equal("\u001b[97;1:3u",
            KittyKeyboard.Evaluate(Ev("a"), AllKeys | Text | EventTypes, KittyKeyboardEventType.Release));

    // ----- ReportAlternateKeys -------------------------------------------------------------

    [Fact]
    public void Shifted_key_appears_as_a_sub_parameter()
    {
        Assert.Equal("\u001b[97:65;2u", KittyKeyboard.Evaluate(Ev("A", "KeyA", shift: true), AllKeys | Alternates));
        Assert.Equal("\u001b[53:37;2u", KittyKeyboard.Evaluate(Ev("%", "Digit5", shift: true), AllKeys | Alternates));
    }

    [Fact]
    public void Unshifted_and_functional_keys_have_no_alternate()
    {
        Assert.Equal("\u001b[97u", KittyKeyboard.Evaluate(Ev("a", "KeyA"), AllKeys | Alternates));
        Assert.Equal("\u001b[27;2u", KittyKeyboard.Evaluate(Ev("Escape", shift: true), AllKeys | Alternates));
    }

    [Fact]
    public void Alternates_and_text_compose()
    {
        Assert.Equal("\u001b[97:65;2;65u",
            KittyKeyboard.Evaluate(Ev("A", "KeyA", shift: true), AllKeys | Alternates | Text));
        Assert.Equal("\u001b[97:65;2:3u",
            KittyKeyboard.Evaluate(Ev("A", "KeyA", shift: true),
                AllKeys | Alternates | Text | EventTypes, KittyKeyboardEventType.Release));
    }

    // ----- No legal flag value may swallow a chord -----------------------------------------

    [Theory]
    [InlineData(Alternates)]
    [InlineData(Text)]
    [InlineData(Alternates | Text)]
    public void Bare_alternates_or_text_flags_fall_back_to_legacy_bytes(KittyKeyboardFlags flags)
    {
        // These flag values ask for richer CSI u sequences but not for escape-code reporting, so
        // an ordinary chord keeps its LEGACY bytes — kitty falls back to the legacy encoding for
        // anything the flags do not cover. Returning null instead leaves Ctrl+C dead until the
        // application clears the flags: the host does not fall back on null by design.
        Assert.Equal("\u0003", KittyKeyboard.Evaluate(Ev("c", "KeyC", ctrl: true), flags));
        Assert.Equal("\u0004", KittyKeyboard.Evaluate(Ev("d", "KeyD", ctrl: true), flags));
        Assert.Equal("\u001a", KittyKeyboard.Evaluate(Ev("z", "KeyZ", ctrl: true), flags));
        Assert.Equal("\u001bx", KittyKeyboard.Evaluate(Ev("x", "KeyX", alt: true), flags));
        Assert.Equal("\u001b\u0003", KittyKeyboard.Evaluate(Ev("c", "KeyC", ctrl: true, alt: true), flags));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void No_flag_combination_swallows_a_control_chord(int flags)
    {
        // The invariant that matters, pinned over every non-zero flag value: an application may
        // set ANY legal combination, and Ctrl+C must still send SOMETHING — the CSI u form or the
        // legacy 0x03, but never nothing. A terminal where Ctrl+C is dead is a stuck terminal.
        Assert.NotNull(KittyKeyboard.Evaluate(Ev("c", "KeyC", ctrl: true), (KittyKeyboardFlags)flags));
    }

    // ----- Releases without ReportEventTypes -----------------------------------------------

    [Fact]
    public void Release_sends_nothing_without_ReportEventTypes()
        => Assert.Null(KittyKeyboard.Evaluate(Ev("a"), Disambiguate, KittyKeyboardEventType.Release));

    // ----- Edge cases ----------------------------------------------------------------------

    [Theory]
    [InlineData("Dead")]
    [InlineData("Unidentified")]
    public void Keys_with_no_codepoint_send_nothing(string key)
    {
        Assert.Null(KittyKeyboard.Evaluate(Ev(key), Disambiguate));
    }

    [Theory]
    [InlineData("PrintScreen", "\u001b[57361u")]
    [InlineData("Pause", "\u001b[57362u")]
    [InlineData("ContextMenu", "\u001b[57363u")]
    [InlineData("MediaPlayPause", "\u001b[57430u")]
    [InlineData("MediaStop", "\u001b[57432u")]
    [InlineData("MediaTrackNext", "\u001b[57435u")]
    [InlineData("MediaTrackPrevious", "\u001b[57436u")]
    [InlineData("AudioVolumeDown", "\u001b[57438u")]
    [InlineData("AudioVolumeUp", "\u001b[57439u")]
    [InlineData("AudioVolumeMute", "\u001b[57440u")]
    public void Rare_functional_keys_use_their_assigned_codepoints(string key, string expected)
    {
        Assert.Equal(expected, KittyKeyboard.Evaluate(Ev(key), Disambiguate));
    }

    // ----- macOS Option unwinding ----------------------------------------------------------

    [Theory]
    [InlineData("ƒ", "KeyF", "\u001b[102;3u")]
    [InlineData("∫", "KeyB", "\u001b[98;3u")]
    [InlineData("∂", "KeyD", "\u001b[100;3u")]
    [InlineData("Dead", "KeyN", "\u001b[110;3u")]  // Option dead keys unwind too
    [InlineData("Dead", "KeyE", "\u001b[101;3u")]
    [InlineData("Dead", "KeyU", "\u001b[117;3u")]
    [InlineData("∞", "Digit5", "\u001b[53;3u")]
    public void Option_composed_keys_unwind_to_the_letter_under_them(string key, string code, string expected)
    {
        Assert.Equal(expected,
            KittyKeyboard.Evaluate(Ev(key, code, alt: true), Disambiguate, KittyKeyboardEventType.Press, macOptionAsAlt: true));
    }

    [Fact]
    public void Option_unwinding_composes_with_other_modifiers()
    {
        Assert.Equal("\u001b[102;4u",
            KittyKeyboard.Evaluate(Ev("Ï", "KeyF", alt: true, shift: true), Disambiguate, KittyKeyboardEventType.Press, true));
        Assert.Equal("\u001b[102;7u",
            KittyKeyboard.Evaluate(Ev("ƒ", "KeyF", alt: true, ctrl: true), Disambiguate, KittyKeyboardEventType.Press, true));
        Assert.Equal("\u001b[102;3:3u",
            KittyKeyboard.Evaluate(Ev("ƒ", "KeyF", alt: true), Disambiguate | EventTypes, KittyKeyboardEventType.Release, true));
    }

    [Fact]
    public void No_unwinding_when_the_host_did_not_ask()
    {
        // Linux Alt is a chord: the key name is already the base character.
        Assert.Equal("\u001b[97;3u",
            KittyKeyboard.Evaluate(Ev("a", "KeyA", alt: true), Disambiguate, KittyKeyboardEventType.Press, false));
        // And a layout where key and code disagree (AZERTY) must follow the KEY, not the code.
        Assert.Equal("\u001b[97;3u",
            KittyKeyboard.Evaluate(Ev("a", "KeyQ", alt: true), Disambiguate, KittyKeyboardEventType.Press, false));
        // A composed character without unwinding reports its own codepoint.
        Assert.Equal("\u001b[402;3u",
            KittyKeyboard.Evaluate(Ev("ƒ", "KeyF", alt: true), Disambiguate, KittyKeyboardEventType.Press, false));
    }

    [Fact]
    public void No_unwinding_when_Option_is_not_held()
        => Assert.Equal("ƒ",
            KittyKeyboard.Evaluate(Ev("ƒ", "KeyF"), Disambiguate, KittyKeyboardEventType.Press, true));

    [Fact]
    public void Unwinding_falls_through_for_non_letter_non_digit_physical_keys()
        => Assert.Equal("\u001b[8230;3u",
            KittyKeyboard.Evaluate(Ev("…", "Semicolon", alt: true), Disambiguate, KittyKeyboardEventType.Press, true));
}
