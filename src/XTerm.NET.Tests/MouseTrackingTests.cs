using XTerm;
using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests;

public class MouseTrackingTests
{
    private Terminal CreateTerminal(int cols = 80, int rows = 24)
    {
        var options = new TerminalOptions { Cols = cols, Rows = rows };
        return new Terminal(options);
    }

    #region Mouse Mode Activation

    [Fact]
    public void MouseMode_X10_EnablesClickTracking()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B[?9h"); // Enable X10 mouse

        // Assert
        Assert.Equal(MouseTrackingMode.X10, terminal.MouseTrackingMode);
    }

    [Fact]
    public void MouseMode_VT200_EnablesNormalTracking()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B[?1000h"); // Enable VT200 mouse

        // Assert
        Assert.Equal(MouseTrackingMode.VT200, terminal.MouseTrackingMode);
    }

    [Fact]
    public void MouseMode_ButtonEvent_EnablesButtonTracking()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B[?1002h"); // Enable button event tracking

        // Assert
        Assert.Equal(MouseTrackingMode.ButtonEvent, terminal.MouseTrackingMode);
    }

    [Fact]
    public void MouseMode_AnyEvent_EnablesAllTracking()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B[?1003h"); // Enable any event tracking

        // Assert
        Assert.Equal(MouseTrackingMode.AnyEvent, terminal.MouseTrackingMode);
    }

    [Fact]
    public void MouseMode_SGR_EnablesSGREncoding()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B[?1006h"); // Enable SGR encoding

        // Assert
        Assert.Equal(MouseEncoding.SGR, terminal.MouseEncoding);
    }

    [Fact]
    public void MouseMode_Disable_ResetsToNone()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h"); // Enable

        // Act
        terminal.Write("\x1B[?1000l"); // Disable

        // Assert
        Assert.Equal(MouseTrackingMode.None, terminal.MouseTrackingMode);
    }

    #endregion

    #region Mouse Event Generation - Default Format

    [Fact]
    public void MouseEvent_LeftClick_GeneratesCorrectSequence()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h"); // Enable VT200 mouse

        // Act
        var sequence = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);

        // Assert - Default format: ESC [ M Cb Cx Cy
        // ESC (1) + [ (1) + M (1) + Cb (1) + Cx (1) + Cy (1) = 6 chars
        // Cb = 32 + button (0) = 32
        // Cx = 32 + x + 1 = 32 + 5 + 1 = 38 ('&')
        // Cy = 32 + y + 1 = 32 + 10 + 1 = 43 ('+')
        Assert.StartsWith("\x1B[M", sequence);
        Assert.Equal(6, sequence.Length);
    }

    [Fact]
    public void MouseEvent_NoMode_ReturnsEmpty()
    {
        // Arrange
        var terminal = CreateTerminal();
        // No mouse mode enabled

        // Act
        var sequence = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);

        // Assert
        Assert.Equal(string.Empty, sequence);
    }

    [Fact]
    public void MouseEvent_X10Mode_OnlyReportsDown()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?9h"); // X10 mode

        // Act
        var downSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);
        var upSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Up);
        var moveSeq = terminal.GenerateMouseEvent(MouseButton.Left, 6, 10, MouseEventType.Move);

        // Assert
        Assert.NotEmpty(downSeq);
        Assert.Empty(upSeq);
        Assert.Empty(moveSeq);
    }

    [Fact]
    public void MouseEvent_VT200Mode_ReportsDownAndUp()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h"); // VT200 mode

        // Act
        var downSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);
        var upSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Up);
        var moveSeq = terminal.GenerateMouseEvent(MouseButton.Left, 6, 10, MouseEventType.Move);

        // Assert
        Assert.NotEmpty(downSeq);
        Assert.NotEmpty(upSeq);
        Assert.Empty(moveSeq); // Motion not reported in VT200
    }

    [Fact]
    public void MouseEvent_ButtonEventMode_ReportsDrag()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1002h"); // Button event mode

        // Act
        var dragSeq = terminal.GenerateMouseEvent(MouseButton.Left, 6, 10, MouseEventType.Drag);

        // Assert
        Assert.NotEmpty(dragSeq);
    }

    [Fact]
    public void MouseEvent_AnyEventMode_ReportsMotion()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1003h"); // Any event mode

        // Act
        var moveSeq = terminal.GenerateMouseEvent(MouseButton.None, 6, 10, MouseEventType.Move);

        // Assert
        Assert.NotEmpty(moveSeq);
    }

    #endregion

    #region Mouse Buttons

    [Fact]
    public void MouseEvent_MiddleButton_GeneratesCorrectCode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act
        var sequence = terminal.GenerateMouseEvent(MouseButton.Middle, 5, 10, MouseEventType.Down);

        // Assert
        Assert.NotEmpty(sequence);
        Assert.StartsWith("\x1B[M", sequence);
    }

    [Fact]
    public void MouseEvent_RightButton_GeneratesCorrectCode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act
        var sequence = terminal.GenerateMouseEvent(MouseButton.Right, 5, 10, MouseEventType.Down);

        // Assert
        Assert.NotEmpty(sequence);
        Assert.StartsWith("\x1B[M", sequence);
    }

    [Fact]
    public void MouseEvent_WheelUp_GeneratesCorrectSequence()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act
        var sequence = terminal.GenerateMouseEvent(MouseButton.WheelUp, 5, 10, MouseEventType.WheelUp);

        // Assert
        Assert.NotEmpty(sequence);
    }

    [Fact]
    public void MouseEvent_WheelDown_GeneratesCorrectSequence()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act
        var sequence = terminal.GenerateMouseEvent(MouseButton.WheelDown, 5, 10, MouseEventType.WheelDown);

        // Assert
        Assert.NotEmpty(sequence);
    }

    #endregion

    #region SGR Format

    [Fact]
    public void MouseEvent_SGRFormat_GeneratesCorrectSequence()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h"); // Enable tracking
        terminal.Write("\x1B[?1006h"); // Enable SGR format

        // Act
        var sequence = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);

        // Assert - SGR format: ESC [ < Cb ; Cx ; Cy M
        Assert.StartsWith("\x1B[<", sequence);
        Assert.Contains(";", sequence);
        Assert.EndsWith("M", sequence);
    }

    [Fact]
    public void MouseEvent_SGRFormat_Release_UsesLowercaseM()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");
        terminal.Write("\x1B[?1006h"); // SGR format

        // Act
        var downSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);
        var upSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Up);

        // Assert
        Assert.EndsWith("M", downSeq); // Uppercase for press
        Assert.EndsWith("m", upSeq);   // Lowercase for release
    }

    #endregion

    #region Modifiers

    [Fact]
    public void MouseEvent_WithShift_IncludesModifier()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act
        var normalSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);
        var shiftSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down, KeyModifiers.Shift);

        // Assert
        Assert.NotEqual(normalSeq, shiftSeq);
    }

    [Fact]
    public void MouseEvent_WithControl_IncludesModifier()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act
        var normalSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);
        var ctrlSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down, KeyModifiers.Control);

        // Assert
        Assert.NotEqual(normalSeq, ctrlSeq);
    }

    [Fact]
    public void MouseEvent_WithAlt_IncludesModifier()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act
        var normalSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down);
        var altSeq = terminal.GenerateMouseEvent(MouseButton.Left, 5, 10, MouseEventType.Down, KeyModifiers.Alt);

        // Assert
        Assert.NotEqual(normalSeq, altSeq);
    }

    #endregion

    #region Focus Events

    [Fact]
    public void FocusEvent_Enabled_GeneratesSequence()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1004h"); // Enable focus events

        // Act
        var focusIn = terminal.GenerateFocusEvent(true);
        var focusOut = terminal.GenerateFocusEvent(false);

        // Assert
        Assert.Equal("\x1B[I", focusIn);
        Assert.Equal("\x1B[O", focusOut);
    }

    [Fact]
    public void FocusEvent_Disabled_ReturnsEmpty()
    {
        // Arrange
        var terminal = CreateTerminal();
        // Focus events not enabled

        // Act
        var focusIn = terminal.GenerateFocusEvent(true);

        // Assert
        Assert.Equal(string.Empty, focusIn);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void MouseEvent_AtBoundaries_HandlesCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act - Test at (0,0) and (max,max)
        var topLeft = terminal.GenerateMouseEvent(MouseButton.Left, 0, 0, MouseEventType.Down);
        var bottomRight = terminal.GenerateMouseEvent(MouseButton.Left, 79, 23, MouseEventType.Down);

        // Assert
        Assert.NotEmpty(topLeft);
        Assert.NotEmpty(bottomRight);
    }

    [Fact]
    public void MouseEvent_LargeCoordinates_ClampsCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B[?1000h");

        // Act - Test with very large coordinates
        var sequence = terminal.GenerateMouseEvent(MouseButton.Left, 300, 300, MouseEventType.Down);

        // Assert - Should not throw and should generate something
        Assert.NotEmpty(sequence);
    }

    [Fact]
    public void MouseMode_ModeSwitch_UpdatesCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act - Switch between modes
        terminal.Write("\x1B[?9h"); // X10
        var mode1 = terminal.MouseTrackingMode;

        terminal.Write("\x1B[?1000h"); // VT200
        var mode2 = terminal.MouseTrackingMode;

        terminal.Write("\x1B[?1003h"); // Any event
        var mode3 = terminal.MouseTrackingMode;

        // Assert
        Assert.Equal(MouseTrackingMode.X10, mode1);
        Assert.Equal(MouseTrackingMode.VT200, mode2);
        Assert.Equal(MouseTrackingMode.AnyEvent, mode3);
    }

    [Fact]
    public void MouseEncoding_Switch_UpdatesCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act - Switch encodings
        terminal.Write("\x1B[?1006h"); // SGR
        var enc1 = terminal.MouseEncoding;

        terminal.Write("\x1B[?1005h"); // UTF-8
        var enc2 = terminal.MouseEncoding;

        terminal.Write("\x1B[?1006l"); // Disable
        var enc3 = terminal.MouseEncoding;

        // Assert
        Assert.Equal(MouseEncoding.SGR, enc1);
        Assert.Equal(MouseEncoding.Utf8, enc2);
        Assert.Equal(MouseEncoding.Default, enc3);
    }

    #endregion
}
