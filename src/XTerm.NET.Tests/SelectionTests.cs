using XTerm.Options;
using XTerm.Selection;

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
    public void SelectionText_ClampsNegativeColumns()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 10, Scrollback = 20 });
        terminal.Write("alpha");

        terminal.Selection.StartSelection(-3, 0);
        terminal.Selection.UpdateSelection(4, 0);
        terminal.Selection.EndSelection();

        Assert.Equal("alpha", terminal.Selection.GetSelectionText());
    }

    [Fact]
    public void SelectionText_ClampsColumnsPastRightEdge()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 10, Scrollback = 20 });
        terminal.Write("alpha");

        terminal.Selection.StartSelection(0, 0);
        terminal.Selection.UpdateSelection(30, 0);
        terminal.Selection.EndSelection();

        Assert.StartsWith("alpha", terminal.Selection.GetSelectionText());
    }

    [Fact]
    public void SelectionText_ReturnsEmpty_WhenTerminalHasNoColumns()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 0, Scrollback = 20 });

        terminal.Selection.StartSelection(0, 0);
        terminal.Selection.UpdateSelection(0, 0);
        terminal.Selection.EndSelection();

        Assert.Equal(string.Empty, terminal.Selection.GetSelectionText());
    }
    
    public void SelectionText_UsesLineFeedLineEndings()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 80, Scrollback = 20 });
        terminal.Write("alpha\r\nbeta\r\ngamma");

        terminal.Selection.StartSelection(0, 0);
        terminal.Selection.UpdateSelection(4, 2);
        terminal.Selection.EndSelection();

        var selectedText = terminal.Selection.GetSelectionText();

        Assert.DoesNotContain("\r", selectedText);
        Assert.Equal(2, selectedText.Count(ch => ch == '\n'));
        Assert.StartsWith("alpha", selectedText);
        Assert.Contains("\nbeta", selectedText);
        Assert.EndsWith("gamma", selectedText);
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

    // ---------------------------------------------------------------- bounds

    [Fact]
    public void TryGetSelection_ReportsNothing_WhenNothingIsSelected()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 5, Cols = 80 });

        Assert.False(terminal.Selection.TryGetSelection(out var range));
        Assert.Equal(default, range);
    }

    /// <summary>
    /// The point of the type: a selection dragged BACKWARDS still reports its ends in order.
    /// </summary>
    /// <remarks>
    /// The two ends are stored in the order the user dragged them, so every caller that wanted to
    /// know what was selected had to know to swap them. This is that comparison, done once.
    /// </remarks>
    [Fact]
    public void TryGetSelection_OrdersTheEnds_HoweverTheDragWent()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 5, Cols = 80, Scrollback = 100 });
        for (int i = 0; i < 10; i++)
            terminal.WriteLine($"Line{i:00}");

        terminal.ScrollToTop();

        terminal.Selection.StartSelection(6, 3);
        terminal.Selection.UpdateSelection(2, 1);
        terminal.Selection.EndSelection();

        Assert.True(terminal.Selection.TryGetSelection(out var backwards));

        terminal.Selection.ClearSelection();
        terminal.Selection.StartSelection(2, 1);
        terminal.Selection.UpdateSelection(6, 3);
        terminal.Selection.EndSelection();

        Assert.True(terminal.Selection.TryGetSelection(out var forwards));

        Assert.Equal(forwards, backwards);
        Assert.True(backwards.StartY < backwards.EndY);
    }

    [Fact]
    public void TryGetSelection_ReportsAbsoluteRows_SoScrollingDoesNotMoveIt()
    {
        // Absolute rows are what makes a range outlive the viewport it was taken in -- the same
        // property IsCellSelected has always had, now visible to a caller.
        var terminal = new Terminal(new TerminalOptions { Rows = 5, Cols = 80, Scrollback = 100 });
        for (int i = 0; i < 20; i++)
            terminal.WriteLine($"Line{i:00}");

        terminal.ScrollToTop();
        terminal.Selection.StartSelection(4, 2);
        terminal.Selection.UpdateSelection(5, 2);
        terminal.Selection.EndSelection();

        Assert.True(terminal.Selection.TryGetSelection(out var before));

        terminal.ScrollLines(1);

        Assert.True(terminal.Selection.TryGetSelection(out var after));
        Assert.Equal(before, after);
    }

    // -------------------------------------------------------------- row spans

    [Fact]
    public void TryGetRowSpan_CoversTheWholeRow_BetweenTheEnds()
    {
        var range = new SelectionRange(StartX: 5, StartY: 2, EndX: 3, EndY: 6);

        Assert.True(range.TryGetRowSpan(4, cols: 80, out var startX, out var endX));
        Assert.Equal(0, startX);
        Assert.Equal(79, endX);
    }

    [Fact]
    public void TryGetRowSpan_StartsAndEndsWhereTheSelectionDoes()
    {
        var range = new SelectionRange(StartX: 5, StartY: 2, EndX: 3, EndY: 6);

        Assert.True(range.TryGetRowSpan(2, cols: 80, out var firstStart, out var firstEnd));
        Assert.Equal(5, firstStart);
        Assert.Equal(79, firstEnd);

        Assert.True(range.TryGetRowSpan(6, cols: 80, out var lastStart, out var lastEnd));
        Assert.Equal(0, lastStart);
        Assert.Equal(3, lastEnd);
    }

    [Fact]
    public void TryGetRowSpan_IsOneSpan_WhenTheSelectionIsWithinOneRow()
    {
        var range = new SelectionRange(StartX: 10, StartY: 3, EndX: 20, EndY: 3);

        Assert.True(range.TryGetRowSpan(3, cols: 80, out var startX, out var endX));
        Assert.Equal(10, startX);
        Assert.Equal(20, endX);
    }

    [Fact]
    public void TryGetRowSpan_DeclinesRowsOutsideTheSelection()
    {
        // The reason a renderer wants this: a row it can skip costs two comparisons rather than one
        // question per column.
        var range = new SelectionRange(StartX: 5, StartY: 2, EndX: 3, EndY: 6);

        Assert.False(range.TryGetRowSpan(1, cols: 80, out _, out _));
        Assert.False(range.TryGetRowSpan(7, cols: 80, out _, out _));
    }

    [Fact]
    public void TryGetRowSpan_ClampsToAGridThatHasSinceNarrowed()
    {
        // A range outlives the width it was made at. Asked about a narrower grid it reports the
        // columns that still exist, and declines the row when none of them do.
        var range = new SelectionRange(StartX: 70, StartY: 2, EndX: 75, EndY: 2);

        Assert.True(range.TryGetRowSpan(2, cols: 80, out var wideStart, out var wideEnd));
        Assert.Equal(70, wideStart);
        Assert.Equal(75, wideEnd);

        Assert.True(range.TryGetRowSpan(2, cols: 40, out var narrowStart, out var narrowEnd));
        Assert.Equal(39, narrowStart);
        Assert.Equal(39, narrowEnd);

        Assert.False(range.TryGetRowSpan(2, cols: 0, out _, out _));
    }

    /// <summary>
    /// The bounds and the per-cell question have to give the same answers, because they are now two
    /// views of one rule and nothing else would notice them drifting.
    /// </summary>
    [Fact]
    public void TryGetRowSpan_AgreesWithIsCellSelected_AcrossTheGrid()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 5, Cols = 20, Scrollback = 100 });
        for (int i = 0; i < 10; i++)
            terminal.WriteLine($"Line{i:00}");

        terminal.ScrollToTop();
        terminal.Selection.StartSelection(7, 1);
        terminal.Selection.UpdateSelection(4, 3);
        terminal.Selection.EndSelection();

        Assert.True(terminal.Selection.TryGetSelection(out var range));

        for (int row = 0; row < terminal.Rows; row++)
        {
            var absolute = terminal.Buffer.YDisp + row;
            var hasSpan = range.TryGetRowSpan(absolute, terminal.Cols, out var startX, out var endX);

            for (int x = 0; x < terminal.Cols; x++)
            {
                var fromSpan = hasSpan && x >= startX && x <= endX;
                Assert.Equal(terminal.Selection.IsCellSelected(x, row), fromSpan);
            }
        }
    }
}
