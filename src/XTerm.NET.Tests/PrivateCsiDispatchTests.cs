using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// A CSI sequence with a private marker is a DIFFERENT command from the one that shares its final
/// character. The identifier used to have its leading '?' or '>' stripped before the command was
/// looked up, so each sequence below ran the wrong handler -- silently, on output any modern
/// program produces during startup.
///
/// <para>Every test here drives the whole stack through <see cref="Terminal.Write(string)"/>,
/// because the parser is what builds the identifier and the bug lived in what it built.</para>
/// </summary>
public class PrivateCsiDispatchTests
{
    private const char Esc = (char)0x1b;

    private static Terminal Fresh(int cols = 80, int rows = 24) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static (Terminal Terminal, List<string> Replies) Listening(int cols = 80, int rows = 24)
    {
        var terminal = Fresh(cols, rows);
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return (terminal, replies);
    }

    // ---------------------------------------------------------------------------------------
    // The misroutes.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// XTMODKEYS. Kitty, neovim and anything else negotiating modifyOtherKeys sends this on
    /// startup; it used to arrive as SGR 4 ; 2 and underline and dim everything printed after it.
    /// </summary>
    [Fact]
    public void XtModkeys_does_not_apply_its_arguments_as_SGR()
    {
        var terminal = Fresh();

        terminal.Write($"{Esc}[>4;2mA");

        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.False(line[0].Attributes.IsUnderline());
    }

    /// <summary>
    /// The Kitty keyboard protocol. The push ("CSI &gt; 1 u") and the query ("CSI ? u") used to
    /// land on RESTORE CURSOR and teleport the cursor to wherever it was last saved. They now
    /// reach the Kitty handler, which does not touch the cursor -- and neither does the pop
    /// ("CSI &lt; u"), whose marker was never stripped.
    /// </summary>
    [Theory]
    [InlineData(">1u")]
    [InlineData("?u")]
    [InlineData("<u")]
    public void Kitty_keyboard_sequences_do_not_restore_the_cursor(string sequence)
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[6;11H");  // row 6, column 11 -> (10, 5)
        terminal.Write($"{Esc}[s");      // save it
        terminal.Write($"{Esc}[16;31H"); // row 16, column 31 -> (30, 15)

        terminal.Write($"{Esc}[{sequence}");

        Assert.Equal(30, terminal.Buffer.X);
        Assert.Equal(15, terminal.Buffer.Y);
    }

    /// <summary>
    /// XTSAVE saves DEC private MODES, not the cursor. It used to overwrite the saved cursor, so a
    /// later "CSI u" restored the position the application happened to be at when it saved its
    /// modes rather than the one it saved on purpose.
    /// </summary>
    [Fact]
    public void XtSave_does_not_overwrite_the_saved_cursor()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[6;11H");  // the position the application means to come back to
        terminal.Write($"{Esc}[s");
        terminal.Write($"{Esc}[16;31H");
        terminal.Write($"{Esc}[?1049s"); // save the alternate-screen mode
        terminal.Write($"{Esc}[1;1H");

        terminal.Write($"{Esc}[u");

        Assert.Equal(10, terminal.Buffer.X);
        Assert.Equal(5, terminal.Buffer.Y);
    }

    /// <summary>
    /// XTRESTORE restores DEC private modes. It used to be read as SET SCROLLING REGION, which
    /// takes the mode number as a row, throws the region away and homes the cursor.
    /// </summary>
    [Fact]
    public void XtRestore_does_not_reset_the_scroll_region_or_move_the_cursor()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[3;10r"); // rows 3..10 -> 0-based 2..9
        terminal.Write($"{Esc}[6;1H");  // row 6 -> Y 5

        terminal.Write($"{Esc}[?1049r");

        Assert.Equal(2, terminal.Buffer.ScrollTop);
        Assert.Equal(9, terminal.Buffer.ScrollBottom);
        Assert.Equal(5, terminal.Buffer.Y);
    }

    /// <summary>
    /// XTVERSION. It shares its final character with DECSCUSR, so asking the terminal what it is
    /// used to change the shape of the cursor.
    /// </summary>
    [Fact]
    public void XtVersion_does_not_change_the_cursor_style()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[2 q"); // steady block

        terminal.Write($"{Esc}[>0q");

        Assert.Equal(CursorStyle.Block, terminal.Options.CursorStyle);
        Assert.False(terminal.Options.CursorBlink);
    }

    /// <summary>
    /// DECLL, "CSI Ps q", loads the keyboard LEDs. It is the same aliasing on the intermediate-byte
    /// axis rather than the marker axis: DECSCUSR is "CSI Ps SP q" and the bare final character used
    /// to be mapped to it as well, so an application clearing its LEDs on startup got a blinking
    /// cursor it never asked for. Nothing here implements DECLL; it is ignored.
    /// </summary>
    [Fact]
    public void Decll_does_not_change_the_cursor_style()
    {
        var (terminal, replies) = Listening();
        terminal.Write($"{Esc}[2 q"); // steady block

        terminal.Write($"{Esc}[0q"); // DECLL 0 -- clear all LEDs

        Assert.Equal(CursorStyle.Block, terminal.Options.CursorStyle);
        Assert.False(terminal.Options.CursorBlink);
        Assert.Empty(replies);

        // The form that carries the SP intermediate is still DECSCUSR, so the guard above is
        // testing the intermediate and not a cursor style that stopped working.
        terminal.Write($"{Esc}[5 q");
        Assert.Equal(CursorStyle.Bar, terminal.Options.CursorStyle);
        Assert.True(terminal.Options.CursorBlink);
    }

    /// <summary>
    /// XTSMTITLE sets how window titles are reported. Read as XTWINOPS, "CSI &gt; 2 t" minimised
    /// the window instead.
    /// </summary>
    [Fact]
    public void XtSmTitle_does_not_perform_a_window_operation()
    {
        var terminal = Fresh();
        terminal.Options.WindowOptions.MinimizeWin = true;
        var minimized = 0;
        terminal.WindowMinimized += (_, _) => minimized++;

        terminal.Write($"{Esc}[>2t");
        Assert.Equal(0, minimized);

        // The non-private form is still XTWINOPS, so the guard above is testing the marker and not
        // a window operation that stopped working.
        terminal.Write($"{Esc}[2t");
        Assert.Equal(1, minimized);
    }

    /// <summary>An unrecognised private sequence is ignored, not partially applied.</summary>
    [Fact]
    public void An_unknown_private_sequence_leaves_the_screen_alone()
    {
        var (terminal, replies) = Listening();
        terminal.Write("hello");
        var before = (terminal.Buffer.X, terminal.Buffer.Y, terminal.GetLine(0));

        terminal.Write($"{Esc}[?42q"); // CSI ? 42 q -- nothing maps it

        Assert.Equal(before, (terminal.Buffer.X, terminal.Buffer.Y, terminal.GetLine(0)));
        Assert.Empty(replies);
    }

    // ---------------------------------------------------------------------------------------
    // The private sequences that ARE implemented still reach their handlers.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void DecSet_and_DecRst_still_toggle_a_private_mode()
    {
        var terminal = Fresh();

        terminal.Write($"{Esc}[?25l");
        Assert.False(terminal.CursorVisible);

        terminal.Write($"{Esc}[?25h");
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public void Decsed_still_erases_the_display()
    {
        var terminal = Fresh();
        terminal.Write("hello");

        terminal.Write($"{Esc}[?2J");

        Assert.Equal("", terminal.GetLine(0).TrimEnd());
    }

    [Fact]
    public void Decsel_still_erases_the_line()
    {
        var terminal = Fresh();
        terminal.Write("hello");
        terminal.Write($"{Esc}[1;1H");

        terminal.Write($"{Esc}[?0K");

        Assert.Equal("", terminal.GetLine(0).TrimEnd());
    }

    [Fact]
    public void Secondary_device_attributes_still_answers()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[>c");

        // What the reply says is the DA handler's business and is asserted in InputHandlerTests.
        // All this test cares about is that ">c" still reaches it once the marker is no longer
        // stripped, so it checks only that the answer is shaped like a secondary DA.
        var reply = Assert.Single(replies);
        Assert.StartsWith($"{Esc}[>", reply);
        Assert.EndsWith("c", reply);
    }

    [Fact]
    public void Dec_device_status_report_still_answers()
    {
        var (terminal, replies) = Listening();
        terminal.Write($"{Esc}[6;11H");

        terminal.Write($"{Esc}[?6n");

        Assert.Equal($"{Esc}[?6;11R", Assert.Single(replies));
    }

    [Fact]
    public void Private_decrqm_still_answers()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?2026$p");

        Assert.Equal($"{Esc}[?2026;2$y", Assert.Single(replies));
    }

    [Fact]
    public void Xtsmgraphics_still_answers_and_scroll_up_still_scrolls()
    {
        var (terminal, replies) = Listening(cols: 40, rows: 6);
        terminal.Write("top line\r\nsecond line");

        terminal.Write($"{Esc}[?1;1;0S");
        Assert.Single(replies);
        Assert.Equal("top line", terminal.GetLine(terminal.Buffer.YBase).TrimEnd());

        // The same final character without the marker is still SCROLL UP.
        terminal.Write($"{Esc}[1S");
        Assert.Equal("second line", terminal.GetLine(terminal.Buffer.YBase).TrimEnd());
    }
}
