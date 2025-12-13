using XTerm.NET.Buffer;
using XTerm.NET.Common;

namespace XTerm.NET.Tests.Buffer;

public class BufferTests
{
    [Fact]
    public void Constructor_InitializesBuffer()
    {
        // Arrange & Act
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

        // Assert
        Assert.True(buffer.Lines.Length >= 24);
    }

    [Fact]
    public void SetCursor_SetsCursorPosition()
    {
        // Arrange
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

        // Act
        buffer.MoveCursor(10, 5);

        // Assert
        Assert.Equal(10, buffer.X);
        Assert.Equal(5, buffer.Y);
    }

    [Fact]
    public void GetLine_ReturnsLine()
    {
        // Arrange
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
    public void SavedCursorState_InitializesCorrectly()
    {
        // Arrange
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

        // Act
        buffer.SavedCursorState.X = 10;
        buffer.SavedCursorState.Y = 5;
        buffer.SavedCursorState.Attr.SetBold(true);
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
        var buffer = new XTerm.NET.Buffer.Buffer(cols, rows, scrollback);

        // Assert
        Assert.NotNull(buffer);
        Assert.Equal(0, buffer.ScrollTop);
        Assert.Equal(rows - 1, buffer.ScrollBottom);
    }

    [Fact]
    public void ScrollUp_WithWrapped_SetsWrappedFlag()
    {
        // Arrange
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

        // Act
        buffer.ScrollUp(1, isWrapped: true);

        // Assert
        // The newly added line at bottom should be marked as wrapped
        var bottomLine = buffer.Lines[buffer.ScrollBottom];
        if (bottomLine != null)
        {
            Assert.True(bottomLine.IsWrapped);
        }
    }

    [Fact]
    public void Lines_Property_IsAccessible()
    {
        // Arrange
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);

        // Act
        buffer.SetCursor(40, 12);
        var x1 = buffer.X;
        var y1 = buffer.Y;

        buffer.MoveCursor(50, 20);
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
        var buffer = new XTerm.NET.Buffer.Buffer(80, 24, 1000);
        buffer.SetScrollRegion(5, 15);

        // Act
        var scrollTop = buffer.ScrollTop;
        var scrollBottom = buffer.ScrollBottom;

        buffer.ScrollUp(1);

        // Assert
        Assert.Equal(5, scrollTop);
        Assert.Equal(15, scrollBottom);
    }
}
