using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// <see cref="TerminalOptions.ColorReportFormat"/> decides the form of every OSC colour reply,
/// and whether there is one.
/// </summary>
public class ColorReportFormatTests
{
    private static readonly string Esc = ((char)0x1B).ToString();
    private static readonly string Bel = ((char)0x07).ToString();

    private static (Terminal terminal, List<string> replies) Create(ColorReportFormat? format = null)
    {
        var options = new TerminalOptions { Cols = 20, Rows = 4 };
        if (format is { } f)
            options.ColorReportFormat = f;
        var terminal = new Terminal(options);
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return (terminal, replies);
    }

    // Each query is preceded by a set of the same colour, so the expected reply does not depend on
    // the default theme. 0x12ab0f has a different digit in every nibble, so a channel reported in
    // the wrong place or at the wrong width cannot match by accident.
    public static TheoryData<string, string, string> Queries => new()
    {
        { "]10;#12ab0f" + Bel, "]10;?" + Bel, "]10;{0}" + Bel },
        { "]11;#12ab0f" + Bel, "]11;?" + Bel, "]11;{0}" + Bel },
        { "]12;#12ab0f" + Bel, "]12;?" + Bel, "]12;{0}" + Bel },
        { "]4;3;#12ab0f" + Bel, "]4;3;?" + Bel, "]4;3;{0}" + Bel },
        { "]5;1;#12ab0f" + Bel, "]5;1;?" + Bel, "]5;1;{0}" + Bel },
        { "]4;257;#12ab0f" + Bel, "]4;257;?" + Bel, "]4;257;{0}" + Bel },
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void By_default_the_reply_is_sixteen_bit(string set, string query, string reply)
    {
        var (terminal, replies) = Create();

        terminal.Write(Esc + set + Esc + query);

        Assert.Equal([Esc + string.Format(reply, "rgb:1212/abab/0f0f")], replies);
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void EightBit_replies_with_two_digits_per_channel(string set, string query, string reply)
    {
        var (terminal, replies) = Create(ColorReportFormat.EightBit);

        terminal.Write(Esc + set + Esc + query);

        Assert.Equal([Esc + string.Format(reply, "rgb:12/ab/0f")], replies);
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void None_sends_no_reply_and_still_sets_the_colour(string set, string query, string _)
    {
        var (terminal, replies) = Create(ColorReportFormat.None);

        terminal.Write(Esc + set + Esc + query);
        Assert.Empty(replies);

        // The set still landed: switching the format back reveals it.
        terminal.Options.ColorReportFormat = ColorReportFormat.EightBit;
        terminal.Write(Esc + query);
        Assert.Single(replies);
        Assert.Contains("rgb:12/ab/0f", replies[0]);
    }

    [Fact]
    public void Every_query_in_a_multi_spec_sequence_uses_the_format()
    {
        var (terminal, replies) = Create(ColorReportFormat.EightBit);

        terminal.Write(Esc + "]10;#000000;#ffffff" + Bel);
        terminal.Write(Esc + "]10;?;?" + Bel);

        Assert.Equal([Esc + "]10;rgb:00/00/00" + Bel, Esc + "]11;rgb:ff/ff/ff" + Bel], replies);
    }

    [Fact]
    public void An_out_of_range_format_falls_back_to_sixteen_bit_rather_than_silence()
    {
        var (terminal, replies) = Create((ColorReportFormat)42);

        terminal.Write(Esc + "]11;#12ab0f" + Bel + Esc + "]11;?" + Bel);

        Assert.Equal([Esc + "]11;rgb:1212/abab/0f0f" + Bel], replies);
    }

    [Fact]
    public void The_ST_terminator_is_still_mirrored()
    {
        var (terminal, replies) = Create(ColorReportFormat.EightBit);

        terminal.Write(Esc + "]4;1;#12ab0f" + Esc + "\\");
        terminal.Write(Esc + "]4;1;?" + Esc + "\\");

        Assert.Equal([Esc + "]4;1;rgb:12/ab/0f" + Esc + "\\"], replies);
    }
}
