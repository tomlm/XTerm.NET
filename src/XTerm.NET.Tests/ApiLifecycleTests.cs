using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Lifecycle and state that outlives a single sequence: what a terminal still believes after a
/// reset, after a dispose, or after a program leaves a mode set.
/// </summary>
public class ApiLifecycleTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static Terminal NewTerminal(int cols = 20, int rows = 6) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    [Fact]
    public void Terminal_is_disposable_and_disposing_twice_is_harmless()
    {
        // It always had the method; without the interface no using statement, DI container or
        // analyzer could see it.
        using (var terminal = NewTerminal())
        {
            terminal.Write("hello");
        }

        var second = NewTerminal();
        second.Dispose();
        second.Dispose();
    }

    [Fact]
    public void Writing_to_a_disposed_terminal_is_ignored_rather_than_thrown()
    {
        // Deliberate: a host reads its pty on a background thread, and disposing the control while
        // a read is in flight is ordinary. Throwing there would kill the read loop.
        var terminal = NewTerminal();
        terminal.Dispose();

        var ex = Record.Exception(() => terminal.Write("after"));
        Assert.Null(ex);
    }

    [Fact]
    public void Reset_restores_the_charset_designations()
    {
        // ResetCharsets existed for this and was called from nowhere, so a program that
        // designated line drawing into G0 and died left the next one printing box characters.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}(0");        // G0 = line drawing
        terminal.Write($"{Esc}c");         // RIS
        terminal.Write("q");

        Assert.Equal("q", terminal.Buffer.Lines[0]![0].Content);
    }

    [Fact]
    public void Reset_restores_the_sixel_modes()
    {
        // They survived RIS, so DECRQM went on reporting mode 80 as set after a reset.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?80h");
        Assert.True(terminal.SixelDisplayMode);

        terminal.Write($"{Esc}c");
        Assert.False(terminal.SixelDisplayMode);
        Assert.True(terminal.SixelPrivateColorRegisters);
    }

    [Fact]
    public void Reverse_wraparound_moves_the_cursor_back_over_the_wrap()
    {
        // DECSET 45 was stored and reported and nothing read it, so a shell erasing a wrapped
        // command line stopped at the wrap.
        var terminal = NewTerminal(cols: 10);
        terminal.Write($"{Esc}[?45h");
        terminal.Write($"{Esc}[2;1H");     // row 2, column 1
        terminal.Write("\b");

        Assert.Equal(0, terminal.Buffer.Y);
        Assert.Equal(9, terminal.Buffer.X);
    }

    [Fact]
    public void A_notification_that_fails_to_build_is_not_raised()
    {
        // Missing braces meant only the inner if was guarded, so a failed build raised the event
        // anyway with null title AND null body.
        var terminal = NewTerminal();
        terminal.Options.KittyNotificationsEnabled = true;
        var raised = new List<string?>();
        terminal.NotificationReceived += (_, e) => raised.Add(e.Title);

        terminal.Write($"{Esc}]99;i=x:d=1;{Esc}\\");   // done, but nothing to show

        Assert.All(raised, t => Assert.False(string.IsNullOrEmpty(t)));
    }
}
