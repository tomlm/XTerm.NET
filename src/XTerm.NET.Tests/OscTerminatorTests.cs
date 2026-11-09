using XTerm;
using XTerm.Events;
using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// <see cref="TerminalEvents.OscReceivedEventArgs.EndedWithBel"/>: which terminator the program used,
/// so a handler can answer in kind.
/// </summary>
public class OscTerminatorTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static TerminalEvents.OscReceivedEventArgs Receive(string sequence)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
        var seen = new List<TerminalEvents.OscReceivedEventArgs>();
        terminal.OscReceived += (_, e) => seen.Add(e);
        terminal.Write(sequence);
        return Assert.Single(seen);
    }

    [Fact]
    public void A_sequence_ended_with_BEL_says_so()
    {
        Assert.True(Receive($"{Esc}]1337;SetMark\u0007").EndedWithBel);
    }

    [Fact]
    public void A_sequence_ended_with_ST_says_not()
    {
        Assert.False(Receive($"{Esc}]1337;SetMark{Esc}\\").EndedWithBel);
    }

    [Fact]
    public void Each_sequence_reports_its_own_terminator()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
        var seen = new List<bool>();
        terminal.OscReceived += (_, e) => seen.Add(e.EndedWithBel);

        terminal.Write($"{Esc}]1337;A\u0007{Esc}]1337;B{Esc}\\{Esc}]1337;C\u0007");

        Assert.Equal(new[] { true, false, true }, seen);
    }

    [Fact]
    public void The_old_constructor_still_builds_one_that_ended_with_ST()
    {
        var e = new TerminalEvents.OscReceivedEventArgs("1", 1, "", "1", recognized: false);
        Assert.False(e.EndedWithBel);
    }
}
