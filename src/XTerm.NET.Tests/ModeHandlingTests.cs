using XTerm.NET;
using XTerm.NET.Common;
using XTerm.NET.Options;

namespace XTerm.NET.Tests;

public class ModeHandlingTests
{
    private Terminal CreateTerminal(int cols = 80, int rows = 24)
    {
        var options = new TerminalOptions { Cols = cols, Rows = rows };
        return new Terminal(options);
    }

    [Fact]
    public void SetMode_InsertMode_EnablesInsertMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.InsertMode);

        // Act
        terminal.Write($"\x1B[{CoreModes.INSERT_MODE}h");

        // Assert
        Assert.True(terminal.InsertMode);
    }

    [Fact]
    public void ResetMode_InsertMode_DisablesInsertMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.InsertMode = true;

        // Act
        terminal.Write($"\x1B[{CoreModes.INSERT_MODE}l");

        // Assert
        Assert.False(terminal.InsertMode);
    }

    [Fact]
    public void SetMode_ApplicationCursorKeys_EnablesAppCursorKeys()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.ApplicationCursorKeys);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.APP_CURSOR_KEYS}h");

        // Assert
        Assert.True(terminal.ApplicationCursorKeys);
    }

    [Fact]
    public void ResetMode_ApplicationCursorKeys_DisablesAppCursorKeys()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationCursorKeys = true;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.APP_CURSOR_KEYS}l");

        // Assert
        Assert.False(terminal.ApplicationCursorKeys);
    }

    [Fact]
    public void SetMode_ShowCursor_EnablesCursorVisibility()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.CursorVisible = false;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.SHOW_CURSOR}h");

        // Assert
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public void ResetMode_ShowCursor_DisablesCursorVisibility()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.True(terminal.CursorVisible); // Default is true

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.SHOW_CURSOR}l");

        // Assert
        Assert.False(terminal.CursorVisible);
    }

    [Fact]
    public void SetMode_ApplicationKeypad_EnablesAppKeypad()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.ApplicationKeypad);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.APP_KEYPAD}h");

        // Assert
        Assert.True(terminal.ApplicationKeypad);
    }

    [Fact]
    public void ResetMode_ApplicationKeypad_DisablesAppKeypad()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationKeypad = true;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.APP_KEYPAD}l");

        // Assert
        Assert.False(terminal.ApplicationKeypad);
    }

    [Fact]
    public void SetMode_BracketedPasteMode_EnablesBracketedPaste()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.BracketedPasteMode);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.BRACKETED_PASTE_MODE}h");

        // Assert
        Assert.True(terminal.BracketedPasteMode);
    }

    [Fact]
    public void ResetMode_BracketedPasteMode_DisablesBracketedPaste()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.BracketedPasteMode = true;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.BRACKETED_PASTE_MODE}l");

        // Assert
        Assert.False(terminal.BracketedPasteMode);
    }

    [Fact]
    public void SetMode_OriginMode_EnablesOriginMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Buffer.SetCursor(10, 10);
        Assert.False(terminal.OriginMode);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.ORIGIN}h");

        // Assert
        Assert.True(terminal.OriginMode);
        // Cursor should be reset to 0,0
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Fact]
    public void ResetMode_OriginMode_DisablesOriginMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.OriginMode = true;
        terminal.Buffer.SetCursor(5, 5);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.ORIGIN}l");

        // Assert
        Assert.False(terminal.OriginMode);
        // Cursor should be reset to 0,0
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Fact]
    public void SetMode_AltBuffer_SwitchesToAltBuffer()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("Normal buffer content");
        var normalBufferContent = terminal.GetLine(0);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.ALT_BUFFER}h");

        // Assert
        terminal.Write("Alt buffer content");
        var altBufferContent = terminal.GetLine(0);
        Assert.Contains("Alt buffer", altBufferContent);
        Assert.DoesNotContain("Normal buffer", altBufferContent);
    }

    [Fact]
    public void ResetMode_AltBuffer_SwitchesToNormalBuffer()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("Normal content");
        terminal.Write($"\x1B[?{CoreModes.ALT_BUFFER}h"); // Switch to alt
        terminal.Write("Alt content");

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.ALT_BUFFER}l"); // Switch back

        // Assert
        var content = terminal.GetLine(0);
        Assert.Contains("Normal content", content);
    }

    [Fact]
    public void SetMode_AltBufferWithCursor_SavesCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Buffer.SetCursor(20, 10);

        // Act - DEC private mode (saves cursor and switches)
        terminal.Write($"\x1B[?{CoreModes.ALT_BUFFER_CURSOR}h");

        // Assert
        // Should be in alt buffer
        terminal.Write("Test");
        var content = terminal.GetLine(0);
        Assert.Contains("Test", content);
    }

    [Fact]
    public void ResetMode_AltBufferWithCursor_RestoresCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Buffer.SetCursor(20, 10);
        var savedX = terminal.Buffer.X;
        var savedY = terminal.Buffer.Y;

        terminal.Write($"\x1B[?{CoreModes.ALT_BUFFER_CURSOR}h"); // Save and switch
        terminal.Buffer.SetCursor(5, 5); // Move cursor in alt buffer

        // Act - DEC private mode (switches back and restores cursor)
        terminal.Write($"\x1B[?{CoreModes.ALT_BUFFER_CURSOR}l");

        // Assert
        Assert.Equal(savedX, terminal.Buffer.X);
        Assert.Equal(savedY, terminal.Buffer.Y);
    }

    [Fact]
    public void SetMode_SendFocusEvents_EnablesFocusEvents()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.SendFocusEvents);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.SEND_FOCUS_EVENTS}h");

        // Assert
        Assert.True(terminal.SendFocusEvents);
    }

    [Fact]
    public void ResetMode_SendFocusEvents_DisablesFocusEvents()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.SendFocusEvents = true;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.SEND_FOCUS_EVENTS}l");

        // Assert
        Assert.False(terminal.SendFocusEvents);
    }

    [Fact]
    public void SetMode_Wraparound_EnablesWraparound()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Options.Wraparound = false;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.WRAPAROUND}h");

        // Assert
        Assert.True(terminal.Options.Wraparound);
    }

    [Fact]
    public void ResetMode_Wraparound_DisablesWraparound()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.True(terminal.Options.Wraparound); // Default is true

        // Act - DEC private mode
        terminal.Write($"\x1B[?{CoreModes.WRAPAROUND}l");

        // Assert
        Assert.False(terminal.Options.Wraparound);
    }

    [Fact]
    public void SetMode_MultipleModes_EnablesAll()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act - Set multiple modes at once
        terminal.Write($"\x1B[?{CoreModes.APP_CURSOR_KEYS};{CoreModes.SHOW_CURSOR};{CoreModes.APP_KEYPAD}h");

        // Assert
        Assert.True(terminal.ApplicationCursorKeys);
        Assert.True(terminal.CursorVisible);
        Assert.True(terminal.ApplicationKeypad);
    }

    [Fact]
    public void ResetMode_MultipleModes_DisablesAll()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationCursorKeys = true;
        terminal.CursorVisible = true;
        terminal.ApplicationKeypad = true;

        // Act - Reset multiple modes at once
        terminal.Write($"\x1B[?{CoreModes.APP_CURSOR_KEYS};{CoreModes.SHOW_CURSOR};{CoreModes.APP_KEYPAD}l");

        // Assert
        Assert.False(terminal.ApplicationCursorKeys);
        Assert.False(terminal.CursorVisible);
        Assert.False(terminal.ApplicationKeypad);
    }

    [Fact]
    public void TerminalReset_ResetsAllModes()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.InsertMode = true;
        terminal.ApplicationCursorKeys = true;
        terminal.ApplicationKeypad = true;
        terminal.BracketedPasteMode = true;
        terminal.OriginMode = true;
        terminal.CursorVisible = false;
        terminal.SendFocusEvents = true;

        // Act
        terminal.Reset();

        // Assert
        Assert.False(terminal.InsertMode);
        Assert.False(terminal.ApplicationCursorKeys);
        Assert.False(terminal.ApplicationKeypad);
        Assert.False(terminal.BracketedPasteMode);
        Assert.False(terminal.OriginMode);
        Assert.True(terminal.CursorVisible); // Default is true
        Assert.False(terminal.SendFocusEvents);
    }
}
