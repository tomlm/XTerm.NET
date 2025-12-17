using XTerm;
using XTerm.Options;

namespace XTerm.Tests;

public class TerminalTests
{
    [Fact]
    public void Constructor_InitializesTerminal()
    {
        // Arrange & Act
        var terminal = new Terminal();

        // Assert
        Assert.NotNull(terminal);
        Assert.NotNull(terminal.Options);
        Assert.NotNull(terminal.Buffer);
        Assert.Equal(80, terminal.Cols);
        Assert.Equal(24, terminal.Rows);
    }

    [Fact]
    public void Constructor_WithOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new TerminalOptions { Cols = 100, Rows = 30 };

        // Act
        var terminal = new Terminal(options);

        // Assert
        Assert.Equal(100, terminal.Cols);
        Assert.Equal(30, terminal.Rows);
        Assert.Equal(options, terminal.Options);
    }

    [Fact]
    public void Constructor_InitializesTerminalState()
    {
        // Arrange & Act
        var terminal = new Terminal();

        // Assert
        Assert.False(terminal.InsertMode);
        Assert.False(terminal.ApplicationCursorKeys);
        Assert.False(terminal.ApplicationKeypad);
        Assert.False(terminal.BracketedPasteMode);
        Assert.False(terminal.OriginMode);
        Assert.Equal(string.Empty, terminal.Title);
    }

    [Fact]
    public void Write_EmptyString_DoesNothing()
    {
        // Arrange
        var terminal = new Terminal();

        // Act & Assert - Should not throw
        terminal.Write("");
        terminal.Write(null!);
    }

    [Fact]
    public void Write_PlainText_PrintsToBuffer()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.Write("Hello");

        // Assert
        var line = terminal.GetLine(0);
        Assert.Contains("Hello", line);
    }

    [Fact]
    public void Write_WithEscapeSequence_ProcessesSequence()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.Write("\x1B[1mBold");

        // Assert
        var line = terminal.GetLine(0);
        Assert.Contains("Bold", line);
    }

    [Fact]
    public void WriteLine_AddsLineFeed()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.WriteLine("Line1");
        terminal.Write("Line2");

        // Assert
        var line0 = terminal.GetLine(0);
        var line1 = terminal.GetLine(1);
        Assert.Contains("Line1", line0);
        Assert.Contains("Line2", line1);
    }

    [Fact]
    public void Resize_ChangesTerminalSize()
    {
        // Arrange
        var terminal = new Terminal();
        var resized = false;
        var newCols = 0;
        var newRows = 0;
        
        terminal.Resized += (sender, e) =>
        {
            resized = true;
            newCols = e.Cols;
            newRows = e.Rows;
        };

        // Act
        terminal.Resize(100, 30);

        // Assert
        Assert.Equal(100, terminal.Cols);
        Assert.Equal(30, terminal.Rows);
        Assert.True(resized);
        Assert.Equal(100, newCols);
        Assert.Equal(30, newRows);
    }

    [Fact]
    public void Resize_SameSize_DoesNotFireEvent()
    {
        // Arrange
        var terminal = new Terminal();
        var resized = false;
        terminal.Resized += (sender, e) => resized = true;

        // Act
        terminal.Resize(80, 24); // Same as default

        // Assert
        Assert.False(resized);
    }

    [Fact]
    public void Reset_ResetsTerminalState()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.InsertMode = true;
        terminal.ApplicationCursorKeys = true;
        terminal.ApplicationKeypad = true;
        terminal.BracketedPasteMode = true;
        terminal.OriginMode = true;
        terminal.Write("Some text");

        // Act
        terminal.Reset();

        // Assert
        Assert.False(terminal.InsertMode);
        Assert.False(terminal.ApplicationCursorKeys);
        Assert.False(terminal.ApplicationKeypad);
        Assert.False(terminal.BracketedPasteMode);
        Assert.False(terminal.OriginMode);
    }

    [Fact]
    public void Clear_ClearsBuffer()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Write("Test content");

        // Act
        terminal.Clear();

        // Assert
        var line = terminal.GetLine(0);
        Assert.DoesNotContain("Test", line);
    }

    [Fact]
    public void ScrollLines_ScrollsViewport()
    {
        // Arrange
        var terminal = new Terminal();
        for (int i = 0; i < 30; i++)
        {
            terminal.WriteLine($"Line {i}");
        }
        
        var scrolled = false;
        terminal.Scrolled += (sender, e) => scrolled = true;

        // Act
        terminal.ScrollLines(5);

        // Assert
        Assert.True(scrolled);
    }

    [Fact]
    public void ScrollToTop_ScrollsToTop()
    {
        // Arrange
        var terminal = new Terminal();
        for (int i = 0; i < 30; i++)
        {
            terminal.WriteLine($"Line {i}");
        }
        
        terminal.ScrollLines(10);
        var scrolled = false;
        terminal.Scrolled += (sender, e) => scrolled = true;

        // Act
        terminal.ScrollToTop();

        // Assert
        Assert.True(scrolled);
        Assert.Equal(0, terminal.Buffer.YDisp);
    }

    [Fact]
    public void ScrollToBottom_ScrollsToBottom()
    {
        // Arrange
        var terminal = new Terminal();
        for (int i = 0; i < 30; i++)
        {
            terminal.WriteLine($"Line {i}");
        }
        
        terminal.ScrollToTop();
        var scrolled = false;
        terminal.Scrolled += (sender, e) => scrolled = true;

        // Act
        terminal.ScrollToBottom();

        // Assert
        Assert.True(scrolled);
        Assert.Equal(terminal.Buffer.YBase, terminal.Buffer.YDisp);
    }

    [Fact]
    public void GetLine_ReturnsLineContent()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Write("Test Line");

        // Act
        var line = terminal.GetLine(0);

        // Assert
        Assert.Contains("Test Line", line);
    }

    [Fact]
    public void GetLine_InvalidIndex_ReturnsEmpty()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        var line = terminal.GetLine(1000);

        // Assert
        Assert.Equal(string.Empty, line);
    }

    [Fact]
    public void GetVisibleLines_ReturnsAllVisibleLines()
    {
        // Arrange
        var terminal = new Terminal(new TerminalOptions { Rows = 5 });
        terminal.WriteLine("Line1");
        terminal.WriteLine("Line2");
        terminal.WriteLine("Line3");

        // Act
        var lines = terminal.GetVisibleLines();

        // Assert
        Assert.Equal(5, lines.Length);
        Assert.Contains("Line1", lines[0]);
        Assert.Contains("Line2", lines[1]);
        Assert.Contains("Line3", lines[2]);
    }

    [Fact]
    public void OnBell_FiresWhenBellReceived()
    {
        // Arrange
        var terminal = new Terminal();
        var bellRang = false;
        terminal.BellRang += (sender, e) => bellRang = true;

        // Act
        terminal.Write("\x07"); // BEL character

        // Assert
        Assert.True(bellRang);
    }

    [Fact]
    public void OnLineFeed_FiresOnLineFeed()
    {
        // Arrange
        var terminal = new Terminal();
        var lineFeedFired = false;
        terminal.LineFed += (sender, e) => lineFeedFired = true;

        // Act
        terminal.Write("\n");

        // Assert
        Assert.True(lineFeedFired);
    }

    [Fact]
    public void Title_CanBeSetViaOscSequence()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.Write("\x1B]0;Test Title\x07");

        // Assert
        Assert.Equal("Test Title", terminal.Title);
    }

    [Fact]
    public void Title_InitiallyEmpty()
    {
        // Arrange & Act
        var terminal = new Terminal();

        // Assert
        Assert.Equal(string.Empty, terminal.Title);
    }

    [Fact]
    public void SwitchToAltBuffer_SwitchesBuffer()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Write("Normal buffer content");

        // Act
        terminal.SwitchToAltBuffer();
        terminal.Write("Alt buffer content");

        // Assert
        var line = terminal.GetLine(0);
        Assert.Contains("Alt buffer", line);
    }

    [Fact]
    public void SwitchToNormalBuffer_RestoresNormalBuffer()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Write("Normal content");
        terminal.SwitchToAltBuffer();
        terminal.Write("Alt content");

        // Act
        terminal.SwitchToNormalBuffer();

        // Assert
        var line = terminal.GetLine(0);
        Assert.Contains("Normal content", line);
    }

    [Fact]
    public void SwitchToAltBuffer_WhenAlreadyInAltBuffer_DoesNothing()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.SwitchToAltBuffer();

        // Act & Assert - Should not throw
        terminal.SwitchToAltBuffer();
    }

    [Fact]
    public void SwitchToNormalBuffer_WhenAlreadyInNormalBuffer_DoesNothing()
    {
        // Arrange
        var terminal = new Terminal();

        // Act & Assert - Should not throw
        terminal.SwitchToNormalBuffer();
    }

    [Fact]
    public void Dispose_ClearsAllEvents()
    {
        // Arrange
        var terminal = new Terminal();
        var count = 0;
        terminal.BellRang += (sender, e) => count++;
        terminal.Scrolled += (sender, e) => count++;

        // Act
        terminal.Dispose();
        terminal.Write("\x07"); // Try to trigger bell
        terminal.ScrollLines(1); // Try to trigger scroll

        // Assert
        Assert.Equal(0, count); // Events should not fire after dispose
    }

    [Fact]
    public void Write_WithBackspace_MovesBack()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Write("ABC");

        // Act
        terminal.Write("\x08"); // Backspace
        terminal.Write("X");

        // Assert
        var line = terminal.GetLine(0);
        Assert.Contains("ABX", line);
    }

    [Fact]
    public void Write_WithTab_MovesToNextTabStop()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Write("A");

        // Act
        terminal.Write("\t"); // Tab
        terminal.Write("B");

        // Assert
        Assert.True(terminal.Buffer.X >= 8); // Should be at or past first tab stop
    }

    [Fact]
    public void Write_WithCarriageReturn_MovesToLineStart()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Write("ABCDE");

        // Act
        terminal.Write("\r"); // Carriage return
        terminal.Write("X");

        // Assert
        Assert.Equal(1, terminal.Buffer.X); // Should be at position 1 after writing X
    }

    [Fact]
    public void Write_CursorMovement_WorksCorrectly()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.Write("Start\x1B[10CHere"); // Move cursor forward 10

        // Assert
        var line = terminal.GetLine(0);
        Assert.Contains("Start", line);
        Assert.Contains("Here", line);
    }

    [Fact]
    public void Write_Colors_ApplyCorrectly()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.Write("\x1B[31mRed\x1B[0m Normal");

        // Assert
        var line = terminal.GetLine(0);
        Assert.Contains("Red", line);
        Assert.Contains("Normal", line);
    }

    [Fact]
    public void Write_BoldText_AppliesAttribute()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.Write("\x1B[1mBold");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsBold());
    }

    [Fact]
    public void Write_MultipleLines_HandlesCorrectly()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.Write("Line1\nLine2\nLine3");

        // Assert
        Assert.Contains("Line1", terminal.GetLine(0));
        Assert.Contains("Line2", terminal.GetLine(1));
        Assert.Contains("Line3", terminal.GetLine(2));
    }

    [Fact]
    public void InsertMode_AffectsPrinting()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Write("Hello");
        terminal.Buffer.SetCursor(2, 0);

        // Act
        terminal.InsertMode = true;
        terminal.Write("X");

        // Assert
        // Character should be inserted, not overwritten
        var line = terminal.GetLine(0);
        Assert.Contains("X", line);
    }

    [Fact]
    public void OriginMode_CanBeToggled()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.OriginMode = true;

        // Assert
        Assert.True(terminal.OriginMode);

        // Act
        terminal.OriginMode = false;

        // Assert
        Assert.False(terminal.OriginMode);
    }

    [Fact]
    public void ApplicationCursorKeys_CanBeToggled()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.ApplicationCursorKeys = true;

        // Assert
        Assert.True(terminal.ApplicationCursorKeys);
    }

    [Fact]
    public void Write_LongText_HandlesCorrectly()
    {
        // Arrange
        var terminal = new Terminal();
        var longText = new string('A', 1000);

        // Act & Assert - Should not throw
        terminal.Write(longText);
    }

    [Fact]
    public void Write_UnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        terminal.Write("Hello ?? ??");

        // Assert
        var line = terminal.GetLine(0);
        Assert.Contains("Hello", line);
        Assert.Contains("??", line);
        Assert.Contains("??", line);
    }

    [Fact]
    public void Reset_ClearsCursorPosition()
    {
        // Arrange
        var terminal = new Terminal();
        terminal.Buffer.SetCursor(10, 5);

        // Act
        terminal.Reset();

        // Assert
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Fact]
    public void Buffer_IsAccessible()
    {
        // Arrange
        var terminal = new Terminal();

        // Act
        var buffer = terminal.Buffer;

        // Assert
        Assert.NotNull(buffer);
        Assert.Equal(terminal.Buffer, buffer);
    }
}
