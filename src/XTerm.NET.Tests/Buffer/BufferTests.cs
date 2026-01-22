using XTerm.Buffer;
using XTerm.Common;

namespace XTerm.Tests.Buffer;

public class BufferTests
{
    [Fact]
    public void Constructor_InitializesBuffer()
    {
        // Arrange & Act
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Assert
        Assert.Equal(0, buffer.YDisp);
        Assert.Equal(0, buffer.YBase);
        Assert.Equal(0, buffer.Y);
        Assert.Equal(0, buffer.X);
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(23, buffer.ScrollBottom);
        Assert.NotNull(buffer.Lines);
        Assert.NotNull(buffer.SavedCursorState);
    }

    [Fact]
    public void Constructor_CreatesLinesForRows()
    {
        // Arrange & Act
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Assert
        Assert.True(buffer.Lines.Length >= 24);
    }

    [Fact]
    public void SetCursor_SetsCursorPosition()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.SetCursor(10, 5);

        // Assert
        Assert.Equal(10, buffer.X);
        Assert.Equal(5, buffer.Y);
    }

    [Fact]
    public void SetCursor_ClampsToBufferBounds()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.SetCursor(-5, -3);

        // Assert
        Assert.Equal(0, buffer.X);
        Assert.Equal(0, buffer.Y);

        // Act
        buffer.SetCursor(100, 50);

        // Assert
        Assert.Equal(79, buffer.X);
        Assert.Equal(23, buffer.Y);
    }

    [Fact]
    public void MoveCursor_MovesCursorWithoutClamping()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.SetCursorRaw(10, 5);

        // Assert
        Assert.Equal(10, buffer.X);
        Assert.Equal(5, buffer.Y);
    }

    [Fact]
    public void GetLine_ReturnsLine()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        var line = buffer.GetLine(0);

        // Assert
        Assert.NotNull(line);
        Assert.Equal(80, line.Length);
    }

    [Fact]
    public void GetBlankLine_ReturnsBlankLine()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        var attr = AttributeData.Default;

        // Act
        var line = buffer.GetBlankLine(attr);

        // Assert
        Assert.NotNull(line);
        Assert.Equal(80, line.Length);
        Assert.False(line.IsWrapped);
    }

    [Fact]
    public void GetBlankLine_WithWrapped_SetsWrapped()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        var attr = AttributeData.Default;

        // Act
        var line = buffer.GetBlankLine(attr, isWrapped: true);

        // Assert
        Assert.True(line.IsWrapped);
    }

    [Fact]
    public void ScrollUp_ScrollsBuffer()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        var initialYBase = buffer.YBase;

        // Act
        buffer.ScrollUp(1);

        // Assert
        Assert.True(buffer.YBase >= initialYBase);
    }

    [Fact]
    public void ScrollUp_MultipleLines_ScrollsMultipleTimes()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        var initialYBase = buffer.YBase;

        // Act
        buffer.ScrollUp(3);

        // Assert
        Assert.True(buffer.YBase >= initialYBase);
    }

    [Fact]
    public void ScrollDown_ScrollsBufferDown()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(3); // Scroll up first to have content to scroll down

        // Act
        buffer.ScrollDown(1);

        // Assert - Should have lines in buffer
        Assert.NotNull(buffer.Lines);
    }

    [Fact]
    public void ScrollDisp_ScrollsDisplay()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(5); // Create scrollback
        var initialYDisp = buffer.YDisp;

        // Act
        buffer.ScrollDisp(2);

        // Assert
        Assert.True(buffer.YDisp >= initialYDisp);
    }

    [Fact]
    public void ScrollDisp_ClampsToYBase()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(3);

        // Act
        buffer.ScrollDisp(100); // Try to scroll way beyond

        // Assert
        Assert.Equal(buffer.YBase, buffer.YDisp);
    }

    [Fact]
    public void ScrollDisp_ClampsToZero()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(3);
        buffer.ScrollDisp(2);

        // Act
        buffer.ScrollDisp(-100); // Try to scroll way before

        // Assert
        Assert.Equal(0, buffer.YDisp);
    }

    [Fact]
    public void ScrollToBottom_ScrollsToBottom()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(5);
        buffer.ScrollDisp(-3); // Scroll up in display

        // Act
        buffer.ScrollToBottom();

        // Assert
        Assert.Equal(buffer.YBase, buffer.YDisp);
    }

    [Fact]
    public void ScrollToTop_ScrollsToTop()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(5);

        // Act
        buffer.ScrollToTop();

        // Assert
        Assert.Equal(0, buffer.YDisp);
    }

    [Fact]
    public void SetScrollRegion_SetsRegion()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.SetScrollRegion(5, 20);

        // Assert
        Assert.Equal(5, buffer.ScrollTop);
        Assert.Equal(20, buffer.ScrollBottom);
    }

    [Fact]
    public void SetScrollRegion_ClampsToBufferBounds()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.SetScrollRegion(-5, 100);

        // Assert
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(23, buffer.ScrollBottom);
    }

    [Fact]
    public void SetScrollRegion_TopGreaterThanBottom_ClampsCorrectly()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.SetScrollRegion(20, 5);

        // Assert
        Assert.Equal(20, buffer.ScrollTop);
        Assert.True(buffer.ScrollBottom >= buffer.ScrollTop);
    }

    [Fact]
    public void ResetScrollRegion_ResetsToFullScreen()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.SetScrollRegion(5, 20);

        // Act
        buffer.ResetScrollRegion();

        // Assert
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(23, buffer.ScrollBottom);
    }

    [Fact]
    public void GetAbsoluteY_ReturnsAbsolutePosition()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(5); // YBase becomes 5

        // Act
        var absolute = buffer.GetAbsoluteY(10);

        // Assert
        Assert.Equal(buffer.YBase + 10, absolute);
    }

    [Fact]
    public void Resize_ResizesBuffer()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.Resize(100, 30);

        // Assert
        // Lines should exist and be accessible
        for (int i = 0; i < 30; i++)
        {
            var line = buffer.Lines[i];
            Assert.NotNull(line);
        }
    }

    [Fact]
    public void Resize_GrowsColumns_UpdatesLineLengths()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.Resize(120, 24);

        // Assert - every line should have the new column count
        for (int i = 0; i < buffer.Lines.Length; i++)
        {
            var line = buffer.Lines[i];
            Assert.NotNull(line);
            Assert.Equal(120, line!.Length);
        }
    }

    [Fact]
    public void Resize_ShrinksColumns_UpdatesLineLengths()
    {
        // Arrange
        var buffer = new TerminalBuffer(120, 24, 1000);

        // Act
        buffer.Resize(60, 24);

        // Assert - every line should have the new column count
        for (int i = 0; i < buffer.Lines.Length; i++)
        {
            var line = buffer.Lines[i];
            Assert.NotNull(line);
            Assert.Equal(60, line!.Length);
        }
    }

    [Fact]
    public void SavedCursorState_InitializesCorrectly()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Assert
        Assert.NotNull(buffer.SavedCursorState);
        Assert.Equal(0, buffer.SavedCursorState.X);
        Assert.Equal(0, buffer.SavedCursorState.Y);
        Assert.Equal(AttributeData.Default, buffer.SavedCursorState.Attr);
        Assert.Equal(CharsetMode.G0, buffer.SavedCursorState.Charset);
    }

    [Fact]
    public void SavedCursorState_CanBeModified()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.SavedCursorState.X = 10;
        buffer.SavedCursorState.Y = 5;
        
        // To modify a struct field, we need to get it, modify it, and set it back
        var attr = buffer.SavedCursorState.Attr;
        attr.SetBold(true);
        buffer.SavedCursorState.Attr = attr;
        
        buffer.SavedCursorState.Charset = CharsetMode.G1;

        // Assert
        Assert.Equal(10, buffer.SavedCursorState.X);
        Assert.Equal(5, buffer.SavedCursorState.Y);
        Assert.True(buffer.SavedCursorState.Attr.IsBold());
        Assert.Equal(CharsetMode.G1, buffer.SavedCursorState.Charset);
    }

    [Theory]
    [InlineData(20, 10, 0)]
    [InlineData(40, 20, 500)]
    [InlineData(100, 50, 2000)]
    public void Constructor_VariousSizes_WorksCorrectly(int cols, int rows, int scrollback)
    {
        // Act
        var buffer = new TerminalBuffer(cols, rows, scrollback);

        // Assert
        Assert.NotNull(buffer);
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(rows - 1, buffer.ScrollBottom);
    }

    [Fact]
    public void ScrollUp_WithWrapped_SetsWrappedFlag()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.ScrollUp(1, isWrapped: true);

        // Assert
        // The newly added line is at the bottom of the active screen area.
        // After scroll, the new line is at position YBase + Rows - 1 in the buffer.
        // Since YBase becomes 1 after scroll (when scrollTop is 0), the new line is at index 24 (1 + 24 - 1).
        var lastActiveRow = buffer.YBase + buffer.Rows - 1;
        var bottomLine = buffer.Lines[lastActiveRow];
        Assert.NotNull(bottomLine);
        Assert.True(bottomLine.IsWrapped);
    }

    [Fact]
    public void Lines_Property_IsAccessible()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        var lines = buffer.Lines;

        // Assert
        Assert.NotNull(lines);
        Assert.True(lines.Length > 0);
    }

    [Fact]
    public void MultipleScrollOperations_MaintainConsistency()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.ScrollUp(2);
        buffer.ScrollDown(1);
        buffer.ScrollUp(3);
        buffer.ScrollDisp(-2);
        buffer.ScrollToBottom();

        // Assert
        Assert.Equal(buffer.YBase, buffer.YDisp);
        Assert.True(buffer.YBase >= 0);
    }

    [Fact]
    public void CursorMovement_ComplexScenario()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.SetCursor(40, 12);
        var x1 = buffer.X;
        var y1 = buffer.Y;

        buffer.SetCursorRaw(50, 20);
        var x2 = buffer.X;
        var y2 = buffer.Y;

        buffer.SetCursor(0, 0);
        var x3 = buffer.X;
        var y3 = buffer.Y;

        // Assert
        Assert.Equal(40, x1);
        Assert.Equal(12, y1);
        Assert.Equal(50, x2);
        Assert.Equal(20, y2);
        Assert.Equal(0, x3);
        Assert.Equal(0, y3);
    }

    [Fact]
    public void ScrollRegion_AffectsScrolling()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.SetScrollRegion(5, 15);

        // Act
        var scrollTop = buffer.ScrollTop;
        var scrollBottom = buffer.ScrollBottom;

        buffer.ScrollUp(1);

        // Assert
        Assert.Equal(5, scrollTop);
        Assert.Equal(15, scrollBottom);
    }

    #region Scrolling Beyond Viewport Tests

    [Fact]
    public void ScrollUp_BeyondViewport_IncrementsYBase()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        Assert.Equal(0, buffer.YBase);

        // Act - Scroll up 10 times (simulating 10 new lines at bottom of screen)
        buffer.ScrollUp(10);

        // Assert
        Assert.Equal(10, buffer.YBase);
        Assert.Equal(10, buffer.YDisp); // Should auto-scroll to bottom
    }

    [Fact]
    public void ScrollUp_YDispFollowsYBase_WhenAtBottom()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.ScrollUp(5);

        // Assert - yDisp should follow yBase when user hasn't scrolled up
        Assert.Equal(buffer.YBase, buffer.YDisp);
        Assert.True(buffer.IsAtBottom);
    }

    [Fact]
    public void ScrollUp_YDispDoesNotFollow_WhenScrolledUp()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(10); // Create some scrollback
        buffer.ScrollToTop(); // Scroll viewport to top

        // Act
        buffer.ScrollUp(5); // More content added

        // Assert - yDisp should stay at 0 (user scrolled up)
        Assert.Equal(0, buffer.YDisp);
        Assert.Equal(15, buffer.YBase);
        Assert.False(buffer.IsAtBottom);
    }

    [Fact]
    public void ScrollUp_BufferLengthGrows_UntilMaxCapacity()
    {
        // Arrange - Small buffer with 10 row viewport and 20 scrollback (30 total)
        var buffer = new TerminalBuffer(80, 10, 20);
        Assert.Equal(10, buffer.Lines.Length); // Initially just viewport rows

        // Act - Scroll up 15 times
        buffer.ScrollUp(15);

        // Assert - Buffer should have grown
        Assert.Equal(25, buffer.Lines.Length); // 10 initial + 15 scrolled
        Assert.Equal(15, buffer.YBase);
    }

    [Fact]
    public void ScrollUp_AtMaxCapacity_RecyclesOldestLines()
    {
        // Arrange - Small buffer: 5 rows viewport, 5 scrollback = 10 max
        var buffer = new TerminalBuffer(80, 5, 5);
        
        // Fill buffer to capacity
        buffer.ScrollUp(5); // Now at 10 lines (max)
        Assert.Equal(10, buffer.Lines.Length);
        Assert.Equal(5, buffer.YBase);

        // Act - Scroll more, should recycle
        buffer.ScrollUp(3);

        // Assert - Length should stay at max, yBase should still be 5 (recycled)
        Assert.Equal(10, buffer.Lines.Length);
        Assert.Equal(5, buffer.YBase); // Stays at 5 because oldest lines are recycled
    }

    [Fact]
    public void ScrollUp_ContentPlacedCorrectly_InActiveArea()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 5, 100);
        
        // Write content to first line
        var line0 = buffer.Lines[0];
        var cell = new BufferCell("A", 1, AttributeData.Default);
        line0?.SetCell(0, ref cell);

        // Act - Scroll up once
        buffer.ScrollUp(1);

        // Assert - Original line 0 is now in scrollback (at index 0)
        // Active area starts at yBase (1), new blank line is at yBase + rows - 1 (5)
        var scrollbackLine = buffer.Lines[0];
        Assert.NotNull(scrollbackLine);
        Assert.Equal("A", scrollbackLine[0].Content);

        // The active area's last line should be blank
        var lastActiveLine = buffer.Lines[buffer.YBase + buffer.Rows - 1];
        Assert.NotNull(lastActiveLine);
        Assert.True(lastActiveLine[0].IsSpace() || lastActiveLine[0].Content == " ");
    }

    [Fact]
    public void ScrollUp_PreservesScrollbackContent()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 5, 100);
        
        // Mark each initial line with a unique identifier
        for (int i = 0; i < 5; i++)
        {
            var line = buffer.Lines[i];
            var cell = new BufferCell(((char)('A' + i)).ToString(), 1, AttributeData.Default);
            line.SetCell(0, ref cell);
        }

        // Act - Scroll up 3 times
        buffer.ScrollUp(3);

        // Assert - First 3 original lines should be in scrollback
        Assert.Equal("A", buffer.Lines[0]?[0].Content);
        Assert.Equal("B", buffer.Lines[1]?[0].Content);
        Assert.Equal("C", buffer.Lines[2]?[0].Content);
        
        // Lines D and E should now be in active area (at yBase + 0 and yBase + 1)
        Assert.Equal("D", buffer.Lines[buffer.YBase]?[0].Content);
        Assert.Equal("E", buffer.Lines[buffer.YBase + 1]?[0].Content);
    }

    [Fact]
    public void ScrollToTop_ShowsScrollbackContent()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 5, 100);
        
        // Mark line 0
        var cell = new BufferCell("X", 1, AttributeData.Default);
        buffer.Lines[0]?.SetCell(0, ref cell);
        
        // Scroll up to create scrollback
        buffer.ScrollUp(10);
        Assert.Equal(10, buffer.YBase);
        Assert.Equal(10, buffer.YDisp);

        // Act
        buffer.ScrollToTop();

        // Assert
        Assert.Equal(0, buffer.YDisp);
        // Line at yDisp (0) should be the original line with "X"
        var visibleLine = buffer.Lines[buffer.YDisp];
        Assert.Equal("X", visibleLine?[0].Content);
    }

    [Fact]
    public void ScrollToBottom_AfterScrollingUp_ReturnsToActiveArea()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 5, 100);
        buffer.ScrollUp(20);
        buffer.ScrollToTop();
        Assert.Equal(0, buffer.YDisp);

        // Act
        buffer.ScrollToBottom();

        // Assert
        Assert.Equal(buffer.YBase, buffer.YDisp);
        Assert.True(buffer.IsAtBottom);
    }

    [Fact]
    public void IsAtBottom_TrueInitially()
    {
        // Arrange & Act
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Assert
        Assert.True(buffer.IsAtBottom);
    }

    [Fact]
    public void IsAtBottom_TrueAfterScrollUp()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.ScrollUp(10);

        // Assert - Should still be at bottom (auto-followed)
        Assert.True(buffer.IsAtBottom);
    }

    [Fact]
    public void IsAtBottom_FalseAfterScrollToTop()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.ScrollUp(10);

        // Act
        buffer.ScrollToTop();

        // Assert
        Assert.False(buffer.IsAtBottom);
    }

    [Fact]
    public void ScrollLines_RelativeScrolling_Works()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 10, 100);
        buffer.ScrollUp(50); // Create lots of scrollback
        buffer.ScrollToTop();
        Assert.Equal(0, buffer.YDisp);

        // Act - Scroll down 25 lines
        buffer.ScrollLines(25);

        // Assert
        Assert.Equal(25, buffer.YDisp);

        // Act - Scroll up 10 lines
        buffer.ScrollLines(-10);

        // Assert
        Assert.Equal(15, buffer.YDisp);
    }

    [Fact]
    public void ScrollLines_ClampsToValidRange()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 10, 100);
        buffer.ScrollUp(20);

        // Act - Try to scroll way past bottom
        buffer.ScrollLines(1000);

        // Assert - Should be clamped to yBase
        Assert.Equal(buffer.YBase, buffer.YDisp);

        // Act - Try to scroll way past top
        buffer.ScrollLines(-1000);

        // Assert - Should be clamped to 0
        Assert.Equal(0, buffer.YDisp);
    }

    [Fact]
    public void ViewportY_Property_ReadsAndWritesYDisp()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 10, 100);
        buffer.ScrollUp(30);

        // Act & Assert - Read
        Assert.Equal(buffer.YDisp, buffer.ViewportY);

        // Act - Write
        buffer.ViewportY = 15;

        // Assert
        Assert.Equal(15, buffer.YDisp);
        Assert.Equal(15, buffer.ViewportY);
    }

    [Fact]
    public void ViewportY_ClampedToValidRange()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 10, 100);
        buffer.ScrollUp(20);

        // Act - Try to set beyond yBase
        buffer.ViewportY = 100;

        // Assert
        Assert.Equal(buffer.YBase, buffer.ViewportY);

        // Act - Try to set negative
        buffer.ViewportY = -10;

        // Assert
        Assert.Equal(0, buffer.ViewportY);
    }

    [Fact]
    public void GetAbsoluteY_CorrectAfterScrolling()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 10, 100);
        buffer.ScrollUp(25);

        // Act & Assert
        // GetAbsoluteY converts viewport-relative Y to buffer-absolute Y
        // For viewport row 0, absolute should be yBase + 0 = 25
        Assert.Equal(25, buffer.GetAbsoluteY(0));
        Assert.Equal(30, buffer.GetAbsoluteY(5));
        Assert.Equal(34, buffer.GetAbsoluteY(9)); // Last viewport row
    }

    [Fact]
    public void BufferLength_MatchesExpectedSize()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 10, 100);
        Assert.Equal(10, buffer.Length); // Initially just viewport rows

        // Act
        buffer.ScrollUp(50);

        // Assert - Should have grown to rows + scrollback used
        Assert.Equal(60, buffer.Length); // 10 initial + 50 scrolled
    }

    [Fact]
    public void LargeScrollback_HandlesCorrectly()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 10000);

        // Act - Simulate a lot of output
        buffer.ScrollUp(5000);

        // Assert
        Assert.Equal(5000, buffer.YBase);
        Assert.Equal(5000, buffer.YDisp);
        Assert.Equal(5024, buffer.Length); // 24 rows + 5000 scrollback
        Assert.True(buffer.IsAtBottom);
    }

    [Fact]
    public void ScrollUp_WithScrollRegion_DoesNotAffectYBase()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.SetScrollRegion(5, 15); // Scroll region in middle of screen

        // Act
        buffer.ScrollUp(3);

        // Assert - yBase should not change when scroll region is set
        Assert.Equal(0, buffer.YBase);
        Assert.Equal(0, buffer.YDisp);
    }

    [Fact]
    public void ScrollDown_WithScrollRegion_DoesNotAffectYBase()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.SetScrollRegion(5, 15);

        // Act
        buffer.ScrollDown(3);

        // Assert
        Assert.Equal(0, buffer.YBase);
        Assert.Equal(0, buffer.YDisp);
    }

    [Fact]
    public void Cols_And_Rows_Properties_ReturnCorrectValues()
    {
        // Arrange
        var buffer = new TerminalBuffer(100, 50, 500);

        // Assert
        Assert.Equal(100, buffer.Cols);
        Assert.Equal(50, buffer.Rows);
    }

    [Fact]
    public void Resize_UpdatesColsAndRows()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);

        // Act
        buffer.Resize(120, 40);

        // Assert
        Assert.Equal(120, buffer.Cols);
        Assert.Equal(40, buffer.Rows);
    }

    [Fact]
    public void Resize_AdjustsScrollBottom()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24, 1000);
        Assert.Equal(23, buffer.ScrollBottom);

        // Act
        buffer.Resize(80, 30);

        // Assert - ScrollBottom should be updated to new rows - 1
        Assert.Equal(29, buffer.ScrollBottom);
    }

    #endregion
}
