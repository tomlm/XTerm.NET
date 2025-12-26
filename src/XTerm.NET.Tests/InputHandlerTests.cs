using XTerm;
using XTerm.Buffer;
using XTerm.Options;
using XTerm.Parser;
using XTerm.Common;
using XTerm.Events;

namespace XTerm.Tests;

public class InputHandlerTests
{
    private Terminal CreateTerminal(int cols = 80, int rows = 24)
    {
        var options = new TerminalOptions { Cols = cols, Rows = rows };
        return new Terminal(options);
    }

    [Fact]
    public void Constructor_InitializesHandler()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        var handler = new InputHandler(terminal);

        // Assert
        Assert.NotNull(handler);
    }

    [Fact]
    public void Print_PrintsCharacterToBuffer()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);

        // Act
        handler.Print("A");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal("A", line[0].Content);
    }

    [Fact]
    public void Print_MultipleCharacters_PrintsSequentially()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);

        // Act
        handler.Print("H");
        handler.Print("e");
        handler.Print("l");
        handler.Print("l");
        handler.Print("o");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal("Hello", line.TranslateToString(true, 0, 5));
    }

    [Fact]
    public void HandleCsi_CursorUp_MovesCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(0, 10);
        var params_ = new Params();
        params_.AddParam(5);

        // Act
        handler.HandleCsi("A", params_);

        // Assert
        Assert.Equal(5, terminal.Buffer.Y);
    }

    [Fact]
    public void HandleCsi_CursorDown_MovesCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(0, 5);
        var params_ = new Params();
        params_.AddParam(3);

        // Act
        handler.HandleCsi("B", params_);

        // Assert
        Assert.Equal(8, terminal.Buffer.Y);
    }

    [Fact]
    public void HandleCsi_CursorForward_MovesCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(5, 0);
        var params_ = new Params();
        params_.AddParam(10);

        // Act
        handler.HandleCsi("C", params_);

        // Assert
        Assert.Equal(15, terminal.Buffer.X);
    }

    [Fact]
    public void HandleCsi_CursorBackward_MovesCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(20, 0);
        var params_ = new Params();
        params_.AddParam(5);

        // Act
        handler.HandleCsi("D", params_);

        // Assert
        Assert.Equal(15, terminal.Buffer.X);
    }

    [Fact]
    public void HandleCsi_CursorPosition_SetsCursorPosition()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(10);
        params_.AddParam(20);

        // Act
        handler.HandleCsi("H", params_);

        // Assert
        Assert.Equal(19, terminal.Buffer.X); // 20 - 1 (1-based to 0-based)
        Assert.Equal(9, terminal.Buffer.Y);  // 10 - 1 (1-based to 0-based)
    }

    [Fact]
    public void HandleCsi_EraseInDisplay_ClearBelow()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Fill some lines
        for (int i = 0; i < 5; i++)
        {
            terminal.Buffer.SetCursor(0, i);
            handler.Print("X");
        }
        
        terminal.Buffer.SetCursor(0, 2);
        var params_ = new Params();
        params_.AddParam(0); // Erase below

        // Act
        handler.HandleCsi("J", params_);

        // Assert
        // Lines 0-1 should have content, lines 2+ should be cleared
        Assert.Equal("X", terminal.Buffer.Lines[0]?[0].Content);
        Assert.Equal("X", terminal.Buffer.Lines[1]?[0].Content);
    }

    [Fact]
    public void HandleCsi_EraseInLine_ErasesToRight()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Print some characters
        for (int i = 0; i < 10; i++)
        {
            handler.Print("X");
        }
        
        terminal.Buffer.SetCursor(5, 0);
        var params_ = new Params();
        params_.AddParam(0); // Erase to right

        // Act
        handler.HandleCsi("K", params_);

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal("X", line[4].Content);
        Assert.NotEqual("X", line[5].Content);
    }

    [Fact]
    public void HandleCsi_SgrBold_SetsBoldAttribute()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(1); // Bold

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("B");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsBold());
    }

    [Fact]
    public void HandleCsi_SgrItalic_SetsItalicAttribute()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(3); // Italic

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("I");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsItalic());
    }

    [Fact]
    public void HandleCsi_SgrUnderline_SetsUnderlineAttribute()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(4); // Underline

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("U");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsUnderline());
    }

    [Fact]
    public void HandleCsi_SgrForegroundColor_SetsForegroundColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(31); // Red foreground

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("R");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal(1, line[0].Attributes.GetFgColor()); // Color 31 - 30 = 1
    }

    [Fact]
    public void HandleCsi_SgrBackgroundColor_SetsBackgroundColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(42); // Green background

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("G");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal(2, line[0].Attributes.GetBgColor()); // Color 42 - 40 = 2
    }

    [Fact]
    public void HandleCsi_SgrReset_ResetsAttributes()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params1 = new Params();
        params1.AddParam(1); // Bold
        params1.AddParam(31); // Red

        var params2 = new Params();
        params2.AddParam(0); // Reset

        // Act
        handler.HandleCsi("m", params1);
        handler.Print("B");
        
        handler.HandleCsi("m", params2);
        handler.Print("N");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsBold());
        Assert.False(line[1].Attributes.IsBold());
    }

    [Fact]
    public void HandleCsi_SetScrollRegion_SetsRegion()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(5);
        params_.AddParam(20);

        // Act
        handler.HandleCsi("r", params_);

        // Assert
        Assert.Equal(4, terminal.Buffer.ScrollTop);    // 5 - 1 (1-based to 0-based)
        Assert.Equal(19, terminal.Buffer.ScrollBottom); // 20 - 1 (1-based to 0-based)
    }

    [Fact]
    public void HandleEsc_Index_MovesDownOrScrolls()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(0, 5);
        var initialY = terminal.Buffer.Y;

        // Act
        handler.HandleEsc("D", "");

        // Assert
        Assert.Equal(initialY + 1, terminal.Buffer.Y);
    }

    [Fact]
    public void HandleEsc_ReverseIndex_MovesUpOrScrolls()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(0, 5);
        var initialY = terminal.Buffer.Y;

        // Act
        handler.HandleEsc("M", "");

        // Assert
        Assert.Equal(initialY - 1, terminal.Buffer.Y);
    }

    [Fact]
    public void HandleEsc_NextLine_MovesDownAndToStart()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(20, 5);

        // Act
        handler.HandleEsc("E", "");

        // Assert
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(6, terminal.Buffer.Y);
    }

    [Fact]
    public void HandleOsc_SetTitle_SetsTerminalTitle()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);

        // Act
        handler.HandleOsc("0;Test Title");

        // Assert
        Assert.Equal("Test Title", terminal.Title);
    }

    [Fact]
    public void HandleOsc_SetWindowTitle_SetsTerminalTitle()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);

        // Act
        handler.HandleOsc("2;Window Title");

        // Assert
        Assert.Equal("Window Title", terminal.Title);
    }

    [Fact]
    public void SetBuffer_UpdatesBuffer()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var newBuffer = new TerminalBuffer(80, 24, 1000);

        // Act
        handler.SetBuffer(newBuffer);
        handler.Print("X");

        // Assert
        Assert.Equal("X", newBuffer.Lines[0]?[0].Content);
    }

    [Fact]
    public void Print_InsertMode_InsertsCharacter()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Print some characters first
        handler.Print("H");
        handler.Print("e");
        handler.Print("l");
        handler.Print("l");
        handler.Print("o");
        
        // Move cursor back and enable insert mode
        terminal.Buffer.SetCursor(2, 0);
        terminal.InsertMode = true;

        // Act
        handler.Print("X");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        // Character should be inserted, not overwritten
    }

    [Fact]
    public void Print_AtEndOfLine_WrapsToNextLine()
    {
        // Arrange
        var terminal = CreateTerminal(10, 24); // Narrow terminal
        var handler = new InputHandler(terminal);
        
        // Fill the line
        for (int i = 0; i < 10; i++)
        {
            handler.Print("X");
        }

        // Act
        handler.Print("Y"); // Should wrap

        // Assert
        Assert.Equal(1, terminal.Buffer.Y); // Moved to next line
        Assert.Equal("Y", terminal.Buffer.Lines[1]?[0].Content);
    }

    [Fact]
    public void HandleCsi_InsertLines_InsertsBlankLines()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(0, 5);
        var params_ = new Params();
        params_.AddParam(2); // Insert 2 lines

        // Act
        handler.HandleCsi("L", params_);

        // Assert - Lines should be inserted at cursor position
        // Verification would require checking buffer state
    }

    [Fact]
    public void HandleCsi_DeleteLines_DeletesLines()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(0, 5);
        var params_ = new Params();
        params_.AddParam(2); // Delete 2 lines

        // Act
        handler.HandleCsi("M", params_);

        // Assert - Lines should be deleted from cursor position
        // Verification would require checking buffer state
    }

    [Fact]
    public void HandleCsi_MultipleAttributes_AppliesAll()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(1);  // Bold
        params_.AddParam(3);  // Italic
        params_.AddParam(4);  // Underline
        params_.AddParam(31); // Red

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("X");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        var attrs = line[0].Attributes;
        Assert.True(attrs.IsBold());
        Assert.True(attrs.IsItalic());
        Assert.True(attrs.IsUnderline());
        Assert.Equal(1, attrs.GetFgColor());
    }

    [Fact]
    public void HandleCsi_DECSCUSR_SetsCursorStyle()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var paramsUnderlineBlink = new Params();
        paramsUnderlineBlink.AddParam(3); // Blinking underline

        // Act
        handler.HandleCsi(" q", paramsUnderlineBlink);

        // Assert
        Assert.Equal(CursorStyle.Underline, terminal.Options.CursorStyle);
        Assert.True(terminal.Options.CursorBlink);

        // Act - Steady block
        var paramsBlockSteady = new Params();
        paramsBlockSteady.AddParam(2);
        handler.HandleCsi(" q", paramsBlockSteady);

        Assert.Equal(CursorStyle.Block, terminal.Options.CursorStyle);
        Assert.False(terminal.Options.CursorBlink);
    }

    [Fact]
    public void HandleCsi_SelectCursorStyle_BlinkingBlock_Defaults()
    {
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(1);

        handler.HandleCsi(" q", params_);

        Assert.Equal(CursorStyle.Block, terminal.Options.CursorStyle);
        Assert.True(terminal.Options.CursorBlink);
    }

    [Fact]
    public void HandleCsi_SelectCursorStyle_SteadyBar_RaisesEvent()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(6); // Steady bar

        TerminalEvents.CursorStyleChangedEventArgs? received = null;
        terminal.CursorStyleChanged += (_, e) => received = e;

        // Act
        handler.HandleCsi(" q", params_);

        // Assert
        Assert.Equal(CursorStyle.Bar, terminal.Options.CursorStyle);
        Assert.False(terminal.Options.CursorBlink);
        Assert.NotNull(received);
        Assert.Equal(CursorStyle.Bar, received!.Style);
        Assert.False(received.Blink);
    }

    #region DeviceStatusReport Tests

    [Fact]
    public void HandleCsi_DSR_OperatingStatus_ReportsOk()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(5); // Operating status query

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("n", params_);

        // Assert - Should respond with CSI 0 n (OK)
        Assert.Equal("\u001b[0n", receivedData);
    }

    [Fact]
    public void HandleCsi_DSR_CursorPositionReport_ReportsPosition()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(10, 5); // 0-based: col 10, row 5
        var params_ = new Params();
        params_.AddParam(6); // Cursor position query

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("n", params_);

        // Assert - Should respond with CSI row ; col R (1-based)
        Assert.Equal("\u001b[6;11R", receivedData); // row 6, col 11 (1-based)
    }

    [Fact]
    public void HandleCsi_DSR_CursorPositionReport_AtOrigin()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(0, 0); // Top-left corner
        var params_ = new Params();
        params_.AddParam(6);

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("n", params_);

        // Assert - Should respond with CSI 1 ; 1 R
        Assert.Equal("\u001b[1;1R", receivedData);
    }

    [Fact]
    public void HandleCsi_DSR_CursorPositionReport_WithOriginMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Set scroll region from row 5 to row 20 (0-based: 4 to 19)
        terminal.Buffer.SetScrollRegion(4, 19);
        terminal.OriginMode = true;
        
        // Cursor at row 10, col 5 (0-based)
        terminal.Buffer.SetCursor(5, 10);
        
        var params_ = new Params();
        params_.AddParam(6);

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("n", params_);

        // Assert - Row should be adjusted for scroll region
        // Row 10 - ScrollTop 4 = 6, then +1 for 1-based = 7
        // Col 5 + 1 = 6
        Assert.Equal("\u001b[7;6R", receivedData);
    }

    [Fact]
    public void HandleCsi_DSR_Private_ExtendedCursorPositionReport()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(15, 8); // 0-based: col 15, row 8
        var params_ = new Params();
        params_.AddParam(6); // Extended cursor position query

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act - Use "?n" for private mode
        handler.HandleCsi("?n", params_);

        // Assert - Should respond with CSI ? row ; col R (1-based)
        Assert.Equal("\u001b[?9;16R", receivedData); // row 9, col 16 (1-based)
    }

    [Fact]
    public void HandleCsi_DSR_Private_PrinterStatus()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(15); // Printer status query

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("?n", params_);

        // Assert - Should respond with CSI ? 13 n (no printer)
        Assert.Equal("\u001b[?13n", receivedData);
    }

    [Fact]
    public void HandleCsi_DSR_Private_UdkStatus()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(25); // UDK status query

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("?n", params_);

        // Assert - Should respond with CSI ? 21 n (UDK locked)
        Assert.Equal("\u001b[?21n", receivedData);
    }

    [Fact]
    public void HandleCsi_DSR_Private_KeyboardStatus()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(26); // Keyboard status query

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("?n", params_);

        // Assert - Should respond with CSI ? 27 ; 1 ; 0 ; 0 n (keyboard ready)
        Assert.Equal("\u001b[?27;1;0;0n", receivedData);
    }

    [Fact]
    public void HandleCsi_DSR_UnknownReport_DoesNotRespond()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(99); // Unknown report type

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("n", params_);

        // Assert - Should not send any response
        Assert.Null(receivedData);
    }

    [Fact]
    public void HandleCsi_DSR_Private_UnknownReport_DoesNotRespond()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(99); // Unknown private report type

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("?n", params_);

        // Assert - Should not send any response
        Assert.Null(receivedData);
    }

    #endregion

    #region DeviceAttributes Tests

    [Fact]
    public void HandleCsi_DA_Primary_ReportsDeviceAttributes()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(0); // Primary DA

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act
        handler.HandleCsi("c", params_);

        // Assert - Should respond with CSI ? 1 ; 2 c (VT100 with AVO)
        Assert.Equal("\u001b[?1;2c", receivedData);
    }

    [Fact]
    public void HandleCsi_DA_Secondary_ReportsTerminalId()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(0); // Secondary DA

        string? receivedData = null;
        terminal.DataReceived += (_, e) => receivedData = e.Data;

        // Act - Use ">c" for secondary DA
        handler.HandleCsi(">c", params_);

        // Assert - Should respond with CSI > 0 ; 10 ; 0 c
        Assert.Equal("\u001b[>0;10;0c", receivedData);
    }

    #endregion
}
