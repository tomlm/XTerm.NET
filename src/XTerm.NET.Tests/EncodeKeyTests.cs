using XTerm;
using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// <see cref="Terminal.EncodeKey"/> against what the Avalonia TerminalView sends for the same
/// keystroke today, mode by mode. The expected bytes are literals, not calls to the generators the
/// method delegates to, so a wrong choice of generator shows as a wrong answer.
/// </summary>
public class EncodeKeyTests
{
    private const string E = "\u001b";

    public enum Mode { Legacy, CursorKeys, Keypad, KittyDisambiguate, KittyAllEvents, Win32, Win32AndKitty }

    private static Terminal Make(Mode mode)
    {
        var t = new Terminal(new TerminalOptions());
        switch (mode)
        {
            case Mode.CursorKeys: t.Write($"{E}[?1h"); break;
            case Mode.Keypad: t.Write($"{E}="); break;
            case Mode.KittyDisambiguate: t.Write($"{E}[>1u"); break;
            case Mode.KittyAllEvents: t.Write($"{E}[>11u"); break;
            case Mode.Win32: t.Write($"{E}[?9001h"); break;
            case Mode.Win32AndKitty: t.Write($"{E}[?9001h{E}[>1u"); break;
        }
        return t;
    }

    /// <summary>Modifiers spelled as a short string: s(hift), c(trl), a(lt), m(eta/super).</summary>
    private static KeyEvent Ev(string key, string code, string mods = "") => new()
    {
        Key = key,
        Code = code,
        ShiftKey = mods.Contains('s'),
        CtrlKey = mods.Contains('c'),
        AltKey = mods.Contains('a'),
        MetaKey = mods.Contains('m'),
    };

    public static TheoryData<Mode, string, string, string, string?> Presses => new()
    {
        // Legacy: the keys TerminalView sends through GenerateKeyInput, and text through
        // GenerateCharInput or as typed.
        { Mode.Legacy, "ArrowUp", "ArrowUp", "", $"{E}[A" },
        { Mode.Legacy, "ArrowLeft", "ArrowLeft", "c", $"{E}[1;5D" },
        { Mode.Legacy, "ArrowRight", "ArrowRight", "s", $"{E}[1;2C" },
        { Mode.Legacy, "Home", "Home", "", $"{E}[H" },
        { Mode.Legacy, "PageDown", "PageDown", "", $"{E}[6~" },
        { Mode.Legacy, "Delete", "Delete", "", $"{E}[3~" },
        { Mode.Legacy, "F1", "F1", "", $"{E}OP" },
        { Mode.Legacy, "F5", "F5", "", $"{E}[15~" },
        { Mode.Legacy, "F12", "F12", "s", $"{E}[24;2~" },
        { Mode.Legacy, "Enter", "Enter", "", "\r" },
        { Mode.Legacy, "Tab", "Tab", "", "\t" },
        { Mode.Legacy, "Tab", "Tab", "s", $"{E}[Z" },
        { Mode.Legacy, "Backspace", "Backspace", "", "\u007f" },
        { Mode.Legacy, "Backspace", "Backspace", "c", "\b" },
        { Mode.Legacy, "Escape", "Escape", "", E },
        { Mode.Legacy, "a", "KeyA", "", "a" },
        { Mode.Legacy, "A", "KeyA", "s", "A" },
        { Mode.Legacy, " ", "Space", "", " " },
        { Mode.Legacy, "é", "KeyE", "", "é" },
        { Mode.Legacy, "c", "KeyC", "c", "\u0003" },
        { Mode.Legacy, " ", "Space", "c", "\u0000" },
        { Mode.Legacy, "x", "KeyX", "a", $"{E}x" },
        { Mode.Legacy, "a", "KeyA", "ca", $"{E}\u0001" },
        { Mode.Legacy, "a", "KeyA", "m", "a" },                 // no Super in legacy
        { Mode.Legacy, "Shift", "ShiftLeft", "s", null },       // a bare modifier sends nothing
        { Mode.Legacy, "7", "Numpad7", "", "7" },

        // DECCKM: cursor keys switch to SS3, modified ones do not.
        { Mode.CursorKeys, "ArrowUp", "ArrowUp", "", $"{E}OA" },
        { Mode.CursorKeys, "Home", "Home", "", $"{E}OH" },
        { Mode.CursorKeys, "ArrowUp", "ArrowUp", "c", $"{E}[1;5A" },

        // DECKPAM: the keypad, told apart from the main digits by its code.
        { Mode.Keypad, "7", "Numpad7", "", $"{E}Ow" },
        { Mode.Keypad, "+", "NumpadAdd", "", $"{E}Ok" },
        { Mode.Keypad, "Enter", "NumpadEnter", "", $"{E}OM" },
        { Mode.Keypad, "7", "Digit7", "", "7" },

        // Kitty, disambiguate only: the ambiguous keys become CSI u, plain text stays text.
        { Mode.KittyDisambiguate, "Escape", "Escape", "", $"{E}[27u" },
        { Mode.KittyDisambiguate, "c", "KeyC", "c", $"{E}[99;5u" },
        { Mode.KittyDisambiguate, "a", "KeyA", "m", $"{E}[97;9u" },  // Super is Kitty's bit 8
        { Mode.KittyDisambiguate, "a", "KeyA", "", "a" },
        { Mode.KittyDisambiguate, "ArrowUp", "ArrowUp", "", $"{E}[A" },
        { Mode.KittyDisambiguate, "Enter", "Enter", "", "\r" },
        { Mode.KittyDisambiguate, "Shift", "ShiftLeft", "s", null },

        // Win32 input mode: one key-down record, VK ; scan ; char ; down ; state ; repeat.
        { Mode.Win32, "a", "KeyA", "", $"{E}[65;0;97;1;0;1_" },
        { Mode.Win32, "A", "KeyA", "s", $"{E}[65;0;65;1;16;1_" },
        { Mode.Win32, "c", "KeyC", "c", $"{E}[67;0;3;1;8;1_" },
        { Mode.Win32, "ArrowUp", "ArrowUp", "", $"{E}[38;0;0;1;256;1_" },
        { Mode.Win32, "Enter", "Enter", "", $"{E}[13;0;13;1;0;1_" },
        { Mode.Win32, "Escape", "Escape", "", $"{E}[27;0;27;1;0;1_" },
        { Mode.Win32, "Backspace", "Backspace", "", $"{E}[8;0;8;1;0;1_" },
        { Mode.Win32, " ", "Space", "", $"{E}[32;0;32;1;0;1_" },
        { Mode.Win32, "F5", "F5", "", $"{E}[116;0;0;1;0;1_" },
        { Mode.Win32, "Shift", "ShiftLeft", "s", $"{E}[16;0;0;1;16;1_" },
        { Mode.Win32, "Alt", "AltRight", "a", $"{E}[18;0;0;1;257;1_" },
        { Mode.Win32, "7", "Numpad7", "", $"{E}[103;0;55;1;0;1_" },
        // NumLock off: the keypad key is navigation, and gets the navigation key's VK.
        { Mode.Win32, "Home", "Numpad7", "", $"{E}[36;0;0;1;0;1_" },
        { Mode.Win32, "ArrowUp", "Numpad8", "", $"{E}[38;0;0;1;0;1_" },
        { Mode.Win32, "Delete", "NumpadDecimal", "", $"{E}[46;0;0;1;0;1_" },
        // Shifted OEM keys, named by their physical code.
        { Mode.Win32, "?", "Slash", "s", $"{E}[191;0;63;1;16;1_" },
        { Mode.Win32, ":", "Semicolon", "s", $"{E}[186;0;58;1;16;1_" },
        { Mode.Win32, "\"", "Quote", "s", $"{E}[222;0;34;1;16;1_" },
        { Mode.Win32, "{", "BracketLeft", "s", $"{E}[219;0;123;1;16;1_" },
        { Mode.Win32, "|", "Backslash", "s", $"{E}[220;0;124;1;16;1_" },
        { Mode.Win32, "~", "Backquote", "s", $"{E}[192;0;126;1;16;1_" },
        { Mode.Win32, "/", "Slash", "", $"{E}[191;0;47;1;0;1_" },
        // A layout whose character names no key: the physical code is all there is to go on.
        { Mode.Win32, "é", "Slash", "", $"{E}[191;0;233;1;0;1_" },
        // And with no physical code at all, from the character on a US layout.
        { Mode.Win32, "?", "", "s", $"{E}[191;0;63;1;16;1_" },
        { Mode.Win32AndKitty, "?", "Slash", "s", $"{E}[191;0;63;1;16;1_" },

        // Win32 is a transport and keeps its precedence over a negotiated Kitty protocol.
        { Mode.Win32AndKitty, "Escape", "Escape", "", $"{E}[27;0;27;1;0;1_" },
    };

    [Theory]
    [MemberData(nameof(Presses))]
    public void Press_MatchesTheControl(Mode mode, string key, string code, string mods, string? expected)
    {
        var t = Make(mode);

        Assert.Equal(expected, t.EncodeKey(Ev(key, code, mods)));
    }

    [Fact]
    public void Legacy_Release_SendsNothing()
    {
        var t = Make(Mode.Legacy);

        Assert.Null(t.EncodeKey(Ev("a", "KeyA"), KittyKeyboardEventType.Release));
        Assert.Null(t.EncodeKey(Ev("ArrowUp", "ArrowUp"), KittyKeyboardEventType.Release));
    }

    [Fact]
    public void Legacy_Repeat_IsAnotherPress()
    {
        var t = Make(Mode.Legacy);

        Assert.Equal($"{E}[A", t.EncodeKey(Ev("ArrowUp", "ArrowUp"), KittyKeyboardEventType.Repeat));
    }

    [Fact]
    public void Kitty_ReleaseIsReportedOnlyWhenTheFlagsAskForIt()
    {
        Assert.Null(Make(Mode.KittyDisambiguate).EncodeKey(Ev("Escape", "Escape"), KittyKeyboardEventType.Release));

        Assert.Equal($"{E}[27;1:3u",
            Make(Mode.KittyAllEvents).EncodeKey(Ev("Escape", "Escape"), KittyKeyboardEventType.Release));
    }

    [Fact]
    public void Win32_Release_IsAKeyUpRecord()
    {
        var t = Make(Mode.Win32);

        Assert.Equal($"{E}[65;0;97;0;0;1_", t.EncodeKey(Ev("a", "KeyA"), KittyKeyboardEventType.Release));
    }

    [Fact]
    public void Win32_ShiftedOemKey_ReleasesToo()
    {
        var t = Make(Mode.Win32);

        Assert.Equal($"{E}[191;0;63;0;16;1_",
            t.EncodeKey(Ev("?", "Slash", "s"), KittyKeyboardEventType.Release));
    }

    [Fact]
    public void Win32_TakesTheVirtualKeyFromKeyCodeWhenGiven()
    {
        var t = Make(Mode.Win32);
        var ev = Ev("q", "", "");
        ev.KeyCode = 0x51;

        Assert.Equal($"{E}[81;0;113;1;0;1_", t.EncodeKey(ev));
    }

    [Fact]
    public void LockedKeyboard_SendsNothing()
    {
        var t = new Terminal(new TerminalOptions { KeyboardActionModeAllowed = true });
        t.Write($"{E}[2h");

        Assert.Null(t.EncodeKey(Ev("a", "KeyA")));
        Assert.Null(t.EncodeKey(Ev("ArrowUp", "ArrowUp")));
    }
}
