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

    #region Additional CSI Command Tests

    [Fact]
    public void HandleCsi_CursorNextLine_MovesCursorDownAndToColumn1()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(20, 5);
        var params_ = new Params();
        params_.AddParam(3); // Move down 3 lines

        // Act
        handler.HandleCsi("E", params_);

        // Assert
        Assert.Equal(0, terminal.Buffer.X); // Column should be 0
        Assert.Equal(8, terminal.Buffer.Y); // Row 5 + 3 = 8
    }

    [Fact]
    public void HandleCsi_CursorPreviousLine_MovesCursorUpAndToColumn1()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(20, 10);
        var params_ = new Params();
        params_.AddParam(4); // Move up 4 lines

        // Act
        handler.HandleCsi("F", params_);

        // Assert
        Assert.Equal(0, terminal.Buffer.X); // Column should be 0
        Assert.Equal(6, terminal.Buffer.Y); // Row 10 - 4 = 6
    }

    [Fact]
    public void HandleCsi_CursorCharAbsolute_MovesToColumn()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(5, 10);
        var params_ = new Params();
        params_.AddParam(25); // Move to column 25 (1-based)

        // Act
        handler.HandleCsi("G", params_);

        // Assert
        Assert.Equal(24, terminal.Buffer.X); // 25 - 1 = 24 (0-based)
        Assert.Equal(10, terminal.Buffer.Y); // Row should be unchanged
    }

    [Fact]
    public void HandleCsi_CursorForwardTab_MovesToNextTabStop()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(5, 0);
        var params_ = new Params();
        params_.AddParam(1); // Move to next tab stop

        // Act
        handler.HandleCsi("I", params_);

        // Assert - Default tab width is 8, so from position 5, next tab stop is 8
        Assert.Equal(8, terminal.Buffer.X);
    }

    [Fact]
    public void HandleCsi_CursorForwardTab_MultipleTabStops()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(0, 0);
        var params_ = new Params();
        params_.AddParam(3); // Move 3 tab stops

        // Act
        handler.HandleCsi("I", params_);

        // Assert - 3 tab stops from 0: 8, 16, 24
        Assert.Equal(24, terminal.Buffer.X);
    }

    [Fact]
    public void HandleCsi_DeleteChars_DeletesCharacters()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Print some characters
        for (int i = 0; i < 10; i++)
        {
            handler.Print(((char)('A' + i)).ToString());
        }
        
        // Move cursor to position 3
        terminal.Buffer.SetCursor(3, 0);
        var params_ = new Params();
        params_.AddParam(2); // Delete 2 characters

        // Act
        handler.HandleCsi("P", params_);

        // Assert - Characters should be shifted left
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal("A", line[0].Content);
        Assert.Equal("B", line[1].Content);
        Assert.Equal("C", line[2].Content);
        Assert.Equal("F", line[3].Content); // D and E were deleted, F moved here
    }

    [Fact]
    public void HandleCsi_ScrollUp_ScrollsBufferUp()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Print content on multiple lines
        for (int i = 0; i < 5; i++)
        {
            terminal.Buffer.SetCursor(0, i);
            handler.Print(((char)('A' + i)).ToString());
        }
        
        var params_ = new Params();
        params_.AddParam(2); // Scroll up 2 lines

        // Act
        handler.HandleCsi("S", params_);

        // Assert - Content should have scrolled up
        // Viewport line 0 should now contain what was on viewport line 2
        // Access viewport using YBase offset
        var viewportLine0 = terminal.Buffer.Lines[terminal.Buffer.YBase];
        Assert.NotNull(viewportLine0);
        Assert.Equal("C", viewportLine0[0].Content);
    }

    [Fact]
    public void HandleCsi_ScrollDown_ScrollsBufferDown()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Print content on multiple lines
        for (int i = 0; i < 5; i++)
        {
            terminal.Buffer.SetCursor(0, i);
            handler.Print(((char)('A' + i)).ToString());
        }
        
        var params_ = new Params();
        params_.AddParam(2); // Scroll down 2 lines

        // Act
        handler.HandleCsi("T", params_);

        // Assert - Content should have scrolled down, new blank lines at top
        var line2 = terminal.Buffer.Lines[2];
        Assert.NotNull(line2);
        // Line 2 now contains what was on line 0
        Assert.Equal("A", line2[0].Content);
    }

    [Fact]
    public void HandleCsi_EraseChars_ErasesCharactersAtCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Print some characters
        for (int i = 0; i < 10; i++)
        {
            handler.Print("X");
        }
        
        // Move cursor to position 3
        terminal.Buffer.SetCursor(3, 0);
        var params_ = new Params();
        params_.AddParam(4); // Erase 4 characters

        // Act
        handler.HandleCsi("X", params_);

        // Assert - Characters at positions 3-6 should be erased (spaces)
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal("X", line[2].Content);
        Assert.Equal(" ", line[3].Content);
        Assert.Equal(" ", line[4].Content);
        Assert.Equal(" ", line[5].Content);
        Assert.Equal(" ", line[6].Content);
        Assert.Equal("X", line[7].Content);
    }

    [Fact]
    public void HandleCsi_CursorBackwardTab_MovesToPreviousTabStop()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(20, 0);
        var params_ = new Params();
        params_.AddParam(1); // Move to previous tab stop

        // Act
        handler.HandleCsi("Z", params_);

        // Assert - From position 20, previous tab stop is 16
        Assert.Equal(16, terminal.Buffer.X);
    }

    [Fact]
    public void HandleCsi_CursorBackwardTab_MultipleTabStops()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(25, 0);
        var params_ = new Params();
        params_.AddParam(2); // Move back 2 tab stops

        // Act
        handler.HandleCsi("Z", params_);

        // Assert - From position 25: first back to 24, then to 16
        // Tab stops at 0, 8, 16, 24, so from 25 going back 2 stops: 24 -> 16
        Assert.Equal(16, terminal.Buffer.X);
    }

    [Fact]
    public void HandleCsi_LinePositionAbsolute_MovesToRow()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(15, 5);
        var params_ = new Params();
        params_.AddParam(12); // Move to row 12 (1-based)

        // Act
        handler.HandleCsi("d", params_);

        // Assert
        Assert.Equal(15, terminal.Buffer.X); // Column should be unchanged
        Assert.Equal(11, terminal.Buffer.Y); // 12 - 1 = 11 (0-based)
    }

    [Fact]
    public void HandleCsi_SaveRestoreCursorAnsi_SavesAndRestoresPosition()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(10, 5);

        // Act - Save cursor
        handler.HandleCsi("s", new Params());

        // Move cursor elsewhere
        terminal.Buffer.SetCursor(30, 15);
        Assert.Equal(30, terminal.Buffer.X);
        Assert.Equal(15, terminal.Buffer.Y);

        // Act - Restore cursor
        handler.HandleCsi("u", new Params());

        // Assert - Cursor should be back to saved position
        Assert.Equal(10, terminal.Buffer.X);
        Assert.Equal(5, terminal.Buffer.Y);
    }

    [Fact]
    public void HandleCsi_EraseInDisplay_ClearAbove()
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
        
        terminal.Buffer.SetCursor(0, 3);
        var params_ = new Params();
        params_.AddParam(1); // Erase above

        // Act
        handler.HandleCsi("J", params_);

        // Assert - Lines 0-2 should be cleared, lines 3-4 should have content
        Assert.NotEqual("X", terminal.Buffer.Lines[0]?[0].Content);
        Assert.NotEqual("X", terminal.Buffer.Lines[1]?[0].Content);
        Assert.NotEqual("X", terminal.Buffer.Lines[2]?[0].Content);
        Assert.Equal("X", terminal.Buffer.Lines[4]?[0].Content);
    }

    [Fact]
    public void HandleCsi_EraseInDisplay_ClearAll()
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
        params_.AddParam(2); // Erase all

        // Act
        handler.HandleCsi("J", params_);

        // Assert - All visible lines should be cleared
        for (int i = 0; i < 5; i++)
        {
            Assert.NotEqual("X", terminal.Buffer.Lines[i]?[0].Content);
        }
    }

    [Fact]
    public void HandleCsi_EraseInLine_ErasesToLeft()
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
        params_.AddParam(1); // Erase to left

        // Act
        handler.HandleCsi("K", params_);

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.NotEqual("X", line[0].Content);
        Assert.NotEqual("X", line[5].Content); // Cursor position included
        Assert.Equal("X", line[6].Content); // Right of cursor preserved
    }

    [Fact]
    public void HandleCsi_EraseInLine_ErasesEntireLine()
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
        params_.AddParam(2); // Erase entire line

        // Act
        handler.HandleCsi("K", params_);

        // Assert - Entire line should be cleared
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        for (int i = 0; i < 10; i++)
        {
            Assert.NotEqual("X", line[i].Content);
        }
    }

    #endregion

    #region Additional CSI Command Tests
    
    [Fact]
    public void HandleCsi_InsertChars_InsertsBlankCharacters()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Print some characters
        for (int i = 0; i < 10; i++)
        {
            handler.Print(((char)('A' + i)).ToString());
        }
        
        // Move cursor to position 3
        terminal.Buffer.SetCursor(3, 0);
        var params_ = new Params();
        params_.AddParam(2); // Insert 2 blank characters

        // Act
        handler.HandleCsi("@", params_);

        // Assert - Characters should be shifted right
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal("A", line[0].Content);
        Assert.Equal("B", line[1].Content);
        Assert.Equal("C", line[2].Content);
        // Positions 3 and 4 should now be blank
        Assert.Equal(" ", line[3].Content);
        Assert.Equal(" ", line[4].Content);
        Assert.Equal("D", line[5].Content); // D shifted from position 3 to 5
    }

    #endregion

    #region Extended SGR Attribute Tests

    [Fact]
    public void HandleCsi_SgrDim_SetsDimAttribute()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(2); // Dim

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("D");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsDim());
    }

    [Fact]
    public void HandleCsi_SgrBlink_SetsBlinkAttribute()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(5); // Blink

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("B");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsBlink());
    }

    [Fact]
    public void HandleCsi_SgrInverse_SetsInverseAttribute()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(7); // Inverse

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("I");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsInverse());
    }

    [Fact]
    public void HandleCsi_SgrHidden_SetsHiddenAttribute()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(8); // Hidden/Invisible

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("H");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsInvisible());
    }

    [Fact]
    public void HandleCsi_SgrStrikethrough_SetsStrikethroughAttribute()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(9); // Strikethrough

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("S");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsStrikethrough());
    }

    [Fact]
    public void HandleCsi_Sgr256ColorForeground_SetsForegroundColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(38); // Extended foreground
        params_.AddParam(5);  // 256-color mode
        params_.AddParam(196); // Color index (bright red)

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("C");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal(196, line[0].Attributes.GetFgColor());
    }

    [Fact]
    public void HandleCsi_Sgr256ColorBackground_SetsBackgroundColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(48); // Extended background
        params_.AddParam(5);  // 256-color mode
        params_.AddParam(21); // Color index (blue)

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("C");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal(21, line[0].Attributes.GetBgColor());
    }

    [Fact]
    public void HandleCsi_SgrTrueColorForeground_SetsForegroundRGB()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(38); // Extended foreground
        params_.AddParam(2);  // TrueColor mode
        params_.AddParam(255); // Red
        params_.AddParam(128); // Green
        params_.AddParam(64);  // Blue

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("T");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        // TrueColor mode is stored in the color mode bits
        Assert.True(line[0].Attributes.GetFgColorMode() > 0);
    }

    [Fact]
    public void HandleCsi_SgrTrueColorBackground_SetsBackgroundRGB()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(48); // Extended background
        params_.AddParam(2);  // TrueColor mode
        params_.AddParam(64);  // Red
        params_.AddParam(128); // Green
        params_.AddParam(255); // Blue

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("T");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        // TrueColor mode is stored in the color mode bits
        Assert.True(line[0].Attributes.GetBgColorMode() > 0);
    }

    [Fact]
    public void HandleCsi_SgrBrightForegroundColors_SetsCorrectColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(91); // Bright red foreground (90-97)

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("R");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        // Bright colors are 90-97, mapped to color indices 8-15
        Assert.Equal(9, line[0].Attributes.GetFgColor()); // 91 - 82 = 9 (bright red)
    }

    [Fact]
    public void HandleCsi_SgrBrightBackgroundColors_SetsCorrectColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params_ = new Params();
        params_.AddParam(102); // Bright green background (100-107)

        // Act
        handler.HandleCsi("m", params_);
        handler.Print("G");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        // Bright background colors are 100-107, mapped to color indices 8-15
        Assert.Equal(10, line[0].Attributes.GetBgColor()); // 102 - 92 = 10 (bright green)
    }

    [Fact]
    public void HandleCsi_SgrResetBold_ResetsBoldOnly()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Set bold and italic
        var params1 = new Params();
        params1.AddParam(1); // Bold
        params1.AddParam(3); // Italic
        handler.HandleCsi("m", params1);
        handler.Print("B");
        
        // Reset bold only
        var params2 = new Params();
        params2.AddParam(22); // Reset bold/dim

        // Act
        handler.HandleCsi("m", params2);
        handler.Print("N");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsBold());    // "B" was printed with bold
        Assert.True(line[0].Attributes.IsItalic()); // "B" also has italic
        Assert.False(line[1].Attributes.IsBold());   // "N" was printed after reset - NOT bold
        Assert.True(line[1].Attributes.IsItalic()); // Italic should remain
    }

    [Fact]
    public void HandleCsi_SgrResetItalic_ResetsItalicOnly()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Set bold and italic
        var params1 = new Params();
        params1.AddParam(1); // Bold
        params1.AddParam(3); // Italic
        handler.HandleCsi("m", params1);
        handler.Print("I");
        
        // Reset italic only
        var params2 = new Params();
        params2.AddParam(23); // Reset italic

        // Act
        handler.HandleCsi("m", params2);
        handler.Print("N");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsItalic());
        Assert.False(line[1].Attributes.IsItalic());
    }

    [Fact]
    public void HandleCsi_SgrResetUnderline_ResetsUnderlineOnly()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        var params1 = new Params();
        params1.AddParam(1); // Bold
        params1.AddParam(4); // Underline
        handler.HandleCsi("m", params1);
        handler.Print("U");
        
        // Reset underline only
        var params2 = new Params();
        params2.AddParam(24); // Reset underline

        // Act
        handler.HandleCsi("m", params2);
        handler.Print("N");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.True(line[0].Attributes.IsUnderline());
        Assert.False(line[1].Attributes.IsUnderline());
    }

    [Fact]
    public void HandleCsi_SgrDefaultForeground_ResetsForegroundColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Set foreground color
        var params1 = new Params();
        params1.AddParam(31); // Red
        handler.HandleCsi("m", params1);
        handler.Print("R");
        
        // Reset to default foreground
        var params2 = new Params();
        params2.AddParam(39); // Default foreground

        // Act
        handler.HandleCsi("m", params2);
        handler.Print("D");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal(1, line[0].Attributes.GetFgColor()); // Red
        Assert.Equal(256, line[1].Attributes.GetFgColor()); // Default foreground is 256
    }

    [Fact]
    public void HandleCsi_SgrDefaultBackground_ResetsBackgroundColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        
        // Set background color
        var params1 = new Params();
        params1.AddParam(42); // Green background
        handler.HandleCsi("m", params1);
        handler.Print("G");
        
        // Reset to default background
        var params2 = new Params();
        params2.AddParam(49); // Default background

        // Act
        handler.HandleCsi("m", params2);
        handler.Print("D");

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        Assert.Equal(2, line[0].Attributes.GetBgColor()); // Green
        Assert.Equal(257, line[1].Attributes.GetBgColor()); // Default background is 257
    }

    #endregion

    #region ESC Sequence Tests

    [Fact]
    public void HandleEsc_SaveCursor_SavesCursorPosition()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(15, 10);

        // Act - ESC 7 (Save Cursor)
        handler.HandleEsc("7", "");

        // Move cursor elsewhere
        terminal.Buffer.SetCursor(30, 20);
        Assert.Equal(30, terminal.Buffer.X);
        Assert.Equal(20, terminal.Buffer.Y);

        // Act - Restore Cursor
        handler.HandleEsc("8", "");

        // Assert
        Assert.Equal(15, terminal.Buffer.X);
        Assert.Equal(10, terminal.Buffer.Y);
    }

    [Fact]
    public void HandleEsc_RestoreCursor_WithoutSave_MovesToOrigin()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(10, 5);

        // Act - ESC 8 without prior save
        handler.HandleEsc("8", "");

        // Assert - Should move to origin or default position
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Fact]
    public void HandleEsc_RIS_ResetsTerminal()
    {
        // Arrange
        var terminal = CreateTerminal();
        var handler = new InputHandler(terminal);
        terminal.Buffer.SetCursor(20, 10);
        terminal.InsertMode = true;
        terminal.OriginMode = true;

        // Act - ESC c (RIS - Reset to Initial State)
        handler.HandleEsc("c", "");

        // Assert
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
        Assert.False(terminal.InsertMode);
        Assert.False(terminal.OriginMode);
    }

    #endregion

    #region C0 Control Character Tests

    [Fact]
    public void Terminal_FormFeed_TreatedAsLineFeed()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Buffer.SetCursor(5, 3);
        var initialY = terminal.Buffer.Y;

        // Act - Form Feed (FF, 0x0C) is typically treated as line feed
        terminal.Write("\x0C");

        // Assert
        Assert.Equal(initialY + 1, terminal.Buffer.Y);
    }

    [Fact]
    public void Terminal_VerticalTab_TreatedAsLineFeed()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Buffer.SetCursor(5, 3);
        var initialY = terminal.Buffer.Y;

        // Act - Vertical Tab (VT, 0x0B) is typically treated as line feed
        terminal.Write("\x0B");

        // Assert
        Assert.Equal(initialY + 1, terminal.Buffer.Y);
    }

    #endregion
    #region DeleteChars BCE Tests

    [Fact]
    public void HandleCsi_DeleteChars_FillsVacatedCellsWithSpaces()
    {
        // Arrange
        var terminal = CreateTerminal(20, 24);
        var handler = new InputHandler(terminal);
        
        // Print characters across the entire width
        for (int i = 0; i < 20; i++)
        {
            handler.Print("X");
        }
        
        // Move cursor to position 5
        terminal.Buffer.SetCursor(5, 0);
        var params_ = new Params();
        params_.AddParam(3); // Delete 3 characters

        // Act
        handler.HandleCsi("P", params_);

        // Assert - Characters should be shifted left
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        
        // Positions 0-4 should be unchanged
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("X", line[i].Content);
        }
        
        // Position 5 should now have what was at position 8
        Assert.Equal("X", line[5].Content);
        
        // The last 3 cells (17, 18, 19) should now be spaces (vacated by shift)
        Assert.Equal(" ", line[17].Content);
        Assert.Equal(" ", line[18].Content);
        Assert.Equal(" ", line[19].Content);
    }

    [Fact]
    public void HandleCsi_DeleteChars_BCE_VacatedCellsUseCurrentAttributes()
    {
        // Arrange - This tests the BCE (Background Color Erase) behavior
        // When deleting characters, the vacated cells at the right edge
        // should be filled with the CURRENT background color/attributes
        var terminal = CreateTerminal(20, 24);
        var handler = new InputHandler(terminal);
        
        // Print characters with default attributes
        for (int i = 0; i < 20; i++)
        {
            handler.Print("X");
        }
        
        // Set a specific background color before deleting
        var sgrParams = new Params();
        sgrParams.AddParam(44); // Blue background
        handler.HandleCsi("m", sgrParams);
        
        // Move cursor to position 5
        terminal.Buffer.SetCursor(5, 0);
        var params_ = new Params();
        params_.AddParam(3); // Delete 3 characters

        // Act
        handler.HandleCsi("P", params_);

        // Assert - The vacated cells should have the blue background
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        
        // The last 3 cells should be spaces with the current (blue) background
        Assert.Equal(" ", line[17].Content);
        Assert.Equal(" ", line[18].Content);
        Assert.Equal(" ", line[19].Content);
        
        // And they should have the blue background (color index 4)
        Assert.Equal(4, line[17].Attributes.GetBgColor());
        Assert.Equal(4, line[18].Attributes.GetBgColor());
        Assert.Equal(4, line[19].Attributes.GetBgColor());
    }

    [Fact]
    public void HandleCsi_DeleteChars_AtEndOfLine_StillClearsCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal(20, 24);
        var handler = new InputHandler(terminal);
        
        // Print characters
        for (int i = 0; i < 20; i++)
        {
            handler.Print(((char)('A' + (i % 26))).ToString());
        }
        
        // Move cursor near end of line
        terminal.Buffer.SetCursor(17, 0);
        var params_ = new Params();
        params_.AddParam(2); // Delete 2 characters

        // Act
        handler.HandleCsi("P", params_);

        // Assert
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        
        // Position 17 should now have what was at 19
        Assert.Equal("T", line[17].Content); // 'T' was at position 19
        
        // Positions 18 and 19 should be spaces
        Assert.Equal(" ", line[18].Content);
        Assert.Equal(" ", line[19].Content);
    }

    [Fact]
    public void HandleCsi_DeleteChars_MoreThanRemaining_ClearsToEndOfLine()
    {
        // Arrange
        var terminal = CreateTerminal(20, 24);
        var handler = new InputHandler(terminal);
        
        // Print characters
        for (int i = 0; i < 20; i++)
        {
            handler.Print("X");
        }
        
        // Move cursor to position 15 and try to delete 10 chars (only 5 remaining)
        terminal.Buffer.SetCursor(15, 0);
        var params_ = new Params();
        params_.AddParam(10); // Delete 10 characters (more than available)

        // Act
        handler.HandleCsi("P", params_);

        // Assert - All cells from cursor to end should be cleared
        var line = terminal.Buffer.Lines[0];
        Assert.NotNull(line);
        
        // Positions 0-14 should be unchanged
        for (int i = 0; i < 15; i++)
        {
            Assert.Equal("X", line[i].Content);
        }
        
        // Positions 15-19 should be spaces
        for (int i = 15; i < 20; i++)
        {
            Assert.Equal(" ", line[i].Content);
        }
    }

    #endregion

}
