using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// What the terminal sends UP to the application when the user types or clicks. These encodings
/// are read by programs that cannot ask for clarification, so being close is being wrong.
/// </summary>
public class InputEncodingTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static Terminal NewTerminal() =>
        new(new TerminalOptions { Cols = 300, Rows = 24 });

    [Fact]
    public void The_x10_report_never_emits_a_byte_utf8_would_split()
    {
        // The report is a byte sequence but the string is UTF-8 encoded on its way to the pty, so
        // anything above 127 becomes two bytes and the application reads a column it was never
        // sent -- "the mouse stops working past column 95". Assert the bytes, not the chars: a
        // char-level bound passes on values UTF-8 still splits.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?1000h");     // VT200 tracking, as a program enables it

        var seq = terminal.GenerateMouseEvent(MouseButton.Left, 80, 10, MouseEventType.Down);

        Assert.NotEmpty(seq);
        Assert.Equal(seq.Length, System.Text.Encoding.UTF8.GetByteCount(seq));
        Assert.All(seq, c => Assert.True(c <= 127, $"emitted U+{(int)c:X4}, which UTF-8 splits"));
    }

    [Fact]
    public void A_click_past_the_addressable_range_is_dropped_rather_than_reported_as_column_95()
    {
        // Clamping produced a confident lie: every click past the limit arrived as the last
        // addressable column, so a TUI acted on whichever widget lives there. vte and konsole drop
        // the report instead, and so does xterm.js at its own ceiling.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?1000h");

        Assert.Equal(string.Empty,
            terminal.GenerateMouseEvent(MouseButton.Left, 250, 10, MouseEventType.Down));
        Assert.Equal(string.Empty,
            terminal.GenerateMouseEvent(MouseButton.Left, 10, 250, MouseEventType.Down));
    }

    [Fact]
    public void Sgr_coordinates_still_carry_the_whole_screen()
    {
        // The X10 ceiling is a property of that encoding's transport, not of the terminal. An
        // application that wants the full width asks for SGR, and must still get it.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?1000h{Esc}[?1006h");

        var seq = terminal.GenerateMouseEvent(MouseButton.Left, 250, 10, MouseEventType.Down);

        Assert.Contains("251", seq);
    }

    [Fact]
    public void X10_mode_reports_no_modifier_bits()
    {
        // X10 (DECSET 9) is the original protocol: button and position, nothing else. Adding
        // modifier bits shifted the button number an application reads.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?9h");        // X10 tracking

        var plain = terminal.GenerateMouseEvent(MouseButton.Left, 5, 5, MouseEventType.Down);
        var shifted = terminal.GenerateMouseEvent(MouseButton.Left, 5, 5, MouseEventType.Down,
                                                  KeyModifiers.Shift);

        Assert.Equal(plain, shifted);
    }

    [Fact]
    public void An_sgr_release_never_emits_a_negative_parameter()
    {
        // MouseButton.None is -1, so a release reported without a button produced ESC[<-1;7;7m --
        // which no parser accepts, so the release the application was waiting for vanished.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?1000h{Esc}[?1006h");   // VT200 tracking, SGR encoding

        var seq = terminal.GenerateMouseEvent(MouseButton.None, 6, 6, MouseEventType.Up);

        Assert.DoesNotContain("-", seq);
    }

    [Fact]
    public void Escape_sends_escape_whatever_is_held()
    {
        // CSI 27 ; mod ~ belongs to protocols an application opts into. Sending it unasked meant
        // a program reading a bare ESC -- vim leaving insert mode, a menu closing -- saw nothing
        // it recognised, so Escape appeared dead with a modifier held.
        var terminal = NewTerminal();

        Assert.Equal(Esc, terminal.GenerateKeyInput(Key.Escape, KeyModifiers.Control));
        Assert.Equal(Esc, terminal.GenerateKeyInput(Key.Escape, KeyModifiers.Shift));
        Assert.Equal(Esc, terminal.GenerateKeyInput(Key.Escape));
    }

    [Fact]
    public void Home_and_end_follow_application_cursor_keys_like_the_arrows()
    {
        var terminal = NewTerminal();
        terminal.ApplicationCursorKeys = true;

        Assert.Equal($"{Esc}OH", terminal.GenerateKeyInput(Key.Home));
        Assert.Equal($"{Esc}OF", terminal.GenerateKeyInput(Key.End));
        Assert.Equal($"{Esc}OA", terminal.GenerateKeyInput(Key.UpArrow));
    }

    [Fact]
    public void The_keypad_operators_follow_application_keypad_mode()
    {
        var terminal = NewTerminal();
        terminal.ApplicationKeypad = true;

        Assert.Equal($"{Esc}Oo", terminal.GenerateKeyInput(Key.KeypadDivide));
        Assert.Equal($"{Esc}OM", terminal.GenerateKeyInput(Key.KeypadEnter));

        terminal.ApplicationKeypad = false;
        Assert.Equal("/", terminal.GenerateKeyInput(Key.KeypadDivide));
        Assert.Equal("\r", terminal.GenerateKeyInput(Key.KeypadEnter));
    }

    [Fact]
    public void Deckpam_and_deckpnm_set_the_keypad_mode()
    {
        // terminfo's smkx is ESC [ ? 1 h ESC =, so the second half used to be dropped and the
        // keypad generators honoured a mode nothing could set.
        var terminal = NewTerminal();

        terminal.Write($"{Esc}=");
        Assert.True(terminal.ApplicationKeypad);

        terminal.Write($"{Esc}>");
        Assert.False(terminal.ApplicationKeypad);
    }

    [Fact]
    public void An_intermediate_stops_a_bare_esc_final_from_matching()
    {
        // The two switches ran one after the other, so a sequence with an intermediate also took
        // the bare arm for its final character: ESC ( = designated G0 and enabled the application
        // keypad on the way past.
        var terminal = NewTerminal();

        terminal.Write($"{Esc}(=");

        Assert.False(terminal.ApplicationKeypad);
    }

    [Fact]
    public void Decaln_does_not_also_restore_the_saved_cursor_state()
    {
        // Same defect, and this one vttest actually sends: ESC # 8 took the "8" arm as DECRC on
        // its way to DECALN. The cursor position hides it -- DECALN homes the cursor afterwards
        // either way -- but DECRC also restores the saved GRAPHIC RENDITION, which DECALN does
        // not touch, so the alignment test came back with whatever attributes were saved.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1m");        // bold on
        terminal.Write($"{Esc}7");          // DECSC saves the cursor AND the bold attribute
        terminal.Write($"{Esc}[0m");        // back to normal

        terminal.Write($"{Esc}#8");         // DECALN only: fill with E, home the cursor
        terminal.Write("X");

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        Assert.False(line[0].Attributes.IsBold());
    }
}
