using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// XTSHIFTESCAPE (CSI &gt; Ps s). Shift is the host's override of mouse tracking; an application
/// that uses Shift as a modifier of its own asks for it back with CSI &gt; 1 s.
/// </summary>
public class ShiftEscapeTests
{
    private const char Esc = (char)0x1b;

    private static Terminal Fresh() => new(new TerminalOptions { Cols = 80, Rows = 24 });

    [Fact]
    public void Shift_belongs_to_the_host_by_default()
    {
        Assert.False(Fresh().ShiftEscape);
    }

    [Fact]
    public void Csi_gt_1_s_hands_shift_to_the_application()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[>1s");
        Assert.True(terminal.ShiftEscape);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    public void Csi_gt_0_s_hands_shift_back_to_the_host(string ps)
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[>1s");
        terminal.Write($"{Esc}[>{ps}s");
        Assert.False(terminal.ShiftEscape);
    }

    [Fact]
    public void An_unknown_value_changes_nothing()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[>1s");
        terminal.Write($"{Esc}[>7s");
        Assert.True(terminal.ShiftEscape);
    }

    [Fact]
    public void Ris_hands_shift_back_to_the_host()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[>1s");
        terminal.Write($"{Esc}c");
        Assert.False(terminal.ShiftEscape);
    }

    /// <summary>
    /// The private marker is what tells it apart from SCOSC (CSI s). Without it in the command map
    /// the sequence was either ignored or, worse, saved the cursor.
    /// </summary>
    [Fact]
    public void It_is_not_save_cursor()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[5;5H");
        terminal.Write($"{Esc}[>1s");
        terminal.Write($"{Esc}[1;1H");
        terminal.Write($"{Esc}[u");   // SCORC: restores whatever CSI s last saved, which is home
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }
}
