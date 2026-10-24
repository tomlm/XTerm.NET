using XTerm.Options;
using XTerm.Parser;

namespace XTerm.Tests.Parser;

/// <summary>
/// Handlers a host registers ahead of the built-in handling, in the shape of xterm.js's
/// parser.registerCsiHandler: newest first, true consumes, false passes the sequence on. Issue #172.
/// </summary>
public class CustomHandlerTests
{
    private static readonly string Esc = ((char)0x1B).ToString();
    private static readonly string SI = ((char)0x0F).ToString();

    private static Terminal NewTerminal(int cols = 20, int rows = 4) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static string Row(Terminal terminal, int y = 0) =>
        terminal.Buffer.Lines[terminal.Buffer.YBase + y]!.TranslateToString(trimRight: true);

    [Fact]
    public void An_execute_handler_can_turn_SI_into_a_printed_blank()
    {
        // The case in #172: the Windows 10 ConPTY counts SI as a printed column, so a host behind it
        // has to as well or the two cursors drift apart.
        var plain = NewTerminal();
        plain.Write("ab" + SI + "c");
        Assert.Equal("abc", Row(plain));

        var terminal = NewTerminal();
        terminal.RegisterExecuteHandler(0x0F, () => { terminal.Print(" "); return true; });
        terminal.Write("ab" + SI + "c");

        Assert.Equal("ab c", Row(terminal));
        Assert.Equal(4, terminal.Buffer.X);
    }

    [Fact]
    public void A_control_inside_a_CSI_can_print_without_disturbing_the_sequence()
    {
        // Why Print exists rather than telling the host to Write: SI here arrives between a CSI's
        // parameter and its final byte, and a nested Write would be parsed as part of the sequence.
        var terminal = NewTerminal();
        terminal.RegisterExecuteHandler(0x0F, () => { terminal.Print(" "); return true; });

        terminal.Write(Esc + "[3" + SI + "Cx");

        // The blank at column 0, then CUF 3, then x.
        Assert.Equal("    x", Row(terminal));
        Assert.Equal(5, terminal.Buffer.X);
    }

    [Fact]
    public void A_handler_that_declines_leaves_the_built_in_handling_in_place()
    {
        var terminal = NewTerminal();
        var seen = 0;
        terminal.RegisterExecuteHandler(0x08, () => { seen++; return false; });

        terminal.Write("abc\b");

        Assert.Equal(1, seen);
        Assert.Equal(2, terminal.Buffer.X);
    }

    [Fact]
    public void The_newest_handler_runs_first_and_consuming_stops_the_older_ones()
    {
        var terminal = NewTerminal();
        var calls = new List<string>();
        terminal.RegisterExecuteHandler(0x07, () => { calls.Add("oldest"); return false; });
        terminal.RegisterExecuteHandler(0x07, () => { calls.Add("middle"); return true; });
        terminal.RegisterExecuteHandler(0x07, () => { calls.Add("newest"); return false; });
        var rang = false;
        terminal.BellRang += (_, _) => rang = true;

        terminal.Write("\a");

        Assert.Equal(new[] { "newest", "middle" }, calls);
        Assert.False(rang);
    }

    [Fact]
    public void Disposing_unregisters_once_and_leaves_the_others()
    {
        var terminal = NewTerminal();
        var calls = new List<string>();
        var first = terminal.RegisterExecuteHandler(0x07, () => { calls.Add("first"); return false; });
        var second = terminal.RegisterExecuteHandler(0x07, () => { calls.Add("second"); return false; });

        second.Dispose();
        second.Dispose();
        terminal.Write("\a");
        Assert.Equal(new[] { "first" }, calls);

        first.Dispose();
        terminal.Write("\a");
        Assert.Equal(new[] { "first" }, calls);
    }

    [Fact]
    public void A_handler_can_unregister_itself_while_it_runs()
    {
        var terminal = NewTerminal();
        var calls = 0;
        IDisposable? registration = null;
        registration = terminal.RegisterExecuteHandler(0x0F, () =>
        {
            calls++;
            registration!.Dispose();
            terminal.Print(" ");
            return true;
        });

        terminal.Write(SI + SI + "x");

        Assert.Equal(1, calls);
        Assert.Equal(" x", Row(terminal));
    }

    [Fact]
    public void Consuming_ESC_does_not_stop_the_escape_sequence_it_opens()
    {
        // The handler replaces what a control does, not the state machine: ESC is still an introducer.
        var terminal = NewTerminal();
        terminal.RegisterExecuteHandler(0x1B, () => true);

        terminal.Write(Esc + "[5Cx");

        Assert.Equal("     x", Row(terminal));
    }

    [Fact]
    public void A_CSI_handler_receives_the_parameters_and_can_consume_the_sequence()
    {
        var terminal = NewTerminal();
        var seen = new List<int>();
        terminal.RegisterCsiHandler("C", p => { seen.Add(p.GetParam(0)); return true; });

        terminal.Write(Esc + "[5Cx");

        Assert.Equal(new[] { 5 }, seen);
        Assert.Equal("x", Row(terminal));
    }

    [Fact]
    public void A_CSI_handler_is_keyed_by_private_marker_and_intermediates_too()
    {
        var terminal = NewTerminal();
        var hits = new List<string>();
        terminal.RegisterCsiHandler("?h", p => { hits.Add("?h " + p.GetParam(0)); return false; });
        terminal.RegisterCsiHandler("h", _ => { hits.Add("h"); return false; });
        terminal.RegisterCsiHandler(" q", p => { hits.Add("DECSCUSR " + p.GetParam(0)); return false; });

        terminal.Write(Esc + "[?25h" + Esc + "[4h" + Esc + "[2 q");

        Assert.Equal(new[] { "?h 25", "h", "DECSCUSR 2" }, hits);
    }

    [Fact]
    public void An_ESC_handler_can_consume_a_reset()
    {
        var terminal = NewTerminal();
        terminal.Write("kept");
        terminal.RegisterEscHandler("c", () => true);

        terminal.Write(Esc + "c");

        Assert.Equal("kept", Row(terminal));
    }

    [Fact]
    public void An_ESC_handler_is_keyed_by_intermediates_and_final()
    {
        var terminal = NewTerminal();
        var hits = new List<string>();
        terminal.RegisterEscHandler("(0", () => { hits.Add("G0 line drawing"); return true; });
        terminal.RegisterEscHandler("0", () => { hits.Add("bare 0"); return true; });

        terminal.Write(Esc + "(0q");

        Assert.Equal(new[] { "G0 line drawing" }, hits);
        // Consumed, so G0 was never designated and q prints as itself rather than a horizontal line.
        Assert.Equal("q", Row(terminal));
    }

    [Fact]
    public void The_parser_events_still_report_what_was_consumed()
    {
        var parser = new EscapeSequenceParser();
        var executed = new List<int>();
        var csi = new List<string>();
        var esc = new List<string>();
        parser.Execute += (_, e) => executed.Add(e.Code);
        parser.Csi += (_, e) => csi.Add(e.Identifier);
        parser.Esc += (_, e) => esc.Add(e.Collected + e.FinalChar);
        parser.RegisterExecuteHandler(0x0F, () => true);
        parser.RegisterCsiHandler("m", _ => true);
        parser.RegisterEscHandler("7", () => true);

        parser.Parse(SI + Esc + "[1m" + Esc + "7");

        Assert.Equal(new[] { 0x0F, 0x1B, 0x1B }, executed);
        Assert.Equal(new[] { "m" }, csi);
        Assert.Equal(new[] { "7" }, esc);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x20)]
    [InlineData(0x41)]
    [InlineData(0x7F)]
    [InlineData(0xA0)]
    public void Only_control_characters_take_an_execute_handler(int code)
    {
        var terminal = NewTerminal();
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.RegisterExecuteHandler(code, () => true));
    }

    [Fact]
    public void Print_rejects_control_characters_before_printing_anything()
    {
        var terminal = NewTerminal();
        Assert.Throws<ArgumentException>(() => terminal.Print("ab\ncd"));
        Assert.Equal("", Row(terminal));
        Assert.Equal(0, terminal.Buffer.X);
    }

    [Fact]
    public void Print_takes_wide_and_astral_characters_as_the_parser_would()
    {
        var printed = NewTerminal();
        printed.Print("a一\U0001F600b");

        var written = NewTerminal();
        written.Write("a一\U0001F600b");

        Assert.Equal(Row(written), Row(printed));
        Assert.Equal(written.Buffer.X, printed.Buffer.X);
    }
}
