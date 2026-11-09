using XTerm;
using XTerm.Common;
using XTerm.Events;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Covers OSC 133 (FinalTerm/FTCS shell integration marks) and OSC 9 (the ConEmu extensions:
/// working directory, progress, notification).
/// </summary>
public class ShellIntegrationTests
{
    private Terminal CreateTerminal(int cols = 80, int rows = 24)
    {
        var options = new TerminalOptions { Cols = cols, Rows = rows };
        return new Terminal(options);
    }

    // ---- OSC 133 -----------------------------------------------------------------------------

    [Fact]
    public void ShellIntegrationState_IsNullBeforeAnyMark()
    {
        // Null is the third state and the reason this property is nullable: a shell with no
        // integration configured is indistinguishable from one sitting at a prompt, and defaulting
        // to PromptStart would assert the shell is idle on no evidence at all.
        var terminal = CreateTerminal();

        Assert.Null(terminal.ShellIntegrationState);
        Assert.Null(terminal.LastCommandExitCode);
    }

    [Theory]
    [InlineData("A", ShellIntegrationMark.PromptStart)]
    [InlineData("B", ShellIntegrationMark.CommandStart)]
    [InlineData("C", ShellIntegrationMark.CommandExecuted)]
    [InlineData("D", ShellIntegrationMark.CommandFinished)]
    public void Osc133_RecordsEachMark(string letter, ShellIntegrationMark expected)
    {
        var terminal = CreateTerminal();
        TerminalEvents.ShellIntegrationEventArgs? received = null;
        terminal.ShellIntegrationMarkReceived += (_, e) => received = e;

        terminal.Write($"\x1B]133;{letter}\x07");

        Assert.Equal(expected, terminal.ShellIntegrationState);
        Assert.NotNull(received);
        Assert.Equal(expected, received!.Mark);
    }

    [Fact]
    public void Osc133_TracksAFullPromptCommandCycle()
    {
        // The sequence a caller actually depends on: at B the shell is waiting for input, at C
        // something else owns the terminal, at D it is the shell's again.
        var terminal = CreateTerminal();
        var marks = new List<ShellIntegrationMark>();
        terminal.ShellIntegrationMarkReceived += (_, e) => marks.Add(e.Mark);

        terminal.Write("\x1B]133;A\x07");
        terminal.Write("\x1B]133;B\x07");
        Assert.Equal(ShellIntegrationMark.CommandStart, terminal.ShellIntegrationState);

        terminal.Write("\x1B]133;C\x07");
        Assert.Equal(ShellIntegrationMark.CommandExecuted, terminal.ShellIntegrationState);

        terminal.Write("\x1B]133;D;0\x07");
        Assert.Equal(ShellIntegrationMark.CommandFinished, terminal.ShellIntegrationState);

        Assert.Equal(
            new[]
            {
                ShellIntegrationMark.PromptStart,
                ShellIntegrationMark.CommandStart,
                ShellIntegrationMark.CommandExecuted,
                ShellIntegrationMark.CommandFinished,
            },
            marks);
    }

    [Fact]
    public void Osc133_CapturesExitCode()
    {
        var terminal = CreateTerminal();
        TerminalEvents.ShellIntegrationEventArgs? received = null;
        terminal.ShellIntegrationMarkReceived += (_, e) => received = e;

        terminal.Write("\x1B]133;D;127\x07");

        Assert.Equal(127, terminal.LastCommandExitCode);
        Assert.Equal(127, received!.ExitCode);
    }

    [Fact]
    public void Osc133_CapturesNegativeExitCode()
    {
        // Microsoft's own pwsh snippet returns -1 for a PowerShell-native error.
        var terminal = CreateTerminal();

        terminal.Write("\x1B]133;D;-1\x07");

        Assert.Equal(-1, terminal.LastCommandExitCode);
    }

    [Fact]
    public void Osc133_LeavesExitCodeNull_WhenTheShellOmitsIt()
    {
        // cmd.exe cannot read the previous command's status from its prompt, so a bare D is normal
        // rather than malformed. Null must not collapse into 0, or every cmd.exe command would look
        // like it succeeded.
        var terminal = CreateTerminal();
        TerminalEvents.ShellIntegrationEventArgs? received = null;
        terminal.ShellIntegrationMarkReceived += (_, e) => received = e;

        terminal.Write("\x1B]133;D\x07");

        Assert.Equal(ShellIntegrationMark.CommandFinished, terminal.ShellIntegrationState);
        Assert.Null(terminal.LastCommandExitCode);
        Assert.Null(received!.ExitCode);
    }

    [Fact]
    public void Osc133_ClearsAStaleExitCode_WhenTheNextCommandReportsNone()
    {
        var terminal = CreateTerminal();

        terminal.Write("\x1B]133;D;3\x07");
        Assert.Equal(3, terminal.LastCommandExitCode);

        terminal.Write("\x1B]133;D\x07");
        Assert.Null(terminal.LastCommandExitCode);
    }

    [Fact]
    public void Osc133_ReportsNoExitCode_OnMarksThatCannotCarryOne()
    {
        var terminal = CreateTerminal();
        TerminalEvents.ShellIntegrationEventArgs? received = null;
        terminal.ShellIntegrationMarkReceived += (_, e) => received = e;

        terminal.Write("\x1B]133;A\x07");

        Assert.Null(received!.ExitCode);
    }

    [Fact]
    public void Osc133_AcceptsStringTerminator()
    {
        // The bash and cmd snippets in Microsoft's docs terminate with ST, not BEL.
        var terminal = CreateTerminal();

        terminal.Write("\x1B]133;D;0\x1B\\");

        Assert.Equal(ShellIntegrationMark.CommandFinished, terminal.ShellIntegrationState);
        Assert.Equal(0, terminal.LastCommandExitCode);
    }

    [Fact]
    public void Osc133_IgnoresUnknownMark_WithoutDisturbingState()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;A\x07");

        terminal.Write("\x1B]133;Z\x07");

        Assert.Equal(ShellIntegrationMark.PromptStart, terminal.ShellIntegrationState);
    }

    [Fact]
    public void Osc133_IgnoresEmptyPayload()
    {
        var terminal = CreateTerminal();

        terminal.Write("\x1B]133\x07");

        Assert.Null(terminal.ShellIntegrationState);
    }

    // ---- OSC 133 : derived shell state -------------------------------------------------------

    private static List<(ShellState Old, ShellState New, int? Exit)> RecordShellStates(Terminal terminal)
    {
        var changes = new List<(ShellState, ShellState, int?)>();
        terminal.ShellStateChanged += (_, e) => changes.Add((e.OldState, e.NewState, e.ExitCode));
        return changes;
    }

    [Fact]
    public void ShellState_IsUnknownBeforeAnyMark()
    {
        var terminal = CreateTerminal();

        Assert.Equal(ShellState.Unknown, terminal.ShellState);
    }

    [Theory]
    [InlineData("A", ShellState.Prompt)]
    [InlineData("B", ShellState.Input)]
    [InlineData("C", ShellState.Running)]
    [InlineData("D;0", ShellState.Prompt)]
    public void ShellState_FollowsEachMark(string mark, ShellState expected)
    {
        var terminal = CreateTerminal();
        var changes = RecordShellStates(terminal);

        terminal.Write($"\x1B]133;{mark}\x07");

        Assert.Equal(expected, terminal.ShellState);
        Assert.Equal(new[] { (ShellState.Unknown, expected, mark == "D;0" ? 0 : (int?)null) }, changes);
    }

    [Fact]
    public void ShellState_TracksAFullCycle_AndReportsTheExitCodeOnlyWhenTheCommandEnds()
    {
        var terminal = CreateTerminal();
        var changes = RecordShellStates(terminal);

        terminal.Write("\x1B]133;A\x07$ \x1B]133;B\x07");
        Assert.Equal(ShellState.Input, terminal.ShellState);

        terminal.Write("make\r\n\x1B]133;C\x07");
        Assert.Equal(ShellState.Running, terminal.ShellState);

        terminal.Write("error\r\n\x1B]133;D;2\x07");
        Assert.Equal(ShellState.Prompt, terminal.ShellState);
        Assert.Equal(2, terminal.LastCommandExitCode);

        // The next prompt's A leaves the state where D put it, so it raises nothing.
        terminal.Write("\x1B]133;A\x07");

        Assert.Equal(
            new (ShellState, ShellState, int?)[]
            {
                (ShellState.Unknown, ShellState.Prompt, null),
                (ShellState.Prompt, ShellState.Input, null),
                (ShellState.Input, ShellState.Running, null),
                (ShellState.Running, ShellState.Prompt, 2),
            },
            changes);
    }

    [Fact]
    public void ShellState_ReportsNoExitCode_ForABareD()
    {
        // cmd.exe always sends a bare D. Null must not read as success.
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;C\x07");
        var changes = RecordShellStates(terminal);

        terminal.Write("\x1B]133;D\x07");

        Assert.Equal(new[] { (ShellState.Running, ShellState.Prompt, (int?)null) }, changes);
    }

    [Fact]
    public void ShellState_RepeatedMarksRaiseNothing()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;C\x07");
        var changes = RecordShellStates(terminal);

        terminal.Write("\x1B]133;C\x07\x1B]133;C\x07");

        Assert.Equal(ShellState.Running, terminal.ShellState);
        Assert.Empty(changes);
    }

    [Fact]
    public void ShellState_FullReset_ReturnsToUnknown()
    {
        // RIS while a command runs is what `reset` itself produces: the state goes to Unknown, and
        // the D that follows when `reset` exits brings it back to Prompt.
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;C\x07");
        var changes = RecordShellStates(terminal);

        terminal.Write("\u001bc");
        Assert.Equal(ShellState.Unknown, terminal.ShellState);

        terminal.Write("\x1B]133;D;0\x07");
        Assert.Equal(ShellState.Prompt, terminal.ShellState);

        Assert.Equal(
            new (ShellState, ShellState, int?)[]
            {
                (ShellState.Running, ShellState.Unknown, null),
                (ShellState.Unknown, ShellState.Prompt, 0),
            },
            changes);
    }

    [Fact]
    public void ShellState_FullResetWithNoState_RaisesNothing()
    {
        var terminal = CreateTerminal();
        var changes = RecordShellStates(terminal);

        terminal.Write("\u001bc");

        Assert.Empty(changes);
    }

    [Fact]
    public void ResetShellState_ForgetsTheStateAndNothingElse()
    {
        // A host connecting the terminal to a new process: the old shell's state must go, its screen
        // must not.
        var terminal = CreateTerminal();
        terminal.Write("old output\r\n\x1B]133;C\x07");
        var changes = RecordShellStates(terminal);

        terminal.ResetShellState();

        Assert.Equal(ShellState.Unknown, terminal.ShellState);
        Assert.Equal(new[] { (ShellState.Running, ShellState.Unknown, (int?)null) }, changes);
        Assert.StartsWith("old output", terminal.Buffer.Lines[0]!.TranslateToString(true));

        // And the next shell's marks are heard from scratch, even a B the old shell had also sent last.
        terminal.ResetShellState();
        terminal.Write("\x1B]133;B\x07");
        Assert.Equal(ShellState.Input, terminal.ShellState);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void ShellState_SoftResetLeavesItAlone()
    {
        // DECSTR resets modes for the program that asked; it says nothing about the shell.
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;C\x07");

        terminal.Write("\x1B[!p");

        Assert.Equal(ShellState.Running, terminal.ShellState);
    }

    [Fact]
    public void ShellState_RecoversFromAMissingD_AtTheNextPrompt()
    {
        // The shell crashed mid-command, or the command was killed before D: a new prompt's A is
        // evidence enough that nothing is running any more.
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;D;5\x07\x1B]133;A\x07\x1B]133;B\x07\x1B]133;C\x07");
        var changes = RecordShellStates(terminal);

        terminal.Write("\x1B]133;A\x07");

        Assert.Equal(ShellState.Prompt, terminal.ShellState);
        Assert.Equal(new[] { (ShellState.Running, ShellState.Prompt, (int?)null) }, changes);
        // The last exit code is still the last one REPORTED; a missing D reports nothing.
        Assert.Equal(5, terminal.LastCommandExitCode);
    }

    [Fact]
    public void ShellState_FollowsTheLatestMark_WhenMarksArriveOutOfOrder()
    {
        var terminal = CreateTerminal();

        // C with no A or B before it: a shell whose integration starts mid-session.
        terminal.Write("\x1B]133;C\x07");
        Assert.Equal(ShellState.Running, terminal.ShellState);

        // B with no A after a D: the prompt mark was dropped.
        terminal.Write("\x1B]133;D\x07\x1B]133;B\x07");
        Assert.Equal(ShellState.Input, terminal.ShellState);

        // A while waiting for input: the prompt was redrawn (Ctrl+C, a resize).
        terminal.Write("\x1B]133;A\x07");
        Assert.Equal(ShellState.Prompt, terminal.ShellState);
    }

    [Fact]
    public void ShellState_FollowsTheInnerShell_WhenShellsNest()
    {
        // `bash` run from bash: the outer shell's C, then the inner shell's own cycle, then the
        // outer shell's D when the inner one exits. The terminal follows whoever spoke last.
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;A\x07\x1B]133;B\x07\x1B]133;C\x07");

        terminal.Write("\x1B]133;A\x07\x1B]133;B\x07");
        Assert.Equal(ShellState.Input, terminal.ShellState);

        terminal.Write("\x1B]133;C\x07");
        Assert.Equal(ShellState.Running, terminal.ShellState);

        terminal.Write("\x1B]133;D;0\x07\x1B]133;A\x07\x1B]133;B\x07");
        Assert.Equal(ShellState.Input, terminal.ShellState);

        // `exit` in the inner shell: the outer shell's D and its next prompt.
        terminal.Write("\x1B]133;C\x07\x1B]133;D;0\x07\x1B]133;A\x07");
        Assert.Equal(ShellState.Prompt, terminal.ShellState);
    }

    [Fact]
    public void ShellState_IsUnchangedByTheAlternateScreen()
    {
        // vim takes the alternate screen inside a running command; neither switch is a shell mark.
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;C\x07");
        var changes = RecordShellStates(terminal);

        terminal.Write("\x1B[?1049h");
        Assert.Equal(ShellState.Running, terminal.ShellState);
        terminal.Write("\x1B[2J~\x1B[?1049l");

        Assert.Equal(ShellState.Running, terminal.ShellState);
        Assert.Empty(changes);
    }

    [Fact]
    public void ShellState_IgnoresUnknownMarks()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]133;C\x07");

        terminal.Write("\x1B]133;Z\x07\x1B]133\x07");

        Assert.Equal(ShellState.Running, terminal.ShellState);
    }

    [Fact]
    public void ShellState_IsCurrent_WhenTheMarkEventIsRaised()
    {
        // A handler of the raw mark event reading ShellState sees the state that mark produced.
        var terminal = CreateTerminal();
        ShellState? seen = null;
        terminal.ShellIntegrationMarkReceived += (_, _) => seen = terminal.ShellState;

        terminal.Write("\x1B]133;C\x07");

        Assert.Equal(ShellState.Running, seen);
    }

    // ---- OSC 9 ; 9 : working directory -------------------------------------------------------

    [Fact]
    public void Osc9Cwd_SetsCurrentDirectory()
    {
        // Microsoft's documented Windows prompts emit 9;9 rather than OSC 7, so a terminal reading
        // only 7 loses the working directory on Windows entirely.
        var terminal = CreateTerminal();
        string? reported = null;
        terminal.DirectoryChanged += (_, e) => reported = e.Directory;

        terminal.Write("\x1B]9;9;C:\\Users\\me\x07");

        Assert.Equal("C:\\Users\\me", terminal.CurrentDirectory);
        Assert.Equal("C:\\Users\\me", reported);
    }

    [Fact]
    public void Osc9Cwd_StripsSurroundingQuotes()
    {
        // The pwsh snippet in Microsoft's docs emits the path already quoted.
        var terminal = CreateTerminal();

        terminal.Write("\x1B]9;9;\"C:\\Program Files\"\x07");

        Assert.Equal("C:\\Program Files", terminal.CurrentDirectory);
    }

    [Fact]
    public void Osc9Cwd_IgnoresEmptyPath()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]9;9;C:\\keep\x07");

        terminal.Write("\x1B]9;9;\x07");

        Assert.Equal("C:\\keep", terminal.CurrentDirectory);
    }

    // ---- OSC 9 ; 4 : progress ----------------------------------------------------------------

    [Theory]
    [InlineData(0, ProgressState.None)]
    [InlineData(1, ProgressState.Normal)]
    [InlineData(2, ProgressState.Error)]
    [InlineData(3, ProgressState.Indeterminate)]
    [InlineData(4, ProgressState.Warning)]
    public void Osc9Progress_RecordsEachState(int raw, ProgressState expected)
    {
        var terminal = CreateTerminal();
        TerminalEvents.ProgressEventArgs? received = null;
        terminal.ProgressChanged += (_, e) => received = e;

        terminal.Write($"\x1B]9;4;{raw};50\x07");

        Assert.Equal(expected, terminal.ProgressState);
        Assert.NotNull(received);
        Assert.Equal(expected, received!.State);
    }

    [Fact]
    public void Osc9Progress_RecordsValue()
    {
        var terminal = CreateTerminal();

        terminal.Write("\x1B]9;4;1;42\x07");

        Assert.Equal(ProgressState.Normal, terminal.ProgressState);
        Assert.Equal(42, terminal.ProgressValue);
    }

    [Fact]
    public void Osc9Progress_ClampsOutOfRangeValue()
    {
        var terminal = CreateTerminal();

        terminal.Write("\x1B]9;4;1;250\x07");

        Assert.Equal(100, terminal.ProgressValue);
    }

    [Fact]
    public void Osc9Progress_ZeroesValue_ForStatesThatHaveNone()
    {
        // Indeterminate carries no percentage; leaving a stale one would render a bar at the old
        // position while claiming the extent is unknown.
        var terminal = CreateTerminal();
        terminal.Write("\x1B]9;4;1;80\x07");

        terminal.Write("\x1B]9;4;3\x07");

        Assert.Equal(ProgressState.Indeterminate, terminal.ProgressState);
        Assert.Equal(0, terminal.ProgressValue);
    }

    [Fact]
    public void Osc9Progress_ClearsOnStateNone()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]9;4;1;80\x07");

        terminal.Write("\x1B]9;4;0\x07");

        Assert.Equal(ProgressState.None, terminal.ProgressState);
        Assert.Equal(0, terminal.ProgressValue);
    }

    [Fact]
    public void Osc9Progress_IgnoresUnknownState()
    {
        var terminal = CreateTerminal();
        terminal.Write("\x1B]9;4;1;80\x07");

        terminal.Write("\x1B]9;4;9;10\x07");

        Assert.Equal(ProgressState.Normal, terminal.ProgressState);
        Assert.Equal(80, terminal.ProgressValue);
    }

    // ---- OSC 9 : notification ----------------------------------------------------------------

    [Fact]
    public void Osc9Notification_RaisesWithText()
    {
        var terminal = CreateTerminal();
        string? text = null;
        terminal.NotificationReceived += (_, e) => text = e.Text;

        terminal.Write("\x1B]9;Build finished\x07");

        Assert.Equal("Build finished", text);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("4")]
    public void Osc9_IgnoresAClaimedSubCommandWithNoPayload(string subCommand)
    {
        // "OSC 9;9" carries a sub-command and nothing else. Falling through to the notification case
        // would raise a toast whose entire body is "9".
        var terminal = CreateTerminal();
        var notifications = new List<string>();
        terminal.NotificationReceived += (_, e) => notifications.Add(e.Text);

        terminal.Write($"\u001b]9;{subCommand}\u0007");

        Assert.Empty(notifications);
    }

    [Fact]
    public void Osc9Notification_StillFiresForTextThatContainsSemicolons()
    {
        // The fallback must keep the whole body. Only a CLAIMED sub-command is special.
        var terminal = CreateTerminal();
        string? text = null;
        terminal.NotificationReceived += (_, e) => text = e.Text;

        terminal.Write("\u001b]9;Build finished; 3 warnings\u0007");

        Assert.Equal("Build finished; 3 warnings", text);
    }

    [Fact]
    public void Osc9Notification_DoesNotFireForProgressOrCwd()
    {
        // The sub-parameters are not notifications. Without this distinction OSC 9;4 would raise a
        // toast reading "4;1;50" on every progress tick.
        var terminal = CreateTerminal();
        var notifications = new List<string>();
        terminal.NotificationReceived += (_, e) => notifications.Add(e.Text);

        terminal.Write("\x1B]9;4;1;50\x07");
        terminal.Write("\x1B]9;9;/home/me\x07");

        Assert.Empty(notifications);
    }
}
