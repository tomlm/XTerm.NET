using XTerm.Options;

namespace XTerm.Tests.Parser;

using XTerm.Parser;

/// <summary>
/// Conformance of the escape-sequence state machine to the VT500 diagram that xterm.js and
/// vt100.net both implement. Each test names a sequence a real program emits and the behavior the
/// reference parsers give it.
/// </summary>
public class ParserConformanceTests
{
    private static readonly string Esc = ((char)0x1B).ToString();
    private static readonly string Can = ((char)0x18).ToString();
    private static readonly string Sub = ((char)0x1A).ToString();
    private static readonly string C1St = ((char)0x9C).ToString();

    private static Terminal NewTerminal() => new(new TerminalOptions { Cols = 40, Rows = 6 });

    private static string Row(Terminal t, int row, int count)
    {
        var line = t.Buffer.Lines[row]!;
        return string.Concat(Enumerable.Range(0, count)
            .Select(i => string.IsNullOrEmpty(line[i].Content) ? " " : line[i].Content));
    }

    [Fact]
    public void An_escape_sequence_does_not_inherit_the_previous_ones_intermediate()
    {
        // terminfo's enacs is ESC ( B ESC ) 0. The leftover "(" from the first designator used to
        // reach the second's dispatch, switching G0 to line drawing -- a terminal that suddenly
        // drew its own prompt in box characters.
        var parser = new EscapeSequenceParser();
        var seen = new List<(string Final, string Collected)>();
        parser.Esc += (_, e) => seen.Add((e.FinalChar, e.Collected));

        parser.Parse($"{Esc}(B{Esc})0");

        Assert.Equal(2, seen.Count);
        Assert.Equal(("B", "("), seen[0]);
        Assert.Equal(("0", ")"), seen[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Can_and_sub_abandon_a_sequence_in_flight(bool useCan)
    {
        // What CAN is for. Without the transition the parser stayed inside the cancelled CSI and
        // read the text after it as parameters, so nothing printed at all.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1;2" + (useCan ? Can : Sub) + "hello");

        Assert.Equal("hello", Row(terminal, 0, 5));
    }

    [Fact]
    public void Can_abandons_an_osc_without_dispatching_half_a_title()
    {
        var terminal = NewTerminal();
        var titles = new List<string>();
        terminal.TitleChanged += (_, e) => titles.Add(e.Title);

        terminal.Write($"{Esc}]0;half a tit{Can}rest");

        Assert.Empty(titles);
        Assert.Equal("rest", Row(terminal, 0, 4));
    }

    [Fact]
    public void C1_string_terminator_ends_an_osc()
    {
        // 0x9C is the terminator ECMA-48 defines for OSC. It was appended to the payload instead,
        // so the sequence never ended and swallowed everything after it.
        var terminal = NewTerminal();
        var titles = new List<string>();
        terminal.TitleChanged += (_, e) => titles.Add(e.Title);

        terminal.Write($"{Esc}]0;my title{C1St}after");

        Assert.Equal(["my title"], titles);
        Assert.Equal("after", Row(terminal, 0, 5));
    }

    [Fact]
    public void Del_is_not_printed_as_a_cell()
    {
        var terminal = NewTerminal();
        terminal.Write("a" + (char)0x7F + "b");

        Assert.Equal("ab", Row(terminal, 0, 2));
    }

    [Fact]
    public void A_private_marker_after_a_parameter_poisons_the_sequence()
    {
        // CSI 1 ? 5 h is malformed. Honouring the half that parsed made it SM 15; the spec's
        // answer is to swallow the rest. This is also the only route into CsiIgnore, which was
        // unreachable before.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1?5h");
        terminal.Write("ok");

        Assert.Equal("ok", Row(terminal, 0, 2));
        Assert.False(terminal.InsertMode);
    }

    [Fact]
    public void A_control_inside_an_escape_intermediate_still_executes()
    {
        // EscapeIntermediate was missing from the control-character dispatch, so a line feed
        // arriving mid-designator vanished instead of moving the cursor.
        var terminal = NewTerminal();
        terminal.Write("first");
        terminal.Write($"{Esc}(" + "\n" + "B");

        Assert.Equal(1, terminal.Buffer.Y);
    }

    [Fact]
    public void Reset_abandons_a_partial_utf8_sequence()
    {
        // The byte entry point holds a truncated multi-byte prefix across calls, by design. Reset
        // forgot it, so the next write's first character was decoded against bytes from before.
        var printed = new List<string>();
        var parser = new EscapeSequenceParser();
        parser.Print += (_, e) => printed.Add(e.Data);

        parser.Parse(new byte[] { 0xE2, 0x82 });          // two thirds of a euro sign
        parser.Reset();
        parser.Parse(new byte[] { 0xE2, 0x82, 0xAC });    // a whole one

        Assert.Equal(["\u20AC"], printed);
    }
}
