using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Cursor motion, margins and tab stops against what xterm does. Each test names the program
/// behavior that goes wrong when the terminal disagrees.
/// </summary>
public class CursorAndMarginTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static Terminal NewTerminal(int cols = 20, int rows = 10) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static string Row(Terminal t, int row, int count)
    {
        var line = t.Buffer.Lines[row]!;
        return string.Concat(Enumerable.Range(0, count)
            .Select(i => string.IsNullOrEmpty(line[i].Content) ? " " : line[i].Content));
    }

    [Fact]
    public void Cursor_up_stops_at_the_top_margin_when_it_starts_inside()
    {
        // A full-screen editor keeps its status line outside the region; a cursor walking out of
        // the region scrolls the wrong rows.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;8r");     // region rows 3..8 (0-based 2..7)
        terminal.Write($"{Esc}[5;1H");     // inside it
        terminal.Write($"{Esc}[10A");      // further up than the region is tall

        Assert.Equal(2, terminal.Buffer.Y);
    }

    [Fact]
    public void Cursor_down_stops_at_the_bottom_margin_when_it_starts_inside()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;8r");
        terminal.Write($"{Esc}[5;1H");
        terminal.Write($"{Esc}[10B");

        Assert.Equal(7, terminal.Buffer.Y);
    }

    [Fact]
    public void Cursor_up_from_outside_the_region_uses_the_screen_edge()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;8r");
        terminal.Write($"{Esc}[10;1H");    // below the region
        terminal.Write($"{Esc}[20A");

        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Fact]
    public void Backspace_from_a_full_line_lands_on_the_last_column()
    {
        // Printing to the end leaves the cursor one PAST the last column. Counting back from that
        // phantom position put a shell's redraw one column right of where it meant.
        var terminal = NewTerminal(cols: 10);
        terminal.Write("0123456789");      // fills the line, pending wrap
        terminal.Write($"{Esc}[1D");       // CUB 1

        Assert.Equal(8, terminal.Buffer.X);
    }

    [Fact]
    public void With_wrapping_off_the_last_column_is_overwritten_not_dropped()
    {
        var terminal = NewTerminal(cols: 10);
        terminal.Write($"{Esc}[?7l");      // DECAWM off
        terminal.Write("0123456789ABC");

        Assert.Equal("012345678C", Row(terminal, 0, 10));
    }

    [Fact]
    public void An_explicit_zero_scroll_region_means_the_whole_screen()
    {
        // CSI 0;0r is how a program resets its region. It used to clamp to a single row.
        var terminal = NewTerminal(rows: 10);
        terminal.Write($"{Esc}[0;0r");

        Assert.Equal(0, terminal.Buffer.ScrollTop);
        Assert.Equal(9, terminal.Buffer.ScrollBottom);
    }

    [Fact]
    public void Insert_and_delete_line_move_the_cursor_to_the_left_margin()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;5H");
        terminal.Write($"{Esc}[L");
        Assert.Equal(0, terminal.Buffer.X);

        terminal.Write($"{Esc}[3;5H");
        terminal.Write($"{Esc}[M");
        Assert.Equal(0, terminal.Buffer.X);
    }

    [Fact]
    public void Save_and_restore_cursor_carry_the_charset()
    {
        // ESC ( 0 selects line drawing. A TUI that saves the cursor mid-border and restores it
        // expects to keep drawing lines, not letters.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}(0");        // G0 = line drawing
        terminal.Write($"{Esc}7");         // DECSC
        terminal.Write($"{Esc}(B");        // G0 = ASCII
        terminal.Write($"{Esc}8");         // DECRC
        terminal.Write("q");               // 'q' is a horizontal line in the DEC set

        Assert.Equal("\u2500", Row(terminal, 0, 1));
    }

    [Fact]
    public void A_program_can_set_and_clear_its_own_tab_stops()
    {
        // `tabs 4` writes stops with HTS. TBC used to acknowledge the request and do nothing.
        var terminal = NewTerminal(cols: 30);
        terminal.Write($"{Esc}[3g");       // clear every stop
        terminal.Write($"{Esc}[1;5H{Esc}H");   // HTS at column 4
        terminal.Write($"{Esc}[1;1H\t");

        Assert.Equal(4, terminal.Buffer.X);
    }

    [Fact]
    public void Clearing_all_stops_removes_the_defaults_too()
    {
        // The earlier test could not catch TBC doing nothing: its custom stop at column 4 merely
        // preceded the untouched default at 8, so a tab landed on 4 either way. This one asks
        // whether a DEFAULT stop was actually removed, which only passes if TBC works.
        var terminal = NewTerminal(cols: 40);
        terminal.Write($"{Esc}[3g");        // clear every stop
        terminal.Write($"{Esc}[1;1H" + "\t");

        // With no stops at all a tab goes to the last column, not to 8.
        Assert.Equal(39, terminal.Buffer.X);
    }

    [Fact]
    public void Backward_tab_uses_the_stops_a_program_set()
    {
        // CBT derived its answer arithmetically, so it ignored HTS stops and disagreed with
        // forward tab on the same screen: from column 6 with a stop at 4 it went to 0.
        var terminal = NewTerminal(cols: 40);
        terminal.Write($"{Esc}[3g");                    // no stops
        terminal.Write($"{Esc}[1;5H{Esc}H");            // HTS at column 4
        terminal.Write($"{Esc}[1;7H");                  // cursor at column 6
        terminal.Write($"{Esc}[Z");                     // CBT

        Assert.Equal(4, terminal.Buffer.X);
    }

    [Fact]
    public void Restoring_a_cursor_that_was_pending_a_wrap_still_wraps()
    {
        // The saved position is X == Cols, one past the last column. Restoring it through the
        // clamp put the cursor ON the last cell, so the next character overwrote that cell
        // instead of wrapping to the next row.
        var terminal = NewTerminal(cols: 10, rows: 4);
        terminal.Write("0123456789");       // fills the line; cursor pending wrap
        terminal.Write($"{Esc}7");          // DECSC
        terminal.Write($"{Esc}[3;1H");      // go elsewhere
        terminal.Write($"{Esc}8");          // DECRC
        terminal.Write("X");

        Assert.Equal("9", Row(terminal, 0, 10)[9..]);   // the last cell survived
        Assert.Equal("X", Row(terminal, 1, 1));         // and X wrapped
    }

    [Fact]
    public void Both_tab_motions_agree_on_the_same_screen()
    {
        // C0 HT hardcoded 8 while CHT honoured the option, so the two disagreed.
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 5, TabStopWidth = 4 });
        terminal.Write("\t");
        var afterHt = terminal.Buffer.X;

        terminal.Write($"{Esc}[1;1H");
        terminal.Write($"{Esc}[1I");       // CHT 1
        Assert.Equal(afterHt, terminal.Buffer.X);
        Assert.Equal(4, afterHt);
    }

    [Fact]
    public void Insert_char_from_a_full_line_acts_on_the_last_column()
    {
        var terminal = NewTerminal(cols: 10);
        terminal.Write("0123456789");
        terminal.Write($"{Esc}[@");        // ICH 1

        Assert.Equal(" ", Row(terminal, 0, 10)[9..]);
    }

    [Fact]
    public void Hpa_and_vpr_move_the_cursor()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[5`");       // HPA to column 5
        Assert.Equal(4, terminal.Buffer.X);

        terminal.Write($"{Esc}[2e");       // VPR down 2
        Assert.Equal(2, terminal.Buffer.Y);
    }
}
