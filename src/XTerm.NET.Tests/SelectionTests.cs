using XTerm.Options;

namespace XTerm.NET.Tests;

public class SelectionTests
{
    [Fact]
    public void SelectionText_RemainsAnchored_WhenViewportScrolls()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 5, Cols = 80, Scrollback = 100 });

        for (int i = 0; i < 20; i++)
        {
            terminal.WriteLine($"Line{i:00}");
        }

        terminal.ScrollToTop();
        terminal.Selection.StartSelection(4, 2);
        terminal.Selection.UpdateSelection(5, 2);
        terminal.Selection.EndSelection();

        Assert.Equal("02", terminal.Selection.GetSelectionText());

        terminal.ScrollLines(1);

        Assert.Equal("02", terminal.Selection.GetSelectionText());
    }

    [Fact]
    public void IsCellSelected_TracksBufferSelectionAcrossViewportScroll()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 5, Cols = 80, Scrollback = 100 });

        for (int i = 0; i < 20; i++)
        {
            terminal.WriteLine($"Line{i:00}");
        }

        terminal.ScrollToTop();
        terminal.Selection.StartSelection(4, 2);
        terminal.Selection.UpdateSelection(5, 2);
        terminal.Selection.EndSelection();

        Assert.True(terminal.Selection.IsCellSelected(4, 2));
        Assert.False(terminal.Selection.IsCellSelected(4, 1));

        terminal.ScrollLines(1);

        Assert.True(terminal.Selection.IsCellSelected(4, 1));
        Assert.False(terminal.Selection.IsCellSelected(4, 2));
    }

    [Fact]
    public void SelectAll_IncludesScrollback_NotJustViewport()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 80, Scrollback = 20 });

        for (int i = 0; i < 8; i++)
        {
            terminal.WriteLine($"Line{i}");
        }

        terminal.Selection.SelectAll();

        var selectedText = terminal.Selection.GetSelectionText();

        Assert.Contains("Line0", selectedText);
        Assert.Contains("Line7", selectedText);
    }

    [Fact]
    public void Selection_IsCleared_WhenTrimRemovesSelectedLines()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 80, Scrollback = 2 });

        for (int i = 0; i < 5; i++)
        {
            terminal.WriteLine($"Line{i}");
        }

        terminal.ScrollToTop();
        var initialTopLine = terminal.GetVisibleLines()[0];
        var expectedSelectedText = initialTopLine[4].ToString();
        terminal.Selection.StartSelection(4, 0);
        terminal.Selection.UpdateSelection(4, 0);
        terminal.Selection.EndSelection();

        Assert.Equal(expectedSelectedText, terminal.Selection.GetSelectionText());

        for (int i = 5; i < 10; i++)
        {
            terminal.WriteLine($"Line{i}");
        }

        Assert.False(terminal.Selection.HasSelection);
        Assert.Equal(string.Empty, terminal.Selection.GetSelectionText());
    }
}
