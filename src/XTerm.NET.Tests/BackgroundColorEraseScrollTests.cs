using XTerm.Options;

namespace XTerm.Tests;

public class BackgroundColorEraseScrollTests
{
    private const string Esc = "\u001b";

    [Fact]
    public void Scrolling_up_fills_the_new_row_with_the_current_background_only()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 5, Rows = 3 });
        terminal.Write($"{Esc}[1;31;44m{Esc}[3;1H\n");

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + 2]!;
        Assert.All(Enumerable.Range(0, line.Length), column =>
        {
            Assert.Equal(4, line[column].Attributes.GetBgColor());
            Assert.Equal(256, line[column].Attributes.GetFgColor());
            Assert.False(line[column].Attributes.IsBold());
        });
    }

    [Fact]
    public void Reverse_scrolling_uses_the_current_background()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 5, Rows = 3 });
        terminal.Write($"{Esc}[42m{Esc}[1;1H{Esc}M");

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        Assert.All(Enumerable.Range(0, line.Length), column =>
            Assert.Equal(2, line[column].Attributes.GetBgColor()));
    }

    [Fact]
    public void A_recycled_scrollback_line_is_reset_with_the_current_background()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 5, Rows = 2, Scrollback = 1 });
        terminal.Write($"{Esc}[46m{Esc}[2;1H\n\n");

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + 1]!;
        Assert.All(Enumerable.Range(0, line.Length), column =>
            Assert.Equal(6, line[column].Attributes.GetBgColor()));
    }

    [Fact]
    public void A_narrow_margin_scroll_fills_only_the_exposed_box_with_the_current_background()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 6, Rows = 3 });
        terminal.Write($"{Esc}[?69h{Esc}[2;5s{Esc}[44m{Esc}[3;2H\n");

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + 2]!;
        Assert.Equal(257, line[0].Attributes.GetBgColor());
        Assert.All(Enumerable.Range(1, 4), column =>
            Assert.Equal(4, line[column].Attributes.GetBgColor()));
        Assert.Equal(257, line[5].Attributes.GetBgColor());
    }

    [Fact]
    public void Alternate_screen_scrolling_pulls_the_same_current_background()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 5, Rows = 3 });
        terminal.Write($"{Esc}[45m{Esc}[?1049h{Esc}[3;1H\n");

        var line = terminal.Buffer.Lines[2]!;
        Assert.All(Enumerable.Range(0, line.Length), column =>
            Assert.Equal(5, line[column].Attributes.GetBgColor()));
    }

    [Theory]
    [InlineData("\u001b[2K", 0, 0)]
    [InlineData("\u001b[2J", 0, 0)]
    [InlineData("\u001b[3X", 0, 0)]
    [InlineData("\u001b[2@", 0, 0)]
    [InlineData("\u001b[2P", 0, 4)]
    [InlineData("\u001b[L", 0, 0)]
    [InlineData("\u001b[M", 2, 0)]
    public void Every_BCE_operation_keeps_only_the_current_background(
        string operation, int row, int column)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 5, Rows = 3 });
        terminal.Write($"{Esc}[1;31;44m{operation}");

        var attributes = terminal.Buffer.Lines[terminal.Buffer.YBase + row]![column].Attributes;
        Assert.Equal(4, attributes.GetBgColor());
        Assert.Equal(256, attributes.GetFgColor());
        Assert.False(attributes.IsBold());
    }

    [Fact]
    public void Reset_clears_the_background_used_by_later_scrolls()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 5, Rows = 3 });
        terminal.Write($"{Esc}[44m");

        terminal.Reset();
        terminal.Write($"{Esc}[3;1H\n");

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + 2]!;
        Assert.All(Enumerable.Range(0, line.Length), column =>
            Assert.Equal(257, line[column].Attributes.GetBgColor()));
    }
}
