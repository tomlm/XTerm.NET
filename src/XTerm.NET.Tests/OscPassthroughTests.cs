using XTerm;
using XTerm.Events;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Covers <see cref="Terminal.OscReceived"/>, the escape hatch for OSC codes this terminal does not
/// implement.
/// </summary>
public class OscPassthroughTests
{
    private Terminal CreateTerminal(int cols = 80, int rows = 24)
    {
        var options = new TerminalOptions { Cols = cols, Rows = rows };
        return new Terminal(options);
    }

    private static List<TerminalEvents.OscReceivedEventArgs> Capture(Terminal terminal)
    {
        var seen = new List<TerminalEvents.OscReceivedEventArgs>();
        terminal.OscReceived += (_, e) => seen.Add(e);
        return seen;
    }

    [Fact]
    public void OscReceived_FiresForUnknownSequence()
    {
        // The reason this event exists: a code with no case here reaches Debug.WriteLine and is
        // otherwise unrecoverable. OSC 1337 is iTerm2's proprietary space; its unknown keys remain
        // available to a listener even though the useful keys have built-in handling.
        //
        // This used to use OSC 133, which was unimplemented when the event was added and is not any
        // more. That is the Recognized contract working rather than a test going stale: a listener
        // filling the gap stops doing so on its own once a code lands in HandleOsc. The pair below
        // pins both halves of it.
        var terminal = CreateTerminal();
        var seen = Capture(terminal);

        terminal.Write("\x1B]1337;SetMark\x07");

        var osc = Assert.Single(seen);
        Assert.Equal(1337, osc.Code);
        Assert.Equal("1337", osc.Identifier);
        Assert.Equal("SetMark", osc.Data);
        Assert.Equal("1337;SetMark", osc.Raw);
        Assert.False(osc.Recognized, "the terminal has no handler for 1337, which is the point");
    }

    [Fact]
    public void OscReceived_ReportsShellIntegrationAsRecognized_NowThatItIsImplemented()
    {
        // The other half: OSC 133 is handled now, so a listener that only wants what this terminal
        // ignores must be told to leave it alone.
        var terminal = CreateTerminal();
        var seen = Capture(terminal);

        terminal.Write("\x1B]133;A\x07");

        var osc = Assert.Single(seen);
        Assert.Equal(133, osc.Code);
        Assert.True(osc.Recognized, "133 reaches a handler now, and Recognized has to say so");
    }

    [Fact]
    public void OscReceived_FiresForKnownSequence_AndReportsItRecognized()
    {
        var terminal = CreateTerminal();
        var seen = Capture(terminal);

        terminal.Write("\x1B]0;A Title\x07");

        var osc = Assert.Single(seen);
        Assert.Equal(0, osc.Code);
        Assert.Equal("A Title", osc.Data);
        Assert.True(osc.Recognized);
    }

    [Fact]
    public void OscReceived_DoesNotDisturbBuiltInHandling()
    {
        // Purely additive: subscribing must not change what the terminal already did.
        var terminal = CreateTerminal();
        Capture(terminal);

        terminal.Write("\x1B]0;Still Set\x07");

        Assert.Equal("Still Set", terminal.Title);
    }

    [Fact]
    public void OscReceived_FiresAfterBuiltInHandling()
    {
        // Ordering is contractual: a listener reads terminal state as settled, not mid-flight.
        var terminal = CreateTerminal();
        string? titleWhenObserved = null;
        terminal.OscReceived += (_, _) => titleWhenObserved = terminal.Title;

        terminal.Write("\x1B]0;Observed\x07");

        Assert.Equal("Observed", titleWhenObserved);
    }

    [Fact]
    public void OscReceived_ReportsNegativeCode_ForNonNumericIdentifier()
    {
        var terminal = CreateTerminal();
        var seen = Capture(terminal);

        terminal.Write("\x1B]notanumber;payload\x07");

        var osc = Assert.Single(seen);
        Assert.Equal(-1, osc.Code);
        Assert.Equal("notanumber", osc.Identifier);
        Assert.Equal("payload", osc.Data);
        Assert.False(osc.Recognized);
    }

    [Fact]
    public void OscReceived_ReportsEmptyData_WhenSequenceHasNoParameters()
    {
        var terminal = CreateTerminal();
        var seen = Capture(terminal);

        terminal.Write("\x1B]133\x07");

        var osc = Assert.Single(seen);
        Assert.Equal(133, osc.Code);
        Assert.Equal(string.Empty, osc.Data);
        Assert.Equal("133", osc.Raw);
    }

    [Fact]
    public void OscReceived_KeepsDataIntact_WhenPayloadContainsSemicolons()
    {
        // Only the FIRST ';' separates identifier from data. OSC 9;4 and OSC 133;D;<exit> both carry
        // their own sub-parameters, and a handler cannot reconstruct them if this splits too eagerly.
        var terminal = CreateTerminal();
        var seen = Capture(terminal);

        terminal.Write("\x1B]9;4;1;50\x07");

        var osc = Assert.Single(seen);
        Assert.Equal(9, osc.Code);
        Assert.Equal("4;1;50", osc.Data);
        Assert.Equal("9;4;1;50", osc.Raw);
    }

    [Fact]
    public void OscReceived_FiresOncePerSequence()
    {
        var terminal = CreateTerminal();
        var seen = Capture(terminal);

        terminal.Write("\x1B]133;A\x07\x1B]133;B\x07\x1B]133;C\x07");

        Assert.Equal(3, seen.Count);
        Assert.Equal(new[] { "A", "B", "C" }, seen.Select(o => o.Data));
    }

    [Fact]
    public void OscReceived_AcceptsStringTerminator_AsWellAsBel()
    {
        // Shell-integration snippets in the wild use both terminators; OSC 133 examples ship with ST.
        var terminal = CreateTerminal();
        var seen = Capture(terminal);

        terminal.Write("\x1B]133;D;0\x1B\\");

        var osc = Assert.Single(seen);
        Assert.Equal(133, osc.Code);
        Assert.Equal("D;0", osc.Data);
    }
}
