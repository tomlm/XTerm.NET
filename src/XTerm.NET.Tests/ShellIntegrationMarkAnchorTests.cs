using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// OSC 133 marks anchored to the lines they were emitted on.
///
/// <para>The events say a mark happened. These say WHERE, which is the half every use of shell
/// integration actually needs — jumping to the previous prompt, selecting a command's output,
/// putting an exit status beside the command that produced it.</para>
/// </summary>
public class ShellIntegrationMarkAnchorTests
{
    private const string Esc = "\u001b";
    private const string Bel = "\u0007";

    private static string Mark(string what) => $"{Esc}]133;{what}{Bel}";

    private static Terminal Fresh(int cols = 20, int rows = 5)
        => new(new TerminalOptions { Cols = cols, Rows = rows });

    private static IReadOnlyList<LineMark> MarksOn(Terminal t, int screenRow)
        => t.Buffer.Lines[t.Buffer.YBase + screenRow]!.Marks;

    [Fact]
    public void A_prompt_mark_lands_on_the_line_it_was_emitted_on()
    {
        var t = Fresh();
        t.Write("\r\n\r\n");
        t.Write(Mark("A") + "$ ");

        Assert.Empty(MarksOn(t, 0));
        Assert.Single(MarksOn(t, 2));
        Assert.Equal(ShellIntegrationMark.PromptStart, MarksOn(t, 2)[0].Kind);
    }

    /// <summary>The column too, so a host can tell the prompt from what follows it.</summary>
    [Fact]
    public void A_mark_records_the_column()
    {
        var t = Fresh();
        t.Write("$ " + Mark("B"));

        Assert.Equal(2, MarksOn(t, 0)[0].Column);
    }

    /// <summary>A prompt emits A then B, and a command with no output finishes on the same line.</summary>
    [Fact]
    public void One_line_can_carry_several_marks()
    {
        var t = Fresh();
        t.Write(Mark("A") + "$ " + Mark("B") + "true" + Mark("C") + Mark("D;0"));

        var marks = MarksOn(t, 0);
        Assert.Equal(4, marks.Count);
        Assert.Equal(
            new[] { ShellIntegrationMark.PromptStart, ShellIntegrationMark.CommandStart,
                    ShellIntegrationMark.CommandExecuted, ShellIntegrationMark.CommandFinished },
            marks.Select(m => m.Kind));
    }

    [Fact]
    public void The_exit_status_is_kept_with_the_mark_that_reported_it()
    {
        var t = Fresh();
        t.Write(Mark("D;7"));

        Assert.Equal(7, MarksOn(t, 0)[0].ExitCode);
    }

    /// <summary>
    /// A bare D reports nothing, which is not the same as reporting success — cmd.exe cannot read
    /// the previous status from its prompt and always sends one.
    /// </summary>
    [Fact]
    public void A_bare_finish_reports_no_status_rather_than_zero()
    {
        var t = Fresh();
        t.Write(Mark("D"));

        Assert.Null(MarksOn(t, 0)[0].ExitCode);
    }

    /// <summary>
    /// Erasing the line does NOT take the mark with it.
    /// </summary>
    /// <remarks>
    /// The one that decides whether this works at all. A mark records a position in the history, not
    /// anything about the content there — and a shell redrawing its prompt with EL, which is most of
    /// them, would otherwise destroy the A mark it had just emitted, a moment before the prompt it
    /// marks is even printed.
    /// </remarks>
    [Fact]
    public void Erasing_the_line_leaves_the_mark_alone()
    {
        var t = Fresh();
        t.Write(Mark("A"));
        t.Write($"\r{Esc}[K$ ");

        Assert.Single(MarksOn(t, 0));
        Assert.Equal(ShellIntegrationMark.PromptStart, MarksOn(t, 0)[0].Kind);
    }

    [Fact]
    public void Reflow_moves_a_mark_to_the_row_and_column_owning_its_position()
    {
        var t = Fresh(cols: 10, rows: 5);
        t.Write("0123456789AB" + Mark("A") + "CD");
        t.Write($"{Esc}[5;1H");

        t.Resize(20, 5);

        Assert.Empty(MarksOn(t, 1));
        var mark = Assert.Single(MarksOn(t, 0));
        Assert.Equal(12, mark.Column);
        Assert.Equal(ShellIntegrationMark.PromptStart, mark.Kind);
    }

    /// <summary>A recycled line is a new line: the ring hands back the object it is about to drop.</summary>
    [Fact]
    public void A_line_reused_by_the_ring_carries_no_marks_over()
    {
        var t = new Terminal(new TerminalOptions { Cols = 20, Rows = 3, Scrollback = 2 });
        t.Write(Mark("A") + "prompt\r\n");

        for (var i = 0; i < 20; i++)
            t.Write($"line {i}\r\n");

        for (var i = 0; i < t.Buffer.Lines.Length; i++)
            Assert.False(t.Buffer.Lines[i]?.HasMarks ?? false,
                         $"row {i} kept a mark from a line the ring had dropped");
    }

    // ---- what the marks are for ------------------------------------------------------------

    [Fact]
    public void Jumping_back_walks_through_the_prompts()
    {
        var t = Fresh(rows: 10);
        for (var i = 0; i < 3; i++)
            t.Write(Mark("A") + $"$ cmd{i}\r\noutput\r\n");

        var from = t.Buffer.Lines.Length;
        var found = new List<int>();
        while (t.TryFindPreviousPrompt(from, out var row))
        {
            found.Add(row);
            from = row;
        }

        Assert.Equal(3, found.Count);
        Assert.Equal(found.OrderByDescending(r => r), found);
    }

    [Fact]
    public void Jumping_forward_walks_the_other_way()
    {
        var t = Fresh(rows: 10);
        for (var i = 0; i < 3; i++)
            t.Write(Mark("A") + $"$ cmd{i}\r\noutput\r\n");

        Assert.True(t.TryFindNextPrompt(-1, out var first));
        Assert.True(t.TryFindNextPrompt(first, out var second));
        Assert.True(second > first, "the search must be strictly below the row given");
    }

    [Fact]
    public void With_no_prompts_there_is_nothing_to_jump_to()
    {
        var t = Fresh();
        t.Write("just some output\r\n");

        Assert.False(t.TryFindPreviousPrompt(t.Buffer.Lines.Length, out _));
        Assert.False(t.TryFindNextPrompt(-1, out _));
    }
}
