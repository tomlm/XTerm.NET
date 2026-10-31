using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// ENQ (0x05) answers with <see cref="TerminalOptions.EnquiryResponse"/>, xterm's answerbackString.
/// </summary>
public class EnquiryResponseTests
{
    private static readonly string Esc = ((char)0x1B).ToString();
    private static readonly string Enq = ((char)0x05).ToString();

    private static (Terminal terminal, List<string> replies) Create(string? answerback = null)
    {
        var options = new TerminalOptions { Cols = 20, Rows = 4 };
        if (answerback is not null)
            options.EnquiryResponse = answerback;
        var terminal = new Terminal(options);
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return (terminal, replies);
    }

    [Fact]
    public void By_default_ENQ_sends_nothing()
    {
        var (terminal, replies) = Create();

        terminal.Write("a" + Enq + "b");

        Assert.Empty(replies);
        Assert.Equal("ab", terminal.Buffer.Lines[0]!.TranslateToString(trimRight: true));
    }

    [Fact]
    public void ENQ_sends_the_configured_answerback()
    {
        var (terminal, replies) = Create("xterm.net");

        terminal.Write("a" + Enq + "b");

        Assert.Equal(["xterm.net"], replies);
        Assert.Equal("ab", terminal.Buffer.Lines[0]!.TranslateToString(trimRight: true));
    }

    [Fact]
    public void The_answerback_is_read_when_ENQ_arrives_not_when_the_terminal_is_built()
    {
        var (terminal, replies) = Create();

        terminal.Options.EnquiryResponse = "later";
        terminal.Write(Enq);

        Assert.Equal(["later"], replies);
    }

    [Fact]
    public void ENQ_inside_an_OSC_payload_does_not_answer()
    {
        var (terminal, replies) = Create("answer");

        terminal.Write(Esc + "]2;ti" + Enq + "tle" + ((char)0x07));

        Assert.Empty(replies);
    }

    [Fact]
    public void ENQ_inside_a_DCS_payload_does_not_answer()
    {
        var (terminal, replies) = Create("answer");

        // DECUDK: a DCS whose payload is passed through, not executed.
        terminal.Write(Esc + "P0;1|17/" + Enq + "61" + Esc + "\\");

        Assert.DoesNotContain("answer", replies);
    }

    [Fact]
    public void A_host_execute_handler_that_consumes_ENQ_wins()
    {
        var (terminal, replies) = Create("answer");
        var seen = 0;
        terminal.RegisterExecuteHandler(0x05, () => { seen++; return true; });

        terminal.Write(Enq);

        Assert.Equal(1, seen);
        Assert.Empty(replies);
    }

    [Fact]
    public void A_host_execute_handler_that_declines_ENQ_leaves_the_answerback()
    {
        var (terminal, replies) = Create("answer");
        terminal.RegisterExecuteHandler(0x05, () => false);

        terminal.Write(Enq);

        Assert.Equal(["answer"], replies);
    }

    [Fact]
    public void The_answerback_is_sent_as_written_under_S8C1T()
    {
        var answerback = Esc + "[custom";
        var (terminal, replies) = Create(answerback);

        terminal.Write(Esc + " G"); // S8C1T
        terminal.Write(Enq);

        Assert.Equal([answerback], replies);
    }
}
