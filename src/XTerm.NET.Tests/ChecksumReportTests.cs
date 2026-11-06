using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// DECRQCRA — the checksum report esctest builds every content assertion on. A terminal without
/// it cannot be conformance-tested at all, which is why it leads the esctest campaign.
/// </summary>
public class ChecksumReportTests
{
    private const string Esc = "";

    // Opted in, as an esctest run has to be: the report is off by default (tomlm/XTerm.NET#190).
    private static Terminal NewTerminal(int cols = 20, int rows = 5, bool enabled = true) =>
        new(new TerminalOptions { Cols = cols, Rows = rows, ChecksumReportsEnabled = enabled });

    /// <summary>
    /// Off by default: DECRQCRA reads the screen back one cell at a time, so a program could recover
    /// what was displayed before it started. Like xterm and WezTerm when it is disallowed, the
    /// request goes unanswered rather than drawing a reply that could pass for a sum.
    /// </summary>
    [Fact]
    public void Is_silent_by_default()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
        terminal.Write("Hello");

        Assert.Null(Reply(terminal, $"{Esc}[1;0;1;1;1;1*y"));
        Assert.Null(Reply(terminal, $"{Esc}[1*y"));
    }

    [Fact]
    public void Is_silent_when_disabled_and_answers_once_enabled()
    {
        var terminal = NewTerminal(enabled: false);
        terminal.Write("Hello");
        Assert.Null(Reply(terminal, $"{Esc}[1;0;1;1;1;1*y"));

        // The option is read per request, so a host can turn it on for a running terminal.
        terminal.Options.ChecksumReportsEnabled = true;
        Assert.Equal(Report(1, 'H'), Reply(terminal, $"{Esc}[1;0;1;1;1;1*y"));
    }

    private static string? Reply(Terminal terminal, string sequence)
    {
        string? reply = null;
        terminal.DataReceived += (_, e) => reply = e.Data;
        terminal.Write(sequence);
        return reply;
    }

    private static int Sum(string text)
    {
        var sum = 0;
        foreach (var ch in text) sum += ch;
        return sum;
    }

    /// <summary>The negated-sum convention xterm used before patch #279, which esctest's default expects.</summary>
    private static string Report(int id, int sum) => $"{Esc}P{id}!~{(0x10000 - sum) & 0xFFFF:X4}{Esc}\\";

    [Fact]
    public void A_single_cells_checksum_is_its_character()
    {
        var terminal = NewTerminal();
        terminal.Write("Hello");

        Assert.Equal(Report(1, 'H'), Reply(terminal, $"{Esc}[1;0;1;1;1;1*y"));
    }

    [Fact]
    public void A_rects_checksum_is_the_sum_of_its_characters()
    {
        var terminal = NewTerminal();
        terminal.Write("Hello");

        Assert.Equal(Report(7, Sum("Hello")), Reply(terminal, $"{Esc}[7;0;1;1;1;5*y"));
    }

    [Fact]
    public void A_run_of_blanks_counts_once_for_the_first_cell()
    {
        // DEC terminals trim the blanks at the end of a row rather than counting them, and the
        // first cell of the area is the documented exception -- it counts whatever it holds, which
        // is what lets esctest read a written space back as 0x20 one cell at a time.
        var terminal = NewTerminal();

        Assert.Equal(Report(2, 0x20), Reply(terminal, $"{Esc}[2;0;2;1;2;3*y"));
    }

    [Fact]
    public void A_single_blank_cell_is_a_space()
    {
        // The shape esctest reads content back in: one cell at a time, expecting the character it
        // put there. Trimming that unconditionally would answer zero for every space on screen.
        var terminal = NewTerminal();

        Assert.Equal(Report(8, 0x20), Reply(terminal, $"{Esc}[8;0;2;1;2;1*y"));
    }

    [Fact]
    public void A_blank_between_two_characters_counts()
    {
        // Only TRAILING blanks are trimmed. One with text still to come on its row is interior,
        // and vttest computes its expectation the same way.
        var terminal = NewTerminal();
        terminal.Write("a b");

        Assert.Equal(Report(9, Sum("a b")), Reply(terminal, $"{Esc}[9;0;1;1;1;3*y"));
    }

    [Fact]
    public void Blanks_trailing_a_row_are_trimmed()
    {
        // The same three cells as above with the tail cut off: 'a', a blank, and nothing after it
        // on the row.
        var terminal = NewTerminal();
        terminal.Write("a b");

        Assert.Equal(Report(10, 'a'), Reply(terminal, $"{Esc}[10;0;1;1;1;2*y"));
    }

    [Fact]
    public void A_rows_trailing_blanks_do_not_carry_into_the_next()
    {
        // Trimming is per row: the blanks after "hi" end with row 1 rather than being revived by
        // the "there" on row 2.
        var terminal = NewTerminal(cols: 8, rows: 2);
        terminal.Write($"hi{Esc}[2;1Hthere");

        Assert.Equal(Report(11, Sum("hi") + Sum("there")), Reply(terminal, $"{Esc}[11*y"));
    }

    [Fact]
    public void The_trailing_half_of_a_wide_character_adds_nothing()
    {
        // The wide character was already counted in full one cell to the left; counting its
        // placeholder too would double it, and counting it as a blank would add a phantom space.
        var terminal = NewTerminal();
        terminal.Write("世");

        Assert.Equal(Report(3, '世'), Reply(terminal, $"{Esc}[3;0;1;1;1;2*y"));
    }

    [Fact]
    public void Coordinates_are_clamped_to_the_screen()
    {
        var terminal = NewTerminal(cols: 10, rows: 3);
        terminal.Write("AB");

        // A rect hanging off every edge still answers, for what the screen actually holds -- the
        // blanks after "AB" trail their row and the two rows below it, so none of them count.
        Assert.Equal(Report(4, Sum("AB")),
                     Reply(terminal, $"{Esc}[4;0;1;1;99;99*y"));
    }

    [Fact]
    public void Omitted_coordinates_mean_the_whole_screen()
    {
        var terminal = NewTerminal(cols: 4, rows: 2);
        terminal.Write("hi");

        Assert.Equal(Report(5, Sum("hi")), Reply(terminal, $"{Esc}[5*y"));
    }

    [Fact]
    public void Attributes_contribute_nothing_to_the_checksum()
    {
        // esctest compares a cell's checksum to the bare codepoint of the character it expects;
        // a weight per attribute bit would fail every assertion on styled text.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1;4;7;31mX");

        Assert.Equal(Report(6, 'X'), Reply(terminal, $"{Esc}[6;0;1;1;1;1*y"));
    }
}
